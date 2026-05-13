namespace Services;

/// <summary>
/// Shared utility for generating URL-safe slugs from arbitrary text.
/// Used consistently across all admin CRUD services.
/// </summary>
public static class SlugGenerator
{
    /// <summary>
    /// Generates a lowercase, hyphen-separated slug from the given input string.
    /// </summary>
    /// <param name="input">The text to convert into a slug.</param>
    /// <returns>A sanitized slug with duplicate hyphens collapsed and trimmed.</returns>
    public static string Generate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var slug = input.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("\"", "")
            .Replace("&", "and")
            .Replace("?", "")
            .Replace("!", "")
            .Replace(",", "")
            .Replace(".", "")
            .Replace(":", "")
            .Replace(";", "")
            .Replace("@", "")
            .Replace("#", "")
            .Replace("$", "")
            .Replace("%", "")
            .Replace("^", "")
            .Replace("*", "")
            .Replace("+", "")
            .Replace("=", "")
            .Replace("|", "")
            .Replace("\\", "")
            .Replace("/", "")
            .Replace("(", "")
            .Replace(")", "")
            .Replace("[", "")
            .Replace("]", "")
            .Replace("{", "")
            .Replace("}", "")
            .Replace("<", "")
            .Replace(">", "");

        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        return slug.Trim('-');
    }
}
