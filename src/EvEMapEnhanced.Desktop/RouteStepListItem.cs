using Avalonia.Media;

namespace EvEMapEnhanced.Desktop;

/// <summary>One row in the route steps list with optional shortcut highlighting.</summary>
public sealed class RouteStepListItem
{
  public static readonly IBrush ShortcutHighlightBrush = new SolidColorBrush(Color.Parse("#E8D5B7"));

  public required string Text { get; init; }

  public bool IsHighlighted { get; init; }

  public IBrush RowBackground => IsHighlighted ? ShortcutHighlightBrush : Brushes.Transparent;
}
