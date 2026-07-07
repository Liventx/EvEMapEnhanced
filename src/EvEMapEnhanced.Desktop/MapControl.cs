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
using EvEMapEnhanced.Core.Structures;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// 2D EVE universe map: Standard mode uses real coordinates; Schematic (Dotlan) anchors
/// each region at its in-game position but lays out systems inside with a gate graph.
/// </summary>
public sealed class MapControl : Control
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 400.0;
    private const double HitRadiusPx = 9.0;
    private const double ClickDragThresholdPx = 4.0;
    private const int GateLineLodThreshold = 5000;
    private const int MaxLabelCandidates = 1500;
    private const double LabelCellSizePx = 13.0;
    private const double DefaultStandardZoom = 3.0;
    private const double DefaultSchematicZoom = 3.0;

    // Dotlan-style jump-range highlight: a bold black outline traced directly on a system's own
    // marker/plate border, rather than a separate ring floating outside it or a recolored border.
    private const double JumpRangeRingWidth = 2.4;

    private static readonly IBrush PanelBackground = new SolidColorBrush(Color.FromArgb(235, 250, 250, 250));
    private static readonly IBrush PanelBorder = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120));
    private static readonly IBrush GateLineBrush = new SolidColorBrush(Color.FromArgb(130, 150, 150, 150));
    // Dotlan's real palette is a plain white map, not a dark theme - both display modes now
    // share a white background; "Schematic" only differs in node/plate style and line tone.
    private static readonly IBrush SchematicBackground = Brushes.White;
    private static readonly IBrush SchematicGateLineBrush = new SolidColorBrush(Color.FromArgb(130, 110, 110, 110));
    private static readonly IBrush RegionConnectionBrush = new SolidColorBrush(Color.FromArgb(150, 150, 150, 150));
    // Dotlan colors any gate crossing a region boundary purple (vs. black for same
    // constellation, red for same region/different constellation) so a border system's
    // regional gate is never mistaken for -- or lost among -- its ordinary local gates.
    private static readonly IBrush InterRegionGateBrush = new SolidColorBrush(Color.FromArgb(210, 150, 60, 190));
    private static readonly IBrush SchematicLabelBrush = new SolidColorBrush(Color.FromArgb(235, 25, 28, 34));
    private static readonly IBrush SchematicRegionLabelBrush = new SolidColorBrush(Color.FromArgb(255, 40, 80, 200));
    private static readonly IBrush StandardLabelHalo = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
    private static readonly IBrush SchematicLabelHalo = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
    private static readonly IBrush GateHighlightBrush = new SolidColorBrush(Color.FromArgb(255, 30, 140, 30));
    private static readonly IBrush JumpRangeFill = new SolidColorBrush(Color.FromArgb(35, 90, 60, 200));
    private static readonly IBrush JumpRangeStroke = new SolidColorBrush(Color.FromArgb(200, 90, 60, 200));
    private static readonly IBrush JumpRouteBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));
    private static readonly IBrush GateRouteBrush = new SolidColorBrush(Color.FromArgb(200, 60, 140, 60));

    // Dotlan/EVE-style "you are here" beacon for the live-tracked pilot location. Drawn in a
    // fixed screen-pixel size (never scaled by zoom) and always last, on top of every plate,
    // label and highlight, so it stays exactly as visible zoomed all the way in as zoomed out.
    private static readonly IBrush PilotBeaconHalo = new SolidColorBrush(Color.FromArgb(70, 255, 205, 0));
    private static readonly IBrush PilotBeaconRing = new SolidColorBrush(Color.FromArgb(255, 255, 205, 0));
    private static readonly IBrush PilotBeaconCore = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));

    private UniverseMap? _map;
    private SchematicMapLayout? _schematicLayout;
    private double _minX, _maxX, _minZ, _maxZ;
    private double _baseScale = 1.0;
    private double _zoom = 1.0;
    private Point _center;
    private MapDisplayMode _displayMode = MapDisplayMode.Schematic;

    private Point? _pressScreenPos;
    private Point? _lastPointerPos;
    private bool _isPanning;
    private bool _leftButtonDown;
    private SolarSystem? _hoveredSystem;
    private int? _contextMenuSystemId;

    private int? _selectedSystemId;
    private double _selectedRangeLy;

    /// <summary>
    /// System the live-tracked pilot is currently in (see <see cref="SelectSystemExternally"/>).
    /// Kept independent of <see cref="_selectedSystemId"/> so clicking elsewhere on the map to
    /// inspect another system never makes the "you are here" beacon disappear.
    /// </summary>
    private int? _pilotSystemId;
    private HashSet<int> _reachableByJump = new();
    private HashSet<int> _gateNeighbors = new();
    private CapitalShipClass? _jumpRangeShipClass;

    /// <summary>Screen-space rectangles of the schematic plates actually drawn last frame, keyed by system id -- used so clicks hit-test against real geometry instead of a fixed-radius circle.</summary>
    private Dictionary<int, Rect> _lastPlateRects = new();

    private readonly ContextMenu _contextMenu;
    private readonly MenuItem _routeFromItem;
    private readonly MenuItem _routeToItem;
    private readonly MenuItem _jumpRangeMenuItem;
    private readonly MenuItem _clearJumpRangeItem;

    public int? FromSystemId { get; set; }
    public int? ToSystemId { get; set; }
    public IReadOnlyList<RouteStep>? RouteSteps { get; set; }

    /// <summary>
    /// Dotlan-style "Jump Range" override: when set, the selected system's reachability
    /// highlight uses this capital class's range instead of whatever ship is picked on the
    /// Route tab. Settable via the map's right-click menu or externally (e.g. a toolbar combo).
    /// </summary>
    public CapitalShipClass? JumpRangeShipClass
    {
        get => _jumpRangeShipClass;
        set
        {
            _jumpRangeShipClass = value;
            UpdateReachability();
            InvalidateVisual();
        }
    }

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

    /// <summary>
    /// Supplies the last-hour NPC kill count per system (ESI system_kills feed), used to color
    /// schematic plates the way Dotlan's "NPC Kills" map filter does. When unset (or a system
    /// has no data yet), plates fall back to security-status coloring.
    /// </summary>
    public Func<int, int?>? NpcKillsProvider { get; set; }

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

        _jumpRangeMenuItem = new MenuItem { Header = "Дальность прыжка (Jump Range)" };
        var jumpRangeItems = new List<object>();
        foreach (var shipClass in Enum.GetValues<CapitalShipClass>())
        {
            var item = new MenuItem { Header = shipClass.ToDisplayLabel() };
            item.Click += (_, _) =>
            {
                if (_contextMenuSystemId is not int id) return;
                _jumpRangeShipClass = shipClass;
                SelectSystem(_map?.Get(id));
            };
            jumpRangeItems.Add(item);
        }
        _clearJumpRangeItem = new MenuItem { Header = "Сбросить дальность прыжка" };
        _clearJumpRangeItem.Click += (_, _) =>
        {
            _jumpRangeShipClass = null;
            UpdateReachability();
            InvalidateVisual();
        };
        jumpRangeItems.Add(_clearJumpRangeItem);
        _jumpRangeMenuItem.ItemsSource = jumpRangeItems;

        _contextMenu = new ContextMenu
        {
            ItemsSource = new object[] { _routeFromItem, _routeToItem, _jumpRangeMenuItem }
        };
    }

    public void SetMap(UniverseMap map)
    {
        _map = map;
        RebuildLayout();
        FitToView();
        InvalidateVisual();
    }

    /// <summary>
    /// Programmatically selects a system (or clears selection) to follow a pilot's reported
    /// location: this both drives the normal selection/jump-range highlight (so "Jump Range"
    /// shows reachability from the pilot's current system) and marks that system with the
    /// always-on-top "you are here" beacon drawn in <see cref="DrawPilotBeacon"/>.
    /// </summary>
    public void SelectSystemExternally(int? systemId)
    {
        _pilotSystemId = systemId;
        SelectSystem(systemId is int id ? _map?.Get(id) : null);
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

    /// <summary>
    /// Pans (without changing the current zoom level) so the given system is centered in the
    /// view -- used by the "Focus" button to jump the view to the live-tracked pilot's current
    /// location without also snapping the zoom level to something unrelated to what the user was
    /// already looking at.
    /// </summary>
    public void CenterOnSystem(int systemId)
    {
        if (_map?.Get(systemId) is not { } system) return;
        _center = Project(system);
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
        _baseScale = Math.Min(w / width, h / height) * 0.78;
        _zoom = _displayMode == MapDisplayMode.Schematic ? DefaultSchematicZoom : DefaultStandardZoom;
    }

    private Point Project(SolarSystem system) =>
        _displayMode == MapDisplayMode.Schematic && _schematicLayout is not null
            ? _schematicLayout.GetPosition(system)
            : WorldProjection.RealPosition(system);

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
                _jumpRangeMenuItem.Header = $"Дальность прыжка от {hit.Name}";
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
        UpdateReachability();
        InvalidateVisual();
    }

    /// <summary>
    /// Recomputes the gate-neighbor and jump-range highlight sets for the currently selected
    /// system. Called both when the selection changes and when the jump-range ship class
    /// override changes (selection unchanged, but the highlighted range needs to update).
    /// </summary>
    private void UpdateReachability()
    {
        _reachableByJump.Clear();
        _gateNeighbors.Clear();
        _selectedRangeLy = 0;

        if (_selectedSystemId is not int selId || _map?.Get(selId) is not { } system) return;

        _gateNeighbors = _map.GateNeighbors(system.Id).ToHashSet();

        var (hull, skills, method) = RouteContextProvider?.Invoke() ?? (null, new PilotSkills(), JumpMethod.Cyno);

        double rangeLy = _jumpRangeShipClass is CapitalShipClass overrideClass
            ? JumpSimulator.MaxRangeLy(overrideClass, skills)
            : hull is not null ? JumpSimulator.MaxRangeLy(hull, skills) : 0;

        if (rangeLy > 0)
        {
            _selectedRangeLy = rangeLy;
            _reachableByJump = _map.SystemsWithinRange(system, rangeLy)
                .Where(t => JumpRules.IsValidJumpLanding(t.System, method))
                .Select(t => t.System.Id)
                .ToHashSet();
        }
    }

    private SolarSystem? HitTestSystem(Point screenPos)
    {
        if (_map is null) return null;

        // Schematic mode draws rectangular plates, not dots -- hit-test against the actual
        // rendered rectangle from the last frame so clicks anywhere on a plate register,
        // including the plate's edges (which the old fixed-radius circle would miss).
        if (_displayMode == MapDisplayMode.Schematic && _lastPlateRects.Count > 0)
        {
            foreach (var (systemId, rect) in _lastPlateRects)
            {
                if (rect.Contains(screenPos) && _map.Get(systemId) is { } plateSystem) return plateSystem;
            }
            return null;
        }

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
            var gatePen = new Pen(schematic ? SchematicGateLineBrush : GateLineBrush, schematic ? 1.2 : 1.0);
            var interRegionPen = new Pen(InterRegionGateBrush, 1.4);
            var drawn = new HashSet<(int, int)>();
            foreach (var (system, screen) in visible)
            {
                foreach (int neighborId in _map.GateNeighbors(system.Id))
                {
                    var neighborSys = _map.Get(neighborId);
                    if (neighborSys is null) continue;
                    bool crossRegion = schematic && neighborSys.RegionId != system.RegionId;

                    if (visibleIds.Contains(neighborId))
                    {
                        var key = system.Id < neighborId ? (system.Id, neighborId) : (neighborId, system.Id);
                        if (!drawn.Add(key)) continue;
                        context.DrawLine(crossRegion ? interRegionPen : gatePen, screen, WorldToScreen(Project(neighborSys)));
                    }
                    else if (crossRegion)
                    {
                        // The neighboring system lives in another region and isn't on screen (the
                        // common case when a single region is in view) -- without this stub, a
                        // border system's regional gate would never be shown at all. Matches
                        // Dotlan's own region maps, which draw a short purple line toward every
                        // off-map regional gate labeled with its destination system.
                        DrawInterRegionGateStub(context, screen, neighborSys);
                    }
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

        // Schematic mode uses Dotlan plates only — no underlying dots.
        if (!schematic)
        {
            foreach (var (system, screen) in visible)
            {
                bool isSelected = system.Id == _selectedSystemId;
                bool isGateNeighbor = _gateNeighbors.Contains(system.Id);
                bool isJumpReachable = _reachableByJump.Contains(system.Id);

                var brush = SecurityBrush(system.Security);
                double r = system.Id == FromSystemId || system.Id == ToSystemId || isSelected ? 5.0 : 2.4;

                // Dotlan-style jump-range highlight: a bold black outline traced directly on the
                // marker's own edge (not a separate ring floating outside it).
                var markerPen = isJumpReachable ? new Pen(Brushes.Black, JumpRangeRingWidth) : null;
                context.DrawEllipse(brush, markerPen, screen, r, r);

                if (isSelected)
                    context.DrawEllipse(null, new Pen(Brushes.Black, 2.0), screen, r + 3, r + 3);
                else if (isGateNeighbor)
                    context.DrawEllipse(null, new Pen(GateHighlightBrush, 2.0), screen, r + 2.5, r + 2.5);
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

        if (_pilotSystemId is int pilotId && _map.Get(pilotId) is { } pilotSystem)
        {
            DrawPilotBeacon(context, WorldToScreen(Project(pilotSystem)));
        }

        DrawOverlayPanel(context);
    }

    /// <summary>
    /// Region-name labels are always drawn first, before gate lines, plates and system labels,
    /// so every later, opaque draw call (a plate's fill, a label's halo, ...) paints over any
    /// part of a region label it happens to sit on top of. That draw-order guarantee -- not a
    /// z-index or clipping trick -- is what keeps a big, bold region label from ever obscuring a
    /// system name, no matter how large the label grows.
    /// </summary>
    private void DrawSchematicRegions(DrawingContext context, Rect viewport)
    {
        if (_schematicLayout is null) return;

        DrawInterRegionConnections(context, viewport);

        var typeface = new Typeface(Typeface.Default.FontFamily, FontStyle.Italic, FontWeight.Bold);
        double fontSize = Math.Clamp(20 + Scale * 0.22, 20, 40);

        foreach (var (regionId, centroid) in _schematicLayout.RegionCentroids)
        {
            var screen = WorldToScreen(centroid);
            if (!viewport.Contains(screen)) continue;
            if (!_schematicLayout.RegionNames.TryGetValue(regionId, out var regionName)) continue;

            var label = new FormattedText(regionName.ToUpperInvariant(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, fontSize, SchematicRegionLabelBrush);
            var labelPos = new Point(screen.X - label.Width / 2, screen.Y - label.Height / 2);
            context.FillRectangle(SchematicLabelHalo, new Rect(labelPos.X - 5, labelPos.Y - 2, label.Width + 10, label.Height + 4));
            context.DrawText(label, labelPos);
        }
    }

    /// <summary>
    /// Draws a single connector line between every pair of regions joined by at least one real
    /// stargate, the same way Dotlan's own universe overview map shows region-to-region gate
    /// links. Drawn beneath region labels and system plates so it reads as background structure.
    /// </summary>
    private void DrawInterRegionConnections(DrawingContext context, Rect viewport)
    {
        if (_schematicLayout is null) return;

        var pen = new Pen(RegionConnectionBrush, 1.2);
        var expanded = new Rect(viewport.X - 200, viewport.Y - 200, viewport.Width + 400, viewport.Height + 400);
        foreach (var (regionA, regionB) in _schematicLayout.RegionConnections)
        {
            if (!_schematicLayout.RegionCentroids.TryGetValue(regionA, out var centroidA)) continue;
            if (!_schematicLayout.RegionCentroids.TryGetValue(regionB, out var centroidB)) continue;

            var screenA = WorldToScreen(centroidA);
            var screenB = WorldToScreen(centroidB);
            if (!expanded.Contains(screenA) && !expanded.Contains(screenB)) continue;

            context.DrawLine(pen, screenA, screenB);
        }
    }

    private const double InterRegionStubLengthPx = 24.0;

    /// <summary>
    /// Draws a short stub toward an off-screen regional-gate neighbor, labeled with its name, so
    /// the gate is never silently dropped just because the neighboring system itself isn't in the
    /// current viewport (see <see cref="Render"/>'s gate-line loop).
    /// </summary>
    private void DrawInterRegionGateStub(DrawingContext context, Point screen, SolarSystem neighbor)
    {
        var neighborScreen = WorldToScreen(Project(neighbor));
        double dx = neighborScreen.X - screen.X, dy = neighborScreen.Y - screen.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double ux = len > 0.01 ? dx / len : 0;
        double uy = len > 0.01 ? dy / len : 1;

        var tip = new Point(screen.X + ux * InterRegionStubLengthPx, screen.Y + uy * InterRegionStubLengthPx);
        context.DrawLine(new Pen(InterRegionGateBrush, 1.4), screen, tip);

        var label = new FormattedText(neighbor.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 8.5, InterRegionGateBrush);
        var labelPos = new Point(tip.X + ux * 3 - (ux < -0.3 ? label.Width : 0), tip.Y + uy * 3 - label.Height / 2);
        context.FillRectangle(SchematicLabelHalo, new Rect(labelPos.X - 2, labelPos.Y - 1, label.Width + 4, label.Height + 2));
        context.DrawText(label, labelPos);
    }

    private HashSet<int>? BuildRouteSystemIds() =>
        RouteSteps is null ? null : RouteSteps.SelectMany(s => new[] { s.FromSystemId, s.ToSystemId }).ToHashSet();

    private bool IsPinnedSystem(int systemId, HashSet<int>? routeSystemIds) =>
        systemId == _selectedSystemId ||
        systemId == FromSystemId ||
        systemId == ToSystemId ||
        systemId == _hoveredSystem?.Id ||
        routeSystemIds?.Contains(systemId) == true;

    private static void OccupyCell(Dictionary<(long Cx, long Cy), List<Rect>> occupied, Rect rect, double cellSize)
    {
        long minCx = (long)Math.Floor(rect.X / cellSize);
        long maxCx = (long)Math.Floor((rect.X + rect.Width) / cellSize);
        long minCy = (long)Math.Floor(rect.Y / cellSize);
        long maxCy = (long)Math.Floor((rect.Y + rect.Height) / cellSize);
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

    private static bool OverlapsCell(Dictionary<(long Cx, long Cy), List<Rect>> occupied, Rect rect, double cellSize)
    {
        long minCx = (long)Math.Floor(rect.X / cellSize);
        long maxCx = (long)Math.Floor((rect.X + rect.Width) / cellSize);
        long minCy = (long)Math.Floor(rect.Y / cellSize);
        long maxCy = (long)Math.Floor((rect.Y + rect.Height) / cellSize);
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

    private void DrawSystemLabels(DrawingContext context, List<(SolarSystem System, Point Screen)> visible, bool schematic)
    {
        if (visible.Count == 0) return;

        if (schematic) DrawSchematicPlates(context, visible);
        else DrawStandardLabels(context, visible);
    }

    /// <summary>
    /// Draws system-name labels with greedy collision avoidance: pinned systems (selected,
    /// hovered, route endpoints/steps) always get a label; everything else is offered in
    /// priority order (gate-hub systems first) and only drawn if its bounding box doesn't
    /// overlap an already-placed label or system marker. This is what keeps the map legible
    /// at any zoom level instead of dumping every name on top of each other.
    /// </summary>
    private void DrawStandardLabels(DrawingContext context, List<(SolarSystem System, Point Screen)> visible)
    {
        const double fontSize = 9.0;
        var typeface = Typeface.Default;
        var routeSystemIds = BuildRouteSystemIds();
        var occupied = new Dictionary<(long Cx, long Cy), List<Rect>>();

        // Reserve the space around every visible dot so labels never start on top of a marker,
        // even one whose own label loses the placement race.
        foreach (var (_, screen) in visible)
        {
            OccupyCell(occupied, new Rect(screen.X - 4, screen.Y - 4, 8, 8), LabelCellSizePx);
        }

        var ordered = visible
            .OrderByDescending(v => IsPinnedSystem(v.System.Id, routeSystemIds))
            .ThenByDescending(v => _map?.GateNeighbors(v.System.Id).Count ?? 0)
            .ThenByDescending(v => v.System.Security)
            .Take(MaxLabelCandidates)
            .ToList();

        foreach (var (system, screen) in ordered)
        {
            bool pinned = IsPinnedSystem(system.Id, routeSystemIds);
            var formatted = new FormattedText(system.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);
            var rect = new Rect(screen.X + 6, screen.Y - formatted.Height / 2, formatted.Width + 3, formatted.Height);

            if (!pinned && OverlapsCell(occupied, rect, LabelCellSizePx)) continue;

            context.FillRectangle(StandardLabelHalo, new Rect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2));
            context.DrawText(formatted, new Point(rect.X + 1, rect.Y));
            OccupyCell(occupied, rect, LabelCellSizePx);
        }
    }

    /// <summary>
    /// Level of detail for a Schematic-mode system plate, from most to least detailed. A region
    /// is always rendered entirely at one tier -- never a mix -- and a tier is only used for a
    /// region once every one of its systems has been checked to fit at that tier without
    /// overlapping any other plate anywhere on screen (including other regions' already-placed
    /// plates). <see cref="PlateTier.Dot"/> is the guaranteed-fits floor: like Standard mode's dot markers,
    /// it is always drawn for every system, even in the (very rare, extreme zoom-out) case where
    /// screen positions themselves have converged closer together than the dot's own size.
    /// </summary>
    private enum PlateTier { Full, Compact, Dot }

    private const double SchematicDotDiameter = 7.0;

    // Base sizes at plateScale == 1 (i.e. at the default Schematic zoom). Every dimension below
    // is this base value times a single clamped scale factor -- true linear/proportional scaling,
    // not independently-clamped fields that hit their floor at different zoom levels and distort
    // the plate's proportions. The name font is intentionally the smallest of the two text lines
    // (rather than the largest, as it used to be) and the minimum width is small enough that the
    // name+kill-count plate keeps fitting well past the default zoom, instead of falling back to
    // a shorter tier as soon as the view zooms out even a little.
    private const double SchematicFullNameFontBase = 7.0;
    private const double SchematicFullKillFontBase = 6.5;
    private const double SchematicFullPadXBase = 3.0;
    private const double SchematicFullPadYBase = 1.5;
    private const double SchematicFullLineGapBase = 0.5;
    private const double SchematicFullMinWidthBase = 26.0;

    private const double SchematicCompactNameFontBase = 6.5;
    private const double SchematicCompactPadXBase = 2.5;
    private const double SchematicCompactPadYBase = 1.0;
    private const double SchematicCompactMinWidthBase = 12.0;

    private const double SchematicPlateMinScale = 0.5;
    private const double SchematicPlateMaxScale = 1.8;

    /// <summary>
    /// Dotlan-style system plates. Unlike the old per-plate greedy layout (which could show some
    /// systems in a region as full plates while silently dropping their neighbors -- the "some
    /// plates visible, some not" bug), plates are now resolved per-region: every system in a
    /// region renders at the same <see cref="PlateTier"/>, chosen as the most detailed tier that
    /// lets every system in that region fit without overlapping anything already placed. This
    /// guarantees both that a region is never partially shown and that non-dot plates never
    /// overlap. As the view zooms out and spacing shrinks, regions degrade from full name+NPC-kill
    /// plates, to a name-and-color-only plate, to a plain NPC-kill-colored dot -- exactly the three
    /// levels of detail Dotlan-style maps are expected to show.
    /// </summary>
    private void DrawSchematicPlates(DrawingContext context, List<(SolarSystem System, Point Screen)> visible)
    {
        if (visible.Count == 0) return;

        double plateScale = Math.Clamp((Scale / _baseScale) / DefaultSchematicZoom, SchematicPlateMinScale, SchematicPlateMaxScale);
        var typeface = Typeface.Default;
        var routeSystemIds = BuildRouteSystemIds();
        const double cellSize = 12.0;

        double fullNameFont = SchematicFullNameFontBase * plateScale;
        double fullKillFont = SchematicFullKillFontBase * plateScale;
        double fullPadX = SchematicFullPadXBase * plateScale;
        double fullPadY = SchematicFullPadYBase * plateScale;
        double fullLineGap = SchematicFullLineGapBase * plateScale;
        double fullMinWidth = SchematicFullMinWidthBase * plateScale;

        double compactNameFont = SchematicCompactNameFontBase * plateScale;
        double compactPadX = SchematicCompactPadXBase * plateScale;
        double compactPadY = SchematicCompactPadYBase * plateScale;
        double compactMinWidth = SchematicCompactMinWidthBase * plateScale;

        Rect ComputeRect(PlateTier tier, SolarSystem system, Point screen)
        {
            switch (tier)
            {
                case PlateTier.Full:
                {
                    var nameText = MeasureText(system.Name, fullNameFont, typeface);
                    int kills = NpcKillsProvider?.Invoke(system.Id) ?? 0;
                    var killText = MeasureText(kills.ToString(CultureInfo.InvariantCulture), fullKillFont, typeface);
                    double width = Math.Max(Math.Max(nameText.Width, killText.Width) + fullPadX * 2, fullMinWidth);
                    double height = fullPadY + nameText.Height + fullLineGap + killText.Height + fullPadY;
                    return new Rect(screen.X - width / 2, screen.Y - height / 2, width, height);
                }
                case PlateTier.Compact:
                {
                    var nameText = MeasureText(system.Name, compactNameFont, typeface);
                    double width = Math.Max(nameText.Width + compactPadX * 2, compactMinWidth);
                    double height = compactPadY * 2 + nameText.Height;
                    return new Rect(screen.X - width / 2, screen.Y - height / 2, width, height);
                }
                default:
                    return new Rect(screen.X - SchematicDotDiameter / 2, screen.Y - SchematicDotDiameter / 2, SchematicDotDiameter, SchematicDotDiameter);
            }
        }

        // Dry-run: does every system in this region fit at this tier without overlapping any
        // plate placed so far (this region's own plates, plus earlier regions' already-committed
        // ones)? Order doesn't matter for the answer -- two rects either overlap or they don't --
        // so there is no "first come, first shown" bias within the region being tested.
        bool RegionFitsAtTier(IEnumerable<(SolarSystem System, Point Screen)> regionSystems, PlateTier tier, Dictionary<(long Cx, long Cy), List<Rect>> baseOccupied)
        {
            var trial = CloneOccupied(baseOccupied);
            foreach (var (system, screen) in regionSystems)
            {
                var rect = ComputeRect(tier, system, screen);
                if (OverlapsCell(trial, rect, cellSize)) return false;
                OccupyCell(trial, rect, cellSize);
            }
            return true;
        }

        _lastPlateRects = new Dictionary<int, Rect>(visible.Count);
        var occupied = new Dictionary<(long Cx, long Cy), List<Rect>>();

        var byRegion = visible.ToLookup(v => v.System.RegionId);
        var pinnedRegionIds = visible
            .Where(v => IsPinnedSystem(v.System.Id, routeSystemIds))
            .Select(v => v.System.RegionId)
            .ToHashSet();
        // Regions holding a pinned system get first pick of space (helps them keep full detail);
        // everything else follows in a stable, deterministic order.
        var regionOrder = byRegion.Select(g => g.Key)
            .OrderByDescending(id => pinnedRegionIds.Contains(id))
            .ThenBy(id => id);

        foreach (var regionId in regionOrder)
        {
            var regionSystems = byRegion[regionId].ToList();

            var tier = RegionFitsAtTier(regionSystems, PlateTier.Full, occupied) ? PlateTier.Full
                : RegionFitsAtTier(regionSystems, PlateTier.Compact, occupied) ? PlateTier.Compact
                : PlateTier.Dot;

            foreach (var (system, screen) in regionSystems)
            {
                var rect = ComputeRect(tier, system, screen);

                int? npcKills = NpcKillsProvider?.Invoke(system.Id);
                var fillBrush = npcKills is int kills ? NpcKillsFillBrush(kills) : PlateFillBrush(system.Security);
                var textBrush = ReadableTextBrush(fillBrush);

                bool isSelected = system.Id == _selectedSystemId;
                bool isFrom = system.Id == FromSystemId;
                bool isTo = system.Id == ToSystemId;
                bool isGateNeighbor = _gateNeighbors.Contains(system.Id);
                bool isJumpReachable = _reachableByJump.Contains(system.Id);

                IBrush borderBrush = isFrom ? Brushes.LimeGreen
                    : isTo ? Brushes.OrangeRed
                    : isSelected ? Brushes.Black
                    : isGateNeighbor ? GateHighlightBrush
                    : Brushes.Black;
                double borderWidth = isSelected || isFrom || isTo ? 2.0 : isGateNeighbor ? 1.6 : 1.0;

                // Dotlan-style jump-range highlight: a bold black outline traced directly on the
                // plate's own border (same rect/corner radius), drawn after the plate so it sits
                // on top, rather than a separate ring floating outside the plate.
                var jumpRangePen = isJumpReachable ? new Pen(Brushes.Black, JumpRangeRingWidth) : null;

                switch (tier)
                {
                    case PlateTier.Full:
                    {
                        var nameText = new FormattedText(system.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fullNameFont, textBrush);
                        var killText = new FormattedText((npcKills ?? 0).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fullKillFont, textBrush);
                        context.DrawRectangle(fillBrush, new Pen(borderBrush, borderWidth), rect, 5, 5);
                        context.DrawText(nameText, new Point(screen.X - nameText.Width / 2, rect.Y + fullPadY));
                        context.DrawText(killText, new Point(screen.X - killText.Width / 2, rect.Y + fullPadY + nameText.Height + fullLineGap));
                        if (jumpRangePen is not null) context.DrawRectangle(null, jumpRangePen, rect, 5, 5);
                        break;
                    }
                    case PlateTier.Compact:
                    {
                        var nameText = new FormattedText(system.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, compactNameFont, textBrush);
                        context.DrawRectangle(fillBrush, new Pen(borderBrush, borderWidth), rect, 4, 4);
                        context.DrawText(nameText, new Point(screen.X - nameText.Width / 2, rect.Y + compactPadY));
                        if (jumpRangePen is not null) context.DrawRectangle(null, jumpRangePen, rect, 4, 4);
                        break;
                    }
                    default:
                        context.DrawEllipse(fillBrush, new Pen(borderBrush, borderWidth), screen, SchematicDotDiameter / 2, SchematicDotDiameter / 2);
                        if (jumpRangePen is not null) context.DrawEllipse(null, jumpRangePen, screen, SchematicDotDiameter / 2, SchematicDotDiameter / 2);
                        break;
                }

                _lastPlateRects[system.Id] = rect;
                OccupyCell(occupied, rect, cellSize);
            }
        }
    }

    private static FormattedText MeasureText(string text, double fontSize, Typeface typeface) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);

    private static Dictionary<(long Cx, long Cy), List<Rect>> CloneOccupied(Dictionary<(long Cx, long Cy), List<Rect>> source)
    {
        var clone = new Dictionary<(long Cx, long Cy), List<Rect>>(source.Count);
        foreach (var (key, list) in source) clone[key] = new List<Rect>(list);
        return clone;
    }

    /// <summary>Null-sec and red low-sec plates are white (Dotlan-style); others use security color.</summary>
    private static IBrush PlateFillBrush(double security) =>
        Math.Round(security, 1) < 0.2 ? Brushes.White : SecurityBrush(security);

    /// <summary>
    /// Dotlan "NPC Kills" style gradient: white (no ratting activity) through green, yellow and
    /// orange up to red for the busiest bot-farm/ratting systems. Stops are tuned against the
    /// real distribution of ESI's last-hour system_kills feed (median ~50, p95 ~650, max ~2500+).
    /// </summary>
    private static readonly (double Kills, Color Color)[] NpcKillsColorStops =
    {
        (0,    Color.FromRgb(0xFF, 0xFF, 0xFF)),
        (25,   Color.FromRgb(0xDC, 0xF0, 0xC2)),
        (75,   Color.FromRgb(0x7A, 0xD1, 0x3C)),
        (200,  Color.FromRgb(0xF5, 0xE0, 0x1E)),
        (500,  Color.FromRgb(0xF2, 0x8C, 0x1E)),
        (1200, Color.FromRgb(0xE0, 0x1E, 0x14)),
    };

    private static IBrush NpcKillsFillBrush(int kills)
    {
        if (kills <= 0) return new SolidColorBrush(NpcKillsColorStops[0].Color);

        for (int i = 1; i < NpcKillsColorStops.Length; i++)
        {
            var (hiKills, hiColor) = NpcKillsColorStops[i];
            if (kills > hiKills && i < NpcKillsColorStops.Length - 1) continue;

            var (loKills, loColor) = NpcKillsColorStops[i - 1];
            double t = hiKills > loKills ? Math.Clamp((kills - loKills) / (hiKills - loKills), 0.0, 1.0) : 1.0;
            return new SolidColorBrush(LerpColor(loColor, hiColor, t));
        }

        return new SolidColorBrush(NpcKillsColorStops[^1].Color);
    }

    private static Color LerpColor(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Picks black or white text for best contrast against a plate fill.</summary>
    private static IBrush ReadableTextBrush(IBrush fillBrush)
    {
        if (fillBrush is not ISolidColorBrush solid) return Brushes.Black;
        var c = solid.Color;
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminance >= 0.5 ? Brushes.Black : Brushes.White;
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

    private static readonly IBrush StructureIconBorder = new SolidColorBrush(Color.FromArgb(220, 20, 20, 22));

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
                context.DrawRectangle(brush, new Pen(StructureIconBorder, 0.8), rect);
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
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xCC, 0x22, 0x22)), new Pen(StructureIconBorder, 0.8), rect);
                var pen = new Pen(Brushes.White, 1.2);
                context.DrawLine(pen, new Point(rect.X, rect.Y), new Point(rect.Right, rect.Bottom));
                context.DrawLine(pen, new Point(rect.Right, rect.Y), new Point(rect.X, rect.Bottom));
                break;
            }
            case StructureKind.Keepstar:
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x88, 0x44, 0xCC)), new Pen(StructureIconBorder, 0.8), center, size + 1, size + 1);
                break;
            case StructureKind.Fortizar:
            {
                var rect = new Rect(center.X - size, center.Y - size, size * 2, size * 2);
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xDD, 0x88, 0x22)), new Pen(StructureIconBorder, 0.8), rect);
                break;
            }
            case StructureKind.Azbel:
            {
                var rect = new Rect(center.X - size * 0.85, center.Y - size * 0.85, size * 1.7, size * 1.7);
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), new Pen(StructureIconBorder, 0.8), rect);
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

    /// <summary>
    /// "You are here" beacon for the live-tracked pilot: a pulsing halo plus a bold black-ringed
    /// dot with a crosshair, all sized in constant screen pixels (not world units), so it reads
    /// exactly the same at any zoom level instead of shrinking away or blending into a plate the
    /// way the plain selection ring can. Always drawn last, on top of every plate/label/route
    /// line, so nothing else on the map can cover it.
    /// </summary>
    private static void DrawPilotBeacon(DrawingContext context, Point screen)
    {
        const double haloR = 17.0;
        const double ringR = 10.0;
        const double coreR = 4.5;
        const double tickGap = 3.0;
        const double tickLen = 6.0;

        context.DrawEllipse(PilotBeaconHalo, null, screen, haloR, haloR);
        context.DrawEllipse(null, new Pen(PilotBeaconRing, 2.2), screen, ringR, ringR);

        var tickPen = new Pen(Brushes.Black, 1.4);
        context.DrawLine(tickPen, new Point(screen.X - ringR - tickGap - tickLen, screen.Y), new Point(screen.X - ringR - tickGap, screen.Y));
        context.DrawLine(tickPen, new Point(screen.X + ringR + tickGap, screen.Y), new Point(screen.X + ringR + tickGap + tickLen, screen.Y));
        context.DrawLine(tickPen, new Point(screen.X, screen.Y - ringR - tickGap - tickLen), new Point(screen.X, screen.Y - ringR - tickGap));
        context.DrawLine(tickPen, new Point(screen.X, screen.Y + ringR + tickGap), new Point(screen.X, screen.Y + ringR + tickGap + tickLen));

        context.DrawEllipse(PilotBeaconCore, new Pen(Brushes.White, 1.4), screen, coreR, coreR);
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
                string shipLabel = _jumpRangeShipClass is CapitalShipClass cls ? cls.ToDisplayLabel() : "текущий корабль (вкладка Маршрут)";
                lines.Add($"Дальность прыжка: {_selectedRangeLy:F1} LY ({shipLabel})");
                lines.Add($"Систем в пределах прыжка: {_reachableByJump.Count}");
            }
            else
            {
                lines.Add("ПКМ → Дальность прыжка, чтобы выбрать корабль");
            }
        }

        if (NpcKillsProvider?.Invoke(focusSystem.Id) is int npcKills)
        {
            lines.Add($"NPC kills (1ч): {npcKills}");
        }

        string text = string.Join('\n', lines);
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13,
            _displayMode == MapDisplayMode.Schematic ? SchematicLabelBrush : Brushes.Black);

        var panelFill = PanelBackground;
        var panelBorder = PanelBorder;

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
