using RabbitMQ.Client;
using System.Text.Json;
if(args.Length!=0 && !args.SequenceEqual(new[]{"--probe"}))throw new ArgumentException("Only --probe or disposable fixture publication is supported.");
var host=Environment.GetEnvironmentVariable("VERIFY_BROKER_HOST")??"127.0.0.1";
var port=int.Parse(Environment.GetEnvironmentVariable("VERIFY_BROKER_PORT")??"5672");
var user=Environment.GetEnvironmentVariable("RABBITMQ_USER")??throw new InvalidOperationException("Broker user required.");
var password=Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD")??throw new InvalidOperationException("Broker password required.");
var factory=new ConnectionFactory{HostName=host,Port=port,UserName=user,Password=password,VirtualHost=Environment.GetEnvironmentVariable("RABBITMQ_VHOST")??"/",AutomaticRecoveryEnabled=false,RequestedConnectionTimeout=TimeSpan.FromSeconds(3)};
using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(10));await using var connection=await factory.CreateConnectionAsync(budget.Token);
if(args.SequenceEqual(new[]{"--probe"})){Console.WriteLine("Broker connection accepted; no topology or messages changed.");return;}
var queue=Environment.GetEnvironmentVariable("RABBITMQ_QUEUE")??"";var exchange=Environment.GetEnvironmentVariable("RABBITMQ_EXCHANGE")??"";
if(!queue.StartsWith("verify-",StringComparison.Ordinal) || !exchange.StartsWith("verify-",StringComparison.Ordinal))throw new InvalidOperationException("Fixture publishing requires disposable verify- topology.");
await using var channel=await connection.CreateChannelAsync(new CreateChannelOptions(true,true),budget.Token);await channel.ExchangeDeclareAsync(exchange,ExchangeType.Direct,true,false,cancellationToken:budget.Token);await channel.QueueDeclareAsync(queue,true,false,false,cancellationToken:budget.Token);await channel.QueueBindAsync(queue,exchange,"job-posting.created.v1",cancellationToken:budget.Token);
var now=DateTimeOffset.UtcNow;var id=Guid.NewGuid();var eventId=Guid.NewGuid();
var body=JsonSerializer.SerializeToUtf8Bytes(new{eventId,eventType="JobPostingCreated",schemaVersion=1,occurredAt=now,correlationId="docker-verification",job=new{id,createdAt=now,title="Docker verification engineer",department="Engineering",location="Toronto",description="Independent search fixture",salaryMin=1,salaryMax=2,closingDate=DateOnly.FromDateTime(now.UtcDateTime).AddDays(5)}});
await channel.BasicPublishAsync(exchange,"job-posting.created.v1",true,new BasicProperties{Persistent=true,ContentType="application/json",MessageId=eventId.ToString("D"),Type="JobPostingCreated",Headers=new Dictionary<string,object?>{{"schemaVersion",1}}},body,budget.Token);Console.WriteLine(id);
