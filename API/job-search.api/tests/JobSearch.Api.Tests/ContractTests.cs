using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobSearch.Api.Contracts;
using Microsoft.Extensions.Primitives;
namespace JobSearch.Api.Tests;
[Trait("Category", "Unit")]
public sealed class ContractTests
{
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/job-created-v1.json"));
    private static EventReadResult Read(string text, EventMetadata? metadata = null) => new EventReader().Read(Encoding.UTF8.GetBytes(text), 65536, metadata ?? new());
    [Fact]
    public void CompleteExpiredFixtureAndResponseContractsPreserveFields()
    {
        var e = Assert.IsType<JobCreatedEvent>(Read(Fixture).Event);
        Assert.Null(Read(Fixture).Error);
        var j=e.Job;
        Assert.Equal(new DateOnly(2026,1,2),j.ClosingDate);
        Assert.Equal("fixture-trace",e.CorrelationId);
        var metadata=new EventMetadata(e.EventId.ToString("D"),"JobPostingCreated",1,"application/json");
        Assert.NotNull(Read(Fixture,metadata).Event);
        var summary=new JobSummary(j.Id,j.CreatedAt,j.Title,j.Department,j.Location,j.SalaryMin,j.SalaryMax,j.ClosingDate);
        var detail=new JobDetail(j.Id,j.CreatedAt,j.Title,j.Department,j.Location,j.Description,j.SalaryMin,j.SalaryMax,j.ClosingDate);
        var page=new JobPage(Array.AsReadOnly(new[]{summary}),null);
        Assert.DoesNotContain("Description",JsonSerializer.Serialize(page));
        Assert.Contains("Build software",JsonSerializer.Serialize(detail));
        Assert.Null(page.NextCursor);Assert.Single(page.Items);
        Assert.Equal(1,EventFingerprint.Version);
        var hash=EventFingerprint.Compute(e);
        Assert.Matches("^[a-f0-9]{64}$",hash);
        var previous=CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");Assert.Equal(hash,EventFingerprint.Compute(e)); }
        finally { CultureInfo.CurrentCulture=previous; }
        var root=JsonNode.Parse(Fixture)!.AsObject();
        var reordered=new JsonObject();foreach(var kv in root.Reverse())reordered[kv.Key]=kv.Value!.DeepClone();
        reordered["unknown"]=true;reordered["job"]!["additive"]=123;
        Assert.Equal(hash,EventFingerprint.Compute(Read(reordered.ToJsonString()).Event!));
        Assert.Equal(hash,EventFingerprint.Compute(e with { CorrelationId="different" }));
        Assert.NotEqual(hash,EventFingerprint.Compute(e with { Job=j with { Title="Changed" } }));
        Assert.Equal(hash,EventFingerprint.Compute(Read(Fixture.Replace("100000.00","1e5")).Event!));
    }
    public static IEnumerable<object[]> InvalidEvents()
    {
        foreach(var text in new[]{"", "null", "[]", "{", Fixture.Replace("\"job\":{","\"job\":null,\"unused\":{")})yield return new object[]{text};
        foreach(var pair in new[]{("eventType","Other"),("eventId","bad"),("eventId","00000000-0000-0000-0000-000000000000"),("occurredAt","2026-01-01"),("occurredAt","2026-99-99T00:00:00Z"),("correlationId",""),("correlationId",new string('x',257))})
        {var n=JsonNode.Parse(Fixture)!;n[pair.Item1]=pair.Item2;yield return new object[]{n.ToJsonString()};}
        foreach(var pair in new[]{("title",""),("title",new string('x',201)),("department",new string('x',101)),("location",new string('x',101)),("description",new string('x',10001)),("closingDate","2026-02-30"),("closingDate","2026-1-02"),("createdAt",new string('x',41)),("id","bad")})
        {var n=JsonNode.Parse(Fixture)!;n["job"]![pair.Item1]=pair.Item2;yield return new object[]{n.ToJsonString()};}
        foreach(var token in new[]{"null","true","\"1\"","-1","150000","999999999999999999999999999999999","0.001","10.000","1e-2147483649"})yield return new object[]{Fixture.Replace("100000.00",token)};
        yield return new object[]{Fixture.Replace("\"eventId\":", "\"eventId\":\"duplicate\",\"eventId\":")};
        yield return new object[]{Fixture.Replace("\"title\":", "\"Title\":\"case\",\"title\":")};
        yield return new object[]{Fixture.Replace("\"title\":\"Engineer\",","")};
        yield return new object[]{Fixture.Replace("\"title\":\"Engineer\"","\"title\":123")};
        yield return new object[]{Fixture.Replace("\"title\":\"Engineer\"","\"title\":null")};
        yield return new object[]{Fixture.Replace("Engineer", @"\uD800")};
    }
    [Theory][MemberData(nameof(InvalidEvents))]
    public void InvalidEventsHaveSafeClassification(string body)
    {
        var result=Read(body);Assert.Null(result.Event);Assert.Equal("invalid_event",result.Error);
    }
    [Fact]
    public void MetadataBoundsVersionAndUtf8AreValidated()
    {
        foreach(var m in new[]{new EventMetadata(MessageId:"other"),new EventMetadata(Type:"other"),new EventMetadata(SchemaVersion:2),new EventMetadata(ContentType:"text/plain")})Assert.Equal("invalid_event",Read(Fixture,m).Error);
        Assert.Equal("unsupported_version",Read(Fixture.Replace("schemaVersion\":1","schemaVersion\":2")).Error);
        var reader=new EventReader();Assert.Equal("event_too_large",reader.Read(Encoding.UTF8.GetBytes(Fixture),1,new()).Error);
        Assert.Equal("invalid_event",reader.Read(new byte[]{255},1,new()).Error);
        Assert.NotNull(Read(Fixture.Replace("2026-01-01T00:00:00Z","2026-01-01T01:00:00.1234567+01:00")).Event);
        Assert.NotNull(Read(Fixture.Replace("100000.00","100000")).Event);
        Assert.NotNull(Read(Fixture.Replace("100000.00","10.000e1")).Event);
        Assert.Equal("invalid_event",Read(Fixture.Replace("150000.00","1000000000")).Error);
    }
    [Fact]
    public void QueryDefaultsNormalizationAndUtcAvailability()
    {
        var reader=new QueryReader(new Clock());
        var q=reader.Read([]).Query!;Assert.Equal(new SearchQuery(null,null,null,20,"newest",null),q);
        q=reader.Read(new Dictionary<string,StringValues>{{"q","  Engineer "},{"department"," Eng "},{"location"," Toronto "},{"limit","50"},{"sort","closing-soon"},{"cursor","opaque"}}).Query!;
        Assert.Equal("Engineer",q.Q);Assert.Equal("Eng",q.Department);Assert.Equal("Toronto",q.Location);Assert.Equal(50,q.Limit);Assert.Equal("closing-soon",q.Sort);Assert.Equal("opaque",q.Cursor);
        Assert.Null(reader.Read([]).Error);
        Assert.Equal(new DateOnly(2026,1,2),reader.TodayUtc());
        Assert.False(QueryReader.Available(new(2026,1,2),reader.TodayUtc()));Assert.True(QueryReader.Available(new(2026,1,3),reader.TodayUtc()));
        Assert.True(QueryReader.TryId("9d9c0c47-b7b6-4c5b-94b1-72102652fc61",out _));Assert.False(QueryReader.TryId("bad",out _));Assert.False(QueryReader.TryId(Guid.Empty.ToString(),out _));
    }
    [Theory]
    [InlineData("other","x")][InlineData("limit","0")][InlineData("limit","51")][InlineData("limit","1.5")][InlineData("sort","wrong")][InlineData("cursor","")][InlineData("cursor","a b")][InlineData("cursor","a\u0001")][InlineData("q","a\u0001")]
    public void InvalidQueries(string key,string value) => Assert.Equal("invalid_query",new QueryReader(TimeProvider.System).Read(new Dictionary<string,StringValues>{{key,value}}).Error);
    [Fact]
    public void QueryLengthsAndDuplicates()
    {
        var reader=new QueryReader(TimeProvider.System);
        foreach(var pair in new[]{("q",200),("department",100),("location",100),("cursor",2048)})
        {Assert.NotNull(reader.Read(new Dictionary<string,StringValues>{{pair.Item1,new string('x',pair.Item2)}}).Query);Assert.Null(reader.Read(new Dictionary<string,StringValues>{{pair.Item1,new string('x',pair.Item2+1)}}).Query);}
        Assert.Null(reader.Read(new Dictionary<string,StringValues>{{"q",new StringValues(new[]{"a","b"})}}).Query);
        Assert.Null(reader.Read(new[]{new KeyValuePair<string,StringValues>("q","a"),new KeyValuePair<string,StringValues>("q","b")}).Query);
        Assert.Null(reader.Read(new Dictionary<string,StringValues>{{"q",StringValues.Empty}}).Query);
        Assert.Null(reader.Read(new Dictionary<string,StringValues>{{"q"," "}}).Query!.Q);
    }
    private sealed class Clock:TimeProvider { public override DateTimeOffset GetUtcNow()=>new(2026,1,1,23,30,0,TimeSpan.FromHours(-2)); }
}
