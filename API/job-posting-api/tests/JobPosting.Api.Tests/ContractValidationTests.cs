using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobPosting.Api.Contracts;
using JobPosting.Api.Validation;

namespace JobPosting.Api.Tests;

[Trait("Category", "Unit")]
public sealed class ContractValidationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ValidJson = """
        {"title":"Engineer","department":"Engineering","location":"Toronto","description":"Build software","salaryMin":10,"salaryMax":20,"closingDate":"2028-02-29"}
        """;

    [Fact]
    public void ReaderPreservesNullableSalariesAndStrictlyTypedFields()
    {
        var result = new CreateJobRequestReader().Read(ValidJson);
        Assert.Null(result.ErrorStatus);
        Assert.Empty(result.Errors);
        var request = Assert.IsType<CreateJobRequest>(result.Request);
        Assert.Equal("Engineer", request.Title);
        Assert.Equal("Engineering", request.Department);
        Assert.Equal("Toronto", request.Location);
        Assert.Equal("Build software", request.Description);
        Assert.Equal(10m, request.SalaryMin);
        Assert.Equal(20m, request.SalaryMax);
        Assert.Equal(new DateOnly(2028, 2, 29), request.ClosingDate);
        Assert.Equal("Engineer", JsonNode.Parse(JsonSerializer.Serialize(request, JsonOptions))!["title"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("10")]
    [InlineData("true")]
    [InlineData("\"private-value\"")]
    [InlineData("{\"salaryMin\":NaN}")]
    [InlineData("{\"salaryMin\":Infinity}")]
    [InlineData("{\"salaryMin\":-Infinity}")]
    [InlineData("{\"title\":\"private-value\",}")]
    [InlineData("{\"title\":\"\\uD800\"}")]
    [InlineData("{\"title\":\"\\uDC00\"}")]
    [InlineData("{\"\\uD800\":\"private-value\"}")]
    public void InvalidJsonOrRootShapeIsSafeBadRequest(string json)
    {
        var result = new CreateJobRequestReader().Read(json);
        Assert.Equal(400, result.ErrorStatus);
        Assert.Null(result.Request);
        Assert.NotEmpty(result.Errors);
        Assert.DoesNotContain("private-value", JsonSerializer.Serialize(result.Errors));
    }

    [Theory]
    [InlineData("title", "1")]
    [InlineData("department", "true")]
    [InlineData("location", "{}")]
    [InlineData("description", "[]")]
    [InlineData("salaryMin", "\"10\"")]
    [InlineData("salaryMax", "false")]
    [InlineData("salaryMin", "[]")]
    [InlineData("salaryMax", "{}")]
    [InlineData("salaryMin", "1e100")]
    [InlineData("salaryMax", "79228162514264337593543950336")]
    [InlineData("closingDate", "20280229")]
    [InlineData("closingDate", "true")]
    public void WrongJsonTypesAndNumericOverflowAre400(string field, string token)
    {
        var result = ReadReplacing(field, token);
        Assert.Equal(400, result.ErrorStatus);
        Assert.Null(result.Request);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Theory]
    [InlineData("2027-02-29")]
    [InlineData("2028-02-30")]
    [InlineData("2028-13-01")]
    [InlineData("2028-00-01")]
    [InlineData("0000-01-01")]
    [InlineData("2028-01-00")]
    [InlineData("2028-2-29")]
    [InlineData("2028-02-29T00:00:00Z")]
    [InlineData(" 2028-02-29 ")]
    [InlineData("02/29/2028")]
    [InlineData("")]
    public void InvalidCalendarDatesAre400(string value)
    {
        var result = ReadReplacing("closingDate", JsonSerializer.Serialize(value));
        Assert.Equal(400, result.ErrorStatus);
        Assert.Contains("closingDate", result.Errors.Keys);
    }

    [Fact]
    public void DuplicateUnknownAndCaseVariantPropertiesAreRejectedWithoutEchoingUnknownNames()
    {
        var reader = new CreateJobRequestReader();
        var duplicate = reader.Read(ValidJson.Replace("\"title\":\"Engineer\"", "\"title\":\"One\",\"title\":\"Two\"", StringComparison.Ordinal));
        Assert.Equal(400, duplicate.ErrorStatus);
        Assert.Contains("title", duplicate.Errors.Keys);
        foreach (var name in new[] { "private-unknown-name", "Title" })
        {
            var node = JsonNode.Parse(ValidJson)!;
            node[name] = "private-unknown-value";
            var result = reader.Read(node.ToJsonString());
            Assert.Equal(400, result.ErrorStatus);
            Assert.Contains("$", result.Errors.Keys);
            Assert.DoesNotContain("private-unknown", JsonSerializer.Serialize(result.Errors));
        }
    }

    [Theory]
    [InlineData("title")]
    [InlineData("department")]
    [InlineData("location")]
    [InlineData("description")]
    [InlineData("salaryMin")]
    [InlineData("salaryMax")]
    [InlineData("closingDate")]
    public void MissingAndNullFieldsAreRequiredWithoutReplacingSalariesWithZero(string field)
    {
        foreach (var remove in new[] { true, false })
        {
            var node = JsonNode.Parse(ValidJson)!.AsObject();
            if (remove) node.Remove(field); else node[field] = null;
            var read = new CreateJobRequestReader().Read(node.ToJsonString());
            Assert.Null(read.ErrorStatus);
            var validation = new JobRequestValidator().Normalize(Assert.IsType<CreateJobRequest>(read.Request));
            Assert.False(validation.IsValid);
            Assert.Null(validation.Request);
            Assert.Contains(field, validation.Errors.Keys);
        }
    }

    [Theory]
    [InlineData("title", 200)]
    [InlineData("department", 100)]
    [InlineData("location", 100)]
    [InlineData("description", 10000)]
    public void TrimmedTextLengthLimitsAndWhitespaceAreEnforced(string field, int limit)
    {
        foreach (var text in new[] { "", " \r\n\t ", new string('a', limit + 1) })
        {
            var result = NormalizeReplacing(field, JsonSerializer.Serialize(text));
            Assert.False(result.IsValid);
            Assert.Contains(field, result.Errors.Keys);
        }
        var maximum = NormalizeReplacing(field, JsonSerializer.Serialize(" \t" + new string('a', limit) + "\r\n "));
        Assert.True(maximum.IsValid);
        var serialized = JsonNode.Parse(JsonSerializer.Serialize(maximum.Request, JsonOptions))!;
        Assert.Equal(new string('a', limit), serialized[field]!.GetValue<string>());
    }

    [Fact]
    public void NormalizationTrimsOnlyEdgesAndPreservesPlainTextContent()
    {
        var node = JsonNode.Parse(ValidJson)!;
        node["title"] = "  Engineer  ";
        node["department"] = " Engineering ";
        node["location"] = " Toronto\t";
        node["description"] = " \r\n<script>plain text</script>\r\n  Keep internal spaces.\t ";
        var result = NormalizeJson(node.ToJsonString());
        var request = Assert.IsType<NormalizedJobRequest>(result.Request);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal("Engineer", request.Title);
        Assert.Equal("Engineering", request.Department);
        Assert.Equal("Toronto", request.Location);
        Assert.Equal("<script>plain text</script>\r\n  Keep internal spaces.", request.Description);
        Assert.Equal(10m, request.SalaryMin);
        Assert.Equal(20m, request.SalaryMax);
        Assert.Equal(new DateOnly(2028, 2, 29), request.ClosingDate);
    }

    [Theory]
    [InlineData("salaryMin", "-0.01")]
    [InlineData("salaryMax", "-0.01")]
    [InlineData("salaryMin", "1000000000")]
    [InlineData("salaryMax", "1000000000")]
    public void AmountBoundsAreSemanticErrors(string field, string token)
    {
        var result = NormalizeReplacing(field, token);
        Assert.False(result.IsValid);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public void ZeroAndMaximumAmountsAreAcceptedWithoutFloatingPointArithmetic()
    {
        var node = JsonNode.Parse(ValidJson)!;
        node["salaryMin"] = 0m;
        node["salaryMax"] = JobRequestValidator.MaximumSalary;
        var result = NormalizeJson(node.ToJsonString());
        Assert.True(result.IsValid);
        Assert.Equal(0m, result.Request!.SalaryMin);
        Assert.Equal(999999999.99m, result.Request.SalaryMax);
        node["salaryMin"] = 999999999.98m;
        Assert.True(NormalizeJson(node.ToJsonString()).IsValid);
    }

    [Theory]
    [InlineData("10.001")]
    [InlineData("10.000")]
    [InlineData("0.000000000000000000000000000001")]
    [InlineData("10.000000000000000000000000000001")]
    [InlineData("1e-3")]
    [InlineData("1e-9223372036854775808")]
    [InlineData("1e-999999999999999999999999999999")]
    public void RawPrecisionCannotBeHiddenByDecimalRoundingOrUnderflow(string token)
    {
        var result = ReadReplacing("salaryMin", token);
        Assert.Equal(422, result.ErrorStatus);
        Assert.Null(result.Request);
        Assert.Contains("salaryMin", result.Errors.Keys);
    }

    [Theory]
    [InlineData("1e1")]
    [InlineData("1E+1")]
    [InlineData("1000e-2")]
    [InlineData("10.000e1")]
    public void ExactExponentNumbersAreSupported(string token)
    {
        var read = ReadReplacing("salaryMin", token);
        Assert.Null(read.ErrorStatus);
        // 10.000e1 = 100; choose a larger maximum for semantic validation.
        var request = read.Request!;
        var normalized = new JobRequestValidator().Normalize(new CreateJobRequest
        {
            Title = request.Title, Department = request.Department, Location = request.Location,
            Description = request.Description, SalaryMin = request.SalaryMin, SalaryMax = 200m, ClosingDate = request.ClosingDate
        });
        Assert.True(normalized.IsValid);
    }

    [Theory]
    [InlineData("0e999999999999999999999999999999")]
    [InlineData("0e9223372036854775807")]
    public void ExtremePositiveExponentCannotChangeZero(string token)
    {
        var result = ReadReplacing("salaryMin", token);
        Assert.Null(result.ErrorStatus);
        Assert.Equal(0m, result.Request!.SalaryMin);
    }

    [Fact]
    public void DirectTypedValidationAlsoRejectsExcessDecimalScale()
    {
        var valid = new CreateJobRequestReader().Read(ValidJson).Request!;
        var result = new JobRequestValidator().Normalize(new CreateJobRequest
        {
            Title = valid.Title, Department = valid.Department, Location = valid.Location, Description = valid.Description,
            SalaryMin = 10.001m, SalaryMax = 20.001m, ClosingDate = valid.ClosingDate
        });
        Assert.False(result.IsValid);
        Assert.Contains("salaryMin", result.Errors.Keys);
        Assert.Contains("salaryMax", result.Errors.Keys);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    public void EqualAndReversedSalaryBoundsAreRejected(int minimum)
    {
        var result = NormalizeReplacing("salaryMin", minimum.ToString(CultureInfo.InvariantCulture));
        Assert.False(result.IsValid);
        Assert.Contains("salaryMax", result.Errors.Keys);
    }

    [Fact]
    public void EntirelyMissingRequestReportsAllSevenRequiredFields()
    {
        var result = NormalizeJson("{}");
        Assert.False(result.IsValid);
        Assert.Equal(7, result.Errors.Count);
    }

    [Fact]
    public void FingerprintIsVersionedFixedOrderAndCultureIndependent()
    {
        var request = NormalizeJson(ValidJson).Request!;
        Assert.Equal(1, JobRequestFingerprint.Version);
        Assert.Equal("[1,\"Engineer\",\"Engineering\",\"Toronto\",\"Build software\",\"10.00\",\"20.00\",\"2028-02-29\"]",
            Encoding.UTF8.GetString(JobRequestFingerprint.Canonicalize(request)));
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var expected = JobRequestFingerprint.Compute(request);
            Assert.Matches("^[a-f0-9]{64}$", expected);
            foreach (var culture in new[] { "en-US", "fr-FR", "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                Assert.Equal(expected, JobRequestFingerprint.Compute(request));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    [InlineData("title", "\"Other\"")]
    [InlineData("department", "\"Other\"")]
    [InlineData("location", "\"Other\"")]
    [InlineData("description", "\"Other\"")]
    [InlineData("salaryMin", "10.01")]
    [InlineData("salaryMax", "20.01")]
    [InlineData("closingDate", "\"2028-03-01\"")]
    public void EveryPayloadFieldParticipatesInFingerprint(string field, string token)
    {
        var original = JobRequestFingerprint.Compute(NormalizeJson(ValidJson).Request!);
        var changed = JobRequestFingerprint.Compute(NormalizeReplacing(field, token).Request!);
        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void EquivalentNumbersTrimmedTextAndPropertyOrderHaveEqualFingerprints()
    {
        var original = JobRequestFingerprint.Compute(NormalizeJson(ValidJson).Request!);
        const string equivalent = """
            {"closingDate":"2028-02-29","salaryMax":20.00,"salaryMin":1e1,"description":" Build software ","location":" Toronto ","department":" Engineering ","title":" Engineer "}
            """;
        Assert.Equal(original, JobRequestFingerprint.Compute(NormalizeJson(equivalent).Request!));
        Assert.Equal(original, JobRequestFingerprint.Compute(NormalizeJson(ValidJson.Replace("\"salaryMin\":10", "\"salaryMin\":10.00", StringComparison.Ordinal)).Request!));
    }

    [Fact]
    public void DelimitersEscapesAndUnicodeRemainUnambiguousPlainText()
    {
        var first = NormalizeReplacing("description", JsonSerializer.Serialize("\"],\"x\"\n雪")).Request!;
        using var canonical = JsonDocument.Parse(JobRequestFingerprint.Canonicalize(first));
        Assert.Equal(first.Description, canonical.RootElement[4].GetString());
        Assert.NotEqual(JobRequestFingerprint.Compute(first), JobRequestFingerprint.Compute(NormalizeReplacing("description", "\"x\"").Request!));
    }

    [Theory]
    [InlineData("2028-02-29T04:59:59Z", "2028-02-29", true)]
    [InlineData("2028-02-29T05:00:00Z", "2028-02-29", false)]
    [InlineData("2028-03-01T04:59:59Z", "2028-03-01", true)]
    [InlineData("2028-03-01T05:00:00Z", "2028-03-01", false)]
    [InlineData("2028-07-01T03:59:59Z", "2028-07-01", true)]
    [InlineData("2028-07-01T04:00:00Z", "2028-07-01", false)]
    [InlineData("2028-03-01T05:00:00Z", "2028-02-29", false)]
    public void FutureDateUsesBusinessTimezoneAcrossMidnightLeapDayAndDaylightSaving(string instant, string closingDate, bool valid)
    {
        var request = NormalizeReplacing("closingDate", JsonSerializer.Serialize(closingDate)).Request!;
        var clock = new FixedClock(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));
        var validator = new NewJobTemporalValidator(clock, TimeZoneInfo.FindSystemTimeZoneById("America/Toronto"));
        var errors = validator.ValidateNew(request);
        Assert.Equal(valid, errors.Count == 0);
        if (!valid) Assert.Contains("closingDate", errors.Keys);
    }

    [Fact]
    public void OldPayloadCanStillNormalizeAndFingerprintBeforeNewDateValidation()
    {
        var request = NormalizeJson(ValidJson).Request!;
        var before = JobRequestFingerprint.Compute(request);
        var clock = new FixedClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.NotEmpty(new NewJobTemporalValidator(clock, TimeZoneInfo.Utc).ValidateNew(request));
        var replay = NormalizeJson(ValidJson);
        Assert.True(replay.IsValid);
        Assert.Equal(before, JobRequestFingerprint.Compute(replay.Request!));
    }

    [Fact]
    public void SavedAndAcceptedContractsRoundTripAllAuthoritativeFieldsAtTopLevel()
    {
        var request = NormalizeJson(ValidJson).Request!;
        var id = Guid.Parse("3340fd95-4f42-4df0-86aa-0cfb57a862fb");
        var created = new DateTimeOffset(2027, 1, 1, 7, 0, 0, TimeSpan.FromHours(2));
        var saved = SavedJobRecord.Create(request, id, created);
        Assert.Equal(id.ToString("D"), saved.Id);
        Assert.Equal(TimeSpan.Zero, saved.CreatedAt.Offset);
        var accepted = JobAcceptedResponse.FromSaved(saved);
        var savedJson = JsonNode.Parse(JsonSerializer.Serialize(saved, JsonOptions))!.AsObject();
        var acceptedJson = JsonNode.Parse(JsonSerializer.Serialize(accepted, JsonOptions))!.AsObject();
        foreach (var field in savedJson) Assert.True(JsonNode.DeepEquals(field.Value, acceptedJson[field.Key]));
        Assert.Equal(9, savedJson.Count);
        Assert.Equal(11, acceptedJson.Count);
        Assert.Equal("accepted", acceptedJson["status"]!.GetValue<string>());
        Assert.Contains("few moments", acceptedJson["message"]!.GetValue<string>());
        Assert.Equal("2028-02-29", acceptedJson["closingDate"]!.GetValue<string>());
        Assert.Equal(created.ToUniversalTime(), DateTimeOffset.Parse(acceptedJson["createdAt"]!.GetValue<string>(), CultureInfo.InvariantCulture));
        var replay = JsonSerializer.Deserialize<JobAcceptedResponse>(JsonSerializer.Serialize(accepted, JsonOptions), JsonOptions)!;
        Assert.Equal(saved.Id, replay.Id);
        Assert.Equal(saved.CreatedAt, replay.CreatedAt);
        Assert.Equal(accepted.Message, replay.Message);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(422)]
    public void ValidationProblemsHaveSafeCamelCaseFieldErrorsAndTrace(int status)
    {
        var errors = new Dictionary<string, string[]> { ["salaryMax"] = ["The maximum salary must be greater than the minimum salary."] };
        var problem = JobApiProblems.Validation(status, errors, "trace-1");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(problem, JsonOptions));
        Assert.Equal(status, problem.Status);
        Assert.Equal(status == 400 ? "Invalid request." : "Validation failed.", problem.Title);
        Assert.Contains("rfc9110", problem.Type);
        Assert.Equal("trace-1", json.RootElement.GetProperty("traceId").GetString());
        Assert.Equal(errors["salaryMax"][0], json.RootElement.GetProperty("errors").GetProperty("salaryMax")[0].GetString());
    }

    [Fact]
    public void UnsupportedProblemCodesAndValidationStatusesFailFast()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobApiProblems.Validation(500, new Dictionary<string, string[]>(), "trace"));
        Assert.Throws<ArgumentOutOfRangeException>(() => JobApiProblems.Failure("unknown", "trace"));
    }

    [Theory]
    [InlineData(JobApiProblems.IdempotencyKeyConflict, 409)]
    [InlineData(JobApiProblems.IdempotencyInProgress, 409)]
    [InlineData(JobApiProblems.PublicationPending, 503)]
    [InlineData(JobApiProblems.DependencyUnavailable, 503)]
    public void FailureCodesAreTopLevelAndExplainSameKeyRecovery(string code, int status)
    {
        var problem = JobApiProblems.Failure(code, "trace-2");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(problem, JsonOptions));
        Assert.Equal(status, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.Contains("rfc9110", problem.Type);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal("trace-2", json.RootElement.GetProperty("traceId").GetString());
        if (code == JobApiProblems.PublicationPending) Assert.Contains("has been saved", problem.Detail);
        if (code == JobApiProblems.DependencyUnavailable) Assert.Contains("uncertain", problem.Detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("valid,other")]
    [InlineData("valid/other")]
    [InlineData("é")]
    [InlineData("\r\n")]
    public void InvalidOpaqueHeaderValuesAreRejected(string? value)
    {
        Assert.False(IdempotencyKeyValidator.TryValidate([value], out var key));
        Assert.Null(key);
    }

    [Fact]
    public void HeaderRequiresExactlyOneBoundedValueAndNeverGeneratesFallback()
    {
        Assert.False(IdempotencyKeyValidator.TryValidate([], out _));
        Assert.False(IdempotencyKeyValidator.TryValidate(["one", "two"], out _));
        Assert.False(IdempotencyKeyValidator.TryValidate([new string('a', 129)], out _));
        foreach (var value in new[] { "a", "AZaz09_-", new string('a', 128), "3340fd95-4f42-4df0-86aa-0cfb57a862fb" })
        {
            Assert.True(IdempotencyKeyValidator.TryValidate([value], out var key));
            Assert.Equal(value, key);
        }
    }

    private static RequestReadResult ReadReplacing(string field, string token)
    {
        var node = JsonNode.Parse(ValidJson)!;
        node[field] = JsonNode.Parse(token);
        return new CreateJobRequestReader().Read(node.ToJsonString());
    }

    private static JobRequestValidationResult NormalizeReplacing(string field, string token) =>
        new JobRequestValidator().Normalize(Assert.IsType<CreateJobRequest>(ReadReplacing(field, token).Request));

    private static JobRequestValidationResult NormalizeJson(string json) =>
        new JobRequestValidator().Normalize(Assert.IsType<CreateJobRequest>(new CreateJobRequestReader().Read(json).Request));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
