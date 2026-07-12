using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EvEMapEnhanced.Core.Routing;

namespace EvEMapEnhanced.Desktop;

public partial class ManualWormholeDialog : Window
{
    public int? ExitSystemId { get; private set; }

    private readonly UniverseMap _map;

    /// <summary>Parameterless ctor required by Avalonia XAML loader.</summary>
    public ManualWormholeDialog() : this("Unknown", new UniverseMap([], [])) { }

    public ManualWormholeDialog(
        string systemName,
        UniverseMap map,
        IEnumerable<string>? systemNames = null,
        int? existingExitSystemId = null)
    {
        InitializeComponent();
        _map = map;
        SystemLabel.Text = $"Система: {systemName}";
        ExitSystemBox.ItemsSource = systemNames ?? map.Systems.Values.Select(s => s.Name).OrderBy(n => n);
        if (existingExitSystemId is int exitId && map.Get(exitId) is { } exitSystem)
        {
            ExitSystemBox.Text = exitSystem.Name;
            OkButton.Content = "Сохранить";
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        ValidationText.IsVisible = false;
        var name = ExitSystemBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ExitSystemId = null;
            Close(true);
            return;
        }

        var system = _map.FindByName(name);
        if (system is null)
        {
            ValidationText.Text = "Система не найдена. Выберите систему из списка.";
            ValidationText.IsVisible = true;
            return;
        }

        ExitSystemId = system.Id;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
