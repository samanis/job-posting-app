using Microsoft.EntityFrameworkCore;
namespace JobSearch.Api.Persistence;

public class SearchDbContext(DbContextOptions<SearchDbContext> options) : DbContext(options)
{
    public DbSet<SearchJob> Jobs => Set<SearchJob>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasPostgresExtension("pg_trgm");
        model.HasSequence<long>("job_ingestion_sequence").StartsAt(1);
        var j = model.Entity<SearchJob>();
        j.ToTable("jobs", t =>
        {
            t.HasCheckConstraint("ck_jobs_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND event_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            t.HasCheckConstraint("ck_jobs_hash", "payload_hash ~ '^[a-f0-9]{64}$' AND hash_version = 1");
            t.HasCheckConstraint("ck_jobs_sequence", "ingestion_sequence > 0");
            t.HasCheckConstraint("ck_jobs_salary", "salary_min >= 0 AND salary_max <= 999999999.99 AND salary_min < salary_max AND scale(salary_min) <= 2 AND scale(salary_max) <= 2");
            t.HasCheckConstraint("ck_jobs_text", "length(btrim(title)) > 0 AND length(btrim(department)) > 0 AND length(btrim(location)) > 0 AND length(btrim(description)) > 0");
            t.HasCheckConstraint("ck_jobs_date", "closing_date BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
            t.HasCheckConstraint("ck_jobs_ticks", "source_created_at_ticks BETWEEN 0 AND 3155378975999999999 AND source_occurred_at_ticks BETWEEN 0 AND 3155378975999999999");
        });
        j.HasKey(x => x.Id).HasName("pk_jobs");
        j.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        j.Property(x => x.EventId).HasColumnName("event_id");
        j.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        j.Property(x => x.HashVersion).HasColumnName("hash_version");
        j.Property(x => x.IngestionSequence).HasColumnName("ingestion_sequence").ValueGeneratedNever();
        j.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        j.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        j.Property(x => x.SourceCreatedAtTicks).HasColumnName("source_created_at_ticks");
        j.Property(x => x.SourceOccurredAtTicks).HasColumnName("source_occurred_at_ticks");
        j.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
        j.Property(x => x.Department).HasColumnName("department").HasMaxLength(100);
        j.Property(x => x.Location).HasColumnName("location").HasMaxLength(100);
        j.Property(x => x.Description).HasColumnName("description").HasMaxLength(10000);
        // Unbounded numeric plus check avoids PostgreSQL numeric(p,2) silently rounding invalid writes.
        j.Property(x => x.SalaryMin).HasColumnName("salary_min").HasColumnType("numeric");
        j.Property(x => x.SalaryMax).HasColumnName("salary_max").HasColumnType("numeric");
        j.Property(x => x.ClosingDate).HasColumnName("closing_date").HasColumnType("date");
        j.HasIndex(x => x.EventId).IsUnique().HasDatabaseName("ux_jobs_event_id");
        j.HasIndex(x => x.IngestionSequence).IsUnique().HasDatabaseName("ux_jobs_ingestion_sequence");
        j.HasIndex(x => new { x.CreatedAt, x.Id }).IsDescending(true, true).HasDatabaseName("ix_jobs_newest");
        j.HasIndex(x => new { x.ClosingDate, x.CreatedAt, x.Id }).IsDescending(false, true, true).HasDatabaseName("ix_jobs_closing");
        j.HasIndex(x => x.Title).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_jobs_title_trgm");
        j.HasIndex(x => x.Description).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_jobs_description_trgm");
        j.HasIndex(x => x.Department).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_jobs_department_trgm");
        j.HasIndex(x => x.Location).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_jobs_location_trgm");
    }
    public virtual Task<List<SearchJob>> FindIdentitiesAsync(Guid id, Guid eventId, CancellationToken token) =>
        Jobs.AsNoTracking().Where(x => x.Id == id || x.EventId == eventId).ToListAsync(token);
    public virtual Task LockIngestionAsync(CancellationToken token) =>
        Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1785620787, 1)", token);
    public virtual Task<long> AllocateSequenceAsync(CancellationToken token) =>
        Database.SqlQueryRaw<long>("SELECT nextval('job_ingestion_sequence') AS \"Value\"").SingleAsync(token);
    public virtual async Task<long> ReadWatermarkAsync(CancellationToken token) =>
        await Jobs.Select(x => (long?)x.IngestionSequence).MaxAsync(token) ?? 0;
}
