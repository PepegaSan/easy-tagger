using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EasyTagger.App;

public static class Prompt
{
    public static string? Ask(Window owner, string title, string label)
    {
        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)owner.FindResource("Bg"),
            Foreground = (System.Windows.Media.Brush)owner.FindResource("Text"),
            FontFamily = (System.Windows.Media.FontFamily)owner.FindResource("AppFont"),
        };
        var input = new TextBox { Margin = new Thickness(0, 8, 0, 12) };
        string? result = null;
        var ok = new Button
        {
            Content = UiText.Get("ok"),
            Style = (Style)owner.FindResource("Chip"),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
        };
        ok.Click += (_, _) =>
        {
            result = input.Text;
            window.DialogResult = true;
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(input);
        panel.Children.Add(Buttons(owner, ok));
        window.Content = panel;
        window.Loaded += (_, _) => input.Focus();
        return window.ShowDialog() == true ? result : null;
    }

    // OK plus a Cancel that Esc also triggers, the way ModelEditorWindow already works.
    public static StackPanel Buttons(Window owner, Button ok)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.Children.Add(ok);
        row.Children.Add(new Button
        {
            Content = UiText.Get("cancel"),
            Style = (Style)owner.FindResource("Chip"),
            IsCancel = true,
        });
        return row;
    }

    static readonly Guid FolderDialogId = new("c3a1e6b4-7d28-4f5a-9b16-2e8d4c0a91f7");

    public static void ChooseFolder(Window owner, string? current, Action<string> apply, Action? whenEmpty = null, string? title = null)
    {
        // Der Dialog darf nicht im selben Mausklick aufgehen. Sonst trifft
        // das Loslassen den Dialog und er schließt sich sofort wieder.
        owner.Dispatcher.BeginInvoke(() =>
        {
            var chosen = ShowFolder(owner, current, title);
            if (!string.IsNullOrWhiteSpace(chosen))
                apply(chosen);
            else
                whenEmpty?.Invoke();
        }, DispatcherPriority.Input);
    }

    static string? ShowFolder(Window owner, string? current, string? title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title ?? UiText.Get("pick-folder"),
            ClientGuid = FolderDialogId,
            DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            dialog.InitialDirectory = current;
        try
        {
            return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            MessageBox.Show(owner, ex.Message, "Easy Tagger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }
}
