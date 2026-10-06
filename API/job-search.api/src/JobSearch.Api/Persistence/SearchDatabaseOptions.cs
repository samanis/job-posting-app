namespace JobSearch.Api.Persistence;

public sealed class SearchDatabaseOptions
{
    public const string SectionName = "SearchDatabase";
    // Nonsecret local defaults. Supply a password through external configuration/PGPASSWORD when running.
    public string ConnectionString { get; set; } = "Host=localhost;Port=5433;Database=job_search;Username=jobsearch";
    public int CommandTimeoutSeconds { get; set; } = 10;
    public int LockTimeoutMilliseconds { get; set; } = 3000;
    public int IdleTransactionTimeoutSeconds { get; set; } = 15;
}
