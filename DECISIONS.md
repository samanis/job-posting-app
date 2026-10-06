# Design decisions

This document explains my main design decisions and the reasons for them. It also lists what I would change in a real product. For setup instructions, see the [README](README.md).

## 1. Separate services for posting and searching

Each API has its own PostgreSQL database. When a job is saved, the posting API sends a `JobPostingCreated` message to RabbitMQ. The search API receives the message and saves the job in its own database.

The brief says that jobs are posted rarely but searched constantly. Keeping the two sides separate has three benefits:

- Each service can grow on its own. For example, the search database has indexes built only for searching.
- The services share nothing except the message format. Neither reads the other's database. Neither needs the other to be running.
- The two databases can move to separate servers without any code changes. Locally, Docker runs them on one PostgreSQL server to keep setup simple. There, heavy search traffic could still slow down posting. In production, I would give each database its own server.

The AI first suggested a simpler design: one database shared by both APIs. That design fits in four hours and would work at this scale. I chose separate databases and RabbitMQ because that is what I would build in production. The brief also allows search results to lag slightly behind new postings, which suits a message queue. The code that sends messages sits behind an interface. RabbitMQ can therefore be replaced without changing the posting logic.

I chose PostgreSQL because it is reliable and works well with EF Core. It also has the indexes the search API needs for fast "contains" searches.

## 2. The posting API returns 202 only after the job is saved and the message is delivered

When a job is posted, the API does three things in order:

1. It saves the job in the database.
2. It sends the message to RabbitMQ.
3. It waits until RabbitMQ confirms that it has stored the message.

Only then does the API return `202 Accepted` with the saved job. If sending fails, the API deletes the job it just saved and returns `503`. This undo step is called compensation. The user can then safely submit again (see section 3).

I chose this rule so that a `202` response has a clear meaning. It means the message is safe in RabbitMQ, so the job will reach search. The API's work ends there. It does not wait for the search API, so posting still works when search is down.

The AI first proposed a transactional outbox. With an outbox, the job and the message are saved together in the database. A background process sends the message to RabbitMQ later. The first plan also had a separate table for duplicate protection. When I reviewed the design, I decided it was over-engineered for this API. The API only serves one form, and it has no endpoint for reading jobs. Automatic retry and recovery were also out of scope. So I removed the outbox and the extra table, and used compensation instead.

Compensation has two weak spots:

- RabbitMQ may store the message, but its confirmation may get lost. The API then deletes the job, while the message stays in the queue. The API logs this case as a critical error.
- The application may crash after saving the job but before sending the message. The job then stays in the database, but search never receives it. Nothing is logged, because the application has stopped.

Also, nobody can post a job while RabbitMQ is down. The API returns `503`, and the user can try again later.

I would keep compensation in production. At a few postings per day, these failures are rare and easy to fix by hand. An outbox would add a background process to build, run and monitor. I would add two safeguards instead:

- An alert on the critical error from the first case.
- A scheduled check for jobs whose `PublishedAt` field is still empty after a few minutes. The API fills in `PublishedAt` only after RabbitMQ confirms the message. So this check finds jobs from the second case.

I would switch to an outbox in two situations. The first is if posting volume grew a lot. The second is if posting had to work while RabbitMQ is down.

## 3. Duplicate protection on the client and the server

Double-clicks, timeouts and retries are the most common causes of duplicate postings. To prevent them, every submission carries an idempotency key. This is a unique ID that the Angular app creates for each new job.

- The app sends the same key every time it retries the same submission.
- The app stores the key in the browser's session storage. Refreshing the page therefore does not start a new submission.
- The API saves the key in the same database row as the job.

When the API sees a key it has already completed, it returns the original response. It does not create a second job. When the same key arrives with different content, the API returns `409 Conflict`.

I kept this simple on purpose. There is no separate table for keys. The database allows each key only once. If two requests with the same key arrive at the same moment, one saves the job and the other is treated as a duplicate.

## 4. Validation on both sides

The brief asks for validation in the browser and on the server. Both check the same rules:

- Every field is required.
- The minimum salary must be less than the maximum.
- The closing date must be in the future.

**In the browser,** the form shows errors before anything is sent. It compares the closing date with the browser's own date.

**On the server,** the API rejects badly formed requests and checks every rule again. It compares the closing date with today's date in Toronto, the configured business time zone. If a rule fails, the API returns `422` with one error per field. The form then shows each error next to its field.

## 5. The two Angular apps

- **Modern Angular 22.** Both apps use standalone components and signals. The posting form uses Angular's new Signal Forms for its fields and validation.
- **Angular Material** gives both apps a consistent and accessible design.
- **Every API response is handled.** The posting app tells apart success, validation errors, conflicts, server failures and network errors. It shows a clear message for each one. After a successful post, it shows the saved job exactly as the API returned it.
- **Search settings in the URL.** The search app stores the filters and current page in the URL. Users can share links, and the browser's back button works. When a filter changes, the app cancels the old request and ignores late responses.

## 6. A search service built for heavy traffic

The search service handles many requests in these ways:

- **Fast paging.** Each page continues from the last job on the previous page. The database does not count through earlier rows, as it would with `OFFSET`. So page 100 is as fast as page 1.
- **Search indexes.** Title, description, department and location have indexes that make "contains" searches fast. Both sort orders also have their own index.
- **Short list results.** The job list leaves out descriptions. Only the job detail response includes the description.
- **Reliable message handling.** If the same message arrives twice, the job is saved only once. Broken messages move to a separate queue, so they do not block good ones.
- **No user sessions on the server.** Several copies of the API can run side by side behind a load balancer. Each copy keeps its own small cache in memory.
- **Response caching**, described below.

### Caching

A job never changes after it is saved, and the brief allows small delays. So responses are kept in a cache for a fixed time:

| Response | Kept in the server's cache | What browsers and CDNs are told |
|---|---|---|
| Job details | 1 hour | Keep it for one day, because the job never changes |
| First page of results | Up to 15 seconds, and never past midnight UTC | Same as the server |
| Later pages | Not cached | Do not keep it, so the page token is checked every time |
| Errors and "not found" | Not cached | Do not keep it, because a missing job may arrive soon |

A CDN (content delivery network) is a set of servers that keeps copies of pages close to users. A "later page" is any request that carries a page token, which is described in the next section.

The midnight rule matters because the search API closes a job at midnight UTC on its closing date. A cached page must never show a job that has already closed.

I measured the effect with a 30-second load test. 80% of requests asked for popular pages. The other 20% asked for varied searches and job details. With caching turned on:

- 99% of requests were answered from the cache, without touching the database.
- The median response time fell from 7.2 ms to 0.2 ms.
- The API handled about 7,760 requests per second instead of 470.

The full results are in the [performance report](API/job-search.api/docs/performance/README.md#cache-comparison-2026-10-06). Most of the gain comes from popular pages. A one-off search still goes to the database, where the query takes about 1 ms.

The cost of caching is a short delay. A new job can take up to 15 seconds to appear on the first page.

I also looked at Redis. Redis would let all copies of the search API share one cache. That helps when several copies run and new jobs must appear instantly. I have left this for later.

I also considered copying every job into Redis and keeping it in sync with the database. I rejected this idea for three reasons:

- Search results change even without new data, because jobs close at midnight.
- Redis cannot run these "contains" searches without extra tools.
- Keeping two data stores in sync is easy to get wrong.

### Page tokens

To get the next page, the app sends a page token (a cursor) that the API returned with the previous page. The token is signed, so users cannot change it. It also remembers which jobs existed when the user started browsing. Every page in one browsing session therefore comes from the same set of jobs.

This is more than the exercise needs. Simple paging by creation date and ID is already fast. It also never shows the same job twice. The extra features add only two things. New jobs cannot appear in the middle of a session. And users cannot tamper with the token. In a real product, I would start with simple paging. I would add these features only if users reported a problem. I kept them here because they work and are fully tested.

## 7. Reliability

Both APIs run in Docker, so they must behave well when something fails. I asked for the first three items below from the start:

- **Central logging and error handling.** Logs are written as structured JSON. Error responses never reveal internal details, such as stack traces or connection strings.
- **Circuit breaker.** If RabbitMQ keeps failing, the posting API stops calling it for a short time. During that time, it returns `503` immediately instead of waiting on every request.
- **Graceful shutdown.** When a container stops, each API first finishes the requests and messages it is already handling.
- **Health checks.** Each API reports whether it is running and whether its dependencies are ready. Docker and load balancers can then send traffic only to healthy copies.

## 8. Testing

- **Unit tests** cover all four projects, and 100% code coverage is required. I set this bar on purpose. It made me test every error case, not only the successful path.
- **Integration tests** run both APIs against real PostgreSQL and RabbitMQ containers. They are kept separate from the unit tests, so they cannot inflate the coverage numbers.
- **Browser tests** for both Angular apps use Playwright. They use fake API responses, so they test each app on its own.
- **A full end-to-end check** started from a fresh copy of the repository. It posted a job in one app, then found and opened it in the other.

## 9. Scope: this took much longer than four hours

I spent much longer than four hours on this exercise. I treated it as a real feature. I wanted to show how I handle failures, consistency and heavy load. The downside is that the solution is bigger than the problem needs. It also takes longer to review.

A four-hour version would look like this:

| Keep | Remove or simplify |
|---|---|
| Both Angular apps, with validation in the browser and on the server | 100% coverage; test only the validation and the API contract |
| Both APIs, EF Core migrations and one Docker Compose file | Signed page tokens; use simple paging instead |
| The RabbitMQ message (or one shared database) | The circuit breaker, shutdown handling and the broken-message queue |
| The idempotency key | The performance tools and the detailed per-stage documents |

## 10. Known gaps and next steps

- **Safeguards for compensation.** Add the alert and the scheduled check from section 2.
- **A CDN in front of the search API.** Later, add Redis as a shared cache once several copies of the API run (section 6).
- **One rule for when a job closes.** The posting form uses the browser's date. The posting API uses Toronto time. The search API uses UTC. Near midnight, they can disagree. For example, a job leaves search on its closing day instead of after it. All three should follow the same rule.
- **Separate database servers** for the two APIs in production (section 1).
- **Login and permissions** for posting jobs. These were out of scope.
- **Editing and closing jobs.** The search API would need to handle update messages.
- **A CI pipeline** that runs all four test suites and starts the Docker setup.

## 11. How I used AI

I used AI as a pair programmer. For each project, I first reviewed the requirements with it. I then worked through a series of small, numbered prompts. I built and tested each step before moving to the next one. I made the design decisions myself. The transcripts show where I changed the AI's suggestions:

- I chose separate databases and RabbitMQ instead of one shared database.
- I chose the strict 202 rule.
- I called the design over-engineered, and removed the outbox and the separate key table.
- I asked whether caching every job in Redis would help. I changed course when the measurements showed it would not.

The prompts are saved in each project. The transcripts are in the [`ai-log/`](ai-log) folder.
