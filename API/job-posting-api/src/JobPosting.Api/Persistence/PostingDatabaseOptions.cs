namespace JobPosting.Api.Persistence;

public sealed class PostingDatabaseOptions
{
    public const string SectionName = "PostingDatabase";
    // Nonsecret local defaults. Supply a password through external configuration/PGPASSWORD when running.
    public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Database=job_postings;Username=jobposting";
    public int CommandTimeoutSeconds { get; set; } = 10;
    public int LockTimeoutMilliseconds { get; set; } = 3000;
    public int IdleTransactionTimeoutSeconds { get; set; } = 15;
}
