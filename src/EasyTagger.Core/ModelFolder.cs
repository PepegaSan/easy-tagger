namespace EasyTagger.Core;

public sealed record ModelFolderPlan(string SourcePath, string TargetPath, bool AlreadyTagged);

public static class ModelFolder
{
    public static FaceModel WithFolderTag(FaceModel model, string folder, bool enabled)
    {
        if (!enabled)
            return model;
        var trimmed = folder.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var label = Renamer.Sanitize(Path.GetFileName(trimmed)).Replace("[", "").Replace("]", "").Trim();
        if (label.Length == 0)
            return model;
        var token = $"[{label}]";
        if (model.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
            return model;
        return new FaceModel
        {
            Name = $"{model.Name} {token}".Trim(),
            Folder = model.Folder,
            Categories = model.Categories.ToList(),
            CategoriesInName = model.CategoriesInName.ToList(),
        };
    }

    public static List<ModelFolderPlan> Plan(string folder, FaceModel model, string action, TagConfig config)
    {
        var plans = new List<ModelFolderPlan>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return plans;
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (!FileScan.IsCandidate(path, config, onlyFinalNames: false))
                continue;
            if (Renamer.IsTaggedWith(path, model.Name))
            {
                plans.Add(new ModelFolderPlan(path, path, true));
                continue;
            }

            var built = Renamer.BuildNewStem(Path.GetFileName(path), model.Name);
            var extension = Path.GetExtension(path);
            string target;
            if (action == "rename_only")
                target = Reserve(folder, built.NewStem, extension, reserved);
            else if (string.IsNullOrWhiteSpace(model.Folder))
                target = "";
            else
                target = Reserve(model.Folder, built.NewStem, extension, reserved);
            plans.Add(new ModelFolderPlan(path, target, false));
        }
        return plans;
    }

    static string Reserve(string directory, string stem, string extension, HashSet<string> reserved)
    {
        var number = 1;
        while (true)
        {
            var name = number == 1 ? stem + extension : $"{stem} ({number}){extension}";
            var candidate = Path.Combine(directory, name);
            number++;
            if (File.Exists(candidate) || !reserved.Add(candidate))
                continue;
            return candidate;
        }
    }
}
