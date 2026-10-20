using System.Windows;
using System.Windows.Controls;

namespace TileSplitter.App;

public sealed class InputDialog : Window
{
    private readonly TextBox input = new()
    {
        Margin = new(0, 8, 0, 12),
        MinWidth = 300
    };
    public string Value => input.Text;

    public InputDialog(Window owner, string title, string prompt, string initial)
    {
        Owner = owner;
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel
        {
            Margin = new(20)
        };
        panel.Children.Add(new TextBlock { Text = prompt, MaxWidth = 500, TextWrapping = TextWrapping.Wrap });
        input.Text = initial;
        panel.Children.Add(input);
        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            Padding = new(20, 5, 20, 5),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ok.Click += (_, _) => DialogResult = true;
        panel.Children.Add(ok);
        Content = panel;
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            Margin = new(0, 8, 0, 0)
        };
        panel.Children.Add(cancel);
        Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
    }
}
