using System.Text.RegularExpressions;

namespace EasyTagger.Core;

public sealed class BatchRule
{
    public string MatchType { get; set; } = BatchRules.MatchFolder;
    public string Match { get; set; } = "";
    public string Tags { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed record BatchPreviewItem(string SourcePath, string NewName, IReadOnlyList<string> Tags, bool Changed);

public static class BatchRules
{
    public const string MatchFolder = "folder_name";
    public const string MatchPath = "path_contains";
    public const string MatchFilename = "filename_contains";
    public const string Separator = " -- ";
    public const int MaxTitleLength = 20;

    static readonly Regex TagToken = new(@"\[([^\]]+)\]|([^\s,\[]+)", RegexOptions.Compiled);

    public static string NormalizeTag(string? tag)
    {
        var text = (tag ?? "").Trim();
        if (text.Length >= 2 && text.StartsWith('[') && text.EndsWith(']'))
            text = text[1..^1].Trim();
        return Renamer.Sanitize(text).ToLowerInvariant().Replace(" ", "");
    }

    public static string FormatTag(string? tag)
    {
        var text = NormalizeTag(tag);
        return text.Length == 0 ? "" : $"[{text}]";
    }

    public static List<string> ParseTags(string? text)
    {
        var tags = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in TagToken.Matches(text ?? ""))
        {
            var raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var tag = NormalizeTag(raw);
            if (tag.Length > 0 && seen.Add(tag))
                tags.Add(tag);
        }
        return tags;
    }

    public static (string Title, List<string> Tags) SplitStem(string stem, string? separator = null)
    {
        separator ??= Separator;
        if (separator.Length > 0 && stem.Contains(separator, StringComparison.Ordinal))
        {
            var cut = stem.LastIndexOf(separator, StringComparison.Ordinal);
            return (stem[..cut].TrimEnd(), ParseTags(stem[(cut + separator.Length)..]));
        }

        var brackets = Regex.Matches(stem, @"\[[^\]]+\]");
        if (brackets.Count > 0)
        {
            var first = brackets[0].Index;
            var prefix = stem[..first].TrimEnd(' ', '-', '_');
            if (prefix.Length > 0)
                return (prefix, ParseTags(stem[first..]));
        }
        return (stem, []);
    }

    public static List<string> MergeTags(IEnumerable<string> existing, IEnumerable<string> added)
    {
        var tags = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in existing.Concat(added))
        {
            var text = NormalizeTag(tag);
            if (text.Length > 0 && seen.Add(text))
                tags.Add(text);
        }
        return tags;
    }

    public static string TruncateTitle(string? title, int maxLength = MaxTitleLength)
    {
        var cleaned = Renamer.Sanitize(title);
        if (cleaned.Length <= maxLength)
            return cleaned;
        return cleaned[..maxLength].TrimEnd(' ', '.');
    }

    public static string BuildTaggedStem(string filename, IEnumerable<string> tags, string? separator = null, int maxTitleLength = MaxTitleLength)
    {
        separator ??= Separator;
        var stem = Path.GetFileNameWithoutExtension(filename);
        var (title, existing) = SplitStem(stem, separator);
        var merged = MergeTags(existing, tags);
        var baseTitle = TruncateTitle(string.IsNullOrEmpty(title) ? stem : title, maxTitleLength);
        if (baseTitle.Length == 0)
            baseTitle = TruncateTitle(stem, maxTitleLength);
        if (merged.Count == 0)
            return baseTitle;
        var rendered = string.Join(" ", merged.Select(FormatTag));
        return $"{baseTitle}{separator}{rendered}";
    }

    public static string[] FolderParts(string filePath, string root)
    {
        var path = Path.GetFullPath(filePath);
        string relative;
        try
        {
            relative = Path.GetRelativePath(Path.GetFullPath(root), path);
        }
        catch (ArgumentException)
        {
            return ParentOnly(path);
        }

        if (relative is "." or "")
            return [];
        if (relative.StartsWith("..", StringComparison.Ordinal))
            return ParentOnly(path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length <= 1)
            return [];
        return parts.Take(parts.Length - 1).Where(part => part.Length > 0 && part is not "." and not "..").ToArray();
    }

    public static bool RuleMatches(string filePath, string root, BatchRule rule)
    {
        var match = (rule.Match ?? "").Trim();
        if (match.Length == 0)
            return false;
        var path = Path.GetFullPath(filePath);
        var type = string.IsNullOrWhiteSpace(rule.MatchType) ? MatchFolder : rule.MatchType;
        if (type == MatchFilename)
            return Path.GetFileName(path).Contains(match, StringComparison.OrdinalIgnoreCase);
        if (type == MatchPath)
            return path.Contains(match, StringComparison.OrdinalIgnoreCase);

        var folders = FolderParts(filePath, root);
        if (folders.Length == 0)
        {
            var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)));
            folders = rootName.Length == 0 ? [] : [rootName];
        }
        return folders.Any(part => part.Equals(match, StringComparison.OrdinalIgnoreCase));
    }

    public static List<string> AutoFolderTags(string filePath, string root) =>
        MergeTags([], FolderParts(filePath, root).Select(NormalizeTag));

    public static List<string> CollectTags(string filePath, string root, IEnumerable<BatchRule> rules, bool useFolderNames)
    {
        var tags = new List<string>();
        if (useFolderNames)
            tags.AddRange(AutoFolderTags(filePath, root));
        foreach (var rule in rules)
        {
            if (!rule.Enabled)
                continue;
            if (RuleMatches(filePath, root, rule))
                tags.AddRange(ParseTags(rule.Tags));
        }
        return MergeTags([], tags);
    }

    public static IEnumerable<string> MediaFiles(string root, IEnumerable<string> extensions, IEnumerable<string>? ignoreContains = null)
    {
        var rootAbs = Path.GetFullPath(root);
        if (!Directory.Exists(rootAbs))
            yield break;
        var extensionsSet = extensions
            .Select(item => item.StartsWith('.') ? item.ToLowerInvariant() : "." + item.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var ignore = (ignoreContains ?? []).Where(token => token.Length > 0).Select(token => token.ToLowerInvariant()).ToList();
        foreach (var path in Directory.EnumerateFiles(rootAbs, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (ignore.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!extensionsSet.Contains(Path.GetExtension(name).ToLowerInvariant()))
                continue;
            yield return path;
        }
    }

    public static List<BatchRule> SuggestFromFolders(string root, IEnumerable<string> extensions, IEnumerable<string>? ignoreContains = null)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in MediaFiles(root, extensions, ignoreContains))
        {
            foreach (var part in FolderParts(path, root))
                names.Add(part);
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new BatchRule
            {
                MatchType = MatchFolder,
                Match = name,
                Tags = NormalizeTag(name),
                Enabled = true,
            })
            .Where(rule => rule.Tags.Length > 0)
            .ToList();
    }

    public static List<BatchPreviewItem> Preview(
        string root,
        IEnumerable<BatchRule> rules,
        IEnumerable<string> extensions,
        IEnumerable<string>? ignoreContains = null,
        string? separator = null,
        bool useFolderNames = false)
    {
        var ruleList = rules.ToList();
        var items = new List<BatchPreviewItem>();
        foreach (var path in MediaFiles(root, extensions, ignoreContains))
        {
            var tags = CollectTags(path, root, ruleList, useFolderNames);
            if (tags.Count == 0)
                continue;
            var oldName = Path.GetFileName(path);
            var newStem = BuildTaggedStem(oldName, tags, separator);
            var newName = newStem + Path.GetExtension(oldName);
            items.Add(new BatchPreviewItem(path, newName, tags, !newName.Equals(oldName, StringComparison.OrdinalIgnoreCase)));
        }
        return items;
    }

    public static string? Apply(BatchPreviewItem item)
    {
        if (!item.Changed)
            return null;
        var source = Path.GetFullPath(item.SourcePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("Source is gone.", source);
        var directory = Path.GetDirectoryName(source)!;
        var stem = Path.GetFileNameWithoutExtension(item.NewName);
        var extension = Path.GetExtension(item.NewName);
        var target = Renamer.ResolveCollision(directory, stem, extension);
        if (Path.GetFullPath(target).Equals(source, StringComparison.OrdinalIgnoreCase))
            return null;
        File.Move(source, target);
        return target;
    }

    public static void Normalize(List<BatchRule>? rules)
    {
        foreach (var rule in rules ?? [])
        {
            rule.Match ??= "";
            rule.Tags ??= "";
            if (rule.MatchType is not (MatchFolder or MatchPath or MatchFilename))
                rule.MatchType = MatchFolder;
        }
    }

    static string[] ParentOnly(string path)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return parent.Length == 0 ? [] : [parent];
    }
}
