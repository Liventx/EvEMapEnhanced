using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace EvEMapEnhanced.Desktop;

/// <summary>Compact dropdown that exposes signed-in characters as checkboxes for multi-select.</summary>
public class MultiSelectDropDown : UserControl
{
    private readonly Button _button;
    private readonly StackPanel _itemsPanel;
    private readonly Dictionary<long, CheckBox> _checkboxes = new();
    private bool _isUpdating;
    private string _placeholder = "(нет)";

    public event EventHandler? SelectionChanged;

    public MultiSelectDropDown()
    {
        _itemsPanel = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
        _button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 11,
            MinHeight = 22,
            Padding = new Thickness(6, 0),
        };
        _button.Flyout = new Flyout
        {
            Content = new ScrollViewer
            {
                Content = _itemsPanel,
                MaxHeight = 280,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            },
        };
        Content = _button;
    }

    public void Configure(string placeholder, double? maxWidth = null)
    {
        _placeholder = placeholder;
        if (maxWidth is double w)
        {
            MinWidth = 120;
            MaxWidth = w;
        }
        UpdateButtonText();
    }

    public void SetItems(IReadOnlyList<(long id, string label)> items, IReadOnlySet<long> selectedIds)
    {
        _isUpdating = true;
        try
        {
            _itemsPanel.Children.Clear();
            _checkboxes.Clear();
            foreach (var (id, label) in items)
            {
                var checkBox = new CheckBox
                {
                    Content = label,
                    IsChecked = selectedIds.Contains(id),
                    FontSize = 11,
                };
                checkBox.IsCheckedChanged += (_, _) => OnCheckChanged();
                _checkboxes[id] = checkBox;
                _itemsPanel.Children.Add(checkBox);
            }
            UpdateButtonText();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    public IReadOnlyList<long> GetSelectedIds() =>
        _checkboxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

    private void OnCheckChanged()
    {
        if (_isUpdating)
        {
            return;
        }

        UpdateButtonText();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateButtonText()
    {
        var selectedNames = _checkboxes.Values
            .Where(cb => cb.IsChecked == true)
            .Select(cb => cb.Content?.ToString())
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var text = selectedNames.Count switch
        {
            0 => _placeholder,
            1 => selectedNames[0]!,
            2 => $"{selectedNames[0]}, {selectedNames[1]}",
            _ => $"{selectedNames[0]}, {selectedNames[1]} (+{selectedNames.Count - 2})",
        };

        _button.Content = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }
}
