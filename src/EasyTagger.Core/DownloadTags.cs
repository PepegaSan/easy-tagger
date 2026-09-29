namespace EasyTagger.Core;

public static class DownloadTags
{
    public static DownloadPicker CreateDefault()
    {
        var picker = new DownloadPicker();
        picker.Types.Add(Item("clip", "Clip", baseFolder: ""));
        picker.Types.Add(Item("video", "Video"));
        picker.Targets.Add(Item("hund", "Hund", folderName: "hund"));
        picker.Targets.Add(Item("katze", "Katze", folderName: "katze"));
        picker.Contents.Add(Item("sitzen", "Sitzen"));
        picker.Contents.Add(Item("laufen", "Laufen"));
        picker.LastSelection = new DownloadSelection
        {
            Type = "clip",
            Target = "hund",
            Contents = ["sitzen"],
        };
        return picker;
    }

    public static void Normalize(DownloadPicker picker)
    {
        picker.Version = 1;
        picker.Types = Clean(picker.Types, "types");
        picker.Targets = Clean(picker.Targets, "targets");
        picker.Contents = Clean(picker.Contents, "contents");
        picker.LastSelection ??= new DownloadSelection();
        var typeIds = picker.Types.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var targetIds = picker.Targets.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var contentIds = picker.Contents.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var selection = picker.LastSelection;
        picker.LastSelection = new DownloadSelection
        {
            Type = typeIds.Contains(selection.Type) ? selection.Type : picker.Types.FirstOrDefault()?.Id ?? "",
            Target = targetIds.Contains(selection.Target) ? selection.Target : picker.Targets.FirstOrDefault()?.Id ?? "",
            Contents = (selection.Contents ?? []).Where(contentIds.Contains).Distinct().ToList(),
        };
    }

    public static CatalogItem AddItem(DownloadPicker picker, string group, string label, string folder = "")
    {
        var clean = (label ?? "").Trim().Trim('[', ']').Trim();
        if (clean.Length == 0)
            throw new InvalidOperationException("empty");
        var list = Group(picker, group);
        var id = TextIds.Slug(clean);
        if (list.Any(item => item.Id == id))
            throw new InvalidOperationException("duplicate");
        var item = Item(id, clean);
        if (group == "types")
            item.BaseFolder = string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder);
        else if (group == "targets")
            item.FolderName = string.IsNullOrWhiteSpace(folder) ? id : folder.Trim();
        list.Add(item);
        Normalize(picker);
        return item;
    }

    public static void RemoveItem(DownloadPicker picker, string group, string id)
    {
        Group(picker, group).RemoveAll(item => item.Id == id);
        Normalize(picker);
    }

    public static void UpdateItem(DownloadPicker picker, string group, string id, string label, string folder = "")
    {
        var list = Group(picker, group);
        var item = list.FirstOrDefault(entry => entry.Id == id)
            ?? throw new InvalidOperationException("missing");
        var clean = (label ?? "").Trim().Trim('[', ']').Trim();
        if (clean.Length == 0)
            throw new InvalidOperationException("empty");
        var newId = TextIds.Slug(clean);
        if (!string.Equals(newId, id, StringComparison.Ordinal) && list.Any(entry => entry.Id == newId))
            throw new InvalidOperationException("duplicate");
        item.Id = newId;
        item.Label = clean;
        item.Tag = clean;
        if (group == "types")
            item.BaseFolder = string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder);
        else if (group == "targets")
            item.FolderName = string.IsNullOrWhiteSpace(folder) ? newId : folder.Trim();
        var selection = picker.LastSelection;
        if (group == "types" && selection.Type == id)
            selection.Type = newId;
        if (group == "targets" && selection.Target == id)
            selection.Target = newId;
        if (group == "contents")
            selection.Contents = (selection.Contents ?? []).Select(content => content == id ? newId : content).ToList();
        Normalize(picker);
    }

    public static List<string> SelectionTags(DownloadPicker picker, DownloadSelection? selection = null)
    {
        selection ??= picker.LastSelection;
        var tags = new List<string>();
        var type = picker.Types.FirstOrDefault(item => item.Id == selection.Type);
        var target = picker.Targets.FirstOrDefault(item => item.Id == selection.Target);
        if (type != null)
            tags.Add(Display(type));
        if (target != null)
            tags.Add(Display(target));
        foreach (var contentId in selection.Contents ?? [])
        {
            var content = picker.Contents.FirstOrDefault(item => item.Id == contentId);
            if (content != null)
                tags.Add(Display(content));
        }
        return tags;
    }

    public static string SelectionName(DownloadPicker picker, DownloadSelection? selection = null) =>
        string.Join(" ", SelectionTags(picker, selection).Select(TextIds.FormatTag));

    public static string SelectionFolder(DownloadPicker picker, DownloadSelection? selection = null)
    {
        selection ??= picker.LastSelection;
        var type = picker.Types.FirstOrDefault(item => item.Id == selection.Type);
        var target = picker.Targets.FirstOrDefault(item => item.Id == selection.Target);
        if (type == null || target == null || string.IsNullOrWhiteSpace(type.BaseFolder))
            return "";
        var child = string.IsNullOrWhiteSpace(target.FolderName) ? target.Id : target.FolderName;
        return Path.GetFullPath(Path.Combine(type.BaseFolder, child));
    }

    public static FaceModel SelectionModel(DownloadPicker picker, DownloadSelection? selection = null) =>
        new()
        {
            Name = SelectionName(picker, selection),
            Folder = SelectionFolder(picker, selection),
        };

    public static void ToggleContent(DownloadPicker picker, string contentId)
    {
        var contents = picker.LastSelection.Contents ?? [];
        picker.LastSelection.Contents = contents.Contains(contentId)
            ? contents.Where(id => id != contentId).ToList()
            : contents.Append(contentId).Distinct().ToList();
    }

    static List<CatalogItem> Group(DownloadPicker picker, string group) => group switch
    {
        "types" => picker.Types,
        "targets" => picker.Targets,
        "contents" => picker.Contents,
        _ => throw new InvalidOperationException("group"),
    };

    static List<CatalogItem> Clean(List<CatalogItem>? raw, string group)
    {
        var clean = new List<CatalogItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in raw ?? [])
        {
            var label = (item.Label ?? item.Tag ?? "").Trim();
            var id = string.IsNullOrWhiteSpace(item.Id) ? TextIds.Slug(label) : item.Id;
            if (label.Length == 0 || !seen.Add(id))
                continue;
            item.Id = id;
            item.Label = label;
            item.Tag = string.IsNullOrWhiteSpace(item.Tag) ? label : item.Tag.Trim().Trim('[', ']');
            if (group == "targets" && string.IsNullOrWhiteSpace(item.FolderName))
                item.FolderName = id;
            clean.Add(item);
        }
        return clean;
    }

    static CatalogItem Item(string id, string label, string baseFolder = "", string folderName = "") =>
        new()
        {
            Id = id,
            Label = label,
            Tag = label,
            BaseFolder = baseFolder,
            FolderName = folderName,
        };

    static string Display(CatalogItem item) =>
        string.IsNullOrWhiteSpace(item.Tag) ? item.Label : item.Tag;
}
