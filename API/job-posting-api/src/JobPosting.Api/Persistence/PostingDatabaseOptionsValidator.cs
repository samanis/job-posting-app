using Microsoft.Extensions.Options;
using Npgsql;

namespace JobPosting.Api.Persistence;

public sealed class PostingDatabaseOptionsValidator : IValidateOptions<PostingDatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, PostingDatabaseOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) errors.Add("PostingDatabase:ConnectionString is required.");
        else
        {
            try
            {
                var parsed = new NpgsqlConnectionStringBuilder(options.ConnectionString);
                if (string.IsNullOrWhiteSpace(parsed.Host) || string.IsNullOrWhiteSpace(parsed.Database) || string.IsNullOrWhiteSpace(parsed.Username))
                    errors.Add("PostingDatabase:ConnectionString must specify host, database and username.");
            }
            catch (ArgumentException) { errors.Add("PostingDatabase:ConnectionString is invalid."); }
        }
        if (options.CommandTimeoutSeconds is < 1 or > 30) errors.Add("PostingDatabase:CommandTimeoutSeconds must be between 1 and 30.");
        if (options.LockTimeoutMilliseconds is < 1 or > 5000) errors.Add("PostingDatabase:LockTimeoutMilliseconds must be between 1 and 5000.");
        if (options.IdleTransactionTimeoutSeconds is < 1 or > 30) errors.Add("PostingDatabase:IdleTransactionTimeoutSeconds must be between 1 and 30.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
