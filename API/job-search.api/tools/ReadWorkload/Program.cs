using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using System.Text.Json;
using JobSearch.Api.Search;
using JobSearch.Api.Contracts;
using JobSearch.Api.Persistence;
using JobSearch.Api.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ReadWorkload;

if (!File.Exists("JobSearch.slnx")) throw new InvalidOperationException("Run from API/job-search.api.");
var seed = args.Length > 0 ? int.Parse(args[0]) : 10000;
var concurrency = args.Length > 1 ? int.Parse(args[1]) : 4;
var seconds = args.Length > 2 ? int.Parse(args[2]) : 10;
var cacheEnabled = args.Length < 4 || args[3] == "cached";
if (args.Length > 4 || (args.Length == 4 && args[3] is not ("cached" or "uncached")) || seed is < 1000 or > 100000 || concurrency is < 1 or > 32 || seconds is < 1 or > 60) throw new ArgumentException("Usage: seed1000..100000 concurrency1..32 duration1..60seconds [cached|uncached]");
var now = DateTimeOffset.UtcNow;
await using var owned = new OwnedPostgres(); await owned.InitializeAsync(); var options = await owned.NewDatabaseAsync(); await using var db = new SearchDbContext(options);
await using var connection = new NpgsqlConnection(db.Database.GetConnectionString()); await connection.OpenAsync();
await using (var command = new NpgsqlCommand("""
INSERT INTO jobs(id,event_id,payload_hash,hash_version,ingestion_sequence,occurred_at,created_at,source_occurred_at_ticks,source_created_at_ticks,title,department,location,description,salary_min,salary_max,closing_date)
SELECT md5('job-'||i)::uuid,md5('event-'||i)::uuid,repeat('a',64),1,i,@now,@now-(i%1000)*interval '1 minute',@ticks,@ticks-(i%1000)*600000000::bigint,
CASE WHEN i%97=0 THEN 'Specialist nebula' ELSE (ARRAY['Engineer','Analyst','Designer','Manager'])[1+i%4] END||' '||i,
(ARRAY['Engineering','Finance','Design','Operations'])[1+i%4],(ARRAY['Toronto','London','Berlin','Remote'])[1+i%4],
repeat('Representative varied description ',32)||CASE WHEN i%101=0 THEN ' rarequasar' ELSE ' common' END,10000+i,20000+i,@day+(i%30)-5
FROM generate_series(1,@seed) i;
SELECT setval('job_ingestion_sequence',@seed);
ANALYZE jobs;
""", connection)) { command.Parameters.AddWithValue("now", now); command.Parameters.AddWithValue("ticks", now.UtcTicks); command.Parameters.AddWithValue("day", DateOnly.FromDateTime(now.UtcDateTime)); command.Parameters.AddWithValue("seed", seed); await command.ExecuteNonQueryAsync(); }
await using (var maintenance = new NpgsqlCommand("VACUUM (ANALYZE) jobs", connection)) await maintenance.ExecuteNonQueryAsync();
var codec = new CursorCodec(Microsoft.Extensions.Options.Options.Create(new CursorOptions { Keys = new() { { "current", Convert.ToBase64String(new byte[32]) } } }));
var plans = new Dictionary<string, JsonElement>();
foreach (var item in new[] { ("newest", (string?)null), ("closing-soon", null), ("newest", "nebula"), ("closing-soon", "rarequasar"), ("newest", "Engineer") })
{
    var q = new SearchQuery(item.Item2, null, null, 20, item.Item1, null); var snapshot = codec.Start(q, now, seed);
    await using var command = JobReadStore.Query(db, q, snapshot, false).CreateDbCommand();
    command.Connection = connection; command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + command.CommandText;
    var raw = (string)(await command.ExecuteScalarAsync())!; using var json = JsonDocument.Parse(raw); plans[item.Item1 + ":" + (item.Item2 ?? "unfiltered")] = json.RootElement.Clone();
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" }); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { { "SearchDatabase:ConnectionString", db.Database.GetConnectionString() }, { "Cursor:Keys:current", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) } });
builder.Services.AddSingleton<TimeProvider>(new Clock(now)); builder.Services.AddSingleton<JobSearch.Api.Messaging.IngestionState>(); builder.Services.AddSearchPersistence(builder.Configuration); builder.Services.AddSearchReads(builder.Configuration, builder.Environment); builder.Services.AddSearchCaching(); builder.Services.AddControllers().AddApplicationPart(typeof(JobsController).Assembly); builder.Services.AddProblemDetails(); builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
var reads = new ReadCounter();
builder.Services.AddDbContextFactory<SearchDbContext>((_, optionsBuilder) => optionsBuilder.AddInterceptors(reads));
long cacheHits = 0;
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, meterListener) => { if (instrument.Meter.Name == SearchMetrics.MeterName && instrument.Name == "search.cache.hits") meterListener.EnableMeasurementEvents(instrument); };
listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref cacheHits, value));
listener.Start();
await using var app = builder.Build(); app.UseMiddleware<RequestDiagnosticsMiddleware>(); app.UseExceptionHandler(); app.UseRouting(); if (cacheEnabled) app.UseOutputCache(); app.MapControllers(); await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(); using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
var id = (await db.Jobs.Select(x => x.Id).FirstAsync()).ToString();
var paths = new[] { "/api/jobs?sort=newest", "/api/jobs?sort=closing-soon", "/api/jobs?q=nebula", "/api/jobs?q=rarequasar&sort=closing-soon", "/api/jobs?q=Engineer&department=Engineering&location=Toronto", "/api/jobs/" + id };
foreach (var path in paths) { using var response = await client.GetAsync(path); response.EnsureSuccessStatusCode(); }
var detailIds = await db.Jobs.OrderBy(x => x.IngestionSequence).Take(1000).Select(x => x.Id).ToArrayAsync();
Interlocked.Exchange(ref reads.Count, 0); Interlocked.Exchange(ref cacheHits, 0);
var sizes = new ConcurrentBag<int>(); var samples = new ConcurrentBag<double>(); var errors = new ConcurrentDictionary<string, int>(); long bytes = 0; var timer = Stopwatch.StartNew(); using var duration = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker => { var index = worker; while (!duration.IsCancellationRequested) { var started = Stopwatch.GetTimestamp(); var current = index++; var path = current % 5 != 0 ? paths[current % paths.Length] : current % 10 == 0 ? "/api/jobs?q=" + Uri.EscapeDataString("Engineer " + (current % seed)) : "/api/jobs/" + detailIds[current % detailIds.Length]; try { using var response = await client.GetAsync(path); var body = await response.Content.ReadAsByteArrayAsync(); sizes.Add(body.Length); Interlocked.Add(ref bytes, body.Length); if (!response.IsSuccessStatusCode) errors.AddOrUpdate("http_" + (int)response.StatusCode, 1, (_, n) => n + 1); } catch (Exception ex) { errors.AddOrUpdate(ex.GetType().Name, 1, (_, n) => n + 1); } samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds); } }));
timer.Stop(); await app.StopAsync(); var sorted = samples.Order().ToArray(); double Percentile(double p) => sorted[(int)Math.Ceiling(sorted.Length * p) - 1];
var report = new { capturedUtc = now, seed, concurrency, cacheEnabled, cacheHits, cacheHitRate = (double)cacheHits / sorted.Length, databaseReads = reads.Count, databaseReadsPerRequest = (double)reads.Count / sorted.Length, workload = "80% six popular routes; 10% numbered substring searches; 10% rotating details; synthetic, no concurrent ingestion", requestedSeconds = seconds, elapsedSeconds = timer.Elapsed.TotalSeconds, environment = new { os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, cpuCount = Environment.ProcessorCount, postgresImage = OwnedPostgres.Image, host = "in-process Kestrel loopback; owned Docker PostgreSQL; mixed warmed HTTP GETs" }, requests = sorted.Length, p50Ms = Percentile(.50), p95Ms = Percentile(.95), p99Ms = Percentile(.99), requestsPerSecond = sorted.Length / timer.Elapsed.TotalSeconds, responseBytes = bytes, maxResponseBytes = sizes.DefaultIfEmpty(0).Max(), errors, plans };
Directory.CreateDirectory("docs/performance"); var reportPath = "docs/performance/cache-" + (cacheEnabled ? "cached" : "uncached") + ".json"; await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { report.requests, report.cacheEnabled, report.cacheHits, report.cacheHitRate, report.databaseReads, report.databaseReadsPerRequest, report.p50Ms, report.p95Ms, report.p99Ms, report.requestsPerSecond, report.errors }));
sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
sealed class ReadCounter : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    public long Count;
    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Count);
        return ValueTask.FromResult(result);
    }
}
