using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyTagger.Core;

namespace EasyTagger.App;

public sealed class PickerWindow : Window
{
    readonly TagConfig _config;
    readonly IReadOnlyList<string> _files;
    readonly Action<FaceModel> _onChosen;
    readonly bool _downloads;
    string _filter = ModelGroups.AllId;
    readonly WrapPanel _chips = new();
    readonly WrapPanel _buttons = new();
    readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 12) };
    readonly TextBlock _target = new()
    {
        Foreground = Res("Muted"),
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        LineHeight = 16,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        // Two fixed lines, so a long preview never shifts the buttons under the mouse.
        Height = 32,
        Margin = new Thickness(0, 4, 0, 0),
    };

    // What Enter picks while the search box has focus: the best match, or the model used last.
    Button? _default;
    // The button that shows the cyan border, so Enter never picks something unseen.
    Button? _marked;

    public PickerWindow(TagConfig config, IReadOnlyList<string> files, Action<FaceModel> onChosen)
    {
        _config = config;
        _files = files;
        _onChosen = onChosen;
        _downloads = config.ActiveProfile == TagConfig.ProfileDownloads;
        Title = "Easy Tagger";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
        Background = Res("Bg");
        Foreground = Res("Text");

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "EASY TAGGER",
            Foreground = Res("Text"),
            FontWeight = FontWeights.ExtraBold,
            FontSize = 16,
        });
        root.Children.Add(new TextBlock
        {
            Text = string.Format(UiText.Get("picker-files"), files.Count, Path.GetFileName(files[0])),
            Foreground = Res("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
        });
        if (!_downloads)
            root.Children.Add(SearchBox());
        root.Children.Add(_chips);
        // With "Alle" the list gets long, so it scrolls instead of pushing the window off the screen.
        root.Children.Add(new ScrollViewer
        {
            Content = _buttons,
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        // Below the list, so the preview changing never moves a button.
        root.Children.Add(_target);
        var open = Card(UiText.Get("open-main"), false);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 8, 0, 0);
        open.Click += (_, _) =>
        {
            if (Application.Current.MainWindow is MainWindow main)
                main.ShowFromTray();
        };
        root.Children.Add(open);
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get(_downloads ? "picker-keys-downloads" : "picker-keys"),
            Foreground = Res("Muted"),
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0),
        });
        Content = root;
        KeyDown += OnKeyDown;
        PreviewTextInput += OnTextInput;
        Loaded += (_, _) =>
        {
            Activate();
            if (_downloads)
                _default?.Focus();
            else
                _search.Focus();
            // The first layout sets the height. After that a shorter result list must not make the window jump.
            Dispatcher.BeginInvoke(() => SizeToContent = SizeToContent.Manual,
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        };
        Rebuild();
    }

    // Typing while a model button has focus still goes into the search.
    void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_downloads || _search.IsKeyboardFocused || string.IsNullOrWhiteSpace(e.Text) || char.IsControl(e.Text[0]))
            return;
        _search.Focus();
        _search.AppendText(e.Text);
        _search.CaretIndex = _search.Text.Length;
        e.Handled = true;
    }

    UIElement SearchBox()
    {
        // A see-through text box of the same style puts the hint exactly where typed text appears.
        var hint = new TextBox
        {
            Text = UiText.Get("picker-search"),
            Foreground = Res("Muted"),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = _search.Margin,
            IsReadOnly = true,
            Focusable = false,
            IsTabStop = false,
            IsHitTestVisible = false,
        };
        _search.TextChanged += (_, _) =>
        {
            hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Rebuild();
        };
        _search.GotKeyboardFocus += (_, _) => Mark(_default);
        _search.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when _default != null:
                    _default.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                    break;
                case Key.Down when _default != null:
                    _default.Focus();
                    e.Handled = true;
                    break;
                case Key.Escape when _search.Text.Length > 0:
                    _search.Clear();
                    e.Handled = true;
                    break;
            }
        };
        return new Grid { Children = { _search, hint } };
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        // Enter presses the focused button, the way Space already does.
        if (e.Key == Key.Enter && Keyboard.FocusedElement is Button focused)
        {
            focused.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }

        // Digits pick by number only while nothing is typed, so a name with a digit stays searchable.
        if (_search.Text.Length > 0)
            return;
        var number = DigitValue(e.Key);
        var keyed = number > 0 ? _config.Models.FirstOrDefault(model => model.Key == number) : null;
        if (keyed != null)
        {
            Choose(keyed);
            e.Handled = true;
        }
    }

    // Returns 1–9 for the digit keys and 0 for anything else.
    static int DigitValue(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
        _ => 0,
    };

    void Rebuild()
    {
        // The buttons are new after a rebuild, so focus goes back to the one that stands for the same thing.
        var focusedId = (Keyboard.FocusedElement as Button)?.Tag is Slot slot ? slot.Id : null;
        _chips.Children.Clear();
        _buttons.Children.Clear();
        _default = null;
        _marked = null;
        if (_downloads)
            BuildDownloads();
        else
            BuildModels();

        var again = focusedId == null
            ? null
            : _chips.Children.OfType<Button>().Concat(_buttons.Children.OfType<Button>())
                .FirstOrDefault(button => button.Tag is Slot item && item.Id == focusedId);
        if (again != null)
            again.Focus();
        else if (_search.IsKeyboardFocused)
            Mark(_default);
    }

    void BuildModels()
    {
        var groups = ModelGroups.AvailableGroups(_config.Models, _config.ModelCategories, UiText.Get("other"));
        if (_filter != ModelGroups.AllId && groups.All(chip => chip.Id != _filter))
            _filter = ModelGroups.AllId;
        AddFilter(ModelGroups.AllId, $"{UiText.Get("all")} ({_config.Models.Count})");
        foreach (var chip in groups)
            AddFilter(chip.Id, $"{chip.Label} ({chip.Count})");

        var query = _search.Text.Trim();
        var models = ModelGroups.Filter(_config.Models, _filter)
            .Where(model => model.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var model in models)
        {
            var current = model;
            // The number comes from the model itself, so it stays put when the list order changes.
            var label = current.Key is int key ? $"{key}  {current.Name}" : current.Name;
            var active = current.Name == _config.ActiveModel;
            var button = Card(label, active, new Slot("model:" + current.Name, active, current));
            if (!string.IsNullOrWhiteSpace(current.Folder))
                button.ToolTip = current.Folder;
            button.Click += (_, _) => Choose(current);
            _buttons.Children.Add(button);
            if (query.Length > 0 ? _default == null : active)
                _default = button;
        }
        _default ??= _buttons.Children.OfType<Button>().FirstOrDefault();

        if (models.Count == 0 && query.Length > 0)
        {
            _buttons.Children.Add(new TextBlock
            {
                Text = string.Format(UiText.Get("picker-no-match"), query),
                Foreground = Res("Muted"),
                Margin = new Thickness(0, 4, 0, 8),
            });
        }
    }

    void AddFilter(string id, string label)
    {
        var active = id == _filter;
        var button = Card(label, active, new Slot("filter:" + id, active, null));
        button.MinHeight = 0;
        button.MinWidth = 0;
        button.Margin = new Thickness(0, 0, 8, 8);
        button.Click += (_, _) =>
        {
            _filter = id;
            Rebuild();
            // Back to the search, so the next letters filter further and Enter picks.
            _search.Focus();
        };
        _chips.Children.Add(button);
    }

    void BuildDownloads()
    {
        var picker = _config.DownloadPicker;
        AddChoice("type", picker.Types, picker.LastSelection.Type, id => picker.LastSelection.Type = id);
        AddChoice("target", picker.Targets, picker.LastSelection.Target, id => picker.LastSelection.Target = id);
        foreach (var item in picker.Contents)
        {
            var current = item;
            var selected = picker.LastSelection.Contents.Contains(current.Id);
            var button = Card(current.Label, selected, new Slot("content:" + current.Id, selected, null));
            button.Click += (_, _) =>
            {
                DownloadTags.ToggleContent(picker, current.Id);
                Rebuild();
            };
            _buttons.Children.Add(button);
        }
        var applyModel = DownloadTags.SelectionModel(picker);
        var apply = Card(UiText.Get("apply"), true, new Slot("apply", true, applyModel));
        apply.Click += (_, _) => Choose(applyModel);
        _buttons.Children.Add(apply);
        _default = apply;
    }

    void AddChoice(string group, List<CatalogItem> items, string selected, Action<string> choose)
    {
        foreach (var item in items)
        {
            var current = item;
            var active = current.Id == selected;
            var button = Card(current.Label, active, new Slot(group + ":" + current.Id, active, null));
            button.Click += (_, _) =>
            {
                choose(current.Id);
                Rebuild();
            };
            _chips.Children.Add(button);
        }
    }

    void Mark(Button? button)
    {
        if (_marked != null)
        {
            if (_marked.Tag is Slot { Active: true })
                _marked.BorderBrush = Res("SignalRed");
            else
                _marked.ClearValue(BorderBrushProperty);
            _marked.ClearValue(BorderThicknessProperty);
        }
        _marked = button;
        if (button != null)
        {
            button.BorderBrush = Res("LabelCyan");
            button.BorderThickness = new Thickness(2);
            // The model used last can sit far down the list. After a rebuild the layout comes first.
            Dispatcher.BeginInvoke(() => button.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        _target.Text = TargetText(Preview(button));
    }

    // A chip without a model of its own previews what Enter would do.
    FaceModel? Preview(Button? button) =>
        (button?.Tag as Slot)?.Model ?? (_default?.Tag as Slot)?.Model;

    // Shows where the first file would end up.
    string TargetText(FaceModel? model)
    {
        if (model == null)
            return "";
        var first = _files[0];
        var oldName = Path.GetFileName(first);
        var newName = Renamer.BuildNewStem(oldName, model.Name).NewStem + Path.GetExtension(first);
        var text = string.Format(UiText.Get("picker-rename"), oldName, newName);
        if (_config.Action != "rename_only")
        {
            text += "  ·  " + (string.IsNullOrWhiteSpace(model.Folder)
                ? UiText.Get("picker-no-folder")
                : string.Format(UiText.Get("picker-folder"), model.Folder));
        }
        if (_files.Count > 1)
            text += string.Format(UiText.Get("picker-more"), _files.Count - 1);
        return text;
    }

    void Choose(FaceModel model)
    {
        // The model picked here is the one the next picker offers first, so Enter repeats it.
        if (!_downloads && _config.Models.Contains(model))
            _config.ActiveModel = model.Name;
        Close();
        _onChosen(model);
    }

    Button Card(string text, bool active, Slot? slot = null)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.FindResource("Chip"),
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 96,
            Tag = slot,
            // The cyan border from Mark is the focus mark here, a second ring would only blur it.
            FocusVisualStyle = null,
        };
        if (active)
        {
            button.Background = Res("SignalRed");
            button.BorderBrush = Res("SignalRed");
            button.Foreground = Res("OnRed");
        }
        button.GotKeyboardFocus += (_, _) => Mark(button);
        button.MouseEnter += (_, _) => _target.Text = TargetText(Preview(button));
        button.MouseLeave += (_, _) => _target.Text = TargetText(Preview(_marked));
        return button;
    }

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    // What a button stands for, so the same one can get focus back after a rebuild.
    sealed record Slot(string Id, bool Active, FaceModel? Model);
}
