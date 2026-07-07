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
using EvEMapEnhanced.Core.Structures;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// 2D EVE universe map with two display modes: standard top-down projection (GARPA-style)
/// and a schematic Dotlan-like layout with region blocks. Supports mouse-wheel zoom,
/// left-drag pan, left-click to highlight jump/gate reachability, and a right-click
/// context menu for route endpoints.
/// </summary>
public sealed class MapControl : Control
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 400.0;
    private const double HitRadiusPx = 7.0;
    private const double ClickDragThresholdPx = 4.0;
    private const int GateLineLodThreshold = 5000;
    private const int MaxLabelCandidates = 1500;
    private const double LabelCellSizePx = 13.0;
    private const double DefaultStandardZoom = 3.0;
    private const double DefaultSchematicZoom = 1.0;

    private static readonly IBrush PanelBackground = new SolidColorBrush(Color.FromArgb(235, 250, 250, 250));
    private static readonly IBrush PanelBorder = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120));
    private static readonly IBrush GateLineBrush = new SolidColorBrush(Color.FromArgb(130, 150, 150, 150));
    private static readonly IBrush SchematicBackground = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x18));
    private static readonly IBrush SchematicRegionFill = new SolidColorBrush(Color.FromArgb(28, 80, 90, 110));
    private static readonly IBrush SchematicRegionBorder = new SolidColorBrush(Color.FromArgb(90, 100, 115, 140));
    private static readonly IBrush SchematicGateLineBrush = new SolidColorBrush(Color.FromArgb(100, 70, 85, 70));
    private static readonly IBrush SchematicLabelBrush = new SolidColorBrush(Color.FromArgb(230, 210, 215, 225));
    private static readonly IBrush SchematicRegionLabelBrush = new SolidColorBrush(Color.FromArgb(235, 130, 170, 220));
    private static readonly IBrush StandardLabelHalo = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
    private static readonly IBrush SchematicLabelHalo = new SolidColorBrush(Color.FromArgb(190, 8, 10, 14));
    private static readonly IBrush GateHighlightBrush = new SolidColorBrush(Color.FromArgb(255, 30, 140, 30));
    private static readonly IBrush JumpRangeFill = new SolidColorBrush(Color.FromArgb(35, 90, 60, 200));
    private static readonly IBrush JumpRangeStroke = new SolidColorBrush(Color.FromArgb(200, 90, 60, 200));
    private static readonly IBrush JumpRouteBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));
    private static readonly IBrush GateRouteBrush = new SolidColorBrush(Color.FromArgb(200, 60, 140, 60));

    private UniverseMap? _map;
    private SchematicMapLayout? _schematicLayout;
    private double _minX, _maxX, _minZ, _maxZ;
    private double _baseScale = 1.0;
    private double _zoom = 1.0;
    private Point _center;
    private MapDisplayMode _displayMode = MapDisplayMode.Standard;

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

    public MapDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode == value) return;
            _displayMode = value;
            RebuildLayout();
            FitToView();
            InvalidateVisual();
        }
    }

    /// <summary>Supplies the ship/skills/method used to compute the jump-range highlight on click.</summary>
    public Func<(ShipHull? Hull, PilotSkills Skills, JumpMethod Method)>? RouteContextProvider { get; set; }

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
        RebuildLayout();
        FitToView();
        InvalidateVisual();
    }

    private void RebuildLayout()
    {
        if (_map is null) return;

        if (_displayMode == MapDisplayMode.Schematic)
        {
            _schematicLayout = SchematicMapLayout.Build(_map, RegionNameProvider is null
                ? null
                : _map.Systems.Values
                    .Select(s => s.RegionId)
                    .Distinct()
                    .ToDictionary(id => id, id => RegionNameProvider(id) ?? $"Region {id}"));
        }
        else
        {
            _schematicLayout = null;
        }

        ComputeBounds();
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
        double scale = Math.Min(w / width, h / height) * 0.75;
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
        _baseScale = Math.Min(w / width, h / height) * (_displayMode == MapDisplayMode.Schematic ? 0.88 : 0.78);
        _zoom = _displayMode == MapDisplayMode.Schematic ? DefaultSchematicZoom : DefaultStandardZoom;
    }

    private Point Project(SolarSystem system) =>
        _displayMode == MapDisplayMode.Schematic && _schematicLayout is not null
            ? _schematicLayout.GetPosition(system)
            : new Point(SpaceMath.MetersToLightYears(system.X), SpaceMath.MetersToLightYears(system.Z));

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

            var (hull, skills, method) = RouteContextProvider?.Invoke() ?? (null, new PilotSkills(), JumpMethod.Cyno);
            if (hull is not null)
            {
                _selectedRangeLy = JumpSimulator.MaxRangeLy(hull, skills);
                _reachableByJump = _map.SystemsWithinRange(system, _selectedRangeLy)
                    .Where(t => JumpRules.IsValidJumpLanding(t.System, method))
                    .Select(t => t.System.Id)
                    .ToHashSet();
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
        bool schematic = _displayMode == MapDisplayMode.Schematic;
        context.FillRectangle(schematic ? SchematicBackground : Brushes.White, new Rect(0, 0, w, h));

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

        if (schematic)
        {
            DrawSchematicRegions(context, viewport);
        }

        if (visible.Count > 0 && visible.Count <= GateLineLodThreshold)
        {
            var visibleIds = visible.Select(v => v.System.Id).ToHashSet();
            var gatePen = new Pen(schematic ? SchematicGateLineBrush : GateLineBrush, schematic ? 0.9 : 1.0);
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
                double radiusPx = schematic
                    ? Math.Clamp(_selectedRangeLy * Scale * 0.35, 18, 120)
                    : _selectedRangeLy * Scale;
                context.DrawEllipse(JumpRangeFill, new Pen(JumpRangeStroke, 1.5, dashStyle: new DashStyle(new double[] { 5, 4 }, 0)), selScreen, radiusPx, radiusPx);
            }
        }

        foreach (var (system, screen) in visible)
        {
            bool isSelected = system.Id == _selectedSystemId;
            bool isGateNeighbor = _gateNeighbors.Contains(system.Id);
            bool isJumpReachable = _reachableByJump.Contains(system.Id);

            var brush = SecurityBrush(system.Security);
            double r = system.Id == FromSystemId || system.Id == ToSystemId || isSelected
                ? (schematic ? 5.5 : 5.0)
                : (schematic ? 3.2 : 2.4);
            context.DrawEllipse(brush, null, screen, r, r);

            if (isSelected)
            {
                context.DrawEllipse(null, new Pen(schematic ? Brushes.White : Brushes.Black, 2.0), screen, r + 3, r + 3);
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

        DrawSystemLabels(context, visible, schematic);

        DrawStructureIcons(context, visible);

        if (RouteSteps is { Count: > 0 })
        {
            var gatePen = new Pen(GateRouteBrush, schematic ? 2.2 : 1.8);
            var jumpPen = new Pen(JumpRouteBrush, schematic ? 3.0 : 2.5);
            foreach (var step in RouteSteps)
            {
                var fromSys = _map.Get(step.FromSystemId);
                var toSys = _map.Get(step.ToSystemId);
                if (fromSys is null || toSys is null) continue;
                var p1 = WorldToScreen(Project(fromSys));
                var p2 = WorldToScreen(Project(toSys));
                if (step.Kind == RouteStepKind.Gate)
                    context.DrawLine(gatePen, p1, p2);
                else
                    DrawJumpArc(context, jumpPen, p1, p2);
            }
        }

        DrawMarker(context, FromSystemId, Brushes.LimeGreen, "ОТ", schematic);
        DrawMarker(context, ToSystemId, Brushes.OrangeRed, "ДО", schematic);

        DrawOverlayPanel(context);
    }

    private void DrawSchematicRegions(DrawingContext context, Rect viewport)
    {
        if (_schematicLayout is null) return;

        foreach (var (regionId, bounds) in _schematicLayout.RegionBounds)
        {
            var topLeft = WorldToScreen(bounds.TopLeft);
            var bottomRight = WorldToScreen(bounds.BottomRight);
            var screenRect = new Rect(topLeft, bottomRight);
            if (!viewport.Intersects(screenRect)) continue;

            context.DrawRectangle(SchematicRegionFill, new Pen(SchematicRegionBorder, 1.2), screenRect, 6, 6);

            if (_schematicLayout.RegionNames.TryGetValue(regionId, out var regionName))
            {
                double fontSize = Math.Clamp(10 + Scale * 0.4, 10, 16);
                var label = new FormattedText(regionName.ToUpperInvariant(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    Typeface.Default, fontSize, SchematicRegionLabelBrush);
                var labelPos = new Point(screenRect.X + (screenRect.Width - label.Width) / 2, screenRect.Y - label.Height - 3);
                context.FillRectangle(SchematicLabelHalo, new Rect(labelPos.X - 3, labelPos.Y - 1, label.Width + 6, label.Height + 2));
                context.DrawText(label, labelPos);
            }
        }
    }

    /// <summary>
    /// Draws system-name labels with greedy collision avoidance: pinned systems (selected,
    /// hovered, route endpoints/steps) always get a label; everything else is offered in
    /// priority order (gate-hub systems first) and only drawn if its bounding box doesn't
    /// overlap an already-placed label or system marker. This is what keeps the map legible
    /// at any zoom level instead of dumping every name on top of each other.
    /// </summary>
    private void DrawSystemLabels(DrawingContext context, List<(SolarSystem System, Point Screen)> visible, bool schematic)
    {
        if (visible.Count == 0) return;

        var labelBrush = schematic ? SchematicLabelBrush : Brushes.Black;
        var haloBrush = schematic ? SchematicLabelHalo : StandardLabelHalo;
        var typeface = Typeface.Default;
        double fontSize = schematic ? 9.5 : 9.0;

        var routeSystemIds = RouteSteps is null
            ? null
            : RouteSteps.SelectMany(s => new[] { s.FromSystemId, s.ToSystemId }).ToHashSet();

        bool IsPinned(int systemId) =>
            systemId == _selectedSystemId ||
            systemId == FromSystemId ||
            systemId == ToSystemId ||
            systemId == _hoveredSystem?.Id ||
            routeSystemIds?.Contains(systemId) == true;

        var occupied = new Dictionary<(long Cx, long Cy), List<Rect>>();

        void Occupy(Rect rect)
        {
            long minCx = (long)Math.Floor(rect.X / LabelCellSizePx);
            long maxCx = (long)Math.Floor((rect.X + rect.Width) / LabelCellSizePx);
            long minCy = (long)Math.Floor(rect.Y / LabelCellSizePx);
            long maxCy = (long)Math.Floor((rect.Y + rect.Height) / LabelCellSizePx);
            for (long cx = minCx; cx <= maxCx; cx++)
            {
                for (long cy = minCy; cy <= maxCy; cy++)
                {
                    var key = (cx, cy);
                    if (!occupied.TryGetValue(key, out var list))
                    {
                        list = new List<Rect>();
                        occupied[key] = list;
                    }
                    list.Add(rect);
                }
            }
        }

        bool Overlaps(Rect rect)
        {
            long minCx = (long)Math.Floor(rect.X / LabelCellSizePx);
            long maxCx = (long)Math.Floor((rect.X + rect.Width) / LabelCellSizePx);
            long minCy = (long)Math.Floor(rect.Y / LabelCellSizePx);
            long maxCy = (long)Math.Floor((rect.Y + rect.Height) / LabelCellSizePx);
            for (long cx = minCx; cx <= maxCx; cx++)
            {
                for (long cy = minCy; cy <= maxCy; cy++)
                {
                    if (!occupied.TryGetValue((cx, cy), out var list)) continue;
                    foreach (var existing in list)
                    {
                        if (existing.Intersects(rect)) return true;
                    }
                }
            }
            return false;
        }

        // Reserve the space around every visible dot so labels never start on top of a marker,
        // even one whose own label loses the placement race.
        foreach (var (_, screen) in visible)
        {
            Occupy(new Rect(screen.X - 4, screen.Y - 4, 8, 8));
        }

        var ordered = visible
            .OrderByDescending(v => IsPinned(v.System.Id))
            .ThenByDescending(v => _map?.GateNeighbors(v.System.Id).Count ?? 0)
            .ThenByDescending(v => v.System.Security)
            .Take(MaxLabelCandidates)
            .ToList();

        foreach (var (system, screen) in ordered)
        {
            bool pinned = IsPinned(system.Id);
            var formatted = new FormattedText(system.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, labelBrush);
            var rect = new Rect(screen.X + 6, screen.Y - formatted.Height / 2, formatted.Width + 3, formatted.Height);

            if (!pinned && Overlaps(rect)) continue;

            context.FillRectangle(haloBrush, new Rect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2));
            context.DrawText(formatted, new Point(rect.X + 1, rect.Y));
            Occupy(rect);
        }
    }

    private static void DrawJumpArc(DrawingContext context, IPen pen, Point p1, Point p2)
    {
        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.5) return;

        var mid = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        double bulge = Math.Clamp(len * 0.18, 10, 55);
        var control = new Point(mid.X - dy / len * bulge, mid.Y + dx / len * bulge);

        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(p1, false);
            gc.QuadraticBezierTo(control, p2);
            gc.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawStructureIcons(DrawingContext context, List<(SolarSystem System, Point Screen)> visible)
    {
        if (_map is null) return;

        var screenById = visible.ToDictionary(v => v.System.Id, v => v.Screen);
        foreach (var (systemId, screen) in screenById)
        {
            var structures = _map.StructuresAt(systemId);
            if (structures.Count == 0) continue;

            for (int i = 0; i < structures.Count; i++)
            {
                double angle = structures.Count == 1
                    ? -Math.PI / 4
                    : (2 * Math.PI * i / structures.Count) - Math.PI / 2;
                const double offset = 9;
                var pos = new Point(screen.X + Math.Cos(angle) * offset, screen.Y + Math.Sin(angle) * offset);
                DrawStructureIcon(context, structures[i].Kind, pos);
            }
        }
    }

    private static void DrawStructureIcon(DrawingContext context, StructureKind kind, Point center)
    {
        const double size = 4.5;
        switch (kind)
        {
            case StructureKind.Ansiblex:
            case StructureKind.CustomJumpBridge:
            {
                var brush = new SolidColorBrush(Color.FromRgb(0x22, 0x66, 0xCC));
                var rect = new Rect(center.X - size, center.Y - size, size * 2, size * 2);
                context.DrawRectangle(brush, new Pen(Brushes.White, 0.8), rect);
                context.DrawLine(new Pen(Brushes.White, 1.0), new Point(rect.X, rect.Bottom), new Point(rect.Right, rect.Y));
                break;
            }
            case StructureKind.CynoBeacon:
            {
                var brush = new SolidColorBrush(Color.FromRgb(0x22, 0xAA, 0x44));
                var top = new Point(center.X, center.Y - size);
                var left = new Point(center.X - size, center.Y + size * 0.6);
                var right = new Point(center.X + size, center.Y + size * 0.6);
                context.DrawLine(new Pen(brush, 2.0), top, left);
                context.DrawLine(new Pen(brush, 2.0), left, right);
                context.DrawLine(new Pen(brush, 2.0), right, top);
                break;
            }
            case StructureKind.CynoJammer:
            {
                var rect = new Rect(center.X - size, center.Y - size, size * 2, size * 2);
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xCC, 0x22, 0x22)), new Pen(Brushes.White, 0.8), rect);
                var pen = new Pen(Brushes.White, 1.2);
                context.DrawLine(pen, new Point(rect.X, rect.Y), new Point(rect.Right, rect.Bottom));
                context.DrawLine(pen, new Point(rect.Right, rect.Y), new Point(rect.X, rect.Bottom));
                break;
            }
            case StructureKind.Keepstar:
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x88, 0x44, 0xCC)), new Pen(Brushes.White, 0.8), center, size + 1, size + 1);
                break;
            case StructureKind.Fortizar:
            {
                var rect = new Rect(center.X - size, center.Y - size, size * 2, size * 2);
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xDD, 0x88, 0x22)), new Pen(Brushes.White, 0.8), rect);
                break;
            }
            case StructureKind.Azbel:
            {
                var rect = new Rect(center.X - size * 0.85, center.Y - size * 0.85, size * 1.7, size * 1.7);
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), new Pen(Brushes.White, 0.8), rect);
                break;
            }
            case StructureKind.Athanor:
            case StructureKind.Tatara:
            {
                var brush = kind == StructureKind.Athanor
                    ? new SolidColorBrush(Color.FromRgb(0xCC, 0xAA, 0x22))
                    : new SolidColorBrush(Color.FromRgb(0x22, 0xAA, 0xAA));
                var top = new Point(center.X, center.Y - size);
                var right = new Point(center.X + size, center.Y);
                var bottom = new Point(center.X, center.Y + size);
                var left = new Point(center.X - size, center.Y);
                context.DrawLine(new Pen(brush, 2.0), top, right);
                context.DrawLine(new Pen(brush, 2.0), right, bottom);
                context.DrawLine(new Pen(brush, 2.0), bottom, left);
                context.DrawLine(new Pen(brush, 2.0), left, top);
                break;
            }
        }
    }

    private void DrawMarker(DrawingContext context, int? systemId, IBrush brush, string label, bool schematic)
    {
        if (systemId is not int id || _map?.Get(id) is not { } system) return;
        var screen = WorldToScreen(Project(system));
        var pen = new Pen(brush, schematic ? 3.0 : 2.5);
        context.DrawEllipse(null, pen, screen, schematic ? 10 : 9, schematic ? 10 : 9);
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
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13,
            _displayMode == MapDisplayMode.Schematic ? SchematicLabelBrush : Brushes.Black);

        var panelFill = _displayMode == MapDisplayMode.Schematic
            ? new SolidColorBrush(Color.FromArgb(235, 24, 28, 36))
            : PanelBackground;
        var panelBorder = _displayMode == MapDisplayMode.Schematic ? SchematicRegionBorder : PanelBorder;

        var panelRect = new Rect(10, 10, formatted.Width + 20, formatted.Height + 16);
        context.DrawRectangle(panelFill, new Pen(panelBorder, 1.0), panelRect, 4, 4);
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
