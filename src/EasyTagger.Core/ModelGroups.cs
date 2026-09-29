namespace EasyTagger.Core;

public sealed class AssignmentRow
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Selected { get; set; }
    public bool InName { get; set; }
    public bool WasInName { get; set; }
}

public sealed record GroupChip(string Id, string Label, int Count);

public static class ModelGroups
{
    public const string AllId = "all";
    public const string OtherId = "other";

    static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["hund"] = "hund",
        ["hunde"] = "hund",
    };

    static readonly Dictionary<string, string> FixedLabels = new(StringComparer.Ordinal)
    {
        ["hund"] = "Hund",
    };

    public static List<string> ExtraTags(string? name)
    {
        var tags = TextIds.ParseBracketTags(name);
        return tags.Count <= 1 ? [] : tags.Skip(1).ToList();
    }

    public static string CanonicalGroupId(string? tag)
    {
        var key = TextIds.Slug(tag).Replace("_", "");
        if (Synonyms.TryGetValue(key, out var mapped))
            return mapped;
        var slug = TextIds.Slug(tag);
        return slug == "tag" && string.IsNullOrWhiteSpace(tag) ? OtherId : slug;
    }

    public static string GroupLabel(string groupId, string fallback = "")
    {
        if (groupId == OtherId)
            return fallback;
        if (FixedLabels.TryGetValue(groupId, out var label))
            return label;
        var text = fallback.Trim();
        return text.Length > 0 ? text : groupId;
    }

    public static List<string> IdsFor(FaceModel model)
    {
        var seen = new List<string>();
        void Add(string groupId)
        {
            if (groupId.Length > 0 && groupId != OtherId && !seen.Contains(groupId))
                seen.Add(groupId);
        }

        foreach (var tag in ExtraTags(model.Name))
            Add(CanonicalGroupId(tag));
        foreach (var item in model.Categories.Concat(model.CategoriesInName))
            Add(CanonicalGroupId(item));
        return seen.Count == 0 ? [OtherId] : seen;
    }

    public static List<ModelCategory> NormalizeCatalog(IEnumerable<ModelCategory>? raw)
    {
        var output = new List<ModelCategory>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in raw ?? [])
        {
            var label = (item.Label ?? "").Trim();
            var groupId = CanonicalGroupId(string.IsNullOrWhiteSpace(item.Id) ? label : item.Id);
            if (label.Length == 0 || groupId.Length == 0 || groupId == OtherId || !seen.Add(groupId))
                continue;
            output.Add(new ModelCategory { Id = groupId, Label = GroupLabel(groupId, label) });
        }
        return output;
    }

    public static ModelCategory AddCategory(List<ModelCategory> catalog, string label)
    {
        var clean = (label ?? "").Trim().Trim('[', ']').Trim();
        if (clean.Length == 0)
            throw new InvalidOperationException("empty");
        var groupId = CanonicalGroupId(clean);
        if (catalog.Any(item => item.Id == groupId))
            throw new InvalidOperationException("duplicate");
        var item = new ModelCategory { Id = groupId, Label = GroupLabel(groupId, clean) };
        catalog.Add(item);
        return item;
    }

    public static void RemoveCategory(List<ModelCategory> catalog, IEnumerable<FaceModel> models, string categoryId)
    {
        var groupId = CanonicalGroupId(categoryId);
        catalog.RemoveAll(item => item.Id == groupId);
        foreach (var model in models)
        {
            model.Categories = model.Categories.Where(item => CanonicalGroupId(item) != groupId).ToList();
            model.CategoriesInName = model.CategoriesInName.Where(item => CanonicalGroupId(item) != groupId).ToList();
        }
    }

    public static string ApplyFilenameCategories(string? name, IEnumerable<string> inNameLabels, IEnumerable<string>? dropIds = null)
    {
        var drop = (dropIds ?? []).Select(CanonicalGroupId).Where(id => id != OtherId).ToHashSet(StringComparer.Ordinal);
        var desired = new List<(string Id, string Label)>();
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in inNameLabels)
        {
            var clean = (label ?? "").Trim().Trim('[', ']').Trim();
            var groupId = CanonicalGroupId(clean);
            if (clean.Length == 0 || groupId == OtherId || !wanted.Add(groupId) || drop.Contains(groupId))
                continue;
            desired.Add((groupId, GroupLabel(groupId, clean)));
        }

        var tags = TextIds.ParseBracketTags(name);
        if (tags.Count == 0)
        {
            var extras = string.Join(" ", desired.Select(item => $"[{item.Label}]"));
            var baseName = (name ?? "").Trim();
            return baseName.Length == 0 ? extras : $"{baseName} {extras}".Trim();
        }

        var kept = new List<string>();
        var keptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags.Skip(1))
        {
            var groupId = CanonicalGroupId(tag);
            if (drop.Contains(groupId) || !keptIds.Add(groupId))
                continue;
            kept.Add(tag);
        }

        foreach (var (groupId, label) in desired)
        {
            if (keptIds.Add(groupId))
                kept.Add(label);
        }

        return string.Join(" ", new[] { $"[{tags[0]}]" }.Concat(kept.Select(tag => $"[{tag}]")));
    }

    public static List<AssignmentRow> AssignmentRows(IEnumerable<ModelCategory>? catalog, FaceModel? model)
    {
        model ??= new FaceModel();
        var nameLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in ExtraTags(model.Name))
            nameLabels.TryAdd(CanonicalGroupId(tag), tag);
        var selectedIds = model.Categories.Select(CanonicalGroupId).ToHashSet(StringComparer.Ordinal);
        var inNameIds = model.CategoriesInName.Select(CanonicalGroupId).ToHashSet(StringComparer.Ordinal);
        var rows = new List<AssignmentRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string groupId, string label, bool fromName)
        {
            if (groupId.Length == 0 || groupId == OtherId || !seen.Add(groupId))
                return;
            bool selected;
            bool inName;
            if (fromName)
            {
                selected = true;
                inName = true;
            }
            else if (selectedIds.Contains(groupId) || inNameIds.Contains(groupId))
            {
                selected = true;
                inName = inNameIds.Contains(groupId);
            }
            else
            {
                selected = false;
                inName = false;
            }

            rows.Add(new AssignmentRow
            {
                Id = groupId,
                Label = label,
                Selected = selected,
                InName = inName,
                WasInName = inName,
            });
        }

        foreach (var item in NormalizeCatalog(catalog))
            Add(item.Id, item.Label, nameLabels.ContainsKey(item.Id));
        foreach (var pair in nameLabels)
            Add(pair.Key, pair.Value, true);
        return rows;
    }

    public static void ApplyAssignment(FaceModel model, IEnumerable<AssignmentRow> rows)
    {
        var list = rows.ToList();
        var selected = list.Where(row => row.Selected).ToList();
        var inName = selected.Where(row => row.InName).ToList();
        var dropIds = list.Where(row => row.WasInName && !row.InName).Select(row => row.Id);
        model.Categories = selected.Select(row => row.Id).ToList();
        model.CategoriesInName = inName.Select(row => row.Id).ToList();
        model.Name = ApplyFilenameCategories(model.Name, inName.Select(row => row.Label), dropIds);
    }

    public static List<GroupChip> AvailableGroups(IEnumerable<FaceModel> models, IEnumerable<ModelCategory>? categories, string otherLabel)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in NormalizeCatalog(categories))
        {
            counts.TryAdd(item.Id, 0);
            labels[item.Id] = item.Label;
        }

        foreach (var model in models)
        {
            foreach (var tag in ExtraTags(model.Name))
            {
                var groupId = CanonicalGroupId(tag);
                labels.TryAdd(groupId, GroupLabel(groupId, tag));
            }

            var groupIds = IdsFor(model);
            if (groupIds.Count == 1 && groupIds[0] == OtherId)
            {
                counts[OtherId] = counts.GetValueOrDefault(OtherId) + 1;
                continue;
            }

            foreach (var groupId in groupIds)
            {
                counts[groupId] = counts.GetValueOrDefault(groupId) + 1;
                labels.TryAdd(groupId, GroupLabel(groupId, groupId));
            }
        }

        var otherCount = counts.GetValueOrDefault(OtherId);
        counts.Remove(OtherId);
        var ranked = counts
            .Select(pair => new GroupChip(pair.Key, labels.GetValueOrDefault(pair.Key, pair.Key), pair.Value))
            .OrderByDescending(chip => chip.Count)
            .ThenBy(chip => chip.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (otherCount > 0)
            ranked.Add(new GroupChip(OtherId, otherLabel, otherCount));
        return ranked;
    }

    public static List<FaceModel> Filter(IEnumerable<FaceModel> models, string? groupId)
    {
        var list = models.ToList();
        if (string.IsNullOrEmpty(groupId) || groupId == AllId)
            return list;
        return list.Where(model => IdsFor(model).Contains(groupId)).ToList();
    }
}
