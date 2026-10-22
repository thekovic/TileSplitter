using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using TileSplitter.Core;

namespace TileSplitter.App;

public sealed class ExportDialog : Window
{
    private readonly ComboBox format = new()
    {
        ItemsSource = new[]
        {
            "PNG",
            "JPEG",
            "BMP"
        },
        MinWidth = 120
    };
    private readonly TextBox directory = new();
    private readonly TextBox prefix = new();
    private readonly TextBox quality = new();
    private readonly TextBox background = new();
    private readonly CheckBox limitFilenameLength = new()
    {
        Content = "Limit filenames to 15 characters (including prefix, excluding extension)",
        Margin = new(0, 10, 0, 4)
    };

    private readonly CheckBox errorsOnly = new()
    {
        Content = "Show only textures with errors",
        Margin = new(0, 0, 0, 8)
    };
    private readonly ListBox list = new()
    {
        MinHeight = 180
    };
    private readonly TextBlock summary = new()
    {
        Margin = new(0, 8, 0, 8),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly Button export = new()
    {
        Content = "Export",
        IsDefault = true,
        Padding = new(20, 5, 20, 5)
    };
    private readonly ImageExporter exporter = new();
    private readonly AtlasImage atlas;
    private readonly DocumentState state;
    private readonly string? projectPath;

    public ExportPlan? Plan { get; private set; }

    public ExportDialog(Window owner, AtlasImage atlas, DocumentState state, string? projectPath)
    {
        Owner = owner;
        Title = "Export textures";
        Width = 720;
        Height = 800;
        MinWidth = 560;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        this.atlas = atlas;
        this.state = state;
        this.projectPath = projectPath;

        var layout = new Grid
        {
            Margin = new(16)
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = layout;

        var panel = new StackPanel();
        layout.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = "Output directory (Alt+D)", Margin = new(0, 4, 0, 4) });
        var row = new DockPanel();
        var browse = new Button
        {
            Content = "_Directory…",
            Padding = new(10, 4, 10, 4)
        };
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(directory);
        panel.Children.Add(row);
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog(this) == true)
            {
                directory.Text = dialog.FolderName;
            }
        };

        Add(panel, "Filename prefix (optional)", prefix);
        Add(panel, "Format", format);
        Add(panel, "JPEG quality (1–100)", quality);
        Add(panel, "Opaque background (#RRGGBB)", background);

        panel.Children.Add(limitFilenameLength);
        panel.Children.Add(errorsOnly);
        panel.Children.Add(summary);

        Grid.SetRow(list, 1);
        layout.Children.Add(list);
        Grid.SetRow(export, 2);
        layout.Children.Add(export);

        directory.Text = state.Export.Directory;
        prefix.Text = state.Export.Prefix;
        format.SelectedItem = state.Export.Format;
        quality.Text = state.Export.JpegQuality.ToString();
        background.Text = state.Export.Background;
        limitFilenameLength.IsChecked = state.Export.LimitFilenameLength;

        directory.TextChanged += (_, _) => Preview();
        prefix.TextChanged += (_, _) => Preview();
        quality.TextChanged += (_, _) => Preview();
        background.TextChanged += (_, _) => Preview();
        format.SelectionChanged += (_, _) => Preview();
        limitFilenameLength.Checked += (_, _) => Preview();
        limitFilenameLength.Unchecked += (_, _) => Preview();
        errorsOnly.Checked += (_, _) => UpdatePreviewList();
        errorsOnly.Unchecked += (_, _) => UpdatePreviewList();
        export.Click += (_, _) =>
        {
            try
            {
                if (Plan is null)
                {
                    return;
                }

                ImageExporter.Preflight(Plan);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                summary.Text = ex.Message;
            }
        };

        Preview();
    }

    private static void Add(Panel panel, string label, Control field)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 3) });
        panel.Children.Add(field);
        System.Windows.Automation.AutomationProperties.SetName(field, label);
    }

    private void Preview()
    {
        try
        {
            if (!int.TryParse(quality.Text, out int jpegQuality))
            {
                throw new InvalidOperationException("JPEG quality must be a whole number.");
            }

            var settings = new ExportSettings(
                (string) format.SelectedItem,
                directory.Text,
                jpegQuality,
                background.Text,
                prefix.Text,
                limitFilenameLength.IsChecked == true);
            Plan = exporter.Plan(atlas, state with { Export = settings }, projectPath);
            UpdatePreviewList();
            summary.Text = $"{Plan.Items.Length} files · {Plan.Unassigned} unassigned tiles · {Plan.EmptyDrafts} empty drafts omitted";
            export.IsEnabled = Plan.Items.Length > 0 && Plan.Items.All(item => item.Error is null);
        }
        catch (Exception ex)
        {
            Plan = null;
            export.IsEnabled = false;
            summary.Text = ex.Message;
            list.ItemsSource = null;
        }
    }

    private void UpdatePreviewList()
    {
        list.ItemsSource = Plan?.Items
            .Where(item => errorsOnly.IsChecked != true || item.Error is not null)
            .Select(item =>
                $"#{item.Id} {item.Name} · {item.Bounds.Width}×{item.Bounds.Height} → {item.Path}"
                + $"{(item.Exists ? " [exists]" : "")}{(item.Error is null ? "" : " — ERROR: " + item.Error)}");
    }
}
