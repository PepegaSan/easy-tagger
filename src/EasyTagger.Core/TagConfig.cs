using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyTagger.Core;

public sealed class FaceModel
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public List<string> Categories { get; set; } = [];
    public List<string> CategoriesInName { get; set; } = [];

    // Optional number key 1–9 for the picker. It belongs to the model, so it survives reordering.
    public int? Key { get; set; }
}

public sealed class ModelCategory
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class CatalogItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Tag { get; set; } = "";
    public string BaseFolder { get; set; } = "";
    public string FolderName { get; set; } = "";
}

public sealed class DownloadSelection
{
    public string Type { get; set; } = "";
    public string Target { get; set; } = "";
    public List<string> Contents { get; set; } = [];
}

public sealed class DownloadPicker
{
    public int Version { get; set; } = 1;
    public List<CatalogItem> Types { get; set; } = [];
    public List<CatalogItem> Targets { get; set; } = [];
    public List<CatalogItem> Contents { get; set; } = [];
    public DownloadSelection LastSelection { get; set; } = new();

    public static DownloadPicker CreateDefault() => DownloadTags.CreateDefault();
}

public sealed class ProfileState
{
    public string WatchFolder { get; set; } = "";
    public string Action { get; set; } = "move";
    public bool WatchOnlyFinalNames { get; set; } = true;
    public bool ProcessExistingOnStart { get; set; }
    public List<FaceModel> Models { get; set; } = [];
    public List<ModelCategory> ModelCategories { get; set; } = [];
    public string ActiveModel { get; set; } = "";
    public DownloadPicker DownloadPicker { get; set; } = new();
    public bool ReencodeEnabled { get; set; }
    public string ReencodeMode { get; set; } = "crf";
    public int ReencodeCrf { get; set; } = 22;
    public int ReencodeVideoBitrate { get; set; } = 3000;
    public int ReencodeAudioBitrate { get; set; } = 160;
    public string ReencodePreset { get; set; } = "medium";
    public bool ReencodeKeepOriginal { get; set; }
    public string FfmpegPath { get; set; } = "";
    public string FfprobePath { get; set; } = "";
}

public sealed class TagConfig
{
    public const string ProfileModels = "models";
    public const string ProfileDownloads = "downloads";

    public string Language { get; set; } = "de";
    public string WatchFolder { get; set; } = "";
    public double PollInterval { get; set; } = 2;
    public int StableChecks { get; set; } = 2;
    public double StableSeconds { get; set; } = 30;
    public string Action { get; set; } = "move";
    public bool ProcessExistingOnStart { get; set; }
    public List<string> Extensions { get; set; } = [".mp4", ".mov", ".mkv", ".avi", ".webm", ".gif", ".m4v"];
    public List<string> IgnoreNameContains { get; set; } = [".tmp", ".part", ".temp", "~"];
    public bool WatchOnlyFinalNames { get; set; } = true;
    public List<FaceModel> Models { get; set; } = [];
    public List<ModelCategory> ModelCategories { get; set; } = [];
    public string ActiveModel { get; set; } = "";
    public bool CloseToTray { get; set; } = true;
    public bool StartInTray { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Alt+E";
    public bool ReencodeEnabled { get; set; }
    public string ReencodeMode { get; set; } = "crf";
    public int ReencodeCrf { get; set; } = 22;
    public int ReencodeVideoBitrate { get; set; } = 3000;
    public int ReencodeAudioBitrate { get; set; } = 160;
    public string ReencodePreset { get; set; } = "medium";
    public bool ReencodeKeepOriginal { get; set; }
    public string FfmpegPath { get; set; } = "";
    public string FfprobePath { get; set; } = "";
    public string ActiveProfile { get; set; } = ProfileModels;
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public string BatchRoot { get; set; } = "";
    public bool BatchUseFolderNames { get; set; } = true;
    public string BatchTagSeparator { get; set; } = " -- ";
    public List<BatchRule> BatchRules { get; set; } = [];
    public string ModelBatchFolder { get; set; } = "";
    public bool ModelBatchFolderTag { get; set; }
    public DownloadPicker DownloadPicker { get; set; } = DownloadPicker.CreateDefault();
    public Dictionary<string, ProfileState> Profiles { get; set; } = new(StringComparer.Ordinal);

    public static TagConfig CreateDefault()
    {
        var config = new TagConfig
        {
            Models = [new FaceModel { Name = "[ExampleModel]", Folder = "" }],
            ActiveModel = "[ExampleModel]",
            DownloadPicker = DownloadPicker.CreateDefault(),
        };
        config.Normalize();
        return config;
    }

    public void Normalize()
    {
        Extensions = Extensions is { Count: > 0 }
            ? Extensions
            : [".mp4", ".mov", ".mkv", ".avi", ".webm", ".gif", ".m4v"];
        IgnoreNameContains ??= [".tmp", ".part", ".temp", "~", ".__reenc__"];
        if (!IgnoreNameContains.Contains(".__reenc__", StringComparer.OrdinalIgnoreCase))
            IgnoreNameContains.Add(".__reenc__");
        if (ReencodeMode is not ("crf" or "bitrate"))
            ReencodeMode = "crf";
        if (string.IsNullOrWhiteSpace(ReencodePreset))
            ReencodePreset = "medium";
        Models ??= [];
        foreach (var model in Models)
        {
            model.Name ??= "";
            model.Folder ??= "";
            model.Categories ??= [];
            model.CategoriesInName ??= [];
        }

        ModelCategories = ModelGroups.NormalizeCatalog(ModelCategories);
        DownloadPicker ??= DownloadPicker.CreateDefault();
        DownloadTags.Normalize(DownloadPicker);
        if (ActiveProfile is not (ProfileModels or ProfileDownloads))
            ActiveProfile = ProfileModels;
        if (Action is not ("move" or "rename_only" or "copy"))
            Action = "move";
        if (string.IsNullOrWhiteSpace(BatchTagSeparator))
            BatchTagSeparator = EasyTagger.Core.BatchRules.Separator;
        BatchRules ??= [];
        EasyTagger.Core.BatchRules.Normalize(BatchRules);
        Profiles ??= new Dictionary<string, ProfileState>(StringComparer.Ordinal);
        EnsureProfiles();
    }

    public void EnsureProfiles()
    {
        if (!Profiles.ContainsKey(ActiveProfile))
            Profiles[ActiveProfile] = CaptureState();
        if (!Profiles.ContainsKey(ProfileModels))
            Profiles[ProfileModels] = ActiveProfile == ProfileModels ? CaptureState() : EmptyModelsState();
        if (!Profiles.ContainsKey(ProfileDownloads))
            Profiles[ProfileDownloads] = ActiveProfile == ProfileDownloads ? CaptureState() : EmptyDownloadsState();
        foreach (var state in Profiles.Values)
            NormalizeState(state);
    }

    public void SwitchProfile(string profileId)
    {
        if (profileId is not (ProfileModels or ProfileDownloads) || profileId == ActiveProfile)
            return;
        Profiles[ActiveProfile] = CaptureState();
        ActiveProfile = profileId;
        ApplyState(Profiles[profileId]);
    }

    public FaceModel? ActiveFaceModel()
    {
        if (ActiveProfile == ProfileDownloads)
            return DownloadTags.SelectionModel(DownloadPicker);
        return Models.FirstOrDefault(model => model.Name == ActiveModel);
    }

    public ProfileState CaptureState() => new()
    {
        WatchFolder = WatchFolder,
        Action = Action,
        WatchOnlyFinalNames = WatchOnlyFinalNames,
        ProcessExistingOnStart = ProcessExistingOnStart,
        Models = CloneModels(Models),
        ModelCategories = ModelCategories.Select(item => new ModelCategory { Id = item.Id, Label = item.Label }).ToList(),
        ActiveModel = ActiveModel,
        DownloadPicker = ClonePicker(DownloadPicker),
        ReencodeEnabled = ReencodeEnabled,
        ReencodeMode = ReencodeMode,
        ReencodeCrf = ReencodeCrf,
        ReencodeVideoBitrate = ReencodeVideoBitrate,
        ReencodeAudioBitrate = ReencodeAudioBitrate,
        ReencodePreset = ReencodePreset,
        ReencodeKeepOriginal = ReencodeKeepOriginal,
        FfmpegPath = FfmpegPath,
        FfprobePath = FfprobePath,
    };

    public void ApplyState(ProfileState state)
    {
        NormalizeState(state);
        WatchFolder = state.WatchFolder;
        Action = state.Action;
        WatchOnlyFinalNames = state.WatchOnlyFinalNames;
        ProcessExistingOnStart = state.ProcessExistingOnStart;
        Models = CloneModels(state.Models);
        ModelCategories = state.ModelCategories.Select(item => new ModelCategory { Id = item.Id, Label = item.Label }).ToList();
        ActiveModel = state.ActiveModel;
        DownloadPicker = ClonePicker(state.DownloadPicker);
        ReencodeEnabled = state.ReencodeEnabled;
        ReencodeMode = state.ReencodeMode;
        ReencodeCrf = state.ReencodeCrf;
        ReencodeVideoBitrate = state.ReencodeVideoBitrate;
        ReencodeAudioBitrate = state.ReencodeAudioBitrate;
        ReencodePreset = state.ReencodePreset;
        ReencodeKeepOriginal = state.ReencodeKeepOriginal;
        FfmpegPath = state.FfmpegPath;
        FfprobePath = state.FfprobePath;
    }

    static ProfileState EmptyModelsState() => new()
    {
        Models = [new FaceModel { Name = "[ExampleModel]", Folder = "" }],
        ActiveModel = "[ExampleModel]",
        DownloadPicker = DownloadPicker.CreateDefault(),
        WatchOnlyFinalNames = true,
    };

    static ProfileState EmptyDownloadsState() => new()
    {
        Models = [],
        ActiveModel = "[Clip] [Hund] [Sitzen]",
        DownloadPicker = DownloadPicker.CreateDefault(),
        WatchOnlyFinalNames = false,
        Action = "move",
    };

    static void NormalizeState(ProfileState state)
    {
        state.Models ??= [];
        foreach (var model in state.Models)
        {
            model.Categories ??= [];
            model.CategoriesInName ??= [];
        }
        state.ModelCategories = ModelGroups.NormalizeCatalog(state.ModelCategories);
        state.DownloadPicker ??= DownloadPicker.CreateDefault();
        DownloadTags.Normalize(state.DownloadPicker);
        if (state.Action is not ("move" or "rename_only" or "copy"))
            state.Action = "move";
    }

    static List<FaceModel> CloneModels(List<FaceModel> models) =>
        models.Select(model => new FaceModel
        {
            Name = model.Name,
            Folder = model.Folder,
            Categories = model.Categories.ToList(),
            CategoriesInName = model.CategoriesInName.ToList(),
        }).ToList();

    static DownloadPicker ClonePicker(DownloadPicker picker)
    {
        var json = JsonSerializer.Serialize(picker, ConfigStore.JsonOptions);
        return JsonSerializer.Deserialize<DownloadPicker>(json, ConfigStore.JsonOptions) ?? DownloadPicker.CreateDefault();
    }
}

public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static TagConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var created = TagConfig.CreateDefault();
            Save(path, created);
            return created;
        }

        TagConfig? config = null;
        try
        {
            config = JsonSerializer.Deserialize<TagConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Eine beschädigte Datei bleibt als .broken liegen, damit die
            // Modelle daraus von Hand zu retten sind.
            TryKeepBroken(path);
        }

        if (config == null)
        {
            var created = TagConfig.CreateDefault();
            Save(path, created);
            return created;
        }

        config.Normalize();
        return config;
    }

    static void TryKeepBroken(string path)
    {
        try
        {
            File.Copy(path, path + ".broken", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the backup the defaults still let the program start.
        }
    }

    public static void Save(string path, TagConfig config)
    {
        config.Normalize();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}

public static class FileScan
{
    public static IEnumerable<string> ListCandidates(TagConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WatchFolder) || !Directory.Exists(config.WatchFolder))
            yield break;
        foreach (var path in Directory.EnumerateFiles(config.WatchFolder))
        {
            if (IsCandidate(path, config))
                yield return path;
        }
    }

    public static bool IsCandidate(string path, TagConfig config, bool onlyFinalNames = true)
    {
        var name = Path.GetFileName(path);
        if (config.IgnoreNameContains.Any(token =>
                token.Length > 0 && name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            return false;
        var ext = Path.GetExtension(name);
        if (!config.Extensions.Any(item => ext.Equals(item, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (onlyFinalNames && config.WatchOnlyFinalNames && !Renamer.IsFinalOutputName(name))
            return false;
        return true;
    }

    public static bool IsStable(int polls, TimeSpan age, TagConfig config) =>
        polls >= Math.Max(1, config.StableChecks) && age.TotalSeconds >= config.StableSeconds;

    public static bool IsFileInUse(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
