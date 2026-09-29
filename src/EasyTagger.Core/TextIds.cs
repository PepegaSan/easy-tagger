using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyTagger.Core;

public static class TextIds
{
    static readonly Regex Bracket = new(@"\[([^\]]+)\]", RegexOptions.Compiled);

    public static string Slug(string? value)
    {
        var text = (value ?? "").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(ch);
        }

        var slug = Regex.Replace(builder.ToString(), @"[^a-zA-Z0-9]+", "_")
            .Trim('_')
            .ToLowerInvariant();
        return slug.Length == 0 ? "tag" : slug;
    }

    public static List<string> ParseBracketTags(string? name) =>
        Bracket.Matches(name ?? "")
            .Select(match => match.Groups[1].Value.Trim())
            .Where(tag => tag.Length > 0)
            .ToList();

    public static string FormatTag(string? label)
    {
        var clean = (label ?? "").Trim().Trim('[', ']').Trim();
        return clean.Length == 0 ? "" : $"[{clean}]";
    }
}
