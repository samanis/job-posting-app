namespace JobPosting.Api.Validation;

public static class IdempotencyKeyValidator
{
    public static bool TryValidate(IReadOnlyList<string?> values, out string? key)
    {
        key = null;
        if (values.Count != 1) return false;
        var value = values[0];
        if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
        foreach (var character in value)
            if (!(char.IsAsciiLetterOrDigit(character) || character == '_' || character == '-')) return false;
        key = value;
        return true;
    }
}
