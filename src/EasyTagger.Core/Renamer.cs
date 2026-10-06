using System.Text.RegularExpressions;

namespace EasyTagger.Core;

public static class Renamer
{
    public const int MaxTitleLength = 20;

    static readonly Regex Pattern = new(
        @"^(?<title>.+?)_(?<dt>\d{8,})(?<rest>(?:_[^_/\\]+)*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Illegal = new(@"[<>:""/\\|?*\u0000-\u001f]", RegexOptions.Compiled);
    static readonly Regex Bracket = new(@"\[[^\]]+\]", RegexOptions.Compiled);

    public static string Sanitize(string? name)
    {
        var cleaned = Illegal.Replace(name ?? "", "").Trim().TrimEnd(' ', '.');
        return cleaned;
    }

    public static bool IsFinalOutputName(string? filename)
    {
        var stem = Path.GetFileNameWithoutExtension(filename ?? "");
        var match = Pattern.Match(stem);
        if (!match.Success)
            return false;
        var title = match.Groups["title"].Value.Trim();
        return title.Length > 0 && !title.All(char.IsDigit);
    }

    public static RenameResult BuildNewStem(string filename, string modelName)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);
        var model = Sanitize(modelName);
        if (IsFinalOutputName(filename))
        {
            var title = Pattern.Match(stem).Groups["title"].Value.Trim();
            title = TrimTitle(title);
            return new RenameResult($"{title}_{model}", true);
        }

        return new RenameResult($"{TrimTitle(stem.Trim())}_{model}", false);
    }

    public static bool IsTaggedWith(string filename, string modelName)
    {
        var expected = Bracket.Matches(modelName)
            .Select(match => match.Value.ToLowerInvariant())
            .ToHashSet();
        if (expected.Count == 0)
            return false;
        var stem = Path.GetFileNameWithoutExtension(filename);
        var present = Bracket.Matches(stem)
            .Select(match => match.Value.ToLowerInvariant())
            .ToHashSet();
        return expected.IsSubsetOf(present);
    }

    public static string ResolveCollision(string destDir, string stem, string ext)
    {
        var candidate = Path.Combine(destDir, stem + ext);
        if (!File.Exists(candidate))
            return candidate;
        var n = 2;
        while (true)
        {
            candidate = Path.Combine(destDir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate))
                return candidate;
            n++;
        }
    }

    public static TagOutcome ApplyTag(string srcPath, FaceModel model, string action)
    {
        srcPath = Path.GetFullPath(srcPath);
        var filename = Path.GetFileName(srcPath);
        var ext = Path.GetExtension(filename);
        var result = BuildNewStem(filename, model.Name);
        string destDir;
        if (action == "rename_only")
            destDir = Path.GetDirectoryName(srcPath)!;
        else if (string.IsNullOrWhiteSpace(model.Folder))
            throw new InvalidOperationException("no-folder");
        else
            destDir = model.Folder;
        Directory.CreateDirectory(destDir);
        var target = ResolveCollision(destDir, result.NewStem, ext);
        if (action == "copy")
            File.Copy(srcPath, target);
        else
            File.Move(srcPath, target);
        return new TagOutcome(srcPath, target, result.MatchedPattern, action);
    }

    public static string UndoTag(string sourcePath, string targetPath, string action)
    {
        targetPath = Path.GetFullPath(targetPath);
        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(targetPath))
            throw new FileNotFoundException("Target is gone.", targetPath);
        if (action == "copy")
        {
            File.Delete(targetPath);
            return sourcePath;
        }

        var dest = sourcePath;
        if (File.Exists(dest))
        {
            var dir = Path.GetDirectoryName(sourcePath)!;
            var stem = Path.GetFileNameWithoutExtension(sourcePath);
            var ext = Path.GetExtension(sourcePath);
            dest = ResolveCollision(dir, stem, ext);
        }
        else
        {
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
        }

        File.Move(targetPath, dest);
        return dest;
    }

    static string TrimTitle(string title)
    {
        if (title.Length <= MaxTitleLength)
            return title;
        return title[..MaxTitleLength].TrimEnd();
    }
}

public sealed record RenameResult(string NewStem, bool MatchedPattern);

// Batch groups the files of one picker run, so "undo last" can revert them together.
public sealed record TagOutcome(string SourcePath, string TargetPath, bool MatchedPattern, string Action, string? Batch = null);
