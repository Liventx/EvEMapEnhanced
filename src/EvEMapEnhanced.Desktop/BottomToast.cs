using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace EvEMapEnhanced.Desktop;

public enum ToastKind
{
    Info,
    Success,
    Warning,
}

/// <summary>Short-lived bottom-left toast for diagnostic feedback that should not crowd the toolbar.</summary>
public sealed class BottomToast : UserControl
{
    private const double FadeInMs = 180;
    private const double VisibleMs = 5500;
    private const double FadeOutMs = 350;

    private readonly Border _panel;
    private readonly TextBlock _text;
    private DispatcherTimer? _timer;
    private DateTime _shownAt;

    public BottomToast()
    {
        IsHitTestVisible = false;
        _text = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        };
        _panel = new Border
        {
            Padding = new Thickness(10, 7),
            CornerRadius = new CornerRadius(4),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 2,
                Blur = 8,
                Color = Color.FromArgb(60, 0, 0, 0),
            }),
            Child = _text,
            IsVisible = false,
            Opacity = 0,
        };
        Content = _panel;
    }

    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        ApplyStyle(kind);
        _text.Text = message;
        _panel.IsVisible = true;
        _shownAt = DateTime.UtcNow;
        _panel.Opacity = 0;

        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        if (!_timer.IsEnabled)
            _timer.Start();

        OnTick(null, EventArgs.Empty);
    }

    public void Hide()
    {
        _timer?.Stop();
        _panel.IsVisible = false;
        _panel.Opacity = 0;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double elapsedMs = (DateTime.UtcNow - _shownAt).TotalMilliseconds;
        if (elapsedMs >= VisibleMs + FadeOutMs)
        {
            Hide();
            return;
        }

        _panel.Opacity = elapsedMs switch
        {
            < FadeInMs => elapsedMs / FadeInMs,
            < VisibleMs => 1.0,
            _ => Math.Max(0, 1.0 - (elapsedMs - VisibleMs) / FadeOutMs),
        };
    }

    private void ApplyStyle(ToastKind kind)
    {
        switch (kind)
        {
            case ToastKind.Success:
                _panel.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                _panel.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 142, 60));
                _text.Foreground = new SolidColorBrush(Color.FromRgb(27, 94, 32));
                break;
            case ToastKind.Warning:
                _panel.Background = new SolidColorBrush(Color.FromRgb(255, 248, 220));
                _panel.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 120, 0));
                _text.Foreground = new SolidColorBrush(Color.FromRgb(140, 0, 0));
                break;
            default:
                _panel.Background = new SolidColorBrush(Color.FromRgb(240, 248, 255));
                _panel.BorderBrush = new SolidColorBrush(Color.FromRgb(90, 120, 160));
                _text.Foreground = new SolidColorBrush(Color.FromRgb(30, 55, 90));
                break;
        }

        _panel.BorderThickness = new Thickness(1);
    }
}
