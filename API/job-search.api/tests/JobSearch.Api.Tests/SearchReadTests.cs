using Microsoft.Extensions.Configuration;
using JobSearch.Api.Search;
using JobSearch.Api.Contracts;
using JobSearch.Api.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Security.Cryptography;
namespace JobSearch.Api.Tests;

[Trait("Category", "Unit")]
public sealed class SearchReadTests
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-05T23:59:59.999Z");
    public static CursorOptions Settings() => new() { Keys = new() { { "current", Convert.ToBase64String(new byte[32]) } } };
    public static CursorCodec Codec() => new(Options.Create(Settings()));
    private static SearchQuery Query(string? cursor = null) => new(null, null, null, 1, "newest", cursor);
    private static CursorPayload Payload() => Codec().Start(Query(), Now, 42) with { Last = new(Now.UtcTicks, Guid.Parse("00000000-0000-0000-0000-000000000001"), new DateOnly(2026, 10, 6).DayNumber) };
    [Fact]
    public void CursorsAuthenticateBindAndExpireAcrossMidnight()
    {
        var codec = Codec(); var payload = Payload(); var cursor = codec.Encode(payload);
        Assert.Equal(payload, codec.Decode(cursor, Query(), Now.AddSeconds(1)).Payload);
        Assert.Equal("cursor_expired", codec.Decode(cursor, Query(), Now.AddMinutes(15)).Error);
        Assert.Equal("cursor_expired", codec.Decode(codec.Encode(payload with { Version = 0 }), Query(), Now).Error);
        foreach (var query in new[] { Query() with { Q = "q" }, Query() with { Department = "d" }, Query() with { Location = "l" }, Query() with { Limit = 2 }, Query() with { Sort = "closing-soon" } }) Assert.Equal("invalid_cursor", codec.Decode(cursor, query, Now).Error);
        Assert.Equal("invalid_cursor", codec.Decode(cursor[..^2] + "AA", Query(), Now).Error);
        Assert.Equal("invalid_cursor", codec.Decode(codec.Encode(payload), Query(), Now.AddTicks(-1)).Error);
    }
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a.b.c")]
    [InlineData("=.A")]
    [InlineData("A..A")]
    [InlineData("AAA.A")]
    [InlineData(".AA")]
    [InlineData("AA.!?")]
    public void BadEncodingReturnsSafeInvalid(string cursor) => Assert.Equal("invalid_cursor", Codec().Decode(cursor, Query(), Now).Error);
    [Fact]
    public void CursorShapesAndAuthenticInvalidBoundsAreRejected()
    {
        var codec = Codec(); Assert.Equal("invalid_cursor", codec.Decode(Sign("{}").Replace("e30.", "e31."), Query(), Now).Error); Assert.Equal("invalid_cursor", codec.Decode(new string('x', 2049), Query(), Now).Error);
        foreach (var json in new[] { "[]", "{}", "null", "{broken", JsonSerializer.Serialize(Payload()).Replace("\"Last\":{", "\"Last\":{\"Unknown\":1,"), JsonSerializer.Serialize(Payload()).Replace("\"KeyId\":\"current\"", "\"KeyId\":null"), JsonSerializer.Serialize(Payload()).Replace("\"KeyId\":\"current\"", "\"KeyId\":2"), JsonSerializer.Serialize(Payload()).Replace("\"Version\":1", "\"Version\":1,\"Version\":1"), JsonSerializer.Serialize(Payload()).Replace("\"Day\":", "\"Day\":9999999999999999999999,\"x\":") }) Assert.Equal("invalid_cursor", codec.Decode(Sign(json), Query(), Now).Error);
        foreach (var payload in new[] { Payload() with { KeyId = "unknown" }, Payload() with { Binding = "wrong" }, Payload() with { Watermark = -1 }, Payload() with { Day = -1 }, Payload() with { Day = 3652059 }, Payload() with { Last = Payload().Last with { ClosingDay = -1 } }, Payload() with { Last = Payload().Last with { ClosingDay = 3652059 } }, Payload() with { Last = Payload().Last with { Id = Guid.Empty } }, Payload() with { Last = Payload().Last with { CreatedTicks = -1 } }, Payload() with { Last = Payload().Last with { CreatedTicks = long.MaxValue } }, Payload() with { IssuedTicks = -1 }, Payload() with { ExpiryTicks = Now.UtcTicks }, Payload() with { ExpiryTicks = Now.AddHours(2).UtcTicks }, Payload() with { IssuedTicks = 3155378975999999998, ExpiryTicks = long.MaxValue } }) Assert.Equal("invalid_cursor", codec.Decode(Sign(JsonSerializer.Serialize(payload)), Query(), payload.IssuedTicks > Now.UtcTicks ? DateTimeOffset.MaxValue : Now).Error);
    }
    private static string Sign(string text)
    { var body = System.Text.Encoding.UTF8.GetBytes(text); return Url(body) + "." + Url(HMACSHA256.HashData(new byte[32], body)); }
    private static string Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    [Fact]
    public void KeyConfigurationRejectsDefaultsAndSupportsRotation()
    {
        var validator = new CursorOptionsValidator(new Environment("Production")); Assert.True(validator.Validate(null, Settings()).Succeeded);
        var bad = new List<CursorOptions> { new(), new() { LifetimeMinutes = 0 }, new() { LifetimeMinutes = 61 }, new() { Keys = Enumerable.Range(0, 5).ToDictionary(x => x.ToString(), x => CursorOptions.DevelopmentKey) }, new() { ActiveKeyId = "missing", Keys = Settings().Keys } };
        foreach (var id in new[] { "", new string('a', 33), "bad!" }) bad.Add(new() { ActiveKeyId = id, Keys = new() { { id, CursorOptions.DevelopmentKey } } });
        foreach (var secret in new[] { Convert.ToBase64String(new byte[31]), CursorOptions.DevelopmentKey, "bad-base64" }) bad.Add(new() { Keys = new() { { "current", secret } } });
        foreach (var o in bad) Assert.True(validator.Validate(null, o).Failed);
        Assert.True(new CursorOptionsValidator(new Environment("Development")).Validate(null, new() { Keys = new() { { "current", CursorOptions.DevelopmentKey } } }).Succeeded);
        var settings = Settings(); settings.Keys["old"] = Convert.ToBase64String(new byte[33]); var codec = new CursorCodec(Options.Create(settings)); var old = Payload() with { KeyId = "old" }; Assert.Equal(old, codec.Decode(codec.Encode(old), Query(), Now).Payload);
        settings.Keys.Remove("old"); Assert.Equal("invalid_cursor", codec.Decode(Sign(JsonSerializer.Serialize(old)), Query(), Now).Error);
    }
    [Fact]
    public void RegistrationIsExternalIoFreeAndDevelopmentKeyIsExplicitlyScoped()
    {
        foreach (var environment in new[] { "Development", "Production" }) foreach (var configured in new[] { false, true })
            {
                var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection(); Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services); Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<TimeProvider>(services, TimeProvider.System); Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IHostEnvironment>(services, new Environment(environment));
                var values = new Dictionary<string, string?>(); if (configured || environment == "Production") values["Cursor:Keys:current"] = Settings().Keys["current"];
                var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(values).Build(); SearchReads.AddSearchReads(services, configuration, new Environment(environment));
                using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
                var options = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IOptions<CursorOptions>>(provider).Value; Assert.Single(options.Keys); Assert.NotNull(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<CursorCodec>(provider)); Assert.NotNull(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<QueryReader>(provider));
                var json = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(provider).Value.JsonSerializerOptions;
                var wire = JsonSerializer.Serialize(Now.AddTicks(1234).ToOffset(TimeSpan.FromHours(3)), json); Assert.Equal("\"2026-10-05T23:59:59.999Z\"", wire); Assert.Equal(Now, JsonSerializer.Deserialize<DateTimeOffset>(wire, json));
            }
    }
    [Fact]
    public async Task ControllerBoundsPagesAndReturnsSafeErrorsWithoutFallback()
    {
        var store = new Store(); var clock = new Clock(); var codec = Codec(); var controller = Controller(store, clock);
        store.Rows = [Row(2), Row(1)]; var page = Assert.IsType<JobPage>(Assert.IsType<OkObjectResult>(await controller.List(default)).Value); Assert.Single(page.Items); Assert.NotNull(page.NextCursor); Assert.Equal(1, clock.Calls); Assert.Equal(1, store.Watermarks);
        controller.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { { "limit", "1" }, { "cursor", page.NextCursor } }); store.Rows = []; page = Assert.IsType<JobPage>(Assert.IsType<OkObjectResult>(await controller.List(default)).Value); Assert.Empty(page.Items); Assert.Null(page.NextCursor); Assert.True(store.Continuation); Assert.Equal(1, store.Watermarks);
        controller.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { { "unknown", "x" } }); Assert.Equal(400, Assert.IsType<ObjectResult>(await controller.List(default)).StatusCode);
        controller.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { { "limit", "1" }, { "cursor", "bad" } }); Assert.Equal(400, Assert.IsType<ObjectResult>(await controller.List(default)).StatusCode);
        controller.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { { "limit", "1" }, { "cursor", codec.Encode(Payload() with { Version = 0 }) } }); Assert.Equal(409, Assert.IsType<ObjectResult>(await controller.List(default)).StatusCode);
        controller.Request.Query = new QueryCollection(new Dictionary<string, StringValues> { { "limit", "1" } });
        foreach (var error in new Exception[] { new Npgsql.NpgsqlException("secret"), new TimeoutException("secret"), new InvalidOperationException("secret", new Npgsql.NpgsqlException("secret")) }) { store.Error = error; Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.List(default)).StatusCode); Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Detail(Row(1).Id.ToString("D"), default)).StatusCode); }
        store.Error = null; Assert.Equal(400, Assert.IsType<ObjectResult>(await controller.Detail("bad", default)).StatusCode); Assert.Equal(404, Assert.IsType<ObjectResult>(await controller.Detail(Row(1).Id.ToString("D"), default)).StatusCode);
        store.Detail = new(Row(1).Id, Now, "t", "d", "l", "full description", 1, 2, new(2026, 10, 4)); Assert.IsType<OkObjectResult>(await controller.Detail(store.Detail.Id.ToString("D"), default));
        store.Error = new InvalidOperationException("secret"); await Assert.ThrowsAsync<InvalidOperationException>(() => controller.List(default)); await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Detail(Row(1).Id.ToString("D"), default));
    }
    public static ReadRow Row(int id) => new(Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}"), Now.UtcTicks, Now, "title", "department", "location", 1, 2, new(2026, 10, 6));
    private static JobsController Controller(Store store, Clock clock) => new(new(clock), Codec(), store, clock, NullLogger<JobsController>.Instance) { ControllerContext = new() { HttpContext = new DefaultHttpContext { Request = { Query = new QueryCollection(new Dictionary<string, StringValues> { { "limit", "1" } }) } } } };
    public sealed class Store : IJobReadStore
    {
        public List<ReadRow> Rows = []; public JobDetail? Detail; public Exception? Error; public int Watermarks; public bool Continuation;
        public Task<long> WatermarkAsync(CancellationToken token) { Watermarks++; return Error is null ? Task.FromResult(42L) : Task.FromException<long>(Error); }
        public Task<List<ReadRow>> ListAsync(SearchQuery q, CursorPayload p, bool continuation, CancellationToken token) { Continuation = continuation; return Error is null ? Task.FromResult(Rows) : Task.FromException<List<ReadRow>>(Error); }
        public Task<JobDetail?> DetailAsync(Guid id, CancellationToken token) => Error is null ? Task.FromResult(Detail) : Task.FromException<JobDetail?>(Error);
    }
    private sealed class Clock : TimeProvider { public int Calls; public override DateTimeOffset GetUtcNow() { Calls++; return Now; } }
    private sealed class Environment(string name) : IHostEnvironment { public string EnvironmentName { get; set; } = name; public string ApplicationName { get; set; } = "test"; public string ContentRootPath { get; set; } = "."; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}
