using System.Windows;
using System.Windows.Controls;
using EasyTagger.Core;

namespace EasyTagger.App;

public sealed class BatchRuleWindow : Window
{
    readonly ComboBox _type = new() { Margin = new Thickness(0, 4, 0, 10) };
    readonly TextBox _match = new() { Margin = new Thickness(0, 4, 0, 10) };
    readonly TextBox _tags = new() { Margin = new Thickness(0, 4, 0, 10) };
    readonly CheckBox _enabled = new() { Margin = new Thickness(0, 4, 0, 10) };

    public BatchRule? Result { get; private set; }

    public BatchRuleWindow(Window owner, BatchRule? initial)
    {
        Title = UiText.Get(initial == null ? "batch-add" : "batch-edit");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = owner;
        ResizeMode = ResizeMode.NoResize;
        Background = (System.Windows.Media.Brush)owner.FindResource("Bg");
        Foreground = (System.Windows.Media.Brush)owner.FindResource("Text");
        FontFamily = (System.Windows.Media.FontFamily)owner.FindResource("AppFont");

        _type.Items.Add(new ComboItem(BatchRules.MatchFolder, UiText.Get("batch-folder")));
        _type.Items.Add(new ComboItem(BatchRules.MatchPath, UiText.Get("batch-path")));
        _type.Items.Add(new ComboItem(BatchRules.MatchFilename, UiText.Get("batch-filename")));
        _type.DisplayMemberPath = nameof(ComboItem.Label);
        var current = initial?.MatchType ?? BatchRules.MatchFolder;
        _type.SelectedItem = _type.Items.Cast<ComboItem>().FirstOrDefault(item => item.Id == current) ?? _type.Items[0];
        _match.Text = initial?.Match ?? "";
        _tags.Text = initial?.Tags ?? "";
        _enabled.Content = UiText.Get("batch-enabled");
        _enabled.IsChecked = initial?.Enabled ?? true;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = UiText.Get("batch-type") });
        root.Children.Add(_type);
        root.Children.Add(new TextBlock { Text = UiText.Get("batch-match") });
        root.Children.Add(_match);
        root.Children.Add(new TextBlock { Text = UiText.Get("batch-tags") });
        root.Children.Add(_tags);
        root.Children.Add(_enabled);

        var ok = new Button
        {
            Content = UiText.Get("apply"),
            Style = (Style)owner.FindResource("Chip"),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
        };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_match.Text))
            {
                MessageBox.Show(this, UiText.Get("batch-match-required"), "Easy Tagger");
                return;
            }
            if (BatchRules.ParseTags(_tags.Text).Count == 0)
            {
                MessageBox.Show(this, UiText.Get("batch-tags-required"), "Easy Tagger");
                return;
            }
            Result = new BatchRule
            {
                MatchType = (_type.SelectedItem as ComboItem)?.Id ?? BatchRules.MatchFolder,
                Match = _match.Text.Trim(),
                Tags = _tags.Text.Trim(),
                Enabled = _enabled.IsChecked == true,
            };
            DialogResult = true;
        };
        root.Children.Add(ok);
        Content = root;
    }

    sealed record ComboItem(string Id, string Label);
}
