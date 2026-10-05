# Requirements review and decisions

Source: Take-Home Exercise_ Job Board Mini-App (1).pdf, pages 1-3, read from the user's Downloads folder; user-provided architecture screenshot; user's current API-only request. This review is not an instruction to carry out the PDF submission actions.

PDF functional requirements: list available jobs, view full details, display postings created by App 1; eventual consistency acceptable. Technical requirements: .NET10 API per app, EF Core database choice with at least one migration, high-volume search/detail workload, unit tests, monorepo. Full submission also requests one root Compose for both APIs/database, local Angular22 apps, README, genuine AI transcripts, frequent commits and public repository submission.

User decisions override implementation scope: generate ONLY ordered prompts in API/job-search.api/prompt; independent search codebase/database; listen to the RabbitMQ already deployed with posting; own durable projection. No implementation, frontend edits, root Compose, posting modifications, commits or publication in this prompt-authoring task.

Design assumptions, not PDF mandates: independent PostgreSQL; row-level event/job deduplication; manual acknowledgments after commit; bounded consumer session reconnection; confirmed quarantine instead of modifying existing source queue; UTC availability; bounded substring filters/keyset cursors; simple immutable snapshot watermark; 100% coverage carried forward from the user's testing requirement. Details are fixed in shared-requirements.md to make the stages executable. These choices intentionally avoid a ledger/outbox/cache/search engine.

Existing search client's contract is proposed, not binding PDF text. GET routes, query fields, sorts, summary/detail shapes and UTC availability are adopted to reduce later integration work; strict UUID details and error/cursor semantics must be documented for future client tests. Existing client is not connected as part of these stages. Posting emits broker-confirmed events but may duplicate through redelivery or compensation/recreation. Deduplicate identical event/job identity; never collapse different jobs based on text.

Shared broker is an interoperability dependency, not a posting service dependency. Standalone search build/tests require no posting source. Docker-mode development can attach explicitly to the existing broker network, while standalone integration fixtures create their own isolated broker and search PG. Never share or read job_postings tables/credentials. The source queue is consumed competitively: test-only readers must not drain real development messages.

Deferred: full-stack root orchestration, Angular connection, authentication/authorization, generalized updates/deletes/event-version evolution, automatic replay/admin repair, broad monitoring backend and public submission/transcript export. Final verification must list these honestly rather than claim full PDF completion.
