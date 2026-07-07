using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EvEMapEnhanced.Core.Structures;

namespace EvEMapEnhanced.Desktop;

public partial class StructuresWindow : Window
{
    private readonly AppServices _services;
    private readonly System.Action _onStructuresChanged;
    private List<UserStructure> _structures = new();

    /// <summary>Parameterless ctor required by Avalonia XAML loader.</summary>
    public StructuresWindow() : this(new AppServices(), static () => { }) { }

    public StructuresWindow(AppServices services, System.Action onStructuresChanged)
    {
        _services = services;
        _onStructuresChanged = onStructuresChanged;
        InitializeComponent();
        PopulateStructureKinds();
        LoadStructuresList();
        RefreshSystemNameLookups();
    }

    private void PopulateStructureKinds()
    {
        foreach (var kind in System.Enum.GetValues<StructureKind>())
        {
            StructureKindCombo.Items.Add(new ComboBoxItem { Content = kind.ToString(), Tag = kind });
        }
        StructureKindCombo.SelectedIndex = 0;
        UpdateLinkedSystemVisibility((StructureKind)((ComboBoxItem)StructureKindCombo.SelectedItem!).Tag!);
    }

    private void RefreshSystemNameLookups()
    {
        if (_services.Map is null) return;
        var names = _services.Map.Systems.Values.Select(s => s.Name).OrderBy(n => n).ToList();
        StructureSystemBox.ItemsSource = names;
        StructureLinkedSystemBox.ItemsSource = names;
    }

    private void OnStructureKindChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (StructureKindCombo.SelectedItem is ComboBoxItem item && item.Tag is StructureKind kind)
        {
            UpdateLinkedSystemVisibility(kind);
        }
    }

    private void UpdateLinkedSystemVisibility(StructureKind kind)
    {
        bool needsLink = kind.IsJumpEdge();
        LinkedSystemLabel.IsVisible = needsLink;
        StructureLinkedSystemBox.IsVisible = needsLink;
    }

    private void LoadStructuresList()
    {
        _structures = _services.UserStructures.LoadAll().ToList();
        RenderStructuresList();
    }

    private void RenderStructuresList()
    {
        var map = _services.Map;
        var lines = _structures.Select(s =>
        {
            string sysName = map?.Get(s.SolarSystemId)?.Name ?? $"#{s.SolarSystemId}";
            string linked = s.LinkedSystemId is int linkedId ? $" <-> {map?.Get(linkedId)?.Name ?? $"#{linkedId}"}" : "";
            return $"{s.Kind}: {s.Name} @ {sysName}{linked}  [{s.Access}]";
        }).ToList();

        StructuresList.ItemsSource = lines;
    }

    private void OnAddStructureClick(object? sender, RoutedEventArgs e)
    {
        if (_services.Map is null) return;
        var map = _services.Map;

        var system = map.FindByName(StructureSystemBox.Text ?? string.Empty);
        if (system is null) return;

        if (StructureKindCombo.SelectedItem is not ComboBoxItem { Tag: StructureKind kind }) return;

        int? linkedId = null;
        if (kind.IsJumpEdge())
        {
            var linked = map.FindByName(StructureLinkedSystemBox.Text ?? string.Empty);
            if (linked is null) return;
            linkedId = linked.Id;
        }

        var structure = new UserStructure
        {
            SolarSystemId = system.Id,
            Kind = kind,
            Name = string.IsNullOrWhiteSpace(StructureNameBox.Text) ? kind.ToString() : StructureNameBox.Text!,
            OwnerTag = string.IsNullOrWhiteSpace(StructureOwnerBox.Text) ? null : StructureOwnerBox.Text,
            Access = (StructureAccessLevel)StructureAccessCombo.SelectedIndex,
            LinkedSystemId = linkedId,
            StrontHours = StrontUpDown.Value is { } stront && stront > 0 ? (double)stront : null,
            Notes = string.IsNullOrWhiteSpace(StructureNotesBox.Text) ? null : StructureNotesBox.Text,
        };

        _services.UserStructures.Save(structure);
        _structures.Add(structure);
        _services.ReloadStructuresOnly();
        RenderStructuresList();
        _onStructuresChanged();
    }

    private void OnDeleteStructureClick(object? sender, RoutedEventArgs e)
    {
        int index = StructuresList.SelectedIndex;
        if (index < 0 || index >= _structures.Count) return;

        var structure = _structures[index];
        _services.UserStructures.Delete(structure.Id);
        _structures.RemoveAt(index);
        _services.ReloadStructuresOnly();
        RenderStructuresList();
        _onStructuresChanged();
    }
}
