using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EasyTagger.Core;
using Forms = System.Windows.Forms;

namespace EasyTagger.App;

public partial class MainWindow : Window
{
    readonly DispatcherTimer _timer = new();
    readonly DispatcherTimer _geometryTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    readonly Dictionary<string, PendingFile> _pending = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _ignoredAtStart = new(StringComparer.OrdinalIgnoreCase);
    readonly List<TagOutcome> _undo = [];
    readonly List<ModelFolderPlan> _modelFolderPlans = [];
    Forms.NotifyIcon? _tray;
    bool _watching;
    bool _exit;
    bool _suppress;
    bool _modelFolderBusy;
    bool _restoring;
    string _modelFilter = ModelGroups.AllId;
    const int UndoLimit = 30;
    PickerWindow? _picker;

    public MainWindow()
    {
        InitializeComponent();
        EncodePresetBox.ItemsSource = new[]
        {
            "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower",
        };
        _timer.Tick += (_, _) => Poll();
        _geometryTimer.Tick += (_, _) =>
        {
            _geometryTimer.Stop();
            SavePlacement();
        };
        LocationChanged += (_, _) => SchedulePlacement();
        SizeChanged += (_, _) => SchedulePlacement();
        LoadUndo();
        CreateTray();
        ApplyLanguage();
        RefreshAll();
        RestorePlacement();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
        RegisterHotkey();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();
        if (!_exit && App.Config.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        UnregisterHotkey();
        if (_tray != null)
        {
            var icon = _tray.Icon;
            _tray.Icon = null;
            _tray.Visible = false;
            _tray.Dispose();
            icon?.Dispose();
        }
        Save();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        System.Windows.Application.Current.Shutdown();
    }

    public void ExitApp()
    {
        _exit = true;
        Close();
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    void CreateTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "Easy Tagger",
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(UiText.Get("show"), null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add(UiText.Get("exit"), null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    static System.Drawing.Icon LoadTrayIcon()
    {
        var info = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        if (info?.Stream == null)
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        using var stream = info.Stream;
        return new System.Drawing.Icon(stream);
    }

    void ApplyLanguage()
    {
        EyebrowText.Text = UiText.Get("eyebrow");
        HeroTitle.Text = UiText.Get("hero");
        HeroSub.Text = UiText.Get("sub");
        ProfileModelsButton.Content = UiText.Get("models");
        ProfileDownloadsButton.Content = UiText.Get("downloads");
        LangDeButton.Content = "Deutsch";
        LangEnButton.Content = "English";
        NavWatchButton.Content = UiText.Get("watch");
        NavModelsButton.Content = UiText.Get("models");
        NavDownloadsButton.Content = UiText.Get("downloads");
        NavBatchButton.Content = UiText.Get("batch");
        NavLogButton.Content = UiText.Get("log");
        WatchTitle.Text = UiText.Get("watch-title");
        ModelsTitle.Text = UiText.Get("models-title");
        DownloadsTitle.Text = UiText.Get("downloads-title");
        LogTitle.Text = UiText.Get("log-title");
        BrowseFolderButton.Content = UiText.Get("browse");
        TagExistingButton.Content = UiText.Get("tag-now");
        UndoButton.Content = UiText.Get("undo");
        ActionMoveButton.Content = UiText.Get("move");
        ActionCopyButton.Content = UiText.Get("copy");
        ActionRenameButton.Content = UiText.Get("rename");
        TypeFolderLabel.Text = UiText.Get("type-folder");
        BrowseTypeFolderButton.Content = UiText.Get("browse");
        EncodeTitle.Text = UiText.Get("encode-title");
        EncodeSummary.Text = UiText.Get("encode-summary");
        EncodeEnabledBox.Content = UiText.Get("encode-enable");
        EncodeHint.Text = UiText.Get("encode-hint");
        EncodeCrfButton.Content = UiText.Get("encode-crf");
        EncodeBitrateButton.Content = UiText.Get("encode-bitrate");
        EncodeCrfLabel.Text = UiText.Get("encode-crf-value");
        EncodeVideoLabel.Text = UiText.Get("encode-video");
        EncodeAudioLabel.Text = UiText.Get("encode-audio");
        EncodePresetLabel.Text = UiText.Get("encode-preset");
        EncodeKeepBox.Content = UiText.Get("encode-keep");
        EncodeFfmpegLabel.Text = UiText.Get("encode-ffmpeg");
        EncodeFfprobeLabel.Text = UiText.Get("encode-ffprobe");
        SaveHotkeyButton.Content = UiText.Get("hotkey");
        FinalNamesBox.Content = UiText.Get("final");
        ProcessExistingBox.Content = UiText.Get("process-existing");
        CloseToTrayBox.Content = UiText.Get("tray");
        StartInTrayBox.Content = UiText.Get("start-tray");
        ClearLogButton.Content = UiText.Get("clear-log");
        BatchTitle.Text = UiText.Get("batch-title");
        BatchSummary.Text = UiText.Get("batch-summary");
        BatchStep1.Text = UiText.Get("batch-step1");
        BatchStep1Hint.Text = UiText.Get("batch-step1-hint");
        BatchStep2.Text = UiText.Get("batch-step2");
        BatchStep2Hint.Text = UiText.Get("batch-step2-hint");
        FolderTagBox.Content = UiText.Get("batch-folder-tag");
        BatchStep3.Text = UiText.Get("batch-step3");
        BatchStep3Hint.Text = UiText.Get("batch-step3-hint");
        BrowseModelFolderButton.Content = UiText.Get("browse");
        PreviewModelFolderButton.Content = UiText.Get("model-folder-preview");
        AddModelButton.Content = UiText.Get("add-model");
        ModelNowHint.Text = UiText.Get("model-now-hint");
        AddTypeButton.Content = UiText.Get("add-type");
        AddTargetButton.Content = UiText.Get("add-target");
        AddContentButton.Content = UiText.Get("add-content");
        TypeLabel.Text = UiText.Get("type");
        TargetLabel.Text = UiText.Get("target");
        ContentLabel.Text = UiText.Get("content");
        PreviewCaption.Text = UiText.Get("preview");
        ToggleWatchButton.Content = UiText.Get(_watching ? "stop" : "start");
        if (_tray?.ContextMenuStrip is { Items.Count: >= 2 } menu)
        {
            menu.Items[0].Text = UiText.Get("show");
            menu.Items[1].Text = UiText.Get("exit");
        }
    }

    void RefreshAll()
    {
        var config = App.Config;
        _suppress = true;
        FolderBox.Text = config.WatchFolder;
        HotkeyBox.Text = config.Hotkey;
        EncodeEnabledBox.IsChecked = config.ReencodeEnabled;
        EncodeCrfBox.Text = config.ReencodeCrf.ToString();
        EncodeVideoBox.Text = config.ReencodeVideoBitrate.ToString();
        EncodeAudioBox.Text = config.ReencodeAudioBitrate.ToString();
        EncodePresetBox.SelectedItem = config.ReencodePreset;
        EncodeKeepBox.IsChecked = config.ReencodeKeepOriginal;
        FfmpegBox.Text = config.FfmpegPath;
        FfprobeBox.Text = config.FfprobePath;
        var type = config.DownloadPicker.Types.FirstOrDefault(item => item.Id == config.DownloadPicker.LastSelection.Type);
        TypeFolderBox.Text = type?.BaseFolder ?? "";
        FinalNamesBox.IsChecked = config.WatchOnlyFinalNames;
        ProcessExistingBox.IsChecked = config.ProcessExistingOnStart;
        CloseToTrayBox.IsChecked = config.CloseToTray;
        config.StartInTray = AutostartService.IsEnabled;
        StartInTrayBox.IsChecked = config.StartInTray;
        ModelFolderBox.Text = config.ModelBatchFolder;
        FolderTagBox.IsChecked = config.ModelBatchFolderTag;
        _suppress = false;
        RefreshEncodeTools();
        PaintProfileButtons();
        PaintActionButtons();
        ModelsSection.Visibility = config.ActiveProfile == TagConfig.ProfileModels
            ? Visibility.Visible
            : Visibility.Collapsed;
        DownloadsSection.Visibility = config.ActiveProfile == TagConfig.ProfileDownloads
            ? Visibility.Visible
            : Visibility.Collapsed;
        NavModelsButton.Visibility = ModelsSection.Visibility;
        NavDownloadsButton.Visibility = DownloadsSection.Visibility;
        BatchSection.Visibility = ModelsSection.Visibility;
        NavBatchButton.Visibility = ModelsSection.Visibility;
        var showWatch = config.ActiveProfile != TagConfig.ProfileModels;
        var watchVisibility = showWatch ? Visibility.Visible : Visibility.Collapsed;
        WatchFolderRow.Visibility = watchVisibility;
        ToggleWatchButton.Visibility = watchVisibility;
        TagExistingButton.Visibility = watchVisibility;
        FinalNamesBox.Visibility = watchVisibility;
        ProcessExistingBox.Visibility = watchVisibility;
        NavWatchButton.Visibility = watchVisibility;
        StatusWatchChip.Visibility = watchVisibility;
        WatchTitle.Text = UiText.Get(showWatch ? "watch-title" : "action-title");
        if (!showWatch && _watching)
        {
            StopWatching();
            Log(UiText.Get("watch-models"));
        }
        BuildCategories();
        BuildModels();
        BuildDownloads();
        UpdateStatus();
    }

    void PaintProfileButtons()
    {
        PaintActive(ProfileModelsButton, App.Config.ActiveProfile == TagConfig.ProfileModels);
        PaintActive(ProfileDownloadsButton, App.Config.ActiveProfile == TagConfig.ProfileDownloads);
        PaintActive(LangDeButton, UiText.German);
        PaintActive(LangEnButton, !UiText.German);
    }

    void PaintActionButtons()
    {
        PaintActive(ActionMoveButton, App.Config.Action == "move");
        PaintActive(ActionCopyButton, App.Config.Action == "copy");
        PaintActive(ActionRenameButton, App.Config.Action == "rename_only");
        PaintActive(EncodeCrfButton, App.Config.ReencodeMode != "bitrate");
        PaintActive(EncodeBitrateButton, App.Config.ReencodeMode == "bitrate");
    }

    void PaintActive(Button button, bool active)
    {
        button.Background = (Brush)FindResource(active ? "SignalRed" : "ChipBg");
        button.BorderBrush = (Brush)FindResource(active ? "SignalRed" : "ChipBorder");
        button.Foreground = (Brush)FindResource(active ? "OnRed" : "Text");
    }

    void UpdateStatus()
    {
        var model = App.Config.ActiveFaceModel();
        StatusWatchText.Text = UiText.Get(_watching ? "running" : "stopped");
        StatusWatchText.Foreground = (Brush)FindResource(_watching ? "SignalRed" : "Text");
        StatusWatchChip.BorderBrush = (Brush)FindResource(_watching ? "SignalRed" : "ChipBorder");
        StatusActionText.Text = UiText.Get(App.Config.Action switch
        {
            "rename_only" => "rename",
            "copy" => "copy",
            _ => "move",
        });
        StatusModelText.Text = string.IsNullOrWhiteSpace(model?.Name) ? UiText.Get("none") : model!.Name;
        var destination = string.IsNullOrWhiteSpace(model?.Folder) ? UiText.Get("no-dest") : model!.Folder;
        ModelFolderActive.Text = string.Format(
            UiText.Get("model-folder-active"),
            string.IsNullOrWhiteSpace(model?.Name) ? UiText.Get("none") : model!.Name,
            destination);
        var runLabel = UiText.Get(App.Config.Action switch
        {
            "copy" => "model-folder-copy",
            "rename_only" => "model-folder-rename",
            _ => "model-folder-move",
        });
        RunModelFolderButton.Content = runLabel;
        RunModelNowButton.Content = UiText.Get(App.Config.Action switch
        {
            "copy" => "model-now-copy",
            "rename_only" => "model-now-rename",
            _ => "model-now-move",
        });
        StatusHotkeyText.Text = App.Config.Hotkey;
        PreviewNameText.Text = DownloadTags.SelectionName(App.Config.DownloadPicker);
        PreviewFolderText.Text = DownloadTags.SelectionFolder(App.Config.DownloadPicker);
        var selectedType = App.Config.DownloadPicker.Types
            .FirstOrDefault(item => item.Id == App.Config.DownloadPicker.LastSelection.Type);
        var baseFolder = selectedType?.BaseFolder ?? "";
        if (TypeFolderBox.Text != baseFolder)
        {
            _suppress = true;
            TypeFolderBox.Text = baseFolder;
            _suppress = false;
        }
    }

    void BuildCategories()
    {
        CategoryPanel.Children.Clear();
        var models = App.Config.Models;
        var chips = new List<GroupChip>
        {
            new(ModelGroups.AllId, $"{UiText.Get("all")} ({models.Count})", models.Count),
        };
        chips.AddRange(ModelGroups.AvailableGroups(models, App.Config.ModelCategories, UiText.Get("other")));
        if (chips.All(chip => chip.Id != _modelFilter))
            _modelFilter = ModelGroups.AllId;
        foreach (var chip in chips)
        {
            var id = chip.Id;
            var button = MakeChip($"{chip.Label}" + (chip.Id == ModelGroups.AllId ? "" : $" ({chip.Count})"), chip.Id == _modelFilter);
            if (chip.Id == ModelGroups.AllId)
                button.Content = chip.Label;
            button.Click += (_, _) =>
            {
                _modelFilter = id;
                BuildCategories();
                BuildModels();
            };
            CategoryPanel.Children.Add(button);
        }
    }

    void BuildModels()
    {
        ModelPanel.Children.Clear();
        foreach (var model in ModelGroups.Filter(App.Config.Models, _modelFilter))
        {
            var current = model;
            var active = current.Name == App.Config.ActiveModel;
            var nameBlock = new TextBlock
            {
                Text = current.Name,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            var folderBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(current.Folder) ? UiText.Get("no-dest") : current.Folder,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                MaxWidth = 210,
            };
            var ink = (Brush)FindResource(active ? "OnRed" : "Text");
            nameBlock.Foreground = ink;
            folderBlock.Foreground = ink;
            var button = new Button
            {
                Content = new StackPanel { Children = { nameBlock, folderBlock } },
                Style = (Style)FindResource("ModelCard"),
                MaxWidth = 240,
            };
            PaintActive(button, active);
            button.Click += (_, _) =>
            {
                App.Config.ActiveModel = current.Name;
                Save();
                BuildModels();
                UpdateStatus();
            };
            var menu = new ContextMenu();
            var edit = new MenuItem { Header = UiText.Get("edit") };
            edit.Click += (_, _) => EditModel(current);
            var remove = new MenuItem { Header = UiText.Get("remove") };
            remove.Click += (_, _) =>
            {
                App.Config.Models.Remove(current);
                if (App.Config.ActiveModel == current.Name)
                    App.Config.ActiveModel = App.Config.Models.FirstOrDefault()?.Name ?? "";
                Save();
                RefreshAll();
            };
            menu.Items.Add(edit);
            menu.Items.Add(remove);
            button.ContextMenu = menu;
            ModelPanel.Children.Add(button);
        }
    }

    void BuildDownloads()
    {
        var picker = App.Config.DownloadPicker;
        FillCatalog(TypePanel, picker.Types, picker.LastSelection.Type, "types", id =>
        {
            picker.LastSelection.Type = id;
            Save();
            BuildDownloads();
            UpdateStatus();
        });
        FillCatalog(TargetPanel, picker.Targets, picker.LastSelection.Target, "targets", id =>
        {
            picker.LastSelection.Target = id;
            Save();
            BuildDownloads();
            UpdateStatus();
        });
        ContentPanel.Children.Clear();
        foreach (var item in picker.Contents)
        {
            var current = item;
            var selected = picker.LastSelection.Contents.Contains(current.Id);
            var button = MakeChip(current.Label, selected);
            button.Click += (_, _) =>
            {
                DownloadTags.ToggleContent(picker, current.Id);
                Save();
                BuildDownloads();
                UpdateStatus();
            };
            AttachItemMenu(button, () => EditDownload("contents", current), () =>
            {
                DownloadTags.RemoveItem(picker, "contents", current.Id);
                Save();
                RefreshAll();
            });
            ContentPanel.Children.Add(button);
        }
    }

    void FillCatalog(WrapPanel panel, List<CatalogItem> items, string selectedId, string group, Action<string> choose)
    {
        panel.Children.Clear();
        foreach (var item in items)
        {
            var current = item;
            var button = MakeChip(current.Label, current.Id == selectedId);
            button.Click += (_, _) => choose(current.Id);
            AttachItemMenu(button, () => EditDownload(group, current), () =>
            {
                DownloadTags.RemoveItem(App.Config.DownloadPicker, group, current.Id);
                Save();
                RefreshAll();
            });
            panel.Children.Add(button);
        }
    }

    Button MakeChip(string text, bool active)
    {
        var button = new Button { Content = text, Style = (Style)FindResource("Chip") };
        PaintActive(button, active);
        return button;
    }

    static void AttachItemMenu(Button button, Action edit, Action remove)
    {
        var menu = new ContextMenu();
        var editItem = new MenuItem { Header = UiText.Get("edit") };
        editItem.Click += (_, _) => edit();
        var item = new MenuItem { Header = UiText.Get("remove") };
        item.Click += (_, _) => remove();
        menu.Items.Add(editItem);
        menu.Items.Add(item);
        button.ContextMenu = menu;
    }

    void EditDownload(string group, CatalogItem item)
    {
        var folder = group switch
        {
            "types" => item.BaseFolder,
            "targets" => item.FolderName,
            _ => "",
        };
        var editor = new DownloadItemWindow(this, group, item.Label, folder);
        if (editor.ShowDialog() != true || string.IsNullOrWhiteSpace(editor.Label))
            return;
        try
        {
            DownloadTags.UpdateItem(App.Config.DownloadPicker, group, item.Id, editor.Label, editor.Folder);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, UiText.Get(ex.Message == "empty" ? "category-empty" : "category-exists"),
                "Easy Tagger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Save();
        RefreshAll();
    }

    void EditModel(FaceModel model)
    {
        var editor = new ModelEditorWindow(this, model, App.Config.ModelCategories);
        if (editor.ShowDialog() != true || editor.Result == null)
            return;
        if (App.Config.Models.Any(other => !ReferenceEquals(other, model) && other.Name == editor.Result.Name))
        {
            MessageBox.Show(this, UiText.Get("model-exists"), "Easy Tagger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var index = App.Config.Models.IndexOf(model);
        if (App.Config.ActiveModel == model.Name)
            App.Config.ActiveModel = editor.Result.Name;
        App.Config.Models[index] = editor.Result;
        Save();
        RefreshAll();
    }

    void OnAddModel(object sender, RoutedEventArgs e)
    {
        var editor = new ModelEditorWindow(this, new FaceModel(), App.Config.ModelCategories);
        if (editor.ShowDialog() != true || editor.Result == null)
            return;
        if (App.Config.Models.Any(model => model.Name == editor.Result.Name))
        {
            MessageBox.Show(this, UiText.Get("model-exists"), "Easy Tagger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        App.Config.Models.Add(editor.Result);
        App.Config.ActiveModel = editor.Result.Name;
        Save();
        RefreshAll();
    }

    void OnBrowseModelFolder(object sender, RoutedEventArgs e)
    {
        Prompt.ChooseFolder(this, ModelFolderBox.Text.Trim(), folder =>
        {
            ModelFolderBox.Text = folder;
            RememberModelFolder();
            Save();
        });
    }

    void OnModelFolderLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppress)
            return;
        RememberModelFolder();
        Save();
    }

    void RememberModelFolder() => App.Config.ModelBatchFolder = ModelFolderBox.Text.Trim();

    static FaceModel BatchModel(FaceModel model, string folder) =>
        ModelFolder.WithFolderTag(model, folder, App.Config.ModelBatchFolderTag);

    void OnPreviewModelFolder(object sender, RoutedEventArgs e) => ShowModelFolderPlan(announce: true);

    bool ShowModelFolderPlan(bool announce)
    {
        _modelFolderPlans.Clear();
        ModelFolderPreview.Items.Clear();
        RememberModelFolder();
        var folder = App.Config.ModelBatchFolder;
        var model = App.Config.Models.FirstOrDefault(item => item.Name == App.Config.ActiveModel);
        if (model == null || string.IsNullOrWhiteSpace(model.Name))
            return FailPlan(UiText.Get("no-model"), announce);
        if (!Directory.Exists(folder))
            return FailPlan(UiText.Get("model-folder-invalid"), announce);
        if (App.Config.Action != "rename_only" && string.IsNullOrWhiteSpace(model.Folder))
            return FailPlan(model.Name + UiText.Get("no-folder"), announce);

        var plans = ModelFolder.Plan(folder, BatchModel(model, folder), App.Config.Action, App.Config);
        _modelFolderPlans.AddRange(plans);
        var ready = plans.Where(plan => !plan.AlreadyTagged).ToList();
        foreach (var plan in ready.Take(300))
            ModelFolderPreview.Items.Add($"{Path.GetFileName(plan.SourcePath)}  →  {plan.TargetPath}");
        if (ready.Count > 300)
            ModelFolderPreview.Items.Add(string.Format(UiText.Get("batch-truncated"), ready.Count));
        var skipped = plans.Count - ready.Count;
        if (ready.Count == 0)
            return FailPlan(ExplainBatchFolder(folder, model.Name, plans.Count > 0), announce);

        ModelFolderStatus.Text = string.Format(UiText.Get("model-folder-status"), ready.Count, skipped)
            + (App.Config.ReencodeEnabled ? "  " + UiText.Get("model-folder-reencode") : "");
        if (announce)
            Log(ModelFolderStatus.Text);
        return true;
    }

    bool FailPlan(string message, bool announce)
    {
        ModelFolderStatus.Text = message;
        if (announce)
            Announce(message);
        return false;
    }

    static string ExplainBatchFolder(string folder, string modelName, bool allTagged)
    {
        if (allTagged)
            return string.Format(UiText.Get("model-folder-all-tagged"), modelName);
        var files = Directory.EnumerateFiles(folder).ToList();
        if (files.Count == 0)
            return Directory.EnumerateDirectories(folder).Any()
                ? UiText.Get("model-folder-subfolders")
                : UiText.Get("model-folder-dir-empty");
        return UiText.Get("model-folder-bad-ext");
    }

    void OnRunModelDirect(object sender, RoutedEventArgs e)
    {
        var model = App.Config.Models.FirstOrDefault(item => item.Name == App.Config.ActiveModel);
        if (model == null || string.IsNullOrWhiteSpace(model.Name))
        {
            Announce(UiText.Get("no-model"));
            return;
        }
        if (App.Config.Action != "rename_only" && string.IsNullOrWhiteSpace(model.Folder))
        {
            Announce(model.Name + UiText.Get("no-folder"));
            return;
        }

        Prompt.ChooseFolder(this, App.Config.ModelBatchFolder, folder =>
        {
            ModelFolderBox.Text = folder;
            RememberModelFolder();
            Save();
            OnRunModelFolder(sender, e);
        });
    }

    void OnRunModelFolder(object sender, RoutedEventArgs e)
    {
        if (_modelFolderBusy)
            return;
        if (!ShowModelFolderPlan(announce: true))
            return;
        var ready = _modelFolderPlans.Where(plan => !plan.AlreadyTagged).ToList();
        if (MessageBox.Show(this, string.Format(UiText.Get("model-folder-confirm"), ready.Count),
                UiText.Get("model-folder-title"), MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        var model = BatchModel(App.Config.Models.First(item => item.Name == App.Config.ActiveModel), App.Config.ModelBatchFolder);
        var action = App.Config.Action;
        var reencode = App.Config.ReencodeEnabled;
        var settings = App.Config;
        SetModelFolderBusy(true);
        Task.Run(() =>
        {
            var done = new List<TagOutcome>();
            var skipped = 0;
            foreach (var plan in ready)
            {
                try
                {
                    if (!File.Exists(plan.SourcePath) || FileScan.IsFileInUse(plan.SourcePath))
                    {
                        skipped++;
                        Dispatcher.Invoke(() => Log(string.Format(UiText.Get("waiting-locked"), Path.GetFileName(plan.SourcePath))));
                        continue;
                    }
                    var outcome = Renamer.ApplyTag(plan.SourcePath, model, action);
                    var finalPath = outcome.TargetPath;
                    if (reencode)
                    {
                        finalPath = Encoder.MaybeReencode(outcome.TargetPath, settings, message =>
                            Dispatcher.Invoke(() => Log(message)));
                    }
                    done.Add(outcome with { TargetPath = finalPath });
                    Dispatcher.Invoke(() => Log($"{Path.GetFileName(outcome.SourcePath)}  →  {finalPath}"));
                }
                catch (Exception ex)
                {
                    skipped++;
                    Dispatcher.Invoke(() => Log(ex.Message));
                }
            }
            return (done, skipped);
        }).ContinueWith(task => Dispatcher.Invoke(() => FinishModelFolder(task)));
    }

    void FinishModelFolder(Task<(List<TagOutcome> Done, int Skipped)> task)
    {
        SetModelFolderBusy(false);
        if (task.IsFaulted)
        {
            Log(task.Exception?.GetBaseException().Message ?? "model");
            return;
        }
        foreach (var outcome in task.Result.Done)
            PushUndo(outcome);
        var message = string.Format(UiText.Get("model-folder-done"), task.Result.Done.Count, task.Result.Skipped);
        Log(message);
        ShowModelFolderPlan(announce: false);
        ModelFolderStatus.Text = message;
    }

    void SetModelFolderBusy(bool busy)
    {
        _modelFolderBusy = busy;
        PreviewModelFolderButton.IsEnabled = !busy;
        RunModelFolderButton.IsEnabled = !busy;
        if (busy)
            ModelFolderStatus.Text = UiText.Get("model-folder-working");
    }

    void OnAddCategory(object sender, RoutedEventArgs e)
    {
        var label = Prompt.Ask(this, UiText.Get("category-add"), UiText.Get("category-name"));
        if (label == null)
            return;
        try
        {
            ModelGroups.AddCategory(App.Config.ModelCategories, label);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, UiText.Get(ex.Message == "empty" ? "category-empty" : "category-exists"),
                "Easy Tagger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Save();
        BuildCategories();
    }

    void OnRemoveCategory(object sender, RoutedEventArgs e)
    {
        var catalog = App.Config.ModelCategories;
        if (catalog.Count == 0)
        {
            MessageBox.Show(this, UiText.Get("category-remove-empty"), "Easy Tagger");
            return;
        }
        var menu = new ContextMenu();
        foreach (var item in catalog)
        {
            var chosen = item;
            var entry = new MenuItem { Header = chosen.Label };
            entry.Click += (_, _) =>
            {
                if (MessageBox.Show(this, UiText.Get("category-remove"), chosen.Label, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return;
                ModelGroups.RemoveCategory(catalog, App.Config.Models, chosen.Id);
                Save();
                RefreshAll();
            };
            menu.Items.Add(entry);
        }
        menu.PlacementTarget = (Button)sender;
        menu.IsOpen = true;
    }

    void OnAddType(object sender, RoutedEventArgs e) => AddDownloadItem("types", true);
    void OnAddTarget(object sender, RoutedEventArgs e) => AddDownloadItem("targets", false);
    void OnAddContent(object sender, RoutedEventArgs e) => AddDownloadItem("contents", false);

    void AddDownloadItem(string group, bool askFolder)
    {
        var label = Prompt.Ask(this, UiText.Get("tag-name"), UiText.Get("name"));
        if (string.IsNullOrWhiteSpace(label))
            return;
        if (askFolder)
        {
            Prompt.ChooseFolder(this, "", chosen => Store(chosen), () => Store(""));
            return;
        }
        Store("");
        return;

        void Store(string folder)
        {
            try
            {
                DownloadTags.AddItem(App.Config.DownloadPicker, group, label, folder);
            }
            catch (InvalidOperationException)
            {
                MessageBox.Show(this, UiText.Get("category-exists"), "Easy Tagger");
                return;
            }
            Save();
            RefreshAll();
        }
    }

    void OnProfileModels(object sender, RoutedEventArgs e) => SwitchProfile(TagConfig.ProfileModels);
    void OnProfileDownloads(object sender, RoutedEventArgs e) => SwitchProfile(TagConfig.ProfileDownloads);

    void SwitchProfile(string profileId)
    {
        RememberFolder();
        App.Config.SwitchProfile(profileId);
        _modelFilter = ModelGroups.AllId;
        Save();
        RefreshAll();
    }

    void OnGerman(object sender, RoutedEventArgs e) => SetLanguage(true);
    void OnEnglish(object sender, RoutedEventArgs e) => SetLanguage(false);

    void SetLanguage(bool german)
    {
        UiText.UseGerman(german);
        App.Config.Language = german ? "de" : "en";
        Save();
        ApplyLanguage();
        RefreshAll();
    }

    void OnNavWatch(object sender, RoutedEventArgs e) => WatchSection.BringIntoView();
    void OnNavModels(object sender, RoutedEventArgs e) => ModelsSection.BringIntoView();
    void OnNavDownloads(object sender, RoutedEventArgs e) => DownloadsSection.BringIntoView();
    void OnToggleEncode(object sender, RoutedEventArgs e) => ToggleSection(EncodeBody, EncodeChevron);

    void OnToggleBatch(object sender, RoutedEventArgs e) => ToggleSection(BatchBody, BatchChevron);

    static void ToggleSection(StackPanel body, TextBlock chevron)
    {
        var open = body.Visibility != Visibility.Visible;
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        chevron.Text = open ? "▾" : "▸";
    }

    void OnNavBatch(object sender, RoutedEventArgs e)
    {
        if (BatchBody.Visibility != Visibility.Visible)
            ToggleSection(BatchBody, BatchChevron);
        BatchSection.BringIntoView();
    }
    void OnNavLog(object sender, RoutedEventArgs e) => LogSection.BringIntoView();

    void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        Prompt.ChooseFolder(this, FolderBox.Text.Trim(), folder =>
        {
            FolderBox.Text = folder;
            RememberFolder();
            Save();
        });
    }

    void OnFolderLostFocus(object sender, RoutedEventArgs e)
    {
        RememberFolder();
        Save();
    }

    void RememberFolder() => App.Config.WatchFolder = FolderBox.Text.Trim();

    void OnToggleWatch(object sender, RoutedEventArgs e)
    {
        _watching = !_watching;
        _pending.Clear();
        _ignoredAtStart.Clear();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(0.5, App.Config.PollInterval));
        if (_watching)
        {
            RememberFolder();
            if (!Directory.Exists(App.Config.WatchFolder))
            {
                _watching = false;
                Announce(UiText.Get("watch-missing"));
                ToggleWatchButton.Content = UiText.Get("start");
                UpdateStatus();
                return;
            }

            var present = FileScan.ListCandidates(App.Config).ToList();
            if (!App.Config.ProcessExistingOnStart)
            {
                foreach (var path in present)
                    _ignoredAtStart.Add(path);
                if (present.Count > 0)
                    Log(string.Format(UiText.Get("watch-ignored"), present.Count));
                else
                    Log(ExplainWatchFolder());
            }
            else if (present.Count == 0)
            {
                Log(ExplainWatchFolder());
            }

            Log(string.Format(UiText.Get("watch-started"), App.Config.WatchFolder));
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
        ToggleWatchButton.Content = UiText.Get(_watching ? "stop" : "start");
        UpdateStatus();
    }

    void OnTagExisting(object sender, RoutedEventArgs e) => TagExisting();

    void TagExisting()
    {
        RememberFolder();
        if (!Directory.Exists(App.Config.WatchFolder))
        {
            Announce(UiText.Get("watch-missing"));
            return;
        }

        var files = FileScan.ListCandidates(App.Config).ToList();
        if (files.Count == 0)
        {
            Announce(ExplainWatchFolder());
            return;
        }

        Log(string.Format(UiText.Get("tag-count"), files.Count));
        foreach (var path in files)
        {
            _ignoredAtStart.Remove(path);
            TryTag(path);
        }
    }

    string ExplainWatchFolder()
    {
        var folder = App.Config.WatchFolder;
        var files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).ToList() : [];
        if (files.Count == 0)
            return UiText.Get("watch-none");
        if (App.Config.WatchOnlyFinalNames && files.Any(path => FileScan.IsCandidate(path, App.Config, onlyFinalNames: false)))
            return UiText.Get("watch-final");
        return UiText.Get("watch-none");
    }

    void Poll()
    {
        try
        {
            ScanOnce();
        }
        catch (Exception ex)
        {
            StopWatching();
            Log(string.Format(UiText.Get("watch-error"), ex.Message));
        }
    }

    void StopWatching()
    {
        _watching = false;
        _timer.Stop();
        _pending.Clear();
        ToggleWatchButton.Content = UiText.Get("start");
        UpdateStatus();
    }

    void ScanOnce()
    {
        if (!Directory.Exists(App.Config.WatchFolder))
        {
            StopWatching();
            Log(UiText.Get("watch-gone"));
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in FileScan.ListCandidates(App.Config))
        {
            if (_ignoredAtStart.Contains(path))
                continue;
            seen.Add(path);
            long length;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                continue;
            }

            // Eine Datei, die gerade erst angelegt wurde, bleibt liegen.
            if (length <= 0)
            {
                _pending[path] = new PendingFile(0, 0, DateTime.UtcNow);
                continue;
            }

            if (!_pending.TryGetValue(path, out var pending))
            {
                _pending[path] = new PendingFile(length, 1, DateTime.UtcNow);
                continue;
            }

            if (pending.Length != length)
            {
                pending.Length = length;
                pending.Polls = 1;
                pending.FirstSeen = DateTime.UtcNow;
                continue;
            }

            pending.Polls++;
            if (!FileScan.IsStable(pending.Polls, DateTime.UtcNow - pending.FirstSeen, App.Config))
                continue;
            if (FileScan.IsFileInUse(path))
            {
                if (!pending.LockNoted)
                {
                    pending.LockNoted = true;
                    Log(string.Format(UiText.Get("waiting-locked"), Path.GetFileName(path)));
                }
                continue;
            }
            pending.LockNoted = false;
            _pending.Remove(path);
            TryTag(path);
        }

        foreach (var key in _pending.Keys.Where(key => !seen.Contains(key)).ToList())
            _pending.Remove(key);
    }

    void TryTag(string path)
    {
        var model = App.Config.ActiveFaceModel();
        if (model == null || string.IsNullOrWhiteSpace(model.Name))
        {
            Log(UiText.Get("no-model"));
            return;
        }
        if (Renamer.IsTaggedWith(path, model.Name))
        {
            if (_ignoredAtStart.Add(path))
                Log(string.Format(UiText.Get("already-tagged"), Path.GetFileName(path)));
            return;
        }
        if (App.Config.Action != "rename_only" && string.IsNullOrWhiteSpace(model.Folder))
        {
            Log(model.Name + UiText.Get("no-folder"));
            return;
        }
        try
        {
            var outcome = Renamer.ApplyTag(path, model, App.Config.Action);
            // Die Quelle bleibt beim Kopieren liegen. Ohne diese Marke nimmt
            // der nächste Durchlauf sie erneut und legt eine weitere Kopie an.
            _ignoredAtStart.Add(path);
            Log($"{Path.GetFileName(outcome.SourcePath)}  →  {outcome.TargetPath}");
            if (!App.Config.ReencodeEnabled)
            {
                PushUndo(outcome);
                return;
            }

            var settings = App.Config;
            _ = Task.Run(() =>
            {
                var finalPath = outcome.TargetPath;
                try
                {
                    finalPath = Encoder.MaybeReencode(outcome.TargetPath, settings, message =>
                        Dispatcher.Invoke(() => Log(message)));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Log(ex.Message));
                }

                // Der Rückgängig-Eintrag entsteht erst jetzt, mit dem Pfad,
                // der nach der Neukodierung wirklich auf der Platte liegt.
                Dispatcher.Invoke(() =>
                {
                    if (!string.Equals(finalPath, outcome.TargetPath, StringComparison.OrdinalIgnoreCase))
                        Log($"{Path.GetFileName(outcome.TargetPath)}  →  {finalPath}");
                    PushUndo(outcome with { TargetPath = finalPath });
                });
            });
        }
        catch (InvalidOperationException ex) when (ex.Message == "no-folder")
        {
            Log(model.Name + UiText.Get("no-folder"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RequeueLocked(path);
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0)
            return;
        var last = _undo[^1];
        try
        {
            var restored = Renamer.UndoTag(last.SourcePath, last.TargetPath, last.Action);
            _undo.RemoveAt(_undo.Count - 1);
            SaveUndo();
            Log(restored);
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    void OnMove(object sender, RoutedEventArgs e)
    {
        App.Config.Action = "move";
        Save();
        PaintActionButtons();
        UpdateStatus();
        Log(string.Format(UiText.Get("action-set"), UiText.Get("move")));
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        App.Config.Action = "copy";
        Save();
        PaintActionButtons();
        UpdateStatus();
        Log(string.Format(UiText.Get("action-set"), UiText.Get("copy")));
    }

    void OnRename(object sender, RoutedEventArgs e)
    {
        App.Config.Action = "rename_only";
        Save();
        PaintActionButtons();
        UpdateStatus();
        Log(string.Format(UiText.Get("action-set"), UiText.Get("rename")));
    }

    void OnBrowseTypeFolder(object sender, RoutedEventArgs e)
    {
        Prompt.ChooseFolder(this, TypeFolderBox.Text.Trim(), folder =>
        {
            TypeFolderBox.Text = folder;
            SaveTypeFolder();
        });
    }

    void OnTypeFolderLostFocus(object sender, RoutedEventArgs e) => SaveTypeFolder();

    void SaveTypeFolder()
    {
        if (_suppress)
            return;
        var type = App.Config.DownloadPicker.Types
            .FirstOrDefault(item => item.Id == App.Config.DownloadPicker.LastSelection.Type);
        if (type == null)
            return;
        type.BaseFolder = TypeFolderBox.Text.Trim();
        Save();
        UpdateStatus();
    }

    void OnEncodeCrf(object sender, RoutedEventArgs e)
    {
        App.Config.ReencodeMode = "crf";
        ReadEncodeFromUi();
        Save();
        PaintActionButtons();
    }

    void OnEncodeBitrate(object sender, RoutedEventArgs e)
    {
        App.Config.ReencodeMode = "bitrate";
        ReadEncodeFromUi();
        Save();
        PaintActionButtons();
    }

    void OnPresetChanged(object sender, SelectionChangedEventArgs e) => OnEncodeChanged(sender, e);

    void OnEncodeChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress || !IsLoaded)
            return;
        ReadEncodeFromUi();
        Save();
        RefreshEncodeTools();
    }

    void OnBrowseFfmpeg(object sender, RoutedEventArgs e) => BrowseTool(FfmpegBox);
    void OnBrowseFfprobe(object sender, RoutedEventArgs e) => BrowseTool(FfprobeBox);

    void BrowseTool(TextBox box)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Programme (*.exe)|*.exe|Alle Dateien|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            box.Text = dialog.FileName;
            ReadEncodeFromUi();
            Save();
            RefreshEncodeTools();
        }
    }

    void ReadEncodeFromUi()
    {
        var config = App.Config;
        config.ReencodeEnabled = EncodeEnabledBox.IsChecked == true;
        config.ReencodeKeepOriginal = EncodeKeepBox.IsChecked == true;
        config.FfmpegPath = FfmpegBox.Text.Trim();
        config.FfprobePath = FfprobeBox.Text.Trim();
        if (int.TryParse(EncodeCrfBox.Text.Trim(), out var crf))
            config.ReencodeCrf = Math.Clamp(crf, 0, 51);
        if (int.TryParse(EncodeVideoBox.Text.Trim(), out var video))
            config.ReencodeVideoBitrate = Math.Max(100, video);
        if (int.TryParse(EncodeAudioBox.Text.Trim(), out var audio))
            config.ReencodeAudioBitrate = Math.Max(32, audio);
        if (EncodePresetBox.SelectedItem is string preset && preset.Length > 0)
            config.ReencodePreset = preset;
    }

    void RefreshEncodeTools()
    {
        var ok = Encoder.ToolsAvailable(App.Config, out var message);
        EncodeToolsText.Text = ok
            ? UiText.Get("encode-tools-ok") + "  " + message
            : UiText.Get("encode-tools-missing") + message;
        EncodeToolsText.Foreground = (Brush)FindResource(ok ? "LabelCyan" : "SignalRed");
    }

    void OnSaveHotkey(object sender, RoutedEventArgs e)
    {
        App.Config.Hotkey = HotkeyBox.Text.Trim();
        Save();
        RegisterHotkey();
        UpdateStatus();
    }

    void OnStartInTray(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppress)
            return;
        var wanted = StartInTrayBox.IsChecked == true;
        Dispatcher.BeginInvoke(() => ApplyAutostart(wanted), System.Windows.Threading.DispatcherPriority.Background);
    }

    void ApplyAutostart(bool wanted)
    {
        if (_suppress || StartInTrayBox.IsChecked != wanted)
            return;
        var ok = false;
        string? error = null;
        try
        {
            ok = AutostartService.TrySet(wanted);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException)
        {
            error = ex.Message;
        }

        if (!ok)
        {
            _suppress = true;
            StartInTrayBox.IsChecked = !wanted;
            _suppress = false;
            Log(error == null ? UiText.Get("autostart-failed") : UiText.Get("autostart-failed") + " " + error);
        }
        else
        {
            Log(UiText.Get(wanted ? "autostart-on" : "autostart-off"));
        }

        App.Config.StartInTray = StartInTrayBox.IsChecked == true;
        Save();
    }

    void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppress)
            return;
        App.Config.WatchOnlyFinalNames = FinalNamesBox.IsChecked == true;
        App.Config.ProcessExistingOnStart = ProcessExistingBox.IsChecked == true;
        App.Config.CloseToTray = CloseToTrayBox.IsChecked == true;
        App.Config.StartInTray = StartInTrayBox.IsChecked == true;
        App.Config.ModelBatchFolderTag = FolderTagBox.IsChecked == true;
        Save();
    }

    void RegisterHotkey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;
        UnregisterHotkey();
        if (!NativeHotkey.TryParse(App.Config.Hotkey, out var modifiers, out var key) ||
            !NativeHotkey.RegisterHotKey(handle, NativeHotkey.Id, modifiers, key))
            Log(UiText.Get("hotkey-failed"));
    }

    void UnregisterHotkey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            NativeHotkey.UnregisterHotKey(handle, NativeHotkey.Id);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeHotkey.WmHotkey)
        {
            handled = true;
            Dispatcher.BeginInvoke(OpenPicker);
        }
        else if (SingleInstance.ShowMessage != 0 && (uint)msg == SingleInstance.ShowMessage)
        {
            handled = true;
            Dispatcher.BeginInvoke(ShowFromTray);
        }
        return IntPtr.Zero;
    }

    void OpenPicker()
    {
        if (_picker != null)
        {
            _picker.Activate();
            return;
        }
        var files = SelectedFiles.Capture();
        if (files.Count == 0)
        {
            var message = UiText.Get("no-selection");
            Log(message);
            if (!string.IsNullOrWhiteSpace(SelectedFiles.LastNote))
                Log(SelectedFiles.LastNote);
            _tray?.ShowBalloonTip(3500, "Easy Tagger", message, Forms.ToolTipIcon.Warning);
            return;
        }

        _picker = new PickerWindow(App.Config, files, model => TagChosenFiles(files, model));
        _picker.Closed += (_, _) =>
        {
            _picker = null;
            Save();
            RefreshAll();
        };
        _picker.Show();
    }

    void TagChosenFiles(IReadOnlyList<string> files, FaceModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            Announce(UiText.Get("no-model"));
            return;
        }
        if (App.Config.Action != "rename_only" && string.IsNullOrWhiteSpace(model.Folder))
        {
            Announce(model.Name + UiText.Get("no-folder"));
            return;
        }

        var action = App.Config.Action;
        var reencode = App.Config.ReencodeEnabled;
        var settings = App.Config;
        Log(string.Format(UiText.Get("picker-run"), files.Count, model.Name));
        _ = Task.Run(() =>
        {
            foreach (var path in files)
            {
                try
                {
                    if (Renamer.IsTaggedWith(path, model.Name))
                    {
                        Dispatcher.Invoke(() => Log(string.Format(UiText.Get("already-tagged"), Path.GetFileName(path))));
                        continue;
                    }
                    var outcome = Renamer.ApplyTag(path, model, action);
                    var finalPath = outcome.TargetPath;
                    if (reencode)
                    {
                        finalPath = Encoder.MaybeReencode(outcome.TargetPath, settings, message =>
                            Dispatcher.Invoke(() => Log(message)));
                    }
                    Dispatcher.Invoke(() =>
                    {
                        PushUndo(outcome with { TargetPath = finalPath });
                        Log($"{Path.GetFileName(outcome.SourcePath)}  →  {finalPath}");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Log(ex.Message));
                }
            }
        });
    }

    void Save() => ConfigStore.Save(App.ConfigPath, App.Config);

    string UndoPath => Path.Combine(Path.GetDirectoryName(App.ConfigPath)!, "undo.json");

    void PushUndo(TagOutcome outcome)
    {
        _undo.Add(outcome);
        while (_undo.Count > UndoLimit)
            _undo.RemoveAt(0);
        SaveUndo();
    }

    void LoadUndo()
    {
        if (!File.Exists(UndoPath))
            return;
        try
        {
            var loaded = JsonSerializer.Deserialize<List<TagOutcome>>(File.ReadAllText(UndoPath), ConfigStore.JsonOptions);
            if (loaded != null)
                _undo.AddRange(loaded.TakeLast(UndoLimit));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken or locked undo file must not stop the program.
        }
    }

    void SaveUndo()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UndoPath)!);
            File.WriteAllText(UndoPath, JsonSerializer.Serialize(_undo, ConfigStore.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(ex.Message);
        }
    }

    void OnClearLog(object sender, RoutedEventArgs e) => LogBox.Clear();

    void Announce(string message)
    {
        Log(message);
        LogSection.BringIntoView();
        MessageBox.Show(this, message, "Easy Tagger", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void Log(string message)
    {
        LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    void RequeueLocked(string path)
    {
        long length = 0;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (IOException)
        {
            // The next poll reads the size again.
        }
        _ignoredAtStart.Remove(path);
        _pending[path] = new PendingFile(length, 0, DateTime.UtcNow) { LockNoted = true };
        Log(string.Format(UiText.Get("move-blocked"), Path.GetFileName(path)));
    }

    void RestorePlacement()
    {
        var config = App.Config;
        if (config.WindowWidth < 400 || config.WindowHeight < 300)
            return;
        _restoring = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = config.WindowWidth;
        Height = config.WindowHeight;
        var minLeft = SystemParameters.VirtualScreenLeft;
        var minTop = SystemParameters.VirtualScreenTop;
        var maxLeft = minLeft + SystemParameters.VirtualScreenWidth - 80;
        var maxTop = minTop + SystemParameters.VirtualScreenHeight - 80;
        Left = Math.Clamp(config.WindowLeft, minLeft, Math.Max(minLeft, maxLeft));
        Top = Math.Clamp(config.WindowTop, minTop, Math.Max(minTop, maxTop));
        _restoring = false;
    }

    void SchedulePlacement()
    {
        if (_restoring || !IsLoaded || WindowState != WindowState.Normal || !IsVisible)
            return;
        _geometryTimer.Stop();
        _geometryTimer.Start();
    }

    void SavePlacement()
    {
        if (_restoring || WindowState != WindowState.Normal || !IsVisible)
            return;
        if (double.IsNaN(Left) || double.IsNaN(Top) || Width < 400 || Height < 300)
            return;
        App.Config.WindowLeft = Left;
        App.Config.WindowTop = Top;
        App.Config.WindowWidth = Width;
        App.Config.WindowHeight = Height;
        Save();
    }

    sealed class PendingFile(long length, int polls, DateTime firstSeen)
    {
        public long Length { get; set; } = length;
        public int Polls { get; set; } = polls;
        public DateTime FirstSeen { get; set; } = firstSeen;
        public bool LockNoted { get; set; }
    }
}
