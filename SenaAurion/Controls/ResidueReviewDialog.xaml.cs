using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SenaAurion.Services;

namespace SenaAurion.Controls;

public sealed partial class ResidueReviewDialog : UserControl
{
    public ObservableCollection<ResidueGroup> Groups { get; } = new();

    public string HeaderText { get; private set; } = "";
    public string SelectionSummaryText => $"{SelectedCount}/{TotalCount} seleccionados";

    public int TotalCount => Groups.Sum(g => g.Items.Count);
    public int SelectedCount => Groups.Sum(g => g.Items.Count(i => i.IsSelected));

    public event EventHandler<IReadOnlyList<ResidueItem>>? DeleteRequested;

    public ResidueReviewDialog()
    {
        InitializeComponent();
        GroupedView.Source = Groups;
    }

    public void Load(AdvancedUninstallScanResult scan)
    {
        HeaderText = $"Residuos encontrados para: {scan.ProgramName}";

        Groups.Clear();
        foreach (var grp in scan.Items
                     .GroupBy(i => i.Category)
                     .OrderBy(g => g.Key.ToString()))
        {
            var g = new ResidueGroup(grp.Key);
            foreach (var item in grp)
                g.Items.Add(new ResidueItemViewModel(item));
            Groups.Add(g);
        }

        Bindings.Update();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var g in Groups)
            foreach (var i in g.Items)
                i.IsSelected = true;
        Bindings.Update();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var g in Groups)
            foreach (var i in g.Items)
                i.IsSelected = false;
        Bindings.Update();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = Groups.SelectMany(g => g.Items)
            .Where(i => i.IsSelected)
            .Select(i => i.Item)
            .ToArray();

        DeleteRequested?.Invoke(this, selected);
    }
}

public sealed class ResidueGroup
{
    public string Name { get; }
    public ObservableCollection<ResidueItemViewModel> Items { get; } = new();

    public ResidueGroup(ResidueCategory category)
    {
        Name = category switch
        {
            ResidueCategory.Folders => "Carpetas",
            ResidueCategory.Files => "Archivos",
            ResidueCategory.Registry => "Registro",
            ResidueCategory.Services => "Servicios",
            ResidueCategory.ScheduledTasks => "Tareas programadas",
            _ => category.ToString()
        };
    }
}

public sealed class ResidueItemViewModel
{
    public ResidueItem Item { get; }
    public string Path => Item.Path;
    public string TypeText => Item.Type.ToString();

    public bool IsSelected { get; set; } = true;

    public ResidueItemViewModel(ResidueItem item) => Item = item;
}

