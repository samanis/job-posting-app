using Microsoft.EntityFrameworkCore;

namespace JobPosting.Api.Persistence;

public class PostingDbContext(DbContextOptions<PostingDbContext> options) : DbContext(options)
{
    public virtual DbSet<JobPostingEntity> Jobs => Set<JobPostingEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var jobs = model.Entity<JobPostingEntity>();
        jobs.ToTable("jobs", table =>
        {
            table.HasCheckConstraint("ck_jobs_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_jobs_text", "title ~ '[^[:space:]]' AND department ~ '[^[:space:]]' AND location ~ '[^[:space:]]' AND description ~ '[^[:space:]]' AND char_length(description) <= 10000");
            // No numeric typmod: checks see the original scale instead of an already-rounded value.
            table.HasCheckConstraint("ck_jobs_salary", "salary_min >= 0 AND salary_max <= 999999999.99 AND salary_min < salary_max AND scale(salary_min) <= 2 AND scale(salary_max) <= 2");
            table.HasCheckConstraint("ck_jobs_date", "closing_date BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
            table.HasCheckConstraint("ck_jobs_created", "isfinite(created_at)");
            table.HasCheckConstraint("ck_jobs_idempotency", "idempotency_key_digest ~ '^[a-f0-9]{64}$' AND request_fingerprint ~ '^[a-f0-9]{64}$' AND canonicalization_version > 0 AND event_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_jobs_publication", "published_at IS NULL OR (isfinite(published_at) AND published_at >= created_at)");
            table.HasCheckConstraint("ck_jobs_response", "(octet_length(response_json) <= 131072 AND json_typeof(response_json::json) = 'object' AND response_json::json->>'id' = id::text AND response_json::json->>'status' = 'accepted') IS TRUE");
        });
        jobs.HasKey(row => row.Id);
        jobs.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        jobs.Property(row => row.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        jobs.Property(row => row.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        jobs.Property(row => row.Department).HasColumnName("department").HasMaxLength(100).IsRequired();
        jobs.Property(row => row.Location).HasColumnName("location").HasMaxLength(100).IsRequired();
        jobs.Property(row => row.Description).HasColumnName("description").HasColumnType("text").IsRequired();
        jobs.Property(row => row.SalaryMin).HasColumnName("salary_min").HasColumnType("numeric");
        jobs.Property(row => row.SalaryMax).HasColumnName("salary_max").HasColumnType("numeric");
        jobs.Property(row => row.ClosingDate).HasColumnName("closing_date").HasColumnType("date");

        jobs.Property(row => row.IdempotencyKeyDigest).HasColumnName("idempotency_key_digest").HasMaxLength(64).IsRequired();
        jobs.HasIndex(row => row.IdempotencyKeyDigest).IsUnique();
        jobs.Property(row => row.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(64).IsRequired();
        jobs.Property(row => row.CanonicalizationVersion).HasColumnName("canonicalization_version");
        jobs.Property(row => row.ResponseJson).HasColumnName("response_json").HasColumnType("text").IsRequired();
        jobs.Property(row => row.EventId).HasColumnName("event_id");
        jobs.HasIndex(row => row.EventId).IsUnique();
        jobs.Property(row => row.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone");
    }

    public virtual Task<JobPostingEntity?> FindByDigestAsync(string digest, CancellationToken cancellationToken)
        => Jobs.SingleOrDefaultAsync(row => row.IdempotencyKeyDigest == digest, cancellationToken);
}
