using System.Text;
using JobSearch.Api.Contracts;
using JobSearch.Api.Configuration;
using JobSearch.Api.Messaging;
using JobSearch.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
namespace JobSearch.Api.IntegrationTests;
[Trait("Category","Integration")]
public sealed class ConsumerIntegrationTests(PostgresFixture pg,RabbitMqFixture rabbit):IClassFixture<PostgresFixture>,IClassFixture<RabbitMqFixture>
{
    private static byte[] Body=>File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures/job-created-v1.json"));
    [Fact]
    public async Task PublishedFixtureIsProjectedOnceAndAckedAfterSqlCommit()
    {
        var db=await pg.NewDatabaseAsync();var settings=rabbit.Settings();await using var connection=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync();await using var inspect=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
        await using var provider=Provider(db,settings);using var cancel=new CancellationTokenSource();var run=provider.GetRequiredService<IConsumerSession>().RunAsync(cancel.Token);
        await WaitConsumer(inspect,settings.Queue,1);await Publish(inspect,settings,Body);await Publish(inspect,settings,Body);
        await WaitRows(db,1);await WaitEmpty(inspect,settings.Queue);cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run);
        await using var context=new SearchDbContext(db);var row=await context.Jobs.SingleAsync();var expected=new EventReader().Read(Body,65536,new()).Event!;Assert.Equal(expected.Job,row.ToJob());Assert.Equal(expected.EventId,row.EventId);Assert.Equal(expected.OccurredAt.UtcTicks,row.SourceOccurredAtTicks);
        // Frozen producer-compatible fixture is expired: accept/ACK it, omit from available lists, retain detail.
        var reads=new JobSearch.Api.Search.JobReadStore(new Factory(db));var query=new SearchQuery(null,null,null,20,"newest",null);
        var snapshot=new JobSearch.Api.Search.CursorCodec(Options.Create(new JobSearch.Api.Search.CursorOptions())).Start(query,DateTimeOffset.UtcNow,await reads.WatermarkAsync(default));
        Assert.Empty(await reads.ListAsync(query,snapshot,false,default));Assert.Equal(expected.Job.Description,(await reads.DetailAsync(expected.Job.Id,default))!.Description);

        Assert.Null(await inspect.BasicGetAsync(settings.Queue,false));
    }
    [Fact]
    public async Task CommitBeforeAckInterruptionRedeliversAsDuplicateOnNewChannel()
    {
        var db=await pg.NewDatabaseAsync();var settings=rabbit.Settings();await using var connection=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync();await using var inspect=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
        var committed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstHandler=new ControlledHandler(new SearchEventHandler(new(),Options.Create(new SearchOptions()),new ProjectionStore(new Factory(db)))){After=(_,_)=>{committed.TrySetResult();throw new IOException("test interruption after real SQL commit before broker ACK");}};
        await using var first=Provider(db,settings,firstHandler);var attempt=first.GetRequiredService<IConsumerSession>().RunAsync(default);await WaitConsumer(inspect,settings.Queue,1);await Publish(inspect,settings,Body);await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));await Assert.ThrowsAsync<IOException>(()=>attempt);
        await using(var context=new SearchDbContext(db))Assert.Single(await context.Jobs.ToListAsync());
        var result=new TaskCompletionSource<DeliveryOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);var secondHandler=new ControlledHandler(new SearchEventHandler(new(),Options.Create(new SearchOptions()),new ProjectionStore(new Factory(db)))){After=(o,_)=>{result.TrySetResult(o);return Task.CompletedTask;}};
        await using var second=Provider(db,settings,secondHandler);using var cancel=new CancellationTokenSource();var run=second.GetRequiredService<IConsumerSession>().RunAsync(cancel.Token);Assert.Equal(DeliveryOutcome.Duplicate,await result.Task.WaitAsync(TimeSpan.FromSeconds(10)));await WaitEmpty(inspect,settings.Queue);cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run);Assert.Null(await inspect.BasicGetAsync(settings.Queue,false));
    }
    [Fact]
    public async Task IndependentConsumersCompeteSafelyAndPrefetchBoundsUnackedWork()
    {
        var db=await pg.NewDatabaseAsync();var settings=rabbit.Settings();settings.Prefetch=2;settings.Concurrency=1;
        await using var connection=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync();await using var inspect=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
        var enter=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked=new ControlledHandler(new SearchEventHandler(new(),Options.Create(new SearchOptions()),new ProjectionStore(new Factory(db)))){Before=async t=>{enter.TrySetResult();await release.Task.WaitAsync(t);}};
        await using var first=Provider(db,settings,blocked);using var stopFirst=new CancellationTokenSource();var one=first.GetRequiredService<IConsumerSession>().RunAsync(stopFirst.Token);await WaitConsumer(inspect,settings.Queue,1);
        for(var i=0;i<5;i++)await Publish(inspect,settings,Body);await enter.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deadline=DateTime.UtcNow.AddSeconds(10);QueueDeclareOk state;
        do{state=await inspect.QueueDeclarePassiveAsync(settings.Queue);if(state.MessageCount==3)break;await Task.Delay(25);}while(DateTime.UtcNow<deadline);
        Assert.Equal(3U,state.MessageCount);Assert.Equal(1,blocked.Active);
        await using var second=Provider(db,settings);using var stopSecond=new CancellationTokenSource();var two=second.GetRequiredService<IConsumerSession>().RunAsync(stopSecond.Token);await WaitConsumer(inspect,settings.Queue,2);await WaitRows(db,1);release.TrySetResult();await WaitEmpty(inspect,settings.Queue);
        stopFirst.Cancel();stopSecond.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>one);await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>two);await using var context=new SearchDbContext(db);Assert.Single(await context.Jobs.ToListAsync());
        Assert.Null(await inspect.BasicGetAsync(settings.Queue,false));
    }
    [Fact]
    public async Task PoisonAndConflictAreQuarantinedWhileValidWorkContinues()
    {
        var db=await pg.NewDatabaseAsync();var settings=rabbit.Settings();await using var connection=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync();await using var inspect=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
        await using var provider=Provider(db,settings);using var stop=new CancellationTokenSource();var run=provider.GetRequiredService<IConsumerSession>().RunAsync(stop.Token);await WaitConsumer(inspect,settings.Queue,1);
        var poison=Encoding.UTF8.GetBytes("{invalid}");await inspect.BasicPublishAsync(settings.Exchange,settings.RoutingKey,true,new BasicProperties{Persistent=true,MessageId="poison"},poison);
        await Publish(inspect,settings,Body);await WaitRows(db,1);
        var conflict=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Body).Replace("Software", "Different",StringComparison.Ordinal));
        if(conflict.SequenceEqual(Body)){var node=System.Text.Json.Nodes.JsonNode.Parse(Body)!;node["job"]!["title"]="Different title";conflict=Encoding.UTF8.GetBytes(node.ToJsonString());}
        await Publish(inspect,settings,conflict);await Publish(inspect,settings,Body);await WaitEmpty(inspect,settings.Queue);stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run);
        var one=await inspect.BasicGetAsync(settings.QuarantineQueue,true);var two=await inspect.BasicGetAsync(settings.QuarantineQueue,true);Assert.NotNull(one);Assert.NotNull(two);Assert.True(one.BasicProperties.Persistent);Assert.Equal(poison,one.Body.ToArray());Assert.Equal("poison",one.BasicProperties.MessageId);Assert.Null(await inspect.BasicGetAsync(settings.Queue,false));
    }
    [Fact]
    public async Task UnknownQuarantineAcceptanceLeavesSourceUnackedAndCanDuplicateQuarantine()
    {
        var db=await pg.NewDatabaseAsync();var settings=rabbit.Settings();await using var connection=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync();await using var inspect=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
        var publisher=new RabbitMqQuarantinePublisher(SearchMessaging.CreateFactory(settings),Options.Create(settings),TimeProvider.System,Microsoft.Extensions.Logging.Abstractions.NullLogger<RabbitMqQuarantinePublisher>.Instance);
        await using(var first=Provider(db,settings,quarantine:new LostConfirmation(publisher)))
        {
            var run=first.GetRequiredService<IConsumerSession>().RunAsync(default);await WaitConsumer(inspect,settings.Queue,1);
            await inspect.BasicPublishAsync(settings.Exchange,settings.RoutingKey,true,new BasicProperties{Persistent=true},Encoding.UTF8.GetBytes("{}"));
            await Assert.ThrowsAsync<QuarantineException>(()=>run.WaitAsync(TimeSpan.FromSeconds(10)));Assert.Equal(1U,(await inspect.QueueDeclarePassiveAsync(settings.Queue)).MessageCount);
        }
        await using var second=Provider(db,settings);using var stop=new CancellationTokenSource();var recovery=second.GetRequiredService<IConsumerSession>().RunAsync(stop.Token);await WaitConsumer(inspect,settings.Queue,1);await Publish(inspect,settings,Body);await WaitRows(db,1);await WaitEmpty(inspect,settings.Queue);stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>recovery);
        Assert.Equal(2U,(await inspect.QueueDeclarePassiveAsync(settings.QuarantineQueue)).MessageCount);Assert.Null(await inspect.BasicGetAsync(settings.Queue,false));
    }
    [Fact]
    public async Task UnavailableQuarantineDoesNotAcknowledgeSource()
    {
        var db=await pg.NewDatabaseAsync();var settings=rabbit.Settings();await using var connection=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync();await using var inspect=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
        // Deliberate incompatible SEARCH-OWNED test topology, never the shared source queue.
        await inspect.ExchangeDeclareAsync(settings.QuarantineExchange,ExchangeType.Direct,false,false);
        await using(var first=Provider(db,settings))
        {
            var run=first.GetRequiredService<IConsumerSession>().RunAsync(default);await WaitConsumer(inspect,settings.Queue,1);await inspect.BasicPublishAsync(settings.Exchange,settings.RoutingKey,true,new BasicProperties{Persistent=true},Encoding.UTF8.GetBytes("{}"));await Assert.ThrowsAsync<QuarantineException>(()=>run.WaitAsync(TimeSpan.FromSeconds(10)));Assert.Equal(1U,(await inspect.QueueDeclarePassiveAsync(settings.Queue)).MessageCount);
        }
        await inspect.ExchangeDeleteAsync(settings.QuarantineExchange);
        await using var second=Provider(db,settings);using var stop=new CancellationTokenSource();var recovery=second.GetRequiredService<IConsumerSession>().RunAsync(stop.Token);await WaitConsumer(inspect,settings.Queue,1);await Publish(inspect,settings,Body);await WaitRows(db,1);await WaitEmpty(inspect,settings.Queue);stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>recovery);Assert.Equal(1U,(await inspect.QueueDeclarePassiveAsync(settings.QuarantineQueue)).MessageCount);Assert.Null(await inspect.BasicGetAsync(settings.Queue,false));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ShutdownDrainsCommittedWorkOrReleasesIncompleteDelivery(bool force)
    {
        var db=await pg.NewDatabaseAsync();var settings=rabbit.Settings();settings.DrainSeconds=1;
        await using var connection=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync();await using var inspect=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new ControlledHandler(new SearchEventHandler(new(),Options.Create(new SearchOptions()),new ProjectionStore(new Factory(db)))){Before=async token=>{entered.TrySetResult();await release.Task.WaitAsync(token);}};
        await using var provider=Provider(db,settings,handler);using var stop=new CancellationTokenSource();var run=provider.GetRequiredService<IConsumerSession>().RunAsync(stop.Token);await WaitConsumer(inspect,settings.Queue,1);await Publish(inspect,settings,Body);await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));stop.Cancel();if(!force)release.SetResult();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run.WaitAsync(TimeSpan.FromSeconds(8)));
        await using var context=new SearchDbContext(db);Assert.Equal(force?0:1,await context.Jobs.CountAsync());Assert.Equal(force?1U:0U,(await inspect.QueueDeclarePassiveAsync(settings.Queue)).MessageCount);
    }
    [Fact]
    public async Task DatabaseOutageLeavesDeliveryUnackedAndRecoveryProjectsIt()
    {
        var db=await pg.NewDatabaseAsync();
        await using(var configured=new SearchDbContext(db))db=SearchPersistence.CreateOptions(new SearchDatabaseOptions{ConnectionString=new Npgsql.NpgsqlConnectionStringBuilder(configured.Database.GetConnectionString()){CancellationTimeout=1000}.ConnectionString,CommandTimeoutSeconds=1,IdleTransactionTimeoutSeconds=1});
        var settings=rabbit.Settings();await using var connection=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync();await using var inspect=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
        await using var first=Provider(db,settings);var attempt=first.GetRequiredService<IConsumerSession>().RunAsync(default);await WaitConsumer(inspect,settings.Queue,1);
        await pg.PauseAsync();
        try
        {
            await Publish(inspect,settings,Body);await Assert.ThrowsAnyAsync<Exception>(()=>attempt.WaitAsync(TimeSpan.FromSeconds(20)));Assert.True(attempt.IsCompleted);Assert.Equal(1U,(await inspect.QueueDeclarePassiveAsync(settings.Queue)).MessageCount);
        }
        finally{await pg.ResumeAsync();}
        await using var second=Provider(db,settings);using var stop=new CancellationTokenSource();var recovery=second.GetRequiredService<IConsumerSession>().RunAsync(stop.Token);await WaitConsumer(inspect,settings.Queue,1);await WaitRows(db,1);await WaitEmpty(inspect,settings.Queue);stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>recovery);Assert.Null(await inspect.BasicGetAsync(settings.Queue,false));
    }
    [Fact]
    public async Task WorkerReconnectsAfterRealBrokerRestartWhileDatabaseRemainsReady()
    {
        var db=await pg.NewDatabaseAsync();var settings=rabbit.Settings();await using var provider=Provider(db,settings);using var stop=new CancellationTokenSource();
        using var worker=new SearchConsumerWorker(provider.GetRequiredService<IConsumerSession>(),Options.Create(settings),TimeProvider.System,Microsoft.Extensions.Logging.Abstractions.NullLogger<SearchConsumerWorker>.Instance,provider.GetRequiredService<IngestionState>(),new BackoffJitter());var run=worker.RunAsync(stop.Token);
        await using(var before=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync()){await using var channel=await before.CreateChannelAsync();await WaitConsumer(channel,settings.Queue,1);}
        await rabbit.RestartAsync();Assert.True(await new JobSearch.Api.Diagnostics.SearchDatabaseProbe(new Factory(db)).ReadyAsync(default));
        await using var connection=await SearchMessaging.CreateFactory(settings).CreateConnectionAsync();await using var inspect=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));await WaitConsumer(inspect,settings.Queue,1,45);await Publish(inspect,settings,Body);await WaitRows(db,1);await WaitEmpty(inspect,settings.Queue);stop.Cancel();await run.WaitAsync(TimeSpan.FromSeconds(10));Assert.Null(await inspect.BasicGetAsync(settings.Queue,false));
    }
    private sealed class LostConfirmation(IQuarantinePublisher inner):IQuarantinePublisher
    {public async Task PublishAsync(QuarantineMessage message,CancellationToken token){await inner.PublishAsync(message,token);throw new QuarantineException(new IOException("Injected lost confirmation after real broker acceptance"));}}
    private static ServiceProvider Provider(DbContextOptions<SearchDbContext> db,ConsumerOptions settings,ISearchEventHandler? handler=null,IQuarantinePublisher? quarantine=null)
    {
        var values=new Dictionary<string,string?>{{"RabbitMq:HostName",settings.HostName},{"RabbitMq:Port",settings.Port.ToString()},{"RabbitMq:UserName",settings.UserName},{"RabbitMq:Password",settings.Password},{"RabbitMq:Exchange",settings.Exchange},{"RabbitMq:Queue",settings.Queue},{"RabbitMq:Prefetch",settings.Prefetch.ToString()},{"RabbitMq:Concurrency",settings.Concurrency.ToString()}};
        values["RabbitMq:QuarantineExchange"]=settings.QuarantineExchange;values["RabbitMq:QuarantineQueue"]=settings.QuarantineQueue;values["RabbitMq:DrainSeconds"]=settings.DrainSeconds.ToString();
        var services=new ServiceCollection().AddLogging();services.AddSingleton<TimeProvider>(TimeProvider.System);services.AddOptions<SearchOptions>();services.AddSingleton<IDbContextFactory<SearchDbContext>>(new Factory(db));services.AddScoped<ISearchProjection,ProjectionStore>();services.AddSearchMessaging(new ConfigurationBuilder().AddInMemoryCollection(values).Build());if(handler is not null)services.AddSingleton(handler);if(quarantine is not null)services.AddSingleton(quarantine);return services.BuildServiceProvider();
    }
    private static async Task Publish(IChannel channel,ConsumerOptions settings,byte[] body)
    {
        var e=new EventReader().Read(body,65536,new()).Event!;
        await channel.BasicPublishAsync(settings.Exchange,settings.RoutingKey,true,new BasicProperties{Persistent=true,ContentType="application/json",MessageId=e.EventId.ToString("D"),Type="JobPostingCreated",Headers=new Dictionary<string,object?>{{"schemaVersion",1}}},body);
    }
    private static async Task WaitRows(DbContextOptions<SearchDbContext> options,int count)
    {
        var deadline=DateTime.UtcNow.AddSeconds(10);while(DateTime.UtcNow<deadline){await using var db=new SearchDbContext(options);if(await db.Jobs.CountAsync()==count)return;await Task.Delay(25);}throw new TimeoutException("Projection did not become durable.");
    }
    private static async Task WaitConsumer(IChannel channel,string queue,uint count,int timeoutSeconds=10)
    {
        // Channel errors close a passive inspection channel, so declare compatible topology first.
        await channel.ExchangeDeclareAsync(queue.Replace("job-post-queue","job-post-exchange"),ExchangeType.Direct,true,false);
        await channel.QueueDeclareAsync(queue,true,false,false);
        var deadline=DateTime.UtcNow.AddSeconds(timeoutSeconds);while(DateTime.UtcNow<deadline){if((await channel.QueueDeclarePassiveAsync(queue)).ConsumerCount==count)return;await Task.Delay(25);}throw new TimeoutException("Consumer did not subscribe.");
    }
    private static async Task WaitEmpty(IChannel channel,string queue)
    {
        // Ready count is only a progress hint; each test also closes sessions and inspects released unacked messages.
        var deadline=DateTime.UtcNow.AddSeconds(10);while(DateTime.UtcNow<deadline){if((await channel.QueueDeclarePassiveAsync(queue)).MessageCount==0){await Task.Delay(100);return;}await Task.Delay(25);}throw new TimeoutException("Queue did not drain.");
    }
    private sealed class Factory(DbContextOptions<SearchDbContext> options):IDbContextFactory<SearchDbContext>{public SearchDbContext CreateDbContext()=>new(options);}
    private sealed class ControlledHandler(ISearchEventHandler inner):ISearchEventHandler
    {
        public int Active;public Func<CancellationToken,Task> Before=_=>Task.CompletedTask;public Func<DeliveryOutcome,CancellationToken,Task> After=(_,_)=>Task.CompletedTask;
        public async Task<DeliveryOutcome> HandleAsync(ReadOnlyMemory<byte>b,EventMetadata m,CancellationToken t){Interlocked.Increment(ref Active);try{await Before(t);var outcome=await inner.HandleAsync(b,m,t);await After(outcome,t);return outcome;}finally{Interlocked.Decrement(ref Active);}}
    }
}
