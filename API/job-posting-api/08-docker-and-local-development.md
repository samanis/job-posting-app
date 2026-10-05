# Stage 8: Docker and reproducible local setup

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; earlier numbered stages must be complete. Preserve unrelated work.

## Tasks

1. Add a multi-stage .NET 10 Dockerfile for posting, pinned verified stable image versions, non-root runtime user and exec-form entrypoint. Keep build context limited with an accurate .dockerignore that still includes required solution/project/migration files.
2. Create/extend API/job-posting-api/docker-compose.yml starting posting API, posting PostgreSQL and RabbitMQ management image, health checks and durable named volumes. Keep Dockerfiles, .dockerignore, environment examples and broker/database settings under API/job-posting-api. Use API/job-posting-api as the build context and working directory for Compose commands. Use loopback published ports for local services and stop_grace_period 45s. Do not create fake search service/database; describe this phase's subset. The user's backend-folder requirement overrides the PDF's repository-root Compose placement.
3. Include safe schema initialization through an explicit migration command or a one-shot migration service, sequenced before API readiness. Prevent every replica from racing migrations. Use service hostnames inside Docker and localhost in host-mode dotnet run configuration.
4. Supply safe local config defaults or committed .env.example, startup validation and environment overrides; ignore real secrets. Document PostgreSQL/RabbitMQ versions and credentials as local-only. No dependency on untracked machine files.
5. Document fresh-clone restore/build/test, docker compose up --build, migrations, local dotnet run, URLs/ports, POST examples with key, replay/conflict, broker outage recovery and docker compose stop. Do not use down -v in the ordinary stop path or delete user volumes.
6. Verify .gitignore/.dockerignore includes all build/run inputs. Update root README minimally to link API instructions and note the later search/client phase, preserving current app setup.

## Acceptance and checks

- Run the mandatory coverage gate: 100% lines, branches and methods for all authored production code, with source completeness and exclusions checked as required by shared-requirements.md. Keep unit/host coverage and external integration evidence accurately distinguished.

- Compose config validates and the posting subset starts from empty named volumes with migrations applied.
- Posting POST works through container networking, including confirmed publication and same-key replay.
- Restart retains jobs/idempotency/outbox/broker data; graceful stop respects configured budgets.
- A clean checkout contains Dockerfile, Compose, migrations, manifests/tool config and safe configuration examples.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.

