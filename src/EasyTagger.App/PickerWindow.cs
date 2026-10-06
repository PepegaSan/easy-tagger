using System.Windows;
using System.Windows.Controls;
using EasyTagger.Core;

namespace EasyTagger.App;

public sealed class PickerWindow : Window
{
    readonly TagConfig _config;
    readonly IReadOnlyList<string> _files;
    readonly Action<FaceModel> _onChosen;
    string _filter = ModelGroups.AllId;
    readonly WrapPanel _chips = new();
    readonly WrapPanel _buttons = new();
    readonly TextBlock _target = new()
    {
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Muted"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
        MinHeight = 18,
    };

    public PickerWindow(TagConfig config, IReadOnlyList<string> files, Action<FaceModel> onChosen)
    {
        _config = config;
        _files = files;
        _onChosen = onChosen;
        Title = "Easy Tagger";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("AppFont");
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("Bg");
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text");

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "EASY TAGGER",
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text"),
            FontWeight = FontWeights.ExtraBold,
            FontSize = 16,
        });
        root.Children.Add(new TextBlock
        {
            Text = string.Format(UiText.Get("picker-files"), files.Count, Path.GetFileName(files[0])),
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
        });
        root.Children.Add(_chips);
        root.Children.Add(_target);
        // With "Alle" the list gets long, so it scrolls instead of pushing the window off the screen.
        root.Children.Add(new ScrollViewer
        {
            Content = _buttons,
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        var open = new Button
        {
            Content = UiText.Get("open-main"),
            Style = (Style)Application.Current.FindResource("Chip"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };
        open.Click += (_, _) =>
        {
            if (Application.Current.MainWindow is MainWindow main)
                main.ShowFromTray();
        };
        root.Children.Add(open);
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("picker-keys"),
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Muted"),
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0),
        });
        Content = root;
        KeyDown += OnKeyDown;
        Loaded += (_, _) =>
        {
            Activate();
            _buttons.Children.OfType<Button>().FirstOrDefault()?.Focus();
        };
        Rebuild();
    }

    void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        // Enter presses the focused button, the way Space already does.
        if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.FocusedElement is Button focused)
        {
            focused.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }

        var number = DigitValue(e.Key);
        var keyed = number > 0 ? _config.Models.FirstOrDefault(model => model.Key == number) : null;
        if (keyed != null)
        {
            Choose(keyed);
            e.Handled = true;
        }
    }

    // Returns 1–9 for the digit keys and 0 for anything else.
    static int DigitValue(System.Windows.Input.Key key) => key switch
    {
        >= System.Windows.Input.Key.D1 and <= System.Windows.Input.Key.D9 => key - System.Windows.Input.Key.D1 + 1,
        >= System.Windows.Input.Key.NumPad1 and <= System.Windows.Input.Key.NumPad9 => key - System.Windows.Input.Key.NumPad1 + 1,
        _ => 0,
    };

    void Rebuild()
    {
        _chips.Children.Clear();
        _buttons.Children.Clear();
        if (_config.ActiveProfile == TagConfig.ProfileDownloads)
            BuildDownloads();
        else
            BuildModels();
    }

    void BuildModels()
    {
        var groups = ModelGroups.AvailableGroups(_config.Models, _config.ModelCategories, UiText.Get("other"));
        if (_filter != ModelGroups.AllId && groups.All(chip => chip.Id != _filter))
            _filter = ModelGroups.AllId;
        AddFilter(ModelGroups.AllId, $"{UiText.Get("all")} ({_config.Models.Count})");
        foreach (var chip in groups)
            AddFilter(chip.Id, $"{chip.Label} ({chip.Count})");

        foreach (var model in ModelGroups.Filter(_config.Models, _filter))
        {
            var current = model;
            // The number comes from the model itself, so it stays put when the list order changes.
            var label = current.Key is int key ? $"{key}  {current.Name}" : current.Name;
            var button = Card(label, current.Name == _config.ActiveModel);
            if (!string.IsNullOrWhiteSpace(current.Folder))
                button.ToolTip = current.Folder;
            button.Click += (_, _) => Choose(current);
            ShowTargetOnFocus(button, current);
            _buttons.Children.Add(button);
        }
    }

    void AddFilter(string id, string label)
    {
        var button = Card(label, id == _filter);
        button.MinHeight = 0;
        button.MinWidth = 0;
        button.Margin = new Thickness(0, 0, 8, 8);
        button.Click += (_, _) =>
        {
            _filter = id;
            Rebuild();
        };
        _chips.Children.Add(button);
    }

    void BuildDownloads()
    {
        var picker = _config.DownloadPicker;
        AddChoice(picker.Types, picker.LastSelection.Type, id => picker.LastSelection.Type = id);
        AddChoice(picker.Targets, picker.LastSelection.Target, id => picker.LastSelection.Target = id);
        foreach (var item in picker.Contents)
        {
            var current = item;
            var button = Card(current.Label, picker.LastSelection.Contents.Contains(current.Id));
            button.Click += (_, _) =>
            {
                DownloadTags.ToggleContent(picker, current.Id);
                Rebuild();
            };
            _buttons.Children.Add(button);
        }
        var applyModel = DownloadTags.SelectionModel(picker);
        var apply = Card(UiText.Get("apply"), true);
        apply.Click += (_, _) => Choose(applyModel);
        ShowTargetOnFocus(apply, applyModel);
        _buttons.Children.Add(apply);
    }

    // Shows where the first file would end up while a choice is hovered or focused.
    void ShowTargetOnFocus(Button button, FaceModel model)
    {
        button.MouseEnter += (_, _) => _target.Text = TargetText(model);
        button.GotKeyboardFocus += (_, _) => _target.Text = TargetText(model);
        button.MouseLeave += (_, _) => _target.Text = "";
        button.LostKeyboardFocus += (_, _) => _target.Text = "";
    }

    string TargetText(FaceModel model)
    {
        var first = _files[0];
        var oldName = Path.GetFileName(first);
        var newName = Renamer.BuildNewStem(oldName, model.Name).NewStem + Path.GetExtension(first);
        var text = string.Format(UiText.Get("picker-rename"), oldName, newName);
        if (_config.Action != "rename_only" && !string.IsNullOrWhiteSpace(model.Folder))
            text += "  ·  " + string.Format(UiText.Get("picker-folder"), model.Folder);
        if (_files.Count > 1)
            text += string.Format(UiText.Get("picker-more"), _files.Count - 1);
        return text;
    }

    void AddChoice(List<CatalogItem> items, string selected, Action<string> choose)
    {
        foreach (var item in items)
        {
            var current = item;
            var button = Card(current.Label, current.Id == selected);
            button.Click += (_, _) =>
            {
                choose(current.Id);
                Rebuild();
            };
            _chips.Children.Add(button);
        }
    }

    void Choose(FaceModel model)
    {
        Close();
        _onChosen(model);
    }

    static Button Card(string text, bool active)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.FindResource("Chip"),
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 96,
        };
        if (active)
        {
            button.Background = (System.Windows.Media.Brush)Application.Current.FindResource("SignalRed");
            button.BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("SignalRed");
            button.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("OnRed");
        }
        return button;
    }
}
