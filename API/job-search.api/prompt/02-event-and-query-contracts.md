# Independent contracts and validation

Read shared-requirements.md and requirements-review.md in this prompt directory first. Apply every shared constraint. Execute only this stage after previous stages have passed; preserve unrelated work.

## Tasks and acceptance

Define LOCAL version1 JobPostingCreated envelope, immutable projection input, summary/detail DTOs and query normalization/error contracts from shared requirements. Document exact JSON and AMQP metadata compatibility with a local complete wire fixture. Strict bounded UTF8 event reader validates envelope/job fields, decimal precision, real calendar dates, UUIDs and offset timestamps; expired jobs accepted. Specify policy for extra fields (ignore additive unknown fields within supported version; reject duplicates and type/case ambiguities) and missing optional AMQP metadata without inventing producer requirements. No payload/unknown field echoes. Define query duplicate/unknown/limit/text/sort/id validation and UTC available-date behavior. Avoid HTTP endpoints backed by fake data.

Acceptance: meaningful valid fixture/expired ingestion, malformed/oversized/unknown-version/metadata mismatch/decimal/date/header cases, canonical hashes stable across property order and process culture; existing proposed search-client alignment documented; coverage100%.

Run appropriate build/tests and mandatory exact 100% coverage gates. Write actual commands/results and remaining limitations in API/job-search.api/ai-log/job-search-api-work-notes.md. Do not proceed to the next stage automatically.
