using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using EvEMapEnhanced.Desktop;

namespace EvEMapEnhanced.Desktop.Tests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>Shared headless Avalonia session so UI-thread work runs against a real app once per run.</summary>
public sealed class HeadlessSessionFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("Headless")]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSessionFixture> { }

/// <summary>
/// Covers the region-tuning developer tools now grouped under their own top-level "Отладка" menu:
/// that the menu exists in the right place with the three tools, and that the two toggles are wired
/// through to the map's debug-grid / region-edit state.
/// </summary>
[Collection("Headless")]
public class DebugMenuTests
{
    private const string DebugGridHeader = "Отладочная сетка (координаты регионов)";
    private const string RegionEditHeader = "Редактировать регионы (перетаскивание мышью)";
    private const string ExportHeader = "Экспортировать координаты регионов...";

    private readonly HeadlessUnitTestSession _session;

    public DebugMenuTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    private static Menu TopMenu(MainWindow window) =>
        window.GetLogicalDescendants().OfType<Menu>().First();

    private static MenuItem TopMenuItem(MainWindow window, string header) =>
        TopMenu(window).Items.OfType<MenuItem>().First(m => (string?)m.Header == header);

    [Fact]
    public Task Debug_menu_sits_immediately_after_the_map_menu() =>
        _session.Dispatch(() =>
        {
            var window = new MainWindow();

            var headers = TopMenu(window).Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
            int mapIndex = headers.IndexOf("Карта");
            int debugIndex = headers.IndexOf("Отладка");

            Assert.True(mapIndex >= 0, "there should be a top-level \"Карта\" menu");
            Assert.True(debugIndex >= 0, "there should be a top-level \"Отладка\" menu");
            Assert.Equal(mapIndex + 1, debugIndex);
        }, CancellationToken.None);

    [Fact]
    public Task Debug_menu_groups_the_three_region_tuning_tools() =>
        _session.Dispatch(() =>
        {
            var window = new MainWindow();

            var debugItems = TopMenuItem(window, "Отладка")
                .Items.OfType<MenuItem>()
                .Select(m => m.Header as string)
                .ToList();

            Assert.Contains(DebugGridHeader, debugItems);
            Assert.Contains(RegionEditHeader, debugItems);
            Assert.Contains(ExportHeader, debugItems);
        }, CancellationToken.None);

    [Fact]
    public Task Map_menu_no_longer_contains_the_debug_tools() =>
        _session.Dispatch(() =>
        {
            var window = new MainWindow();

            var mapDescendants = TopMenuItem(window, "Карта")
                .GetLogicalDescendants().OfType<MenuItem>()
                .Select(m => m.Header as string)
                .ToList();

            Assert.DoesNotContain(DebugGridHeader, mapDescendants);
            Assert.DoesNotContain(RegionEditHeader, mapDescendants);
            Assert.DoesNotContain(ExportHeader, mapDescendants);
        }, CancellationToken.None);

    [Fact]
    public Task Toggling_the_debug_grid_item_drives_the_map() =>
        _session.Dispatch(() =>
        {
            var window = new MainWindow();
            var map = window.FindControl<MapControl>("RouteMap")!;
            var item = window.FindControl<MenuItem>("DebugGridMenuItem")!;

            Assert.False(map.ShowDebugGrid);

            item.IsChecked = true;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(map.ShowDebugGrid);

            item.IsChecked = false;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(map.ShowDebugGrid);
        }, CancellationToken.None);

    [Fact]
    public Task Toggling_the_region_edit_item_drives_the_map() =>
        _session.Dispatch(() =>
        {
            var window = new MainWindow();
            var map = window.FindControl<MapControl>("RouteMap")!;
            var item = window.FindControl<MenuItem>("RegionEditMenuItem")!;

            Assert.False(map.RegionEditMode);

            item.IsChecked = true;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(map.RegionEditMode);

            item.IsChecked = false;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(map.RegionEditMode);
        }, CancellationToken.None);
}
