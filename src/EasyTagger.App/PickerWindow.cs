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
        root.Children.Add(_buttons);
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
        Content = root;
        Rebuild();
    }

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
            var button = Card(current.Name, current.Name == _config.ActiveModel);
            button.Click += (_, _) => Choose(current);
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
        var apply = Card(UiText.Get("apply"), true);
        apply.Click += (_, _) => Choose(DownloadTags.SelectionModel(picker));
        _buttons.Children.Add(apply);
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
