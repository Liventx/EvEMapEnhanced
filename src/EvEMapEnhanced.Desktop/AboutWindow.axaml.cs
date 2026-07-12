using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace EvEMapEnhanced.Desktop;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ProductNameText.Text = AppMetadata.ProductName;
        VersionText.Text = $"Версия {AppMetadata.CurrentVersion}";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnGitHubLinkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            OpenGitHub();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        GitHubLink.PointerPressed += OnGitHubLinkPointerPressed;
    }

    protected override void OnClosed(EventArgs e)
    {
        GitHubLink.PointerPressed -= OnGitHubLinkPointerPressed;
        base.OnClosed(e);
    }

    private static void OpenGitHub()
    {
        try
        {
            BrowserLauncher.OpenOrThrow(AppMetadata.GitHubRepositoryUrl);
        }
        catch
        {
            // Ignore: opening the browser from About is best-effort.
        }
    }
}
