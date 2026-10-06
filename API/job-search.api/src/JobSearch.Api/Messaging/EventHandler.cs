using JobSearch.Api.Contracts;
using JobSearch.Api.Configuration;
using JobSearch.Api.Persistence;
using Microsoft.Extensions.Options;
namespace JobSearch.Api.Messaging;

public enum DeliveryOutcome { Inserted, Duplicate }
public sealed class PermanentDeliveryException(string code) : Exception("The event cannot be projected.")
{
    public string Code { get; } = code;
}
public interface ISearchEventHandler
{
    Task<DeliveryOutcome> HandleAsync(ReadOnlyMemory<byte> body, EventMetadata metadata, CancellationToken token);
}
public sealed class SearchEventHandler(EventReader reader, IOptions<SearchOptions> options, ISearchProjection projection) : ISearchEventHandler
{
    public async Task<DeliveryOutcome> HandleAsync(ReadOnlyMemory<byte> body, EventMetadata metadata, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = reader.Read(body, options.Value.MaximumEventBodyBytes, metadata);
        if (result.Event is null) throw new PermanentDeliveryException(result.Error!);
        var outcome = await projection.ProjectAsync(result.Event, token);
        return outcome switch
        {
            ProjectionOutcome.Inserted => DeliveryOutcome.Inserted,
            ProjectionOutcome.Duplicate => DeliveryOutcome.Duplicate,
            _ => throw new PermanentDeliveryException("identity_conflict")
        };
    }
}
