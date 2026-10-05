using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPosting.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    salary_min = table.Column<decimal>(type: "numeric", nullable: false),
                    salary_max = table.Column<decimal>(type: "numeric", nullable: false),
                    closing_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_created", "isfinite(created_at)");
                    table.CheckConstraint("ck_jobs_date", "closing_date BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
                    table.CheckConstraint("ck_jobs_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_jobs_salary", "salary_min >= 0 AND salary_max <= 999999999.99 AND salary_min < salary_max AND scale(salary_min) <= 2 AND scale(salary_max) <= 2");
                    table.CheckConstraint("ck_jobs_text", "title ~ '[^[:space:]]' AND department ~ '[^[:space:]]' AND location ~ '[^[:space:]]' AND description ~ '[^[:space:]]' AND char_length(description) <= 10000");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    envelope_json = table.Column<string>(type: "text", nullable: false),
                    lease_owner = table.Column<Guid>(type: "uuid", nullable: true),
                    claim_generation = table.Column<long>(type: "bigint", nullable: false),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.event_id);
                    table.UniqueConstraint("AK_outbox_messages_event_id_job_id", x => new { x.event_id, x.job_id });
                    table.CheckConstraint("ck_outbox_envelope", "(octet_length(envelope_json) <= 131072 AND json_typeof(envelope_json::json) = 'object' AND envelope_json::json->>'eventId' = event_id::text AND envelope_json::json->>'eventType' = event_type AND (envelope_json::json->>'schemaVersion')::integer = schema_version AND envelope_json::json->'job'->>'id' = job_id::text) IS TRUE");
                    table.CheckConstraint("ck_outbox_identity", "event_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_outbox_progress", "attempt_count >= 0 AND claim_generation >= 0 AND (lease_owner IS NULL) = (lease_expires_at IS NULL) AND (published_at IS NULL OR lease_owner IS NULL)");
                    table.CheckConstraint("ck_outbox_schema", "event_type = 'JobPostingCreated' AND schema_version > 0");
                    table.CheckConstraint("ck_outbox_times", "isfinite(occurred_at) AND isfinite(next_attempt_at) AND (lease_expires_at IS NULL OR isfinite(lease_expires_at)) AND (published_at IS NULL OR isfinite(published_at))");
                    table.ForeignKey(
                        name: "FK_outbox_messages_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    key_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    canonicalization_version = table.Column<int>(type: "integer", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    response_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => new { x.scope, x.key_digest });
                    table.CheckConstraint("ck_ledger_identity", "scope = 'create-job' AND key_digest ~ '^[a-f0-9]{64}$' AND fingerprint ~ '^[a-f0-9]{64}$' AND canonicalization_version > 0 AND isfinite(created_at)");
                    table.CheckConstraint("ck_ledger_response", "(octet_length(response_json) <= 131072 AND json_typeof(response_json::json) = 'object' AND response_json::json->>'id' = job_id::text AND response_json::json->>'status' = 'accepted') IS TRUE");
                    table.ForeignKey(
                        name: "FK_idempotency_records_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_idempotency_records_outbox_messages_event_id_job_id",
                        columns: x => new { x.event_id, x.job_id },
                        principalTable: "outbox_messages",
                        principalColumns: new[] { "event_id", "job_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_event_id",
                table: "idempotency_records",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_event_id_job_id",
                table: "idempotency_records",
                columns: new[] { "event_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_job_id",
                table: "idempotency_records",
                column: "job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_due_pending",
                table: "outbox_messages",
                columns: new[] { "next_attempt_at", "lease_expires_at" },
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_job_id",
                table: "outbox_messages",
                column: "job_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "jobs");
        }
    }
}
