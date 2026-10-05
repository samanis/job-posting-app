using JobPosting.Api.Contracts;
using JobPosting.Api.Idempotency;
using JobPosting.Api.Messaging;
using JobPosting.Api.Persistence;
using JobPosting.Api.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobPosting.Api.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class PostingWorkflowIntegrationTests(PostgresFixture postgres, RabbitMqFixture broker)
    : IClassFixture<PostgresFixture>, IClassFixture<RabbitMqFixture>
{
    private static CreateJobRequest Request() => new() { Title="Engineer", Department="Engineering", Location="Toronto", Description="Plain text", SalaryMin=10m, SalaryMax=100m, ClosingDate=new(2028,2,29) };
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026,10,5,12,0,0,TimeSpan.Zero); }
    private sealed class Factory(DbContextOptions<PostingDbContext> options) : IDbContextFactory<PostingDbContext>
    { public PostingDbContext CreateDbContext() => new(options); }
    private sealed class Faults(IPostingWriteStore store, string mode) : IPostingWriteStore
    {
        public Task CreateAsync(PendingPosting row,CancellationToken token) => store.CreateAsync(row,token);
        public Task<PersistedPosting?> ReadAsync(string digest,CancellationToken token) => store.ReadAsync(digest,token);
        public Task<bool> MarkPublishedAsync(Guid job,Guid message,DateTimeOffset at,CancellationToken token) => mode=="mark" ? Task.FromException<bool>(new IOException("injected status failure")) : store.MarkPublishedAsync(job,message,at,token);
        public Task<bool> DeleteUnpublishedAsync(Guid job,Guid message,CancellationToken token) => mode=="cleanup" ? Task.FromException<bool>(new IOException("injected cleanup failure")) : store.DeleteUnpublishedAsync(job,message,token);
    }
    private sealed class Send(IJobEventPublisher publisher,string mode) : IJobEventPublisher
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PublishAsync(JobPostingCreated message,CancellationToken token)
        {
            Entered.TrySetResult();
            if(mode=="race") await Release.Task.WaitAsync(token);
            if(mode=="rejected") throw new JobPublicationException(PublicationFailure.NotAccepted,new IOException("injected pre-send failure"));
            await publisher.PublishAsync(message,token);
            // Fault injection after REAL confirmation models a caller that lost that acknowledgement.
            if(mode is "unknown" or "cleanup") throw new JobPublicationException(PublicationFailure.AcceptanceUnknown,new IOException("injected lost acknowledgement"));
        }
    }
    [Theory]
    [InlineData("success")]
    [InlineData("race")]
    [InlineData("rejected")]
    [InlineData("unknown")]
    [InlineData("cleanup")]
    [InlineData("mark")]
    public async Task RealDatabaseAndBrokerVerifyPublicationAndFailureWindows(string mode)
    {
        var options=await postgres.NewDatabaseAsync();
        var settings=broker.Settings();
        await using var publisher=new RabbitMqJobEventPublisher(RabbitMqMessaging.CreateFactory(settings),Options.Create(settings),NullLogger<RabbitMqJobEventPublisher>.Instance);
        var store=new Faults(new PostingWriteStore(new Factory(options)),mode);
        var sender=new Send(publisher,mode); var clock=new Clock();
        var workflow=new PostingWorkflow(new PostingCoordinator(store,new(),new(clock,TimeZoneInfo.Utc),clock),store,sender,clock,NullLogger<PostingWorkflow>.Instance);
        var attempt=workflow.SubmitAsync(["key"],Request(),"trace",default);
        if(mode=="race")
        {
            await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var duplicate=await workflow.SubmitAsync(["key"],Request(),"duplicate",default);
            Assert.Equal(PostingOutcome.PublicationUnresolved,duplicate.Outcome);
            sender.Release.TrySetResult();
        }
        if(mode is "success" or "race")
        {
            var result=await attempt; Assert.Equal(PostingOutcome.Published,result.Outcome);
            var replay=await workflow.SubmitAsync(["key"],Request(),"retry",default);
            Assert.Equal(PostingOutcome.Published,replay.Outcome);
            Assert.Equal(result.Posting!.Job.ResponseJson,replay.Posting!.Job.ResponseJson);
        }
        else
        {
            var error=await Assert.ThrowsAsync<PostingWorkflowException>(()=>attempt);
            Assert.Equal(mode is "cleanup" or "mark" ? JobApiProblems.PublicationUnresolved : JobApiProblems.PublicationFailed,error.Code);
        }
        await using var verification=new PostingDbContext(options);
        var row=await verification.Jobs.SingleOrDefaultAsync();
        if(mode is "unknown" or "rejected") Assert.Null(row);
        else { Assert.NotNull(row); Assert.Equal(mode is "success" or "race",row.PublishedAt.HasValue); }
        if(mode is "cleanup" or "mark")
            Assert.Equal(PostingOutcome.PublicationUnresolved,(await workflow.SubmitAsync(["key"],Request(),"retry",default)).Outcome);
        if(mode=="rejected") return; // Pre-send injection never declares broker topology.
        await using var connection=await RabbitMqMessaging.CreateFactory(settings).CreateConnectionAsync();
        await using var channel=await connection.CreateChannelAsync();
        var message=await channel.BasicGetAsync(settings.Queue,true); Assert.NotNull(message);
        if(row is not null) Assert.Equal(row.EventId.ToString("D"),message.BasicProperties.MessageId);
        Assert.Null(await channel.BasicGetAsync(settings.Queue,true));
    }
}
