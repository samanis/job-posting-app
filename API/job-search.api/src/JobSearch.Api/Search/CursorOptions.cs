using Microsoft.Extensions.Options;
namespace JobSearch.Api.Search;

public sealed class CursorOptions
{
    public const string DevelopmentKey = "ZGV2ZWxvcG1lbnQtb25seS1uZXZlci11c2UtaW4tcHJvZHVjdGlvbiE=";
    public string ActiveKeyId { get; set; } = "current";
    public Dictionary<string, string> Keys { get; set; } = new();
    public int LifetimeMinutes { get; set; } = 15;
}

public sealed class CursorOptionsValidator(IHostEnvironment environment) : IValidateOptions<CursorOptions>
{
    public ValidateOptionsResult Validate(string? name, CursorOptions o)
    {
        if (o.LifetimeMinutes is < 1 or > 60 || o.Keys.Count is < 1 or > 4 || !o.Keys.ContainsKey(o.ActiveKeyId))
            return ValidateOptionsResult.Fail("Cursor configuration is invalid.");
        foreach (var key in o.Keys)
        {
            if (key.Key.Length is < 1 or > 32 || key.Key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
                return ValidateOptionsResult.Fail("Cursor key identifiers are invalid.");
            try
            {
                if (Convert.FromBase64String(key.Value).Length < 32
                    || (!environment.IsDevelopment() && key.Value == CursorOptions.DevelopmentKey))
                    return ValidateOptionsResult.Fail("Cursor signing keys must be externally configured secure keys.");
            }
            catch (FormatException)
            {
                return ValidateOptionsResult.Fail("Cursor signing keys must be base64 encoded.");
            }
        }
        return ValidateOptionsResult.Success;
    }
}
