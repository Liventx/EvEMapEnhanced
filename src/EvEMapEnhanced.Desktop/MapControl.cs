using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using EvEMapEnhanced.Core.Stats;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// GARPA-style 2D top-down (X/Z) projection of the EVE universe: white background,
/// system name labels, mouse-wheel zoom, left-drag pan, left-click to highlight the
/// systems reachable from a system (by gate and by the currently selected ship's jump
/// range) with a stats overlay, and a right-click context menu ("Маршрут отсюда" /
/// "Маршрут сюда") that is the primary way to pick route endpoints.
/// </summary>
public sealed class MapControl : Control
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 400.0;
    private const double HitRadiusPx = 7.0;
    private const double ClickDragThresholdPx = 4.0;
    private const int LabelLodThreshold = 800;
    private const int GateLineLodThreshold = 4000;

    private static readonly IBrush PanelBackground = new SolidColorBrush(Color.FromArgb(235, 250, 250, 250));
    private static readonly IBrush PanelBorder = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120));
    private static readonly IBrush GateLineBrush = new SolidColorBrush(Color.FromArgb(130, 150, 150, 150));
    private static readonly IBrush GateHighlightBrush = new SolidColorBrush(Color.FromArgb(255, 30, 140, 30));
    private static readonly IBrush JumpRangeFill = new SolidColorBrush(Color.FromArgb(35, 90, 60, 200));
    private static readonly IBrush JumpRangeStroke = new SolidColorBrush(Color.FromArgb(200, 90, 60, 200));

    private UniverseMap? _map;
    private double _minX, _maxX, _minZ, _maxZ;
    private double _baseScale = 1.0;
    private double _zoom = 1.0;
    private Point _center;

    private Point? _pressScreenPos;
    private Point? _lastPointerPos;
    private bool _isPanning;
    private bool _leftButtonDown;
    private SolarSystem? _hoveredSystem;
    private int? _contextMenuSystemId;

    private int? _selectedSystemId;
    private double _selectedRangeLy;
    private HashSet<int> _reachableByJump = new();
    private HashSet<int> _gateNeighbors = new();

    private readonly ContextMenu _contextMenu;
    private readonly MenuItem _routeFromItem;
    private readonly MenuItem _routeToItem;

    public int? FromSystemId { get; set; }
    public int? ToSystemId { get; set; }
    public IReadOnlyList<RouteStep>? RouteSteps { get; set; }

    /// <summary>Supplies the ship/skills used to compute the jump-range highlight on click.</summary>
    public Func<(ShipHull? Hull, PilotSkills Skills)>? RouteContextProvider { get; set; }

    /// <summary>Supplies cached kill-activity stats for the overlay panel (no network calls).</summary>
    public Func<int, SystemStats?>? StatsProvider { get; set; }

    /// <summary>Resolves a region id to its display name for the overlay panel.</summary>
    public Func<int, string?>? RegionNameProvider { get; set; }

    public event Action<int>? RouteFromRequested;
    public event Action<int>? RouteToRequested;

    public MapControl()
    {
        ClipToBounds = true;
        Focusable = true;

        _routeFromItem = new MenuItem { Header = "Маршрут отсюда" };
        _routeFromItem.Click += (_, _) => { if (_contextMenuSystemId is int id) RouteFromRequested?.Invoke(id); };
        _routeToItem = new MenuItem { Header = "Маршрут сюда" };
        _routeToItem.Click += (_, _) => { if (_contextMenuSystemId is int id) RouteToRequested?.Invoke(id); };
        _contextMenu = new ContextMenu { ItemsSource = new object[] { _routeFromItem, _routeToItem } };
    }

    public void SetMap(UniverseMap map)
    {
        _map = map;
        ComputeBounds();
        FitToView();
        InvalidateVisual();
    }

    /// <summary>Pans/zooms so that every given system is visible, e.g. after building a route.</summary>
    public void FitToSystems(IEnumerable<int> systemIds)
    {
        if (_map is null) return;
        var points = systemIds.Select(id => _map.Get(id)).Where(s => s is not null).Select(s => Project(s!)).ToList();
        if (points.Count == 0) return;

        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minZ = points.Min(p => p.Y), maxZ = points.Max(p => p.Y);
        _center = new Point((minX + maxX) / 2, (minZ + maxZ) / 2);

        double width = Math.Max(maxX - minX, 1.0);
        double height = Math.Max(maxZ - minZ, 1.0);
        double w = Bounds.Width > 0 ? Bounds.Width : 900;
        double h = Bounds.Height > 0 ? Bounds.Height : 600;
        double scale = Math.Min(w / width, h / height) * 0.6;
        _zoom = Math.Clamp(scale / _baseScale, MinZoom, MaxZoom);

        InvalidateVisual();
    }

    private void ComputeBounds()
    {
        if (_map is null || _map.Systems.Count == 0) return;
        var projected = _map.Systems.Values.Select(Project).ToList();
        _minX = projected.Min(p => p.X);
        _maxX = projected.Max(p => p.X);
        _minZ = projected.Min(p => p.Y);
        _maxZ = projected.Max(p => p.Y);
    }

    private void FitToView()
    {
        _center = new Point((_minX + _maxX) / 2, (_minZ + _maxZ) / 2);
        double width = Math.Max(_maxX - _minX, 1.0);
        double height = Math.Max(_maxZ - _minZ, 1.0);
        double w = Bounds.Width > 0 ? Bounds.Width : 900;
        double h = Bounds.Height > 0 ? Bounds.Height : 600;
        _baseScale = Math.Min(w / width, h / height) * 0.92;
        _zoom = 1.0;
    }

    private static Point Project(SolarSystem system) =>
        new(SpaceMath.MetersToLightYears(system.X), SpaceMath.MetersToLightYears(system.Z));

    private double Scale => _baseScale * _zoom;

    private Point WorldToScreen(Point world)
    {
        double w = Bounds.Width, h = Bounds.Height;
        return new Point((world.X - _center.X) * Scale + w / 2, (world.Y - _center.Y) * Scale + h / 2);
    }

    private Point ScreenToWorld(Point screen)
    {
        double w = Bounds.Width, h = Bounds.Height;
        return new Point((screen.X - w / 2) / Scale + _center.X, (screen.Y - h / 2) / Scale + _center.Y);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_map is null) return;

        var pos = e.GetPosition(this);
        var worldBefore = ScreenToWorld(pos);
        double factor = e.Delta.Y > 0 ? 1.25 : 0.8;
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        var worldAfter = ScreenToWorld(pos);
        _center += worldBefore - worldAfter;

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        _lastPointerPos = point.Position;
        _pressScreenPos = point.Position;

        if (point.Properties.IsLeftButtonPressed)
        {
            _leftButtonDown = true;
            _isPanning = false;
            e.Pointer.Capture(this);
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            var hit = HitTestSystem(point.Position);
            if (hit is not null)
            {
                _contextMenuSystemId = hit.Id;
                _routeFromItem.Header = $"Маршрут отсюда: {hit.Name}";
                _routeToItem.Header = $"Маршрут сюда: {hit.Name}";
                ContextMenu = _contextMenu;
            }
            else
            {
                ContextMenu = null;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_leftButtonDown)
        {
            if (!_isPanning && _pressScreenPos is { } press)
            {
                double dx = pos.X - press.X, dy = pos.Y - press.Y;
                if (dx * dx + dy * dy > ClickDragThresholdPx * ClickDragThresholdPx) _isPanning = true;
            }

            if (_isPanning && _lastPointerPos is { } last)
            {
                var deltaScreen = pos - last;
                _center -= new Point(deltaScreen.X / Scale, deltaScreen.Y / Scale);
                InvalidateVisual();
            }
        }
        else
        {
            var hovered = HitTestSystem(pos);
            if (!ReferenceEquals(hovered, _hoveredSystem))
            {
                _hoveredSystem = hovered;
                InvalidateVisual();
            }
        }

        _lastPointerPos = pos;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (e.InitialPressMouseButton == MouseButton.Left && _leftButtonDown)
        {
            if (!_isPanning)
            {
                var hit = HitTestSystem(e.GetPosition(this));
                SelectSystem(hit);
            }
            _leftButtonDown = false;
            _isPanning = false;
        }

        e.Pointer.Capture(null);
    }

    private void SelectSystem(SolarSystem? system)
    {
        _selectedSystemId = system?.Id;
        _reachableByJump.Clear();
        _gateNeighbors.Clear();
        _selectedRangeLy = 0;

        if (system is not null && _map is not null)
        {
            _gateNeighbors = _map.GateNeighbors(system.Id).ToHashSet();

            var (hull, skills) = RouteContextProvider?.Invoke() ?? (null, new PilotSkills());
            if (hull is not null)
            {
                _selectedRangeLy = JumpSimulator.MaxRangeLy(hull, skills);
                _reachableByJump = _map.SystemsWithinRange(system, _selectedRangeLy).Select(t => t.System.Id).ToHashSet();
            }
        }

        InvalidateVisual();
    }

    private SolarSystem? HitTestSystem(Point screenPos)
    {
        if (_map is null) return null;

        SolarSystem? best = null;
        double bestDistSq = HitRadiusPx * HitRadiusPx;

        foreach (var system in _map.Systems.Values)
        {
            var screen = WorldToScreen(Project(system));
            double dx = screen.X - screenPos.X, dy = screen.Y - screenPos.Y;
            double distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = system;
            }
        }
        return best;
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.FillRectangle(Brushes.White, new Rect(0, 0, w, h));

        if (_map is null || _map.Systems.Count == 0)
        {
            var placeholder = new FormattedText("Скачайте SDE, чтобы увидеть карту",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 16, Brushes.Gray);
            context.DrawText(placeholder, new Point(20, 20));
            return;
        }

        var viewport = new Rect(-20, -20, w + 40, h + 40);

        var visible = new List<(SolarSystem System, Point Screen)>();
        foreach (var system in _map.Systems.Values)
        {
            var screen = WorldToScreen(Project(system));
            if (viewport.Contains(screen)) visible.Add((system, screen));
        }

        if (visible.Count > 0 && visible.Count <= GateLineLodThreshold)
        {
            var visibleIds = visible.Select(v => v.System.Id).ToHashSet();
            var gatePen = new Pen(GateLineBrush, 1.0);
            var drawn = new HashSet<(int, int)>();
            foreach (var (system, screen) in visible)
            {
                foreach (int neighborId in _map.GateNeighbors(system.Id))
                {
                    if (!visibleIds.Contains(neighborId)) continue;
                    var key = system.Id < neighborId ? (system.Id, neighborId) : (neighborId, system.Id);
                    if (!drawn.Add(key)) continue;
                    var neighbor = _map.Get(neighborId);
                    if (neighbor is null) continue;
                    context.DrawLine(gatePen, screen, WorldToScreen(Project(neighbor)));
                }
            }
        }

        // Jump-range highlight for the currently selected system.
        if (_selectedSystemId is int selId && _map.Get(selId) is { } selectedSystem)
        {
            var selScreen = WorldToScreen(Project(selectedSystem));
            if (_selectedRangeLy > 0)
            {
                double radiusPx = _selectedRangeLy * Scale;
                context.DrawEllipse(JumpRangeFill, new Pen(JumpRangeStroke, 1.5, dashStyle: new DashStyle(new double[] { 5, 4 }, 0)), selScreen, radiusPx, radiusPx);
            }
        }

        foreach (var (system, screen) in visible)
        {
            bool isSelected = system.Id == _selectedSystemId;
            bool isGateNeighbor = _gateNeighbors.Contains(system.Id);
            bool isJumpReachable = _reachableByJump.Contains(system.Id);

            var brush = SecurityBrush(system.Security);
            double r = system.Id == FromSystemId || system.Id == ToSystemId || isSelected ? 5.0 : 2.4;
            context.DrawEllipse(brush, null, screen, r, r);

            if (isSelected)
            {
                context.DrawEllipse(null, new Pen(Brushes.Black, 2.0), screen, r + 3, r + 3);
            }
            else if (isGateNeighbor)
            {
                context.DrawEllipse(null, new Pen(GateHighlightBrush, 2.0), screen, r + 2.5, r + 2.5);
            }
            else if (isJumpReachable)
            {
                context.DrawEllipse(null, new Pen(JumpRangeStroke, 2.0), screen, r + 2.5, r + 2.5);
            }
        }

        if (visible.Count <= LabelLodThreshold)
        {
            var labelBrush = Brushes.Black;
            foreach (var (system, screen) in visible)
            {
                var label = new FormattedText(system.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, labelBrush);
                context.DrawText(label, new Point(screen.X + 5, screen.Y - 5));
            }
        }

        if (RouteSteps is { Count: > 0 })
        {
            var gatePen = new Pen(Brushes.DodgerBlue, 2.5);
            var jumpPen = new Pen(Brushes.OrangeRed, 2.5, dashStyle: new DashStyle(new double[] { 4, 3 }, 0));
            foreach (var step in RouteSteps)
            {
                var fromSys = _map.Get(step.FromSystemId);
                var toSys = _map.Get(step.ToSystemId);
                if (fromSys is null || toSys is null) continue;
                var p1 = WorldToScreen(Project(fromSys));
                var p2 = WorldToScreen(Project(toSys));
                context.DrawLine(step.Kind == RouteStepKind.Gate ? gatePen : jumpPen, p1, p2);
            }
        }

        DrawMarker(context, FromSystemId, Brushes.LimeGreen, "ОТ");
        DrawMarker(context, ToSystemId, Brushes.OrangeRed, "ДО");

        DrawOverlayPanel(context);
    }

    private void DrawMarker(DrawingContext context, int? systemId, IBrush brush, string label)
    {
        if (systemId is not int id || _map?.Get(id) is not { } system) return;
        var screen = WorldToScreen(Project(system));
        var pen = new Pen(brush, 2.5);
        context.DrawEllipse(null, pen, screen, 9, 9);
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, new SolidColorBrush(((ISolidColorBrush)brush).Color));
        context.DrawText(text, new Point(screen.X + 11, screen.Y - 22));
    }

    private void DrawOverlayPanel(DrawingContext context)
    {
        var focusSystem = (_selectedSystemId is int selId ? _map?.Get(selId) : null) ?? _hoveredSystem;
        if (focusSystem is null) return;

        var lines = new List<string> { focusSystem.Name };

        string? regionName = RegionNameProvider?.Invoke(focusSystem.RegionId);
        lines.Add(regionName is not null ? $"Регион: {regionName}" : $"Регион #{focusSystem.RegionId}");
        lines.Add($"Security: {focusSystem.Security:F1}");

        if (focusSystem.Id == _selectedSystemId)
        {
            lines.Add($"Гейт-соседей: {_gateNeighbors.Count}");
            if (_selectedRangeLy > 0)
            {
                lines.Add($"Дальность прыжка: {_selectedRangeLy:F1} LY");
                lines.Add($"Систем в пределах прыжка: {_reachableByJump.Count}");
            }
            else
            {
                lines.Add("Выберите корабль, чтобы увидеть дальность прыжка");
            }
        }

        var stats = StatsProvider?.Invoke(focusSystem.Id);
        if (stats is not null)
        {
            lines.Add($"Килы 24ч: {stats.KillsLast24H} (score {stats.ActivityScore:F0})");
        }

        string text = string.Join('\n', lines);
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.Black);

        var panelRect = new Rect(10, 10, formatted.Width + 20, formatted.Height + 16);
        context.DrawRectangle(PanelBackground, new Pen(PanelBorder, 1.0), panelRect, 4, 4);
        context.DrawText(formatted, new Point(panelRect.X + 10, panelRect.Y + 8));
    }

    private static IBrush SecurityBrush(double security)
    {
        double rounded = Math.Round(security, 1);
        return rounded switch
        {
            >= 1.0 => new SolidColorBrush(Color.FromRgb(0x2F, 0xEF, 0xEF)),
            >= 0.9 => new SolidColorBrush(Color.FromRgb(0x48, 0xF0, 0xC0)),
            >= 0.8 => new SolidColorBrush(Color.FromRgb(0x00, 0xEF, 0x47)),
            >= 0.7 => new SolidColorBrush(Color.FromRgb(0x00, 0xD0, 0x00)),
            >= 0.6 => new SolidColorBrush(Color.FromRgb(0x8F, 0xC7, 0x2F)),
            >= 0.5 => new SolidColorBrush(Color.FromRgb(0xC7, 0xC7, 0x00)),
            >= 0.4 => new SolidColorBrush(Color.FromRgb(0xD7, 0x77, 0x00)),
            >= 0.3 => new SolidColorBrush(Color.FromRgb(0xF0, 0x60, 0x00)),
            >= 0.2 => new SolidColorBrush(Color.FromRgb(0xF0, 0x48, 0x00)),
            >= 0.1 => new SolidColorBrush(Color.FromRgb(0xD7, 0x30, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0xC0, 0x10, 0x10)),
        };
    }
}
