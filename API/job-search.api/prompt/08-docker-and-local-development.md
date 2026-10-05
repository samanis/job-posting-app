# Independent Docker and existing-broker setup

Read shared-requirements.md and requirements-review.md in this prompt directory first. Apply every shared constraint. Execute only this stage after previous stages have passed; preserve unrelated work.

## Tasks and acceptance

Add application-local multistage non-root .NET10 API image, explicit migration target, narrow Docker context, pinned verified images, local .env.example with ignored real secrets, and Compose for ONLY search API/search PostgreSQL. No posting source/database dependency. Connect via configurable external broker network/hostname to the already deployed job-posting-api RabbitMQ; document discoverable network and credentials, loopback host-mode connection, startup ordering external readiness without posting service depends_on. Do not create a second source broker/queue accidentally or require the posting API process. Provide clear host/Docker commands and free independent API/PG ports. External network must be explicitly provisioned/configured; report missing broker/network instead of silently starting posting.

Provide isolated verification fixtures/profile/tools with own broker/search PG for clean checkout testing, not paths/imports into posting folder. Require configured HMAC cursor key. Durable PG volumes,45s stop, internal service DNS, explicit migration success gate. Normal stop preserves volumes; cleanup only disposable project identifiers created by verifier. Root full-stack Compose is deferred and documented as a PDF submission gap, not implemented here.

Acceptance: independent locked image build without posting folder in context; fresh search DB migration, local existing-broker connection, queued fixture-to-GET flow, non-root runtime, restart persistent jobs and consumer reconnect, ignored secrets/all required inputs not ignored; coverage100%.

Run appropriate build/tests and mandatory exact 100% coverage gates. Write actual commands/results and remaining limitations in API/job-search.api/ai-log/job-search-api-work-notes.md. Do not proceed to the next stage automatically.
