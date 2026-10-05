using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPosting.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobRowIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fail atomically rather than invent idempotency/event identity for incomplete old rows.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM jobs j
                        LEFT JOIN idempotency_records i ON i.job_id = j.id
                        LEFT JOIN outbox_messages o ON o.job_id = j.id
                        WHERE i.job_id IS NULL OR o.job_id IS NULL OR i.event_id <> o.event_id)
                    THEN RAISE EXCEPTION 'Incomplete posting metadata; migration aborted'; END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<int>(
                name: "canonicalization_version",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "jobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key_digest",
                table: "jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_fingerprint",
                table: "jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "response_json",
                table: "jobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE jobs j SET
                    idempotency_key_digest = i.key_digest,
                    request_fingerprint = i.fingerprint,
                    canonicalization_version = i.canonicalization_version,
                    response_json = i.response_json,
                    event_id = i.event_id,
                    published_at = o.published_at
                FROM idempotency_records i JOIN outbox_messages o ON o.event_id = i.event_id
                WHERE i.job_id = j.id;
                ALTER TABLE jobs ALTER COLUMN canonicalization_version DROP DEFAULT,
                    ALTER COLUMN event_id DROP DEFAULT,
                    ALTER COLUMN idempotency_key_digest DROP DEFAULT,
                    ALTER COLUMN request_fingerprint DROP DEFAULT,
                    ALTER COLUMN response_json DROP DEFAULT;
                """);
            migrationBuilder.DropTable(name: "idempotency_records");
            migrationBuilder.DropTable(name: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_event_id",
                table: "jobs",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jobs_idempotency_key_digest",
                table: "jobs",
                column: "idempotency_key_digest",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_idempotency",
                table: "jobs",
                sql: "idempotency_key_digest ~ '^[a-f0-9]{64}$' AND request_fingerprint ~ '^[a-f0-9]{64}$' AND canonicalization_version > 0 AND event_id <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_publication",
                table: "jobs",
                sql: "published_at IS NULL OR (isfinite(published_at) AND published_at >= created_at)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_response",
                table: "jobs",
                sql: "(octet_length(response_json) <= 131072 AND json_typeof(response_json::json) = 'object' AND response_json::json->>'id' = id::text AND response_json::json->>'status' = 'accepted') IS TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Job-row migration cannot reconstruct removed publication envelopes. Restore a verified backup instead.");
    }
}
