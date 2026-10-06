using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearch.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateSequence(
                name: "job_ingestion_sequence");

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hash_version = table.Column<int>(type: "integer", nullable: false),
                    ingestion_sequence = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_occurred_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    source_created_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    salary_min = table.Column<decimal>(type: "numeric", nullable: false),
                    salary_max = table.Column<decimal>(type: "numeric", nullable: false),
                    closing_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_date", "closing_date BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
                    table.CheckConstraint("ck_jobs_hash", "payload_hash ~ '^[a-f0-9]{64}$' AND hash_version = 1");
                    table.CheckConstraint("ck_jobs_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND event_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_jobs_salary", "salary_min >= 0 AND salary_max <= 999999999.99 AND salary_min < salary_max AND scale(salary_min) <= 2 AND scale(salary_max) <= 2");
                    table.CheckConstraint("ck_jobs_sequence", "ingestion_sequence > 0");
                    table.CheckConstraint("ck_jobs_text", "length(btrim(title)) > 0 AND length(btrim(department)) > 0 AND length(btrim(location)) > 0 AND length(btrim(description)) > 0");
                    table.CheckConstraint("ck_jobs_ticks", "source_created_at_ticks BETWEEN 0 AND 3155378975999999999 AND source_occurred_at_ticks BETWEEN 0 AND 3155378975999999999");
                });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_closing",
                table: "jobs",
                columns: new[] { "closing_date", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_department_trgm",
                table: "jobs",
                column: "department")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_description_trgm",
                table: "jobs",
                column: "description")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_location_trgm",
                table: "jobs",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_newest",
                table: "jobs",
                columns: new[] { "created_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_title_trgm",
                table: "jobs",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ux_jobs_event_id",
                table: "jobs",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_jobs_ingestion_sequence",
                table: "jobs",
                column: "ingestion_sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropSequence(
                name: "job_ingestion_sequence");
        }
    }
}
