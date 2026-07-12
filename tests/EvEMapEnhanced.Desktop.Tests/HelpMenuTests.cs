using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using EvEMapEnhanced.Desktop;

namespace EvEMapEnhanced.Desktop.Tests;

[Collection("Headless")]
public class HelpMenuTests
{
    private readonly HeadlessUnitTestSession _session;

    public HelpMenuTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    private static Menu TopMenu(MainWindow window) =>
        window.GetLogicalDescendants().OfType<Menu>().First();

    private static MenuItem TopMenuItem(MainWindow window, string header) =>
        TopMenu(window).Items.OfType<MenuItem>().First(m => (string?)m.Header == header);

    [Fact]
    public Task Help_menu_sits_after_the_visible_map_menu() =>
        _session.Dispatch(() =>
        {
            var window = new MainWindow();

            var headers = TopMenu(window).Items
                .OfType<MenuItem>()
                .Where(m => m.IsVisible)
                .Select(m => m.Header as string)
                .ToList();
            int mapIndex = headers.IndexOf("Карта");
            int helpIndex = headers.IndexOf("Помощь");

            Assert.True(mapIndex >= 0, "there should be a visible top-level \"Карта\" menu");
            Assert.True(helpIndex >= 0, "there should be a top-level \"Помощь\" menu");
            Assert.Equal(mapIndex + 1, helpIndex);
        }, CancellationToken.None);

    [Fact]
    public Task Help_menu_contains_update_and_about_entries() =>
        _session.Dispatch(() =>
        {
            var window = new MainWindow();

            var helpItems = TopMenuItem(window, "Помощь")
                .Items.OfType<MenuItem>()
                .Select(m => m.Header as string)
                .ToList();

            Assert.Contains("Проверка обновлений", helpItems);
            Assert.Contains("О программе", helpItems);
            Assert.Equal(2, helpItems.Count);
        }, CancellationToken.None);

    [Fact]
    public Task About_dialog_shows_product_details_author_and_github_link() =>
        _session.Dispatch(() =>
        {
            var dialog = new AboutWindow();

            Assert.Equal(AppMetadata.ProductName, dialog.FindControl<TextBlock>("ProductNameText")!.Text);
            Assert.Equal($"Версия {AppMetadata.CurrentVersion}", dialog.FindControl<TextBlock>("VersionText")!.Text);
            Assert.Equal("GitHub", dialog.FindControl<TextBlock>("GitHubLink")!.Text);
        }, CancellationToken.None);
}

public class GitHubReleaseCheckerTests
{
    [Theory]
    [InlineData("v1.0.4", "1.0.3", true)]
    [InlineData("1.0.3", "1.0.3", false)]
    [InlineData("1.0.2", "1.0.3", false)]
    public void IsNewerVersion_compares_semver_tags(string latest, string current, bool expected) =>
        Assert.Equal(expected, GitHubReleaseChecker.IsNewerVersion(latest, current));

    [Theory]
    [InlineData("v1.0.3", "1.0.3")]
    [InlineData("V2.0.0", "2.0.0")]
    public void NormalizeVersionTag_strips_leading_v(string tag, string expected) =>
        Assert.Equal(expected, GitHubReleaseChecker.NormalizeVersionTag(tag));
}
