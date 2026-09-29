using System.Windows;
using System.Windows.Controls;
using EasyTagger.Core;

namespace EasyTagger.App;

public sealed class ModelEditorWindow : Window
{
    readonly TextBox _name = new() { Margin = new Thickness(0, 4, 0, 10) };
    readonly TextBox _folder = new() { Margin = new Thickness(0, 4, 0, 10) };
    readonly List<(AssignmentRow Row, CheckBox Selected, CheckBox InName)> _rows = [];

    public FaceModel? Result { get; private set; }

    public ModelEditorWindow(Window owner, FaceModel initial, IReadOnlyList<ModelCategory> categories)
    {
        Title = UiText.Get("models");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = owner;
        ResizeMode = ResizeMode.NoResize;
        Background = (System.Windows.Media.Brush)owner.FindResource("Bg");
        Foreground = (System.Windows.Media.Brush)owner.FindResource("Text");
        FontFamily = (System.Windows.Media.FontFamily)owner.FindResource("AppFont");

        _name.Text = initial.Name;
        _folder.Text = initial.Folder;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = UiText.Get("model-name") });
        root.Children.Add(_name);
        root.Children.Add(new TextBlock { Text = UiText.Get("model-folder") });
        var folderRow = new DockPanel();
        var browse = new Button
        {
            Content = "...",
            Style = (Style)owner.FindResource("Chip"),
        };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) =>
        {
            Prompt.ChooseFolder(this, _folder.Text.Trim(), folder => _folder.Text = folder);
        };
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_folder);
        root.Children.Add(folderRow);

        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("categories"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 6),
        });
        var rows = ModelGroups.AssignmentRows(categories, initial);
        if (rows.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = UiText.Get("no-categories"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
        foreach (var row in rows)
        {
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var selected = new CheckBox { Content = row.Label, IsChecked = row.Selected };
            var inName = new CheckBox
            {
                Content = UiText.Get("in-name"),
                IsChecked = row.InName,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            DockPanel.SetDock(inName, Dock.Right);
            line.Children.Add(inName);
            line.Children.Add(selected);
            root.Children.Add(line);
            _rows.Add((row, selected, inName));
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = new Button { Content = UiText.Get("cancel"), Style = (Style)owner.FindResource("Chip"), IsCancel = true };
        var ok = new Button { Content = UiText.Get("ok"), Style = (Style)owner.FindResource("Chip"), IsDefault = true };
        ok.Click += (_, _) => Accept(initial);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    void Accept(FaceModel initial)
    {
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, UiText.Get("model-required"), "Easy Tagger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var model = new FaceModel
        {
            Name = name,
            Folder = _folder.Text.Trim(),
            Categories = initial.Categories.ToList(),
            CategoriesInName = initial.CategoriesInName.ToList(),
        };
        var rows = _rows.Select(entry =>
        {
            var wantsName = entry.InName.IsChecked == true;
            var selected = entry.Selected.IsChecked == true || wantsName;
            return new AssignmentRow
            {
                Id = entry.Row.Id,
                Label = entry.Row.Label,
                WasInName = entry.Row.WasInName,
                Selected = selected,
                InName = wantsName && selected,
            };
        });
        ModelGroups.ApplyAssignment(model, rows);
        Result = model;
        DialogResult = true;
    }
}
