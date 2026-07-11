using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EvEMapEnhanced.Desktop;

public partial class ManualWormholeDialog : Window
{
    public string? ExitComment { get; private set; }

    /// <summary>Parameterless ctor required by Avalonia XAML loader.</summary>
    public ManualWormholeDialog() : this("Unknown") { }

    public ManualWormholeDialog(string systemName, string? existingComment = null)
    {
        InitializeComponent();
        SystemLabel.Text = $"Система: {systemName}";
        ExitCommentBox.Text = existingComment ?? string.Empty;
        OkButton.Content = existingComment is null ? "Добавить" : "Сохранить";
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        ExitComment = ExitCommentBox.Text?.Trim();
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
