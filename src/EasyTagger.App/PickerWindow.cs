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
    // Gets the chosen name and folder plus the profile whose action and re-encoding apply.
    readonly Action<FaceModel, string> _onChosen;
    // The tab shown. It starts on the main window's profile but switching it leaves that profile alone,
    // so a running download watch keeps going while a few files get a model.
    string _profile;
    bool _downloads => _profile == TagConfig.ProfileDownloads;
    IPickerProfile View => _config.PickerProfile(_profile);
    readonly WrapPanel _tabs = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _keys = new() { FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };
    UIElement? _searchBox;
    string _filter = ModelGroups.FavId;
    readonly WrapPanel _chips = new();
    // Downloads: one labelled row each for type, target and content, so the three never blur together.
    readonly StackPanel _rows = new();
    readonly WrapPanel _buttons = new();
    // Fav and "Alle" hold every model, so the list scrolls instead of pushing the window off the screen.
    // The cap only holds while the window fits its content; after that the list fills the window.
    const double ListHeight = 380;
    const double GlowRoom = 5;
    readonly ScrollViewer _list = new()
    {
        MaxHeight = ListHeight,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    // Set once the size comes from the user, saved or dragged. Then the window keeps it, even across tabs.
    bool _userSize;
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
    // Its border from before, so unmarking puts back the red of a chosen model or the cyan of a chosen category.
    object? _markedBorder;

    public PickerWindow(TagConfig config, IReadOnlyList<string> files, Action<FaceModel, string> onChosen)
    {
        _config = config;
        _files = files;
        _onChosen = onChosen;
        _profile = config.ActiveProfile;
        Title = "Easy Tagger";
        Width = 560;
        MinWidth = 360;
        MinHeight = 300;
        if (config.PickerWidth >= MinWidth && config.PickerHeight >= MinHeight)
        {
            // A smaller screen than last time must not push the window off it.
            var area = SystemParameters.WorkArea;
            Width = Math.Min(config.PickerWidth, area.Width);
            Height = Math.Min(config.PickerHeight, area.Height);
            _userSize = true;
            _list.MaxHeight = double.PositiveInfinity;
        }
        else
        {
            SizeToContent = SizeToContent.Height;
        }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
        Background = Res("Bg");
        Foreground = Res("Text");

        var top = new StackPanel();
        // The tabs sit top right, like the profile buttons in the main window.
        var header = new DockPanel();
        DockPanel.SetDock(_tabs, Dock.Right);
        header.Children.Add(_tabs);
        header.Children.Add(new TextBlock
        {
            Text = "EASY TAGGER",
            Foreground = Res("Text"),
            FontWeight = FontWeights.ExtraBold,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });
        top.Children.Add(header);
        top.Children.Add(new TextBlock
        {
            Text = string.Format(UiText.Get("picker-files"), files.Count, Path.GetFileName(files[0])),
            Foreground = Res("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
        });
        _searchBox = SearchBox();
        top.Children.Add(_searchBox);
        top.Children.Add(_chips);

        var bottom = new StackPanel();
        // Below the list, so the preview changing never moves a button.
        bottom.Children.Add(_target);
        var open = Card(UiText.Get("open-main"), false);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 8, 0, 0);
        open.Click += (_, _) =>
        {
            if (Application.Current.MainWindow is MainWindow main)
                main.ShowFromTray();
        };
        bottom.Children.Add(open);
        _keys.Foreground = Res("Muted");
        bottom.Children.Add(_keys);

        // The list fills what the rest leaves, so a taller window shows more models.
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        // The scroll area clips at its edge. It reaches a few pixels further out and the content moves back
        // in by the same amount, so the red glow of the models at the edge stays whole and nothing shifts.
        _list.Margin = new Thickness(-GlowRoom, -GlowRoom, -GlowRoom, 0);
        _list.Content = new StackPanel { Margin = new Thickness(GlowRoom, GlowRoom, GlowRoom, 0), Children = { _rows, _buttons } };
        root.Children.Add(_list);
        Content = root;
        // Once the height is frozen, only a drag of the border changes the size.
        SizeChanged += (_, _) =>
        {
            if (IsLoaded && SizeToContent == SizeToContent.Manual)
                _userSize = true;
        };
        Closing += (_, _) =>
        {
            if (!_userSize || WindowState != WindowState.Normal)
                return;
            _config.PickerWidth = ActualWidth;
            _config.PickerHeight = ActualHeight;
        };
        // PreviewKeyDown on the window, so digits and Enter still work while the search box has focus.
        // A TextBox marks KeyDown handled and the bubbling handler on the window never runs.
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnTextInput;
        Loaded += (_, _) =>
        {
            Activate();
            FocusStart();
            FreezeHeight();
        };
        ShowProfile(_profile);
    }

    void ShowProfile(string profile)
    {
        var switching = profile != _profile;
        _profile = profile;
        _filter = ModelGroups.FavId;
        _search.Clear();
        _tabs.Children.Clear();
        AddTab(TagConfig.ProfileModels, "models");
        AddTab(TagConfig.ProfileDownloads, "downloads");
        _searchBox!.Visibility = _downloads ? Visibility.Collapsed : Visibility.Visible;
        _keys.Text = UiText.Get(_downloads ? "picker-keys-downloads" : "picker-keys");
        Rebuild();
        if (!switching)
            return;
        FocusStart();
        if (_userSize)
            return;
        // The other tab has a different height; let it size itself once, then hold still again.
        _list.MaxHeight = ListHeight;
        SizeToContent = SizeToContent.Height;
        FreezeHeight();
    }

    void AddTab(string profile, string label)
    {
        var tab = Card(UiText.Get(label), profile == _profile);
        tab.MinWidth = 0;
        tab.Margin = new Thickness(8, 0, 0, 0);
        tab.Click += (_, _) => ShowProfile(profile);
        _tabs.Children.Add(tab);
    }

    void FocusStart()
    {
        if (_downloads)
            _default?.Focus();
        else
            _search.Focus();
    }

    // The first layout sets the height. After that a shorter result list must not make the window jump,
    // and the list takes whatever height the window gets.
    void FreezeHeight() =>
        Dispatcher.BeginInvoke(() =>
        {
            SizeToContent = SizeToContent.Manual;
            _list.MaxHeight = double.PositiveInfinity;
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);

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
        return new Grid { Children = { _search, hint } };
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ShowProfile(_downloads ? TagConfig.ProfileModels : TagConfig.ProfileDownloads);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (!_downloads && _search.IsKeyboardFocused && _search.Text.Length > 0)
            {
                _search.Clear();
                e.Handled = true;
                return;
            }
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (!_downloads && _search.IsKeyboardFocused && _default != null)
            {
                _default.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }
            if (Keyboard.FocusedElement is Button focused)
            {
                focused.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Down && !_downloads && _search.IsKeyboardFocused && _default != null)
        {
            _default.Focus();
            e.Handled = true;
            return;
        }

        // Digits pick by number only while nothing is typed, so a name with a digit stays searchable.
        if (_search.Text.Length > 0 || _downloads)
            return;
        var number = DigitValue(e.Key);
        var keyed = number > 0 ? View.Models.FirstOrDefault(model => model.Key == number) : null;
        if (keyed == null)
            return;
        Choose(keyed);
        e.Handled = true;
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
        _rows.Children.Clear();
        _buttons.Children.Clear();
        _default = null;
        _marked = null;
        if (_downloads)
            BuildDownloads();
        else
            BuildModels();

        var again = focusedId == null
            ? null
            : _chips.Children.OfType<Button>()
                .Concat(_rows.Children.OfType<WrapPanel>().SelectMany(row => row.Children.OfType<Button>()))
                .Concat(_buttons.Children.OfType<Button>())
                .FirstOrDefault(button => button.Tag is Slot item && item.Id == focusedId);
        if (again != null)
            again.Focus();
        else if (_search.IsKeyboardFocused)
            Mark(_default);
    }

    void BuildModels()
    {
        var view = View;
        var groups = ModelGroups.AvailableGroups(view.Models, view.ModelCategories, UiText.Get("other"));
        if (_filter is not (ModelGroups.FavId or ModelGroups.AllId) && groups.All(chip => chip.Id != _filter))
            _filter = ModelGroups.FavId;
        AddFilter(ModelGroups.FavId, UiText.Get("fav"));
        AddFilter(ModelGroups.AllId, $"{UiText.Get("all")} ({view.Models.Count})");
        foreach (var chip in groups)
            AddFilter(chip.Id, $"{chip.Label} ({chip.Count})");

        var query = _search.Text.Trim();
        var models = ModelGroups.Filter(view.Models, _filter)
            .Where(model => model.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var model in models)
        {
            var current = model;
            // The number comes from the model itself, so it stays put when the list order changes.
            var label = current.Key is int key ? $"{key}  {current.Name}" : current.Name;
            var active = current.Name == view.ActiveModel;
            var button = Card(label, active, new Slot("model:" + current.Name, active, current), "ModelChip");
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
        var button = Card(label, active, new Slot("filter:" + id, active, null), "CategoryChip");
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
        var picker = View.DownloadPicker;
        AddChoice("type", picker.Types, picker.LastSelection.Type, id => picker.LastSelection.Type = id);
        AddChoice("target", picker.Targets, picker.LastSelection.Target, id => picker.LastSelection.Target = id);
        var contents = Row("content");
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
            contents.Children.Add(button);
        }
        var applyModel = DownloadTags.SelectionModel(picker);
        var apply = Card(UiText.Get("apply"), true, new Slot("apply", true, applyModel));
        apply.Click += (_, _) => Choose(applyModel);
        _buttons.Children.Add(apply);
        _default = apply;
    }

    void AddChoice(string group, List<CatalogItem> items, string selected, Action<string> choose)
    {
        var row = Row(group);
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
            row.Children.Add(button);
        }
    }

    // A row that starts with the group's name: "type", "target" or "content".
    WrapPanel Row(string group)
    {
        var row = new WrapPanel();
        row.Children.Add(new TextBlock
        {
            Text = UiText.Get(group),
            Foreground = Res("Muted"),
            Width = 64,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        });
        _rows.Children.Add(row);
        return row;
    }

    void Mark(Button? button)
    {
        if (_marked != null)
        {
            if (_markedBorder is Brush border)
                _marked.BorderBrush = border;
            else
                _marked.ClearValue(BorderBrushProperty);
            _marked.ClearValue(BorderThicknessProperty);
        }
        _marked = button;
        if (button != null)
        {
            _markedBorder = button.ReadLocalValue(BorderBrushProperty);
            // On a chosen category, which is cyan already, a cyan border would vanish.
            button.BorderBrush = Equals(_markedBorder, Res("LabelCyan")) ? Res("Text") : Res("LabelCyan");
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
        if (View.Action != "rename_only")
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
        // Each pick also moves it up in Fav.
        if (!_downloads && View.Models.Contains(model))
        {
            View.ActiveModel = model.Name;
            model.Uses++;
        }
        Close();
        _onChosen(model, _profile);
    }

    Button Card(string text, bool active, Slot? slot = null, string style = "Chip")
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.FindResource(style),
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 96,
            Tag = slot,
            // The cyan border from Mark is the focus mark here, a second ring would only blur it.
            FocusVisualStyle = null,
        };
        if (active)
        {
            // A chosen category lights up cyan, so it never looks like a model picked in red.
            var category = style == "CategoryChip";
            button.Background = Res(category ? "LabelCyan" : "SignalRed");
            button.BorderBrush = Res(category ? "LabelCyan" : "SignalRed");
            button.Foreground = Res(category ? "OnCyan" : "OnRed");
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
