using System.Windows;
using System.Windows.Controls;

namespace TileSplitter.App;

public sealed class CollisionDialog : Window
{
    public CollisionPolicy Policy { get; private set; } = CollisionPolicy.Cancel;

    public CollisionDialog(Window owner, IEnumerable<string> paths)
    {
        Owner = owner;
        Title = "Existing export files";
        Width = 650;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel
        {
            Margin = new(16)
        };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "Choose one policy for all listed existing files." });
        panel.Children.Add(new ListBox { ItemsSource = paths, MaxHeight = 260, Margin = new(0, 10, 0, 10) });
        var row = new WrapPanel();
        panel.Children.Add(row);
        foreach (var (text, policy) in new[]
        {
            ("_Overwrite listed files", CollisionPolicy.Overwrite),
            ("_Skip existing files", CollisionPolicy.Skip),
            ("_Cancel", CollisionPolicy.Cancel)
        })
        {
            var button = new Button
            {
                Content = text,
                Margin = new(0, 0, 10, 0),
                Padding = new(10, 6, 10, 6)
            };
            button.Click += (_, _) =>
            {
                Policy = policy;
                DialogResult = policy != CollisionPolicy.Cancel;
            };
            row.Children.Add(button);
        }
    }
}

public sealed class ExportProgress : Window
{
    private readonly TextBlock text = new()
    {
        Margin = new(10),
        TextWrapping = TextWrapping.Wrap
    };
    public void SetText(string value)
    {
        text.Text = value;
    }

    public ExportProgress(Window owner, CancellationTokenSource cancellation)
    {
        Owner = owner;
        Title = "Export progress";
        Width = 450;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel
        {
            Margin = new(16)
        };
        panel.Children.Add(text);
        var button = new Button
        {
            Content = "_Cancel after current texture (Esc)",
            IsCancel = false,
            Padding = new(10)
        };
        button.Click += (_, _) =>
        {
            cancellation.Cancel();
            button.IsEnabled = false;
        };
        panel.Children.Add(button);
        Content = panel;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                cancellation.Cancel();
            }
        };
        Closing += (_, _) => cancellation.Cancel();
    }
}

public sealed class ExportReport : Window
{
    public ExportReport(Window owner, ExportResult result)
    {
        Owner = owner;
        Title = "Export result";
        Width = 750;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel
        {
            Margin = new(16)
        };
        panel.Children.Add(new TextBlock { Text = result.Summary });
        panel.Children.Add(new ListBox
        {
            MaxHeight = 400,
            MinHeight = 150,
            Margin = new(0, 10, 0, 10),
            ItemsSource = result.Items.Select(item =>
                $"{item.Status}: {item.Path}{(item.Error is null ? "" : " — " + item.Error)}")
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Completed files remain. Resolve any errors and export again to retry remaining textures.",
            TextWrapping = TextWrapping.Wrap
        });
        var close = new Button
        {
            Content = "_Close",
            IsDefault = true,
            Margin = new(0, 10, 0, 0)
        };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
    }
}
