using System.Windows;
using System.Windows.Controls;

namespace EasyTagger.App;

public sealed class DownloadItemWindow : Window
{
    readonly TextBox _name = new() { Margin = new Thickness(0, 4, 0, 10) };
    readonly TextBox _folder = new() { Margin = new Thickness(0, 4, 0, 10) };

    public string? Label { get; private set; }
    public string Folder { get; private set; } = "";

    public DownloadItemWindow(Window owner, string group, string label, string folder)
    {
        Title = UiText.Get("edit");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = owner;
        ResizeMode = ResizeMode.NoResize;
        Background = (System.Windows.Media.Brush)owner.FindResource("Bg");
        Foreground = (System.Windows.Media.Brush)owner.FindResource("Text");
        FontFamily = (System.Windows.Media.FontFamily)owner.FindResource("AppFont");

        _name.Text = label;
        _folder.Text = folder;
        var showFolder = group is "types" or "targets";

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = UiText.Get("name") });
        root.Children.Add(_name);
        if (showFolder)
        {
            root.Children.Add(new TextBlock
            {
                Text = group == "types" ? UiText.Get("type-folder") : UiText.Get("folder-name"),
            });
            root.Children.Add(_folder);
        }

        var ok = new Button
        {
            Content = UiText.Get("apply"),
            Style = (Style)owner.FindResource("Chip"),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
        };
        ok.Click += (_, _) =>
        {
            Label = _name.Text.Trim();
            Folder = _folder.Text.Trim();
            DialogResult = true;
        };
        root.Children.Add(ok);
        Content = root;
        Loaded += (_, _) => _name.Focus();
    }
}
