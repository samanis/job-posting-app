# Independent Docker and local development (Stage 8)

Normal docker-compose.yml contains search API, search PostgreSQL and a one-shot migration service only. Build context is this application directory: no posting folder, frontend, tests, development tools, generated output or secrets enter the image. Images are pinned by tag/digest to the verified baseline: SDK10.0.101, ASP.NET10.0.1, PostgreSQL18.6. Both API and migration targets run as APP_UID1654. Curl supplies a bounded DB-readiness health check. The curl apt package is resolved during build, so the whole resulting image is not claimed bit-for-bit reproducible solely from base digests. Build/restore uses existing NuGet locks and pinned EF tool10.0.4; runtime contains no SDK/tool restore.

## Existing broker and Docker startup

From API/job-search.api:

```powershell
Copy-Item .env.example .env
# Edit .env: random search DB password, actual existing broker credentials, network and DNS name.
$keyBytes=New-Object byte[] 32
$rng=[Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($keyBytes)
$rng.Dispose()
# Put this generated value into CURSOR_SIGNING_KEY in ignored .env, shared consistently across replicas.
[Convert]::ToBase64String($keyBytes)
docker compose --env-file .env config --quiet
docker compose --env-file .env up --build --detach job-search-api
```

Set real secrets yourself; .env.example does not supply a signing key or broker password and cannot start unchanged. Neither .env nor .env.* secrets are tracked. Required Docker/source/tool/lock/example files remain eligible for Git. Do not publish rendered Compose config: it resolves secrets. Use config --quiet for validation.

Local independent ports: Docker API http://localhost:5101, PostgreSQL localhost:5433; both configurable. Internal DB DNS is search-postgres:5432, database job_search/user jobsearch, its own durable search-data volume. It is never the posting DB/user/volume. The migration target waits for DB health; API waits for migration exit0. Failed migration prevents API startup. EF bundle is built with --configuration Release (EF's -c means context). The API does not apply migrations at startup.

Discover your existing broker's real container/network without needing any posting API process:

```powershell
docker ps --format '{{.Names}}'
docker inspect <existing-broker-container> --format '{{json .NetworkSettings.Networks}}'
docker exec <existing-broker-container> rabbitmq-diagnostics -q check_running
```

Observed local baseline was container job-posting-api-rabbitmq-1 on network job-posting-api_default with DNS alias rabbitmq. Set RABBITMQ_NETWORK/RABBITMQ_HOST to discovered values, and RABBITMQ_USER/PASSWORD/VHOST to actual externally managed broker configuration. No path into posting source/configuration is required by Compose or committed verification tools. Only search API joins that external network; its database/migrator stay on the search network. An absent external network fails visibly: search does not create it, start posting or create another broker. A configured but unreachable/authentication-failing broker yields safe ingestion Backoff/health503 while DB read readiness remains independent.

For a deliberately new shared network, provision it explicitly and attach the already-existing broker (substitute actual names):

```powershell
docker network create job-search-broker
docker network connect job-search-broker <existing-broker-container>
# Set RABBITMQ_NETWORK=job-search-broker and a discoverable broker alias/name.
```

No RabbitMQ service is in normal Compose; no posting depends_on exists. Ensure external broker readiness before enabling ingestion. Default durable exchange job-post-exchange/source queue job-post-queue/routing job-posting.created.v1 remain compatible. Search replicas compete on that same queue. Do not run a test reader against developer source messages. Broker management remains at its existing published UI (commonly localhost:15672), not a search-owned duplicate.

## Host mode

Provision the same separate search DB explicitly (with .env configured, docker compose up --detach postgres), then configure environment variables in this shell, independently from Compose:

```powershell
$env:SearchDatabase__ConnectionString='Host=127.0.0.1;Port=5433;Database=job_search;Username=jobsearch'
# Set PGPASSWORD to the search DB password, RabbitMq__UserName/Password to actual broker credentials.
# Set Cursor__Keys__current to the same generated signing key used in Docker.
$env:RabbitMq__HostName='127.0.0.1'
$env:RabbitMq__Port='5672'
# Use the broker's actual published port; Docker DNS rabbitmq does not resolve on the host.
dotnet tool restore
dotnet ef database update --project src/JobSearch.Api
dotnet run --project src/JobSearch.Api --launch-profile http
```

Host launch profile listens on localhost:5100 and exposes development /openapi/v1.json. Production Docker does not expose OpenAPI. Environment/secret configuration is explicit; Compose .env values are not automatically host .NET configuration. Configure Cursor__Keys__current externally for this host workflow as well; Production/Staging enforce it at startup. The known development-only fallback is described in search.md, but this setup uses the generated key. Run only one search consumer against real source messages unless competing consumption is intended.

Health endpoints: /health/live no dependencies, /health/ready DB/schema readiness, /health/ingestion consumer state. Startup ordering never requires a posting HTTP service. A bad broker is shown through ingestion health, not hidden by inventing a broker or pretending projections succeeded.

## Stop, update and persistence

```powershell
docker compose --env-file .env stop
# Resume, preserving search data:
docker compose --env-file .env up --detach job-search-api
# Remove containers/networks but preserve named data:
docker compose --env-file .env down
```

Do not add --volumes for normal stop/cleanup: that deletes search data. All services use45-second stop grace; host30-second shutdown includes24-second drain plus bounded cleanup. Forced termination may leave unacknowledged work for redelivery. Before a migration update, build updated images and rerun the migration service explicitly (e.g. stop API, docker compose up --build --force-recreate migrate, verify exit0, then start API). Preserve existing migration history; no automatic rollback/repair is supplied.

## Clean-checkout isolated verification

```powershell
./tools/Verify-Compose.ps1
```

The explicit compose.verify.yml overlay adds an owned fixture broker only for this verifier. Script-generated GUID project/network/topology names, ephemeral loopback ports, ignored random-secret env file and separate search volume prevent touching any developer resources. It validates Compose, builds from narrow context, starts isolated DB/broker, publishes a local producer-compatible persistent fixture before the consumer starts, gates migrations, checks list/detail identity and UID!=0, restarts its broker, then stops/resumes API and DB to prove durable job preservation. Finally it removes ONLY its generated project/volumes and labelled external fixture network and restores shell environment; build images/cache remain. A hard termination can bypass cleanup; inspect exact jobsearch-verify-* project identifiers rather than deleting broad Docker resources.

BrokerFixture --probe opens a bounded AMQP connection and does not declare, publish or consume. Publishing mode refuses any source exchange/queue without the verify- prefix. It uses local contract JSON, not posting classes/database. Fixtures never drain development job-post-queue. Actual verification also probed the already-running broker from host and from the built non-root runtime on its real network, with no topology or message mutation.

Verified on Linux amd64 Docker Desktop. Dockerfile handles linux-arm64 RID selection but arm64 was not executed. Required broker/network/credential provisioning and local volumes are explicit. Root full-stack Compose/Angular wiring remains a broader PDF submission gap and is not implemented by this API-only stage. Existing read performance numbers do not imply container performance or production capacity.

References: [Docker external networks](https://docs.docker.com/reference/compose-file/networks/), [Compose migration dependencies/stop grace](https://docs.docker.com/reference/compose-file/services/), [.NET non-root containers](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/containers).

