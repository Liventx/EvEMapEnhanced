using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Models;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using EvEMapEnhanced.Core.Stats;
using EvEMapEnhanced.Core.Structures;

namespace EvEMapEnhanced.Desktop;

/// <summary>
/// 2D EVE universe map: Standard mode uses real coordinates; Schematic (Dotlan) anchors
/// each region at its in-game position but lays out systems inside with a gate graph.
/// </summary>
public sealed class MapControl : Control, ICustomHitTest
{
    public const double MinZoomLevel = 0.05;
    public const double MaxZoomLevel = 400.0;
    private const double MinZoom = MinZoomLevel;
    private const double MaxZoom = MaxZoomLevel;
    private const double HitRadiusPx = 9.0;
    private const double ClickDragThresholdPx = 4.0;
    private const int GateLineLodThreshold = 5000;
    private const int MaxLabelCandidates = 1500;
    private const double LabelCellSizePx = 13.0;
    private const double DefaultStandardZoom = 3.0;
    private const double DefaultSchematicZoom = SchematicPlateLayoutPolicy.DefaultSchematicZoom;
    /// <summary>Standard/main-map system markers match the Jump Range mini-map radii.</summary>
    private const double StandardMarkerRadius = 3.5;
    private const double StandardMarkerRadiusSelected = 5.0;
    private const double StandardMarkerRadiusOutOfRange = 3.0;
    /// <summary>
    /// Screen-space margin used when clipping gate stubs to off-screen neighbors. Keeps Skia from
    /// dropping geometry whose far endpoint lands at extreme pixel coordinates under high zoom.
    /// </summary>
    private const double GateLineClipMarginPx = 64.0;
    /// <summary>Cap Standard jump-range ellipses so extreme zoom cannot explode the draw list.</summary>
    private const double StandardJumpRangeRadiusCapFactor = 4.0;
    /// <summary>At or below this zoom the Schematic region names paint as an overlay on top of everything; above it they fall behind system plates/labels.</summary>
    private const double RegionLabelOverlayMaxZoom = 5.0;

    // Dotlan-style jump-range highlight: a bold black outline traced directly on a system's own
    // marker/plate border, rather than a separate ring floating outside it or a recolored border.
    private const double JumpRangeRingWidth = 4.0;
    private static readonly DashStyle JumpRangeSimulationDashStyle = new(new double[] { 4, 3 }, 0);
    private static readonly IBrush JumpRangeIntersectionBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0x5A, 0xFF));
    // Source ("origin") systems the simulation intersections are computed from get a bold orange
    // outline so the pilot can tell the fixed anchors apart from merely reachable/overlapping systems.
    private static readonly IBrush JumpRangeSimulationOriginBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00));

    private static readonly IBrush GateLineBrush = new SolidColorBrush(Color.FromArgb(200, 90, 90, 90));
    // Dotlan's real palette is a plain white map, not a dark theme - both display modes now
    // share a white background; "Schematic" only differs in node/plate style and line tone.
    private static readonly IBrush SchematicBackground = Brushes.White;
    private static readonly IBrush SchematicGateLineBrush = new SolidColorBrush(Color.FromArgb(200, 70, 70, 70));
    private static readonly IBrush SchematicCrossRegionGateLineBrush = new SolidColorBrush(Color.FromArgb(200, 175, 175, 175));
    private static readonly IBrush SchematicRegionLabelBrush = new SolidColorBrush(Color.FromArgb(200, 55, 95, 195));
    private static readonly IBrush StandardLabelHalo = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
    private static readonly IBrush SchematicLabelHalo = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
    // Debug grid overlay (developer aid for hand-tuning the curated region grid).
    private static readonly IBrush DebugGridLineBrush = new SolidColorBrush(Color.FromArgb(70, 40, 90, 200));
    private static readonly IBrush DebugGridLabelBrush = new SolidColorBrush(Color.FromArgb(230, 25, 70, 190));
    private static readonly IBrush DebugRegionCoordBrush = new SolidColorBrush(Color.FromArgb(235, 200, 40, 40));
    private static readonly IBrush DebugReadoutBackground = new SolidColorBrush(Color.FromArgb(230, 20, 20, 30));
    private static readonly IBrush DebugReadoutText = Brushes.White;

    private static readonly IBrush GateHighlightBrush = new SolidColorBrush(Color.FromArgb(255, 30, 140, 30));
    private static readonly IBrush JumpRangeFill = new SolidColorBrush(Color.FromArgb(35, 90, 60, 200));
    private static readonly IBrush JumpRangeStroke = new SolidColorBrush(Color.FromArgb(200, 90, 60, 200));
    private static readonly IBrush JumpRangeMiniMapBackgroundGateBrush = new SolidColorBrush(Color.FromArgb(140, 145, 145, 145));
    private static readonly IBrush JumpRangeMiniMapBorderGateBrush = new SolidColorBrush(Color.FromArgb(185, 120, 120, 120));
    private static readonly IBrush JumpRangeMiniMapOutOfRangeMarkerBrush = new SolidColorBrush(Color.FromArgb(210, 80, 80, 80));
    private static readonly IBrush JumpRangeMiniMapOutOfRangeLabelHalo = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));
    private static readonly IBrush PvPHotHighlight = new SolidColorBrush(Color.FromArgb(255, 220, 50, 50));
    private static readonly IBrush PvPRecentHighlight = new SolidColorBrush(Color.FromArgb(255, 235, 190, 40));
    private static readonly IBrush PvPNpcCapitalHighlight = new SolidColorBrush(Color.FromArgb(255, 170, 70, 230));
    private static readonly IBrush PvPHotFill = new SolidColorBrush(Color.FromArgb(70, 220, 50, 50));
    private static readonly IBrush PvPRecentFill = new SolidColorBrush(Color.FromArgb(65, 235, 190, 40));
    private static readonly IBrush PvPNpcCapitalFill = new SolidColorBrush(Color.FromArgb(80, 170, 70, 230));
    private static readonly IBrush TrackedCharacterHintBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x5C, 0x9A));
    private static readonly IBrush SearchedSystemMarkerBrush = new SolidColorBrush(Color.FromArgb(255, 220, 50, 50));
    private static readonly IBrush SearchedSystemMarkerFill = new SolidColorBrush(Color.FromArgb(50, 220, 50, 50));
    // Sansha incursion overlay: muted salad-green glow on infested systems.
    private const double SanshaIncursionAnimMinZoom = RegionLabelOverlayMaxZoom;
    private static readonly Color SanshaIncursionColor = Color.FromRgb(0xCC, 0xF5, 0x8E);
    private static readonly Color TheraWormholeColor = Color.FromRgb(0xE8, 0xA8, 0x38);
    private static readonly Color TurnurWormholeColor = Color.FromRgb(0x9B, 0x6B, 0x3D);
    private static readonly Color ManualWormholeColor = Color.FromRgb(0x4A, 0x4A, 0x4A);
    private static readonly IBrush TheraWormholeHintBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x6A, 0x12));
    private static readonly IBrush TurnurWormholeHintBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x44, 0x23));
    private static readonly IBrush ManualWormholeHintBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    // Dockable NPC-station flag: a small light-green (салатовый) square in a plate's bottom-right corner.
    private static readonly IBrush NpcStationMarkerBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0xE0, 0x3C));
    private static readonly IBrush NpcStationNoCloneMarkerBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x2B, 0x2B));
    private static readonly Pen NpcStationMarkerPen = new(new SolidColorBrush(Color.FromArgb(210, 30, 70, 10)), 0.4);
    // Standard-mode markers (and mini-map) need a dark outline so white security fills stay visible.
    private static readonly Pen StandardSystemOutlinePen = new(new SolidColorBrush(Color.FromArgb(220, 60, 60, 60)), 1.0);
    private static readonly IBrush JumpRouteBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));
    private static readonly IBrush GateRouteBrush = new SolidColorBrush(Color.FromArgb(255, 0x00, 0xFF, 0x44));
    private static readonly IBrush GateRouteGlowBrush = new SolidColorBrush(Color.FromArgb(100, 0x00, 0xFF, 0x44));
    private static readonly IBrush WormholeRouteBrush = new SolidColorBrush(Color.FromArgb(255, 0xE8, 0xA8, 0x38));
    private static readonly IBrush WormholeRouteGlowBrush = new SolidColorBrush(Color.FromArgb(100, 0xE8, 0xA8, 0x38));

    // Dotlan/EVE-style "you are here" beacon for the live-tracked pilot location. Fixed screen-pixel
    // size at normal zoom; shrinks on the universe overview via _wideZoomHighlightScale.
    private static readonly IBrush PilotBeaconHalo = new SolidColorBrush(Color.FromArgb(70, 255, 205, 0));
    private static readonly IBrush PilotBeaconRing = new SolidColorBrush(Color.FromArgb(255, 255, 205, 0));
    private static readonly IBrush PilotBeaconCore = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));

    // Live-tracked cyno pilot beacon: same crosshair style as the main pilot marker, but light blue (cyan).
    private static readonly IBrush CynoBeaconHalo = new SolidColorBrush(Color.FromArgb(70, 80, 190, 255));
    private static readonly IBrush CynoBeaconRing = new SolidColorBrush(Color.FromArgb(255, 0, 170, 255));
    private static readonly IBrush CynoBeaconCore = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));

    // Live-tracked SC pilot beacon: same crosshair shape, but deep blue (distinct from cyno cyan).
    private static readonly IBrush ScBeaconHalo = new SolidColorBrush(Color.FromArgb(70, 35, 55, 150));
    private static readonly IBrush ScBeaconRing = new SolidColorBrush(Color.FromArgb(255, 20, 40, 130));
    private static readonly IBrush ScBeaconCore = new SolidColorBrush(Color.FromRgb(0x14, 0x2A, 0x8C));

    private UniverseMap? _map;
    private SchematicMapLayout? _schematicLayout;
    private Dictionary<int, Point> _standardRegionCentroids = new();
    /// <summary>Zoom level after the last <see cref="FocusJumpRange"/> auto-fit; mini-map region labels sit on top at or below this.</summary>
    private double _jumpRangeFocusZoom = DefaultStandardZoom;
    private double _minX, _maxX, _minZ, _maxZ;
    private double _baseScale = 1.0;
    private double _zoom = 1.0;
    /// <summary>Per-frame scale for fixed-pixel overlays when the universe overview is zoomed out.</summary>
    private double _wideZoomHighlightScale = 1.0;
    private Point _center;
    private MapDisplayMode _displayMode = MapDisplayMode.Schematic;
    private MapPlateColorMode _plateColorMode = MapPlateColorMode.NpcKills;

    private Point? _pressScreenPos;
    private Point? _lastPointerPos;
    private bool _isPanning;
    private bool _leftButtonDown;
    private SolarSystem? _hoveredSystem;
    /// <summary>System hovered on a linked map instance (e.g. the Jump Range mini-map).</summary>
    private int? _linkedHoveredSystemId;
    private int? _searchedSystemId;
    private int? _contextMenuSystemId;

    private int? _selectedSystemId;
    /// <summary>
    /// System the jump-range circle and reachability highlight are computed from. Normally follows
    /// click selection; when <see cref="PinJumpRangeOrigin"/> is on, left-clicks only update
    /// <see cref="_selectedSystemId"/> and leave this unchanged so the pilot can inspect other
    /// systems without losing their fixed jump-range overlay.
    /// </summary>
    private int? _jumpRangeOriginSystemId;
    private double _selectedRangeLy;

    /// <summary>
    /// System the live-tracked pilot is currently in (see <see cref="SelectSystemExternally"/>).
    /// Kept independent of <see cref="_selectedSystemId"/> so clicking elsewhere on the map to
    /// inspect another system never makes the "you are here" beacon disappear.
    /// </summary>
    private int? _pilotSystemId;
    /// <summary>Systems where live-tracked cyno pilots are currently located.</summary>
    private HashSet<int> _cynoSystemIds = new();
    /// <summary>Systems where live-tracked SC pilots are currently located.</summary>
    private HashSet<int> _scSystemIds = new();
    private bool _pinJumpRangeOrigin;
    private double _jumpOriginPulsePhase;
    private DispatcherTimer? _jumpOriginAnimTimer;
    private double _gateRouteAnimPhase;
    private DispatcherTimer? _gateRouteAnimTimer;
    private double _incursionAnimPhase;
    private DispatcherTimer? _incursionAnimTimer;
    private bool _incursionsActive;
    private double _wormholeAnimPhase;
    private DispatcherTimer? _wormholeAnimTimer;
    private bool _wormholesActive;
    private bool _manualWormholesActive = true;
    private HashSet<int> _reachableByJump = new();
    private HashSet<int> _gateNeighbors = new();
    private HashSet<int> _lastNotifiedReachable = new();
    private CapitalShipClass? _jumpRangeShipClass;
    private bool _jumpRangeSimulationActive;
    private readonly List<JumpRangeSimulationLayer> _simulationLayers = new();
    /// <summary>Systems that could be added as the next simulation origin (jump range hits the current intersection).</summary>
    private readonly HashSet<int> _simulationOriginCandidates = new();
    private bool _simulationCompletionNotified;
    private SimulationToast? _simulationToast;
    private DispatcherTimer? _simulationToastTimer;

    private sealed class JumpRangeSimulationLayer
    {
        public required int OriginSystemId { get; init; }
        public required double RangeLy { get; init; }
        public required HashSet<int> ReachableSystemIds { get; init; }
    }

    private sealed class SimulationToast
    {
        public required string Text { get; init; }
        public required Point ScreenPos { get; init; }
        public DateTime StartedAt { get; init; }
    }

    /// <summary>Screen-space rectangles of the schematic plates actually drawn last frame, keyed by system id -- used so clicks hit-test against real geometry instead of a fixed-radius circle.</summary>
    private Dictionary<int, Rect> _lastPlateRects = new();

    /// <summary>Plate detail tier resolved on the last schematic render; drives the wide-zoom hover hint and dot hit-test padding.</summary>
    private SchematicPlateDetailTier _currentPlateTier = SchematicPlateDetailTier.Dot;

    private readonly ContextMenu _contextMenu;
    private readonly MenuItem _routeFromItem;
    private readonly MenuItem _routeToItem;
    private readonly MenuItem _routeWaypointItem;
    private readonly MenuItem _zkillboardItem;
    private readonly MenuItem _copySystemNameItem;
    private readonly MenuItem _addManualWormholeItem;
    private readonly MenuItem _removeManualWormholeItem;

    public int? FromSystemId { get; set; }
    public int? ToSystemId { get; set; }

    /// <summary>Ordered intermediate waypoint system IDs the active route passes through.</summary>
    public IReadOnlyList<int>? WaypointSystemIds { get; set; }

    private IReadOnlyList<RouteStep>? _routeSteps;
    public IReadOnlyList<RouteStep>? RouteSteps
    {
        get => _routeSteps;
        set
        {
            _routeSteps = value;
            SyncGateRouteAnimation();
        }
    }

    /// <summary>System last selected by a left-click (or cleared by clicking empty space).</summary>
    public int? SelectedSystemId => _selectedSystemId;

    /// <summary>System the active jump-range overlay is anchored to.</summary>
    public int? JumpRangeOriginSystemId => _jumpRangeOriginSystemId;

    /// <summary>
    /// When true, left-click selection and live pilot tracking no longer move the jump-range
    /// origin; only <see cref="SetJumpRangeOrigin"/> (or unchecking Focus) may change it.
    /// </summary>
    public bool PinJumpRangeOrigin
    {
        get => _pinJumpRangeOrigin;
        set => _pinJumpRangeOrigin = value;
    }

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
            UpdateSimulationReachability();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// When enabled, left-clicks add jump-range overlays without moving the main profile origin.
    /// Multiple origins accumulate; their intersection is highlighted in blue.
    /// </summary>
    public bool JumpRangeSimulationActive
    {
        get => _jumpRangeSimulationActive;
        set
        {
            if (_jumpRangeSimulationActive == value) return;
            _jumpRangeSimulationActive = value;
            if (value)
            {
                SeedSimulationFromCurrentJumpRangeOrigin();
            }
            else
            {
                _simulationLayers.Clear();
                _simulationOriginCandidates.Clear();
                _simulationCompletionNotified = false;
                ClearSimulationToast();
                SyncJumpOriginAnimation();
                NotifyReachabilityIfChanged();
            }
            InvalidateVisual();
        }
    }

    /// <summary>Origins currently included in jump-range simulation mode.</summary>
    public IReadOnlyList<int> JumpRangeSimulationOriginIds =>
        _simulationLayers.Select(layer => layer.OriginSystemId).ToList();

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

    /// <summary>
    /// Reports whether a system contains at least one NPC station (from the SDE). Systems that do
    /// get a small light-green square in the bottom-right corner of their schematic plate.
    /// </summary>
    public Func<int, bool>? HasNpcStationProvider { get; set; }

    /// <summary>
    /// Reports whether a system has NPC stations but none with cloning or jump-clone services.
    /// Such systems get a diagonal red half-overlay on the station marker square.
    /// </summary>
    public Func<int, bool>? NpcStationNoCloneProvider { get; set; }

    /// <summary>Whether plates use the NPC-kills gradient or security-status colors.</summary>
    public MapPlateColorMode PlateColorMode
    {
        get => _plateColorMode;
        set
        {
            if (_plateColorMode == value) return;
            _plateColorMode = value;
            InvalidateVisual();
        }
    }

    /// <summary>Current zoom multiplier applied on top of the auto-fit base scale.</summary>
    public double ZoomLevel
    {
        get => _zoom;
        set => SetZoomLevel(value, zoomAnchorScreen: null);
    }

    /// <summary>Raised whenever <see cref="ZoomLevel"/> changes (wheel, slider, fit, etc.).</summary>
    public event EventHandler<double>? ZoomLevelChanged;

    /// <summary>Maps a 0–100 slider value to a zoom level on a logarithmic scale.</summary>
    public static double ZoomFromSlider(double sliderValue) =>
        Math.Exp(Math.Log(MinZoomLevel) + Math.Clamp(sliderValue, 0, 100) / 100.0 * (Math.Log(MaxZoomLevel) - Math.Log(MinZoomLevel)));

    /// <summary>Maps the current zoom level to a 0–100 slider value on a logarithmic scale.</summary>
    public static double SliderFromZoom(double zoomLevel)
    {
        zoomLevel = Math.Clamp(zoomLevel, MinZoomLevel, MaxZoomLevel);
        double t = (Math.Log(zoomLevel) - Math.Log(MinZoomLevel)) / (Math.Log(MaxZoomLevel) - Math.Log(MinZoomLevel));
        return t * 100.0;
    }

    /// <summary>Human-readable zoom level for the toolbar label (e.g. "3.00").</summary>
    public static string FormatZoomLevel(double zoomLevel) =>
        Math.Clamp(zoomLevel, MinZoomLevel, MaxZoomLevel).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Supplies recent PvP activity for jump-reachable systems (zKillboard). Only systems within
    /// the active jump range are highlighted red (hot) or yellow (recent).
    /// </summary>
    public Func<int, PvPActivityStats>? PvPActivityProvider { get; set; }

    /// <summary>True when the given solar system is currently infested by a Sansha Nation incursion.</summary>
    public Func<int, bool>? SanshaIncursionProvider { get; set; }

    /// <summary>Active Thera/Turnur wormhole signatures touching the given solar system.</summary>
    public Func<int, IReadOnlyList<WormholeConnection>>? WormholeConnectionsProvider { get; set; }

    /// <summary>User-placed wormhole marker on the given solar system, when present and not expired.</summary>
    public Func<int, ManualWormholeMarker?>? ManualWormholeProvider { get; set; }

    /// <summary>True when at least one user-placed wormhole marker is active (drives animation).</summary>
    public Func<bool>? HasAnyManualWormholesProvider { get; set; }

    /// <summary>When false, wormhole markers (EvE-Scout and manual) and hover hints are hidden.</summary>
    public bool ShowWormholes { get; set; } = true;

    /// <summary>Alliance name holding the system's IHUB, when known from ESI sovereignty data.</summary>
    public Func<int, string?>? IhubAllianceProvider { get; set; }

    /// <summary>Names of live-tracked characters currently in the given solar system.</summary>
    public Func<int, IReadOnlyList<string>>? CharactersInSystemProvider { get; set; }

    /// <summary>Last-known solar system of the main profile, when available.</summary>
    public Func<int?>? MainProfileSystemIdProvider { get; set; }

    /// <summary>Whether overlays follow jump range or all nullsec systems.</summary>
    public ZKillboardScope PvPScope { get; set; } = ZKillboardScope.JumpRange;

    /// <summary>Resolves a region id to its display name for hover tooltips and layout labels.</summary>
    public Func<int, string?>? RegionNameProvider { get; set; }

    /// <summary>
    /// When true, a floating tooltip with the hovered system's name and region is drawn near the
    /// pointer. Intended for the Jump Range mini-map; the main map keeps this off per spec.
    /// </summary>
    public bool ShowHoverTooltips { get; set; }

    /// <summary>
    /// Developer aid for hand-tuning the curated in-game region grid: overlays the Schematic map
    /// with the curated 0-100 coordinate grid, each region's current curated (x, y), and a live
    /// readout of the coordinate under the pointer so values can be read off and typed into
    /// <c>ingame-region-positions.json</c>.
    /// </summary>
    public bool ShowDebugGrid
    {
        get => _showDebugGrid;
        set
        {
            if (_showDebugGrid == value) return;
            _showDebugGrid = value;
            InvalidateVisual();
        }
    }
    private bool _showDebugGrid;

    /// <summary>
    /// When enabled (Schematic mode), left-dragging a region moves that whole region cluster so the
    /// curated in-game region grid can be tuned by hand; the debug grid is drawn automatically while
    /// this is on. Use <see cref="BuildRegionPositionsJson"/> to export the tuned coordinates.
    /// </summary>
    public bool RegionEditMode
    {
        get => _regionEditMode;
        set
        {
            if (_regionEditMode == value) return;
            _regionEditMode = value;
            InvalidateVisual();
        }
    }
    private bool _regionEditMode;

    private int? _draggingRegionId;

    /// <summary>Raised after a region is moved in edit mode, so the host can offer to re-export the JSON.</summary>
    public event Action? RegionPositionsChanged;

    /// <summary>Serializes the current (possibly hand-tuned) region grid to ingame-region-positions.json shape.</summary>
    public string? BuildRegionPositionsJson() => _schematicLayout?.BuildRegionPositionsJson();

    private bool DebugGridVisible => _showDebugGrid || _regionEditMode;

    /// <summary>
    /// Highlights the given system with a green gate-neighbor-style outline. Set by a linked map
    /// when the user hovers a system there (mini-map hover → main Schematic map highlight).
    /// </summary>
    public int? LinkedHoveredSystemId
    {
        get => _linkedHoveredSystemId;
        set
        {
            if (_linkedHoveredSystemId == value) return;
            _linkedHoveredSystemId = value;
            InvalidateVisual();
        }
    }

    /// <summary>Red search highlight for a system picked via the right-panel search box.</summary>
    public int? SearchedSystemId
    {
        get => _searchedSystemId;
        set
        {
            if (_searchedSystemId == value) return;
            _searchedSystemId = value;
            InvalidateVisual();
        }
    }

    public event Action<int>? RouteFromRequested;
    public event Action<int>? RouteToRequested;

    /// <summary>Raised when the user asks to add a system as an intermediate route waypoint.</summary>
    public event Action<int>? RouteWaypointRequested;

    /// <summary>Raised when the user asks to open a system's zKillboard page in the browser.</summary>
    public event Action<int>? ZKillboardOpenRequested;

    /// <summary>Raised when the user asks to add or edit a manual wormhole marker on a system.</summary>
    public event Action<int>? ManualWormholeAddRequested;

    /// <summary>Raised when the user asks to remove a manual wormhole marker from a system.</summary>
    public event Action<int>? ManualWormholeRemoveRequested;

    /// <summary>Raised whenever the click-driven system selection changes.</summary>
    public event Action<int?>? SelectedSystemChanged;

    /// <summary>Raised whenever the jump-range origin changes. Drives the Jump Range mini-map so it stays
    /// synced even when <see cref="PinJumpRangeOrigin"/> prevents left-clicks from moving it.
    /// </summary>
    public event Action<int?>? JumpRangeOriginChanged;

    /// <summary>Raised when the jump-reachable system set changes (origin, ship class, or skills).</summary>
    public event Action<IReadOnlyCollection<int>>? JumpReachabilityChanged;

    /// <summary>Raised when simulation mode has active origins but no further origin picks are possible.</summary>
    public event Action? JumpRangeSimulationExhausted;

    /// <summary>Raised when the pointer-hover target changes (including cleared on pointer leave).</summary>
    public event Action<int?>? HoveredSystemChanged;

    public bool HitTest(Point point) =>
        new Rect(0, 0, Bounds.Width, Bounds.Height).ContainsExclusive(point);

    public MapControl()
    {
        ClipToBounds = true;
        Focusable = true;

        _routeFromItem = new MenuItem { Header = "Маршрут отсюда" };
        _routeFromItem.Click += (_, _) => { if (_contextMenuSystemId is int id) RouteFromRequested?.Invoke(id); };
        _routeToItem = new MenuItem { Header = "Маршрут сюда" };
        _routeToItem.Click += (_, _) => { if (_contextMenuSystemId is int id) RouteToRequested?.Invoke(id); };
        _routeWaypointItem = new MenuItem { Header = "Add waypoint" };
        _routeWaypointItem.Click += (_, _) => { if (_contextMenuSystemId is int id) RouteWaypointRequested?.Invoke(id); };
        _zkillboardItem = new MenuItem { Header = "Открыть на zKillboard" };
        _zkillboardItem.Click += (_, _) => { if (_contextMenuSystemId is int id) ZKillboardOpenRequested?.Invoke(id); };

        _copySystemNameItem = new MenuItem { Header = "Скопировать название системы" };
        _copySystemNameItem.Click += async (_, _) =>
        {
            if (_contextMenuSystemId is not int id || _map?.Get(id) is not { } system) return;
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            try
            {
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText(system.Name));
                await clipboard.SetDataAsync(data);
            }
            catch
            {
                // Clipboard may be unavailable (e.g. headless).
            }
        };

        _addManualWormholeItem = new MenuItem { Header = "Добавить Wormhole..." };
        _addManualWormholeItem.Click += (_, _) =>
        {
            if (_contextMenuSystemId is int id) ManualWormholeAddRequested?.Invoke(id);
        };
        _removeManualWormholeItem = new MenuItem { Header = "Удалить Wormhole" };
        _removeManualWormholeItem.Click += (_, _) =>
        {
            if (_contextMenuSystemId is int id) ManualWormholeRemoveRequested?.Invoke(id);
        };

        _contextMenu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                _routeFromItem, _routeToItem, _routeWaypointItem,
                new Separator(),
                _copySystemNameItem, _zkillboardItem,
                new Separator(),
                _addManualWormholeItem, _removeManualWormholeItem
            }
        };
    }

    public void SetMap(UniverseMap map)
    {
        _map = map;
        RebuildLayout();
        if (IsJumpRangeMiniMap)
        {
            if (_jumpRangeOriginSystemId is int originId)
                FocusJumpRange(originId);
            else
                FitToView();
        }
        else
        {
            FitToView();
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Programmatically marks the live-tracked pilot's system and, unless jump-range origin is
    /// pinned, also drives the jump-range highlight from that system.
    /// </summary>
    public void SelectSystemExternally(int? systemId)
    {
        if (_pilotSystemId == systemId && (_pinJumpRangeOrigin || _jumpRangeOriginSystemId == systemId))
        {
            InvalidateVisual();
            return;
        }

        _pilotSystemId = systemId;
        SyncJumpOriginAnimation();
        if (_pinJumpRangeOrigin)
        {
            InvalidateVisual();
        }
        else
        {
            SelectSystem(systemId is int id ? _map?.Get(id) : null);
        }
    }

    /// <summary>Updates live-tracked cyno pilots' systems and redraws their light-blue beacons.</summary>
    public void SetCynoLocations(IEnumerable<int> systemIds)
    {
        _cynoSystemIds = systemIds.ToHashSet();
        InvalidateVisual();
    }

    /// <summary>Updates live-tracked SC pilots' systems and redraws their deep-blue beacons.</summary>
    public void SetScLocations(IEnumerable<int> systemIds)
    {
        _scSystemIds = systemIds.ToHashSet();
        InvalidateVisual();
    }

    /// <summary>
    /// Sets the system the jump-range overlay is anchored to, recomputes reachability, and
    /// notifies listeners (e.g. the Jump Range mini-map).
    /// </summary>
    public void SetJumpRangeOrigin(int? systemId)
    {
        if (_jumpRangeOriginSystemId == systemId) return;

        _jumpRangeOriginSystemId = systemId;
        UpdateReachability();
        SyncJumpOriginAnimation();
        InvalidateVisual();
        JumpRangeOriginChanged?.Invoke(_jumpRangeOriginSystemId);
    }

    /// <summary>
    /// Selects a route-planning system and makes it the jump-range origin without invoking
    /// click-only behaviors such as adding a jump-range simulation layer.
    /// </summary>
    public void SelectRoutePlanningSystem(int systemId)
    {
        if (_map?.Get(systemId) is null) return;

        _selectedSystemId = systemId;
        SetJumpRangeOrigin(systemId);
        InvalidateVisual();
        SelectedSystemChanged?.Invoke(_selectedSystemId);
    }

    private void RebuildLayout()
    {
        if (_map is null) return;

        BuildStandardRegionCentroids();

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

    private void BuildStandardRegionCentroids()
    {
        _standardRegionCentroids.Clear();
        if (_map is null) return;

        foreach (var group in _map.Systems.Values.GroupBy(s => s.RegionId))
        {
            var positions = group.Select(WorldProjection.RealPosition).ToList();
            _standardRegionCentroids[group.Key] = new Point(
                positions.Average(p => p.X),
                positions.Average(p => p.Y));
        }
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
        SetZoomLevel(Math.Clamp(scale / _baseScale, MinZoom, MaxZoom), zoomAnchorScreen: null);
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

    private void UpdateBaseScale()
    {
        if (_map is null || _map.Systems.Count == 0) return;

        double width = Math.Max(_maxX - _minX, 1.0);
        double height = Math.Max(_maxZ - _minZ, 1.0);
        double w = Bounds.Width > 0 ? Bounds.Width : 900;
        double h = Bounds.Height > 0 ? Bounds.Height : 600;
        _baseScale = Math.Min(w / width, h / height) * 0.78;
    }

    private void FitToView()
    {
        _center = new Point((_minX + _maxX) / 2, (_minZ + _maxZ) / 2);
        UpdateBaseScale();
        double zoom = _displayMode == MapDisplayMode.Schematic ? DefaultSchematicZoom : DefaultStandardZoom;
        SetZoomLevel(zoom, zoomAnchorScreen: null);
        if (IsJumpRangeMiniMap)
            _jumpRangeFocusZoom = _zoom;
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
        double factor = e.Delta.Y > 0 ? 1.25 : 0.8;
        SetZoomLevel(_zoom * factor, pos);
        e.Handled = true;
    }

    private void SetZoomLevel(double zoom, Point? zoomAnchorScreen)
    {
        double clamped = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(_zoom - clamped) < 1e-9) return;

        Point anchor = zoomAnchorScreen ?? new Point(
            Bounds.Width > 0 ? Bounds.Width / 2 : 450,
            Bounds.Height > 0 ? Bounds.Height / 2 : 300);
        var worldBefore = ScreenToWorld(anchor);
        _zoom = clamped;
        var worldAfter = ScreenToWorld(anchor);
        _center += worldBefore - worldAfter;

        UpdateIncursionAnimationTimer();
        UpdateWormholeAnimationTimer();
        InvalidateVisual();
        ZoomLevelChanged?.Invoke(this, _zoom);
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
            // In region-edit mode, grabbing a region starts a drag-move instead of a pan/selection.
            if (_regionEditMode && _displayMode == MapDisplayMode.Schematic
                && FindRegionAt(point.Position) is int regionId)
            {
                _draggingRegionId = regionId;
                e.Pointer.Capture(this);
                return;
            }

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
                _routeWaypointItem.Header = $"Add waypoint: {hit.Name}";
                _zkillboardItem.Header = $"zKillboard: {hit.Name}";
                bool hasManualWormhole = ManualWormholeProvider?.Invoke(hit.Id) is not null;
                _addManualWormholeItem.Header = hasManualWormhole
                    ? $"Изменить Wormhole: {hit.Name}"
                    : $"Добавить Wormhole: {hit.Name}";
                _addManualWormholeItem.IsVisible = !IsJumpRangeMiniMap;
                _removeManualWormholeItem.Header = $"Удалить Wormhole: {hit.Name}";
                _removeManualWormholeItem.IsVisible = !IsJumpRangeMiniMap && hasManualWormhole;
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

        if (_draggingRegionId is int regionId && _schematicLayout is not null)
        {
            if (_lastPointerPos is { } last)
            {
                var deltaWorld = new Point((pos.X - last.X) / Scale, (pos.Y - last.Y) / Scale);
                _schematicLayout.MoveRegionBy(regionId, deltaWorld);
                RegionPositionsChanged?.Invoke();
                InvalidateVisual();
            }
            _lastPointerPos = pos;
            return;
        }

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
                HoveredSystemChanged?.Invoke(hovered?.Id);
                InvalidateVisual();
            }
            else if ((ShowHoverTooltips || MainMapHoverHintActive || MainMapSovereigntyHintActive || ResolveMainProfileSystemId() is not null || HasMonitoredPvPActivity(_hoveredSystem?.Id)) && _hoveredSystem is not null)
            {
                InvalidateVisual();
            }
            else if (DebugGridVisible)
            {
                InvalidateVisual();
            }
        }

        _lastPointerPos = pos;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredSystem is not null)
        {
            _hoveredSystem = null;
            HoveredSystemChanged?.Invoke(null);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_draggingRegionId is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            _draggingRegionId = null;
            e.Pointer.Capture(null);
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Left && _leftButtonDown)
        {
            if (!_isPanning)
            {
                var pos = e.GetPosition(this);
                var hit = HitTestSystem(pos);
                SelectSystem(hit, pos);
            }
            _leftButtonDown = false;
            _isPanning = false;
        }

        e.Pointer.Capture(null);
    }

    private void SelectSystem(SolarSystem? system, Point? clickScreenPos = null)
    {
        _selectedSystemId = system?.Id;
        if (_jumpRangeSimulationActive && system is not null)
        {
            var toastPos = clickScreenPos ?? WorldToScreen(Project(system));
            if (!TryAddSimulationLayer(system.Id, toastPos))
            {
                SelectedSystemChanged?.Invoke(_selectedSystemId);
                return;
            }
        }
        else if (!_pinJumpRangeOrigin)
        {
            SetJumpRangeOrigin(system?.Id);
        }
        else
        {
            InvalidateVisual();
        }

        SelectedSystemChanged?.Invoke(_selectedSystemId);
    }

    /// <summary>
    /// Selects the given system (using whichever <see cref="JumpRangeShipClass"/> and
    /// <see cref="RouteContextProvider"/> are already configured on this instance) and pans/zooms
    /// so its full jump-range circle fits comfortably in the current viewport. Intended for a
    /// small Standard-mode "regular map" instance dedicated to jump-range planning, since Standard
    /// mode is the only mode whose range circle is drawn to true LY scale (Schematic mode clamps
    /// and compresses it, since its layout isn't distance-accurate).
    /// </summary>
    public void FocusJumpRange(int? systemId)
    {
        _selectedSystemId = systemId;
        _jumpRangeOriginSystemId = systemId;
        UpdateReachability();

        if (systemId is int id && _map?.Get(id) is { } system)
        {
            UpdateBaseScale();
            _center = Project(system);

            double w = Bounds.Width > 0 ? Bounds.Width : 260;
            double h = Bounds.Height > 0 ? Bounds.Height : 260;
            double radiusLy = _selectedRangeLy > 0 ? _selectedRangeLy : 4.0;
            double desiredScale = Math.Min(w, h) * 0.42 / radiusLy;
            SetZoomLevel(Math.Clamp(desiredScale / _baseScale, MinZoom, MaxZoom), zoomAnchorScreen: null);
            _jumpRangeFocusZoom = _zoom;
        }

        SyncJumpOriginAnimation();
        InvalidateVisual();
        JumpReachabilityChanged?.Invoke(BuildMonitoredJumpRangeSystems());
    }

    /// <summary>
    /// Pulsing green outline on the jump-range origin when the live-tracked pilot is elsewhere
    /// (or not tracked). Hidden when the pilot beacon already marks that system.
    /// </summary>
    private void SyncJumpOriginAnimation()
    {
        bool needsAnim = _jumpRangeOriginSystemId is int originId && _pilotSystemId != originId;
        if (!needsAnim)
        {
            _jumpOriginAnimTimer?.Stop();
            _jumpOriginAnimTimer = null;
            return;
        }

        if (_jumpOriginAnimTimer is not null) return;

        _jumpOriginAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _jumpOriginAnimTimer.Tick += (_, _) =>
        {
            _jumpOriginPulsePhase += 0.09;
            InvalidateVisual();
        };
        _jumpOriginAnimTimer.Start();
    }

    private void SyncGateRouteAnimation()
    {
        bool needsAnim = RouteSteps?.Any(step => step.Kind is RouteStepKind.Gate or RouteStepKind.Wormhole) == true;
        if (!needsAnim)
        {
            _gateRouteAnimTimer?.Stop();
            _gateRouteAnimTimer = null;
            return;
        }

        if (_gateRouteAnimTimer is not null) return;

        _gateRouteAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _gateRouteAnimTimer.Tick += (_, _) =>
        {
            _gateRouteAnimPhase += 0.12;
            InvalidateVisual();
        };
        _gateRouteAnimTimer.Start();
    }

    /// <summary>
    /// Soft salad-green glow on Sansha-incursion systems when zoomed in past the overview threshold.
    /// </summary>
    public void SyncIncursionAnimation(bool active = true)
    {
        _incursionsActive = active;
        UpdateIncursionAnimationTimer();
    }

    /// <summary>
    /// Slow ripple markers for active Thera/Turnur wormholes from EvE-Scout and user-placed markers.
    /// </summary>
    public void SyncWormholeAnimation(bool active = true)
    {
        _wormholesActive = active;
        UpdateWormholeAnimationTimer();
    }

    /// <summary>
    /// Keeps manual wormhole marker animation running while any user markers are active.
    /// </summary>
    public void SyncManualWormholeAnimation(bool active = true)
    {
        _manualWormholesActive = active;
        UpdateWormholeAnimationTimer();
    }

    private void UpdateWormholeAnimationTimer()
    {
        bool needsEveScoutAnim = ShowWormholes
            && _wormholesActive
            && WormholeConnectionsProvider is not null;
        bool needsManualAnim = ShowWormholes
            && _manualWormholesActive
            && ManualWormholeProvider is not null
            && HasAnyManualWormholesProvider?.Invoke() == true;
        bool needsAnim = needsEveScoutAnim || needsManualAnim;
        if (!needsAnim)
        {
            _wormholeAnimTimer?.Stop();
            _wormholeAnimTimer = null;
            return;
        }

        if (_wormholeAnimTimer is not null) return;

        _wormholeAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _wormholeAnimTimer.Tick += (_, _) =>
        {
            _wormholeAnimPhase += 0.05;
            InvalidateVisual();
        };
        _wormholeAnimTimer.Start();
    }

    private void UpdateIncursionAnimationTimer()
    {
        bool needsAnim = _incursionsActive
            && SanshaIncursionProvider is not null
            && _zoom > SanshaIncursionAnimMinZoom;
        if (!needsAnim)
        {
            _incursionAnimTimer?.Stop();
            _incursionAnimTimer = null;
            return;
        }

        if (_incursionAnimTimer is not null) return;

        _incursionAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _incursionAnimTimer.Tick += (_, _) =>
        {
            _incursionAnimPhase += 0.06;
            InvalidateVisual();
        };
        _incursionAnimTimer.Start();
    }

    private void DrawAnimatedGateRouteLine(DrawingContext context, Point p1, Point p2, bool schematic)
    {
        double pulse = 0.7 + 0.3 * Math.Sin(_gateRouteAnimPhase * 1.4);
        double thickness = (schematic ? 3.6 : 3.0) * pulse;

        context.DrawLine(new Pen(GateRouteGlowBrush, thickness + 5), p1, p2);

        var dashPen = new Pen(GateRouteBrush, thickness)
        {
            LineCap = PenLineCap.Round,
            DashStyle = new DashStyle(new double[] { 10, 7 }, _gateRouteAnimPhase * 17),
        };
        context.DrawLine(dashPen, p1, p2);
    }

    private void DrawAnimatedWormholeRouteLine(DrawingContext context, Point p1, Point p2, bool schematic)
    {
        double pulse = 0.7 + 0.3 * Math.Sin(_gateRouteAnimPhase * 1.4);
        double thickness = (schematic ? 3.6 : 3.0) * pulse;

        context.DrawLine(new Pen(WormholeRouteGlowBrush, thickness + 5), p1, p2);

        var dashPen = new Pen(WormholeRouteBrush, thickness)
        {
            LineCap = PenLineCap.Round,
            DashStyle = new DashStyle(new double[] { 6, 5 }, _gateRouteAnimPhase * 13),
        };
        context.DrawLine(dashPen, p1, p2);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _jumpOriginAnimTimer?.Stop();
        _jumpOriginAnimTimer = null;
        _gateRouteAnimTimer?.Stop();
        _gateRouteAnimTimer = null;
        _incursionAnimTimer?.Stop();
        _incursionAnimTimer = null;
        _wormholeAnimTimer?.Stop();
        _wormholeAnimTimer = null;
        ClearSimulationToast();
        base.OnDetachedFromVisualTree(e);
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

        if (_jumpRangeOriginSystemId is not int originId || _map?.Get(originId) is not { } system)
        {
            NotifyReachabilityIfChanged();
            return;
        }

        _gateNeighbors = _map.GateNeighbors(system.Id).ToHashSet();

        var (rangeLy, reachable) = ComputeJumpReachability(system);
        if (rangeLy > 0)
        {
            _selectedRangeLy = rangeLy;
            _reachableByJump = reachable;
        }

        NotifyReachabilityIfChanged();
    }

    private (double RangeLy, HashSet<int> Reachable) ComputeJumpReachability(SolarSystem origin)
    {
        if (_map is null)
            return (0, new HashSet<int>());

        var (hull, skills, method) = RouteContextProvider?.Invoke() ?? (null, PilotSkills.MaxSkills(), JumpMethod.Cyno);

        double rangeLy = _jumpRangeShipClass is CapitalShipClass overrideClass
            ? JumpSimulator.MaxRangeLy(overrideClass, skills)
            : hull is not null
                ? JumpSimulator.MaxRangeLy(hull, skills)
                : JumpSimulator.MaxRangeLy(CapitalShipClass.BlackOps, skills);

        if (rangeLy <= 0)
            return (0, new HashSet<int>());

        var reachable = _map.SystemsWithinRange(origin, rangeLy)
            .Where(t => JumpRules.IsValidJumpLanding(t.System, method))
            .Select(t => t.System.Id)
            .ToHashSet();
        return (rangeLy, reachable);
    }

    private bool TryAddSimulationLayer(int originSystemId, Point toastScreenPos)
    {
        if (_simulationLayers.Any(layer => layer.OriginSystemId == originSystemId))
        {
            InvalidateVisual();
            return true;
        }

        if (_map?.Get(originSystemId) is not { } system)
            return false;

        var (rangeLy, reachable) = ComputeJumpReachability(system);
        if (rangeLy <= 0)
            return false;

        var candidate = new JumpRangeSimulationLayer
        {
            OriginSystemId = originSystemId,
            RangeLy = rangeLy,
            ReachableSystemIds = reachable,
        };

        if (_simulationLayers.Count >= 1 &&
            ComputeSimulationIntersection(_simulationLayers.Append(candidate)).Count == 0)
        {
            ShowSimulationToast(toastScreenPos, "Пересечений нет");
            return false;
        }

        _simulationLayers.Add(candidate);
        RecomputeSimulationOriginCandidates();
        InvalidateVisual();
        return true;
    }

    private void SeedSimulationFromCurrentJumpRangeOrigin()
    {
        if (_jumpRangeOriginSystemId is not int originId ||
            _map?.Get(originId) is not { } system ||
            _simulationLayers.Any(layer => layer.OriginSystemId == originId))
        {
            return;
        }

        var (rangeLy, reachable) = ComputeJumpReachability(system);
        if (rangeLy <= 0)
            return;

        _simulationLayers.Add(new JumpRangeSimulationLayer
        {
            OriginSystemId = originId,
            RangeLy = rangeLy,
            ReachableSystemIds = reachable,
        });
        RecomputeSimulationOriginCandidates();
    }

    private static HashSet<int> GetSimulationLayerCoverage(JumpRangeSimulationLayer layer)
    {
        var coverage = new HashSet<int>(layer.ReachableSystemIds);
        coverage.Add(layer.OriginSystemId);
        return coverage;
    }

    private static HashSet<int> ComputeSimulationIntersection(IEnumerable<JumpRangeSimulationLayer> layers)
    {
        var layerList = layers.ToList();
        if (layerList.Count == 0)
            return new HashSet<int>();

        var intersection = GetSimulationLayerCoverage(layerList[0]);
        for (int i = 1; i < layerList.Count; i++)
            intersection.IntersectWith(GetSimulationLayerCoverage(layerList[i]));
        return intersection;
    }

    /// <summary>
    /// Systems that are not yet simulation origins but whose jump range overlaps the current
    /// intersection — valid picks for the next simulation click.
    /// </summary>
    private void RecomputeSimulationOriginCandidates()
    {
        _simulationOriginCandidates.Clear();
        if (!_jumpRangeSimulationActive || _simulationLayers.Count < 1 || _map is null)
        {
            _simulationCompletionNotified = false;
            return;
        }

        var currentIntersection = ComputeSimulationIntersection(_simulationLayers);
        if (currentIntersection.Count > 0)
        {
            var originIds = _simulationLayers.Select(layer => layer.OriginSystemId).ToHashSet();
            foreach (var system in _map.Systems.Values)
            {
                if (originIds.Contains(system.Id))
                    continue;

                var (_, reachable) = ComputeJumpReachability(system);
                reachable.Add(system.Id);
                if (reachable.Overlaps(currentIntersection))
                    _simulationOriginCandidates.Add(system.Id);
            }
        }

        NotifySimulationCompletedIfExhausted();
    }

    private void NotifySimulationCompletedIfExhausted()
    {
        bool exhausted = _jumpRangeSimulationActive
            && _simulationLayers.Count >= 1
            && _simulationOriginCandidates.Count == 0;

        if (exhausted && !_simulationCompletionNotified)
        {
            _simulationCompletionNotified = true;
            JumpRangeSimulationExhausted?.Invoke();
        }
        else if (!exhausted)
        {
            _simulationCompletionNotified = false;
        }
    }

    private void ShowSimulationToast(Point screenPos, string text)
    {
        _simulationToast = new SimulationToast
        {
            Text = text,
            ScreenPos = screenPos,
            StartedAt = DateTime.UtcNow,
        };

        if (_simulationToastTimer is null)
        {
            _simulationToastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _simulationToastTimer.Tick += (_, _) =>
            {
                if (_simulationToast is null ||
                    (DateTime.UtcNow - _simulationToast.StartedAt).TotalMilliseconds > 2000)
                {
                    ClearSimulationToast();
                }

                InvalidateVisual();
            };
            _simulationToastTimer.Start();
        }

        InvalidateVisual();
    }

    private void ClearSimulationToast()
    {
        _simulationToast = null;
        _simulationToastTimer?.Stop();
        _simulationToastTimer = null;
    }

    private void UpdateSimulationReachability()
    {
        if (_simulationLayers.Count == 0 || _map is null)
            return;

        for (int i = 0; i < _simulationLayers.Count; i++)
        {
            var layer = _simulationLayers[i];
            if (_map.Get(layer.OriginSystemId) is not { } system)
                continue;

            var (rangeLy, reachable) = ComputeJumpReachability(system);
            _simulationLayers[i] = new JumpRangeSimulationLayer
            {
                OriginSystemId = layer.OriginSystemId,
                RangeLy = rangeLy,
                ReachableSystemIds = reachable,
            };
        }

        RecomputeSimulationOriginCandidates();
    }

    private bool IsSimulationCandidateOriginSystem(int systemId) =>
        _jumpRangeSimulationActive &&
        _simulationLayers.Count >= 1 &&
        _simulationOriginCandidates.Contains(systemId);

    private bool IsSimulationHighlightedSystem(int systemId) =>
        IsSimulationOriginSystem(systemId) ||
        IsSimulationIntersectionSystem(systemId) ||
        IsSimulationCandidateOriginSystem(systemId);

    private bool IsSimulationIntersectionSystem(int systemId) =>
        _jumpRangeSimulationActive &&
        _simulationLayers.Count >= 2 &&
        ComputeSimulationIntersection(_simulationLayers).Contains(systemId);

    private bool IsSimulationOriginSystem(int systemId) =>
        _jumpRangeSimulationActive &&
        _simulationLayers.Any(layer => layer.OriginSystemId == systemId);

    private Pen? CreateSimulationOutlinePen(int systemId)
    {
        if (!IsSimulationHighlightedSystem(systemId))
            return null;

        // Origins are the sources the intersections are measured from, so they take visual
        // priority over the reachable/intersection styling with a solid orange outline.
        if (IsSimulationOriginSystem(systemId))
            return new Pen(JumpRangeSimulationOriginBrush, JumpRangeRingWidth);

        if (IsSimulationIntersectionSystem(systemId))
            return new Pen(JumpRangeIntersectionBrush, JumpRangeRingWidth);

        return new Pen(Brushes.Black, JumpRangeRingWidth, dashStyle: JumpRangeSimulationDashStyle);
    }

    private void NotifyReachabilityIfChanged()
    {
        var monitored = BuildMonitoredJumpRangeSystems();
        if (monitored.SetEquals(_lastNotifiedReachable)) return;
        _lastNotifiedReachable = monitored;
        JumpReachabilityChanged?.Invoke(monitored);
    }

    /// <summary>Jump-range systems monitored for zKillboard overlays: reachable neighbors plus the origin.</summary>
    private HashSet<int> BuildMonitoredJumpRangeSystems()
    {
        var monitored = new HashSet<int>(_reachableByJump);
        if (_jumpRangeOriginSystemId is int originId)
            monitored.Add(originId);
        return monitored;
    }

    /// <summary>Systems currently monitored for jump-range zKillboard overlays.</summary>
    public IReadOnlyCollection<int> MonitoredJumpRangeSystemIds => BuildMonitoredJumpRangeSystems();

    /// <summary>Re-emits reachability even when the set is unchanged (e.g. after SDE reload).</summary>
    public void ForceNotifyJumpReachabilityChanged()
    {
        _lastNotifiedReachable.Clear();
        NotifyReachabilityIfChanged();
    }

    /// <summary>Recomputes main and simulation jump-range highlights after skills or hull context changes.</summary>
    public void RefreshJumpRangeHighlights()
    {
        UpdateReachability();
        UpdateSimulationReachability();
        InvalidateVisual();
    }

    private SolarSystem? HitTestSystem(Point screenPos)
    {
        if (_map is null) return null;

        // Schematic mode draws rectangular plates, not dots -- hit-test against the actual
        // rendered rectangle from the last frame so clicks anywhere on a plate register,
        // including the plate's edges (which the old fixed-radius circle would miss).
        if (_displayMode == MapDisplayMode.Schematic && _lastPlateRects.Count > 0)
        {
            // Dot-tier markers are tiny, so pad the hit area to make wide-zoom hover hints usable.
            double pad = _currentPlateTier == SchematicPlateDetailTier.Dot ? 4.0 : 0.0;
            foreach (var (systemId, rect) in _lastPlateRects)
            {
                var hitRect = pad > 0 ? rect.Inflate(pad) : rect;
                if (hitRect.Contains(screenPos) && _map.Get(systemId) is { } plateSystem) return plateSystem;
            }
            return null;
        }

        SolarSystem? best = null;
        double hitRadius = ShowHoverTooltips ? 14.0 : HitRadiusPx;
        double bestDistSq = hitRadius * hitRadius;

        var viewport = new Rect(-hitRadius, -hitRadius, Bounds.Width + hitRadius * 2, Bounds.Height + hitRadius * 2);
        foreach (var system in _map.Systems.Values)
        {
            var screen = WorldToScreen(Project(system));
            if (!viewport.Contains(screen)) continue;
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

        // At wide zoom region labels sit on top of systems; when zoomed in they fall behind.
        bool regionLabelsOnTop = schematic && _zoom <= RegionLabelOverlayMaxZoom;
        bool miniMapRegionLabelsOnTop = IsJumpRangeMiniMap && (!HasJumpRangeOverlay || _zoom <= _jumpRangeFocusZoom);
        _wideZoomHighlightScale = ComputeWideZoomHighlightScale(schematic, regionLabelsOnTop, miniMapRegionLabelsOnTop);
        if (schematic && !regionLabelsOnTop)
        {
            DrawSchematicRegions(context, viewport);
        }
        else if (!schematic && IsJumpRangeMiniMap && !miniMapRegionLabelsOnTop)
        {
            DrawStandardRegionLabels(context, viewport);
        }

        // Cross-region gate lines are drawn before plates and labels so they stay in the background.
        if (visible.Count > 0 && visible.Count <= GateLineLodThreshold)
        {
            var visibleIds = visible.Select(v => v.System.Id).ToHashSet();
            var gatePen = new Pen(schematic ? SchematicGateLineBrush : GateLineBrush, schematic ? 2.4 : 2.0);
            var crossRegionGatePen = new Pen(SchematicCrossRegionGateLineBrush, 1.0);
            var drawn = new HashSet<(int, int)>();
            var gateClip = new Rect(
                -GateLineClipMarginPx,
                -GateLineClipMarginPx,
                w + GateLineClipMarginPx * 2,
                h + GateLineClipMarginPx * 2);

            if (schematic)
            {
                foreach (var (system, screen) in visible)
                {
                    foreach (int neighborId in _map.GateNeighbors(system.Id))
                    {
                        var neighborSys = _map.Get(neighborId);
                        if (neighborSys is null || neighborSys.RegionId == system.RegionId) continue;

                        var key = system.Id < neighborId ? (system.Id, neighborId) : (neighborId, system.Id);
                        if (!drawn.Add(key)) continue;
                        if (!TryClipLineToRect(screen, WorldToScreen(Project(neighborSys)), gateClip, out var a, out var b))
                            continue;
                        context.DrawLine(crossRegionGatePen, a, b);
                    }
                }
            }

            drawn.Clear();
            bool miniMapView = !schematic && IsJumpRangeMiniMap && HasJumpRangeOverlay;
            var backgroundGatePen = new Pen(JumpRangeMiniMapBackgroundGateBrush, 1.0);
            var borderGatePen = new Pen(JumpRangeMiniMapBorderGateBrush, 1.15);

            if (miniMapView)
            {
                // Pass 1: muted off-range and border connections — always behind markers/labels.
                var drawnBackground = new HashSet<(int, int)>();
                foreach (var (system, screen) in visible)
                {
                    foreach (int neighborId in _map.GateNeighbors(system.Id))
                    {
                        var neighborSys = _map.Get(neighborId);
                        if (neighborSys is null || !visibleIds.Contains(neighborId)) continue;

                        var key = system.Id < neighborId ? (system.Id, neighborId) : (neighborId, system.Id);
                        if (!drawnBackground.Add(key)) continue;

                        bool aInRange = IsJumpRangeHighlightedSystem(system.Id);
                        bool bInRange = IsJumpRangeHighlightedSystem(neighborId);
                        if (aInRange && bInRange) continue;

                        var pen = aInRange || bInRange ? borderGatePen : backgroundGatePen;
                        context.DrawLine(pen, screen, WorldToScreen(Project(neighborSys)));
                    }
                }

                // Pass 2: in-range gate graph on top of the muted background web.
                drawn.Clear();
            }

            foreach (var (system, screen) in visible)
            {
                foreach (int neighborId in _map.GateNeighbors(system.Id))
                {
                    var neighborSys = _map.Get(neighborId);
                    if (neighborSys is null) continue;
                    if (schematic && neighborSys.RegionId != system.RegionId) continue;

                    var key = system.Id < neighborId ? (system.Id, neighborId) : (neighborId, system.Id);
                    if (!drawn.Add(key)) continue;

                    var neighborScreen = WorldToScreen(Project(neighborSys));
                    bool neighborVisible = visibleIds.Contains(neighborId);

                    if (miniMapView)
                    {
                        if (!neighborVisible) continue;
                        bool aInRange = IsJumpRangeHighlightedSystem(system.Id);
                        bool bInRange = IsJumpRangeHighlightedSystem(neighborId);
                        if (!aInRange || !bInRange) continue;

                        context.DrawLine(gatePen, screen, neighborScreen);
                    }
                    else if (schematic)
                    {
                        // Intra-region Schematic gates still require both endpoints on-screen.
                        if (!neighborVisible) continue;
                        context.DrawLine(gatePen, screen, neighborScreen);
                    }
                    else
                    {
                        // Standard mode: keep stubs to off-screen neighbors so zoomed-in views
                        // still show the local gate graph, and clip far endpoints for Skia.
                        if (!TryClipLineToRect(screen, neighborScreen, gateClip, out var a, out var b))
                            continue;
                        context.DrawLine(gatePen, a, b);
                    }
                }
            }
        }

        // Jump-range highlight for the anchored origin system (hidden on the main map during simulation).
        if (!UseSimulationJumpRangeStyling &&
            _jumpRangeOriginSystemId is int originId && _map.Get(originId) is { } originSystem)
        {
            var originScreen = WorldToScreen(Project(originSystem));
            if (_selectedRangeLy > 0)
            {
                double radiusPx = schematic
                    ? Math.Clamp(_selectedRangeLy * Scale * 0.35, 18, 120) * _wideZoomHighlightScale
                    : ClampStandardJumpRangeRadiusPx(_selectedRangeLy * Scale, w, h);
                context.DrawEllipse(JumpRangeFill, new Pen(JumpRangeStroke, 1.5, dashStyle: new DashStyle(new double[] { 5, 4 }, 0)), originScreen, radiusPx, radiusPx);
            }
        }

        if (!IsJumpRangeMiniMap)
        {
            foreach (var layer in _simulationLayers)
            {
                if (_map.Get(layer.OriginSystemId) is not { } simOrigin || layer.RangeLy <= 0)
                    continue;

                var simScreen = WorldToScreen(Project(simOrigin));
                double simRadiusPx = schematic
                    ? Math.Clamp(layer.RangeLy * Scale * 0.35, 18, 120) * _wideZoomHighlightScale
                    : ClampStandardJumpRangeRadiusPx(layer.RangeLy * Scale, w, h);
                context.DrawEllipse(JumpRangeFill, new Pen(JumpRangeStroke, 1.5, dashStyle: new DashStyle(new double[] { 5, 4 }, 0)), simScreen, simRadiusPx, simRadiusPx);
            }
        }

        // Schematic mode uses Dotlan plates only — no underlying dots.
        if (!schematic)
            DrawStandardSystemMarkers(context, visible);

        if (LocationBeaconsBehindPlates(schematic))
            DrawAllLocationBeacons(context, schematic);

        DrawSystemLabels(context, visible, schematic);

        DrawStructureIcons(context, visible);

        if (RouteSteps is { Count: > 0 })
        {
            var jumpPen = new Pen(JumpRouteBrush, schematic ? 3.0 : 2.5);
            foreach (var step in RouteSteps)
            {
                var fromSys = _map.Get(step.FromSystemId);
                var toSys = _map.Get(step.ToSystemId);
                if (fromSys is null || toSys is null) continue;
                var p1 = WorldToScreen(Project(fromSys));
                var p2 = WorldToScreen(Project(toSys));
                if (step.Kind == RouteStepKind.Gate)
                    DrawAnimatedGateRouteLine(context, p1, p2, schematic);
                else if (step.Kind == RouteStepKind.Wormhole)
                    DrawAnimatedWormholeRouteLine(context, p1, p2, schematic);
                else
                    DrawJumpArc(context, jumpPen, p1, p2);
            }
        }

        DrawMarker(context, FromSystemId, Brushes.LimeGreen, "ОТ", schematic);
        if (WaypointSystemIds is { Count: > 0 } waypoints)
        {
            for (int i = 0; i < waypoints.Count; i++)
                DrawMarker(context, waypoints[i], Brushes.Gold, $"П{i + 1}", schematic);
        }
        DrawMarker(context, ToSystemId, Brushes.OrangeRed, "ДО", schematic);

        DrawJumpOriginPulse(context, schematic);
        DrawLinkedHoverHighlight(context, schematic);

        if (!LocationBeaconsBehindPlates(schematic))
            DrawAllLocationBeacons(context, schematic);

        DrawPvPActivityHighlights(context, schematic, visible);
        DrawSanshaIncursionHighlights(context, schematic, visible);
        DrawEveScoutWormholeHighlights(context, schematic, visible);
        DrawManualWormholeHighlights(context, schematic, visible);
        DrawSearchedSystemHighlight(context, schematic);

        // Wide-zoom region labels paint last (before the hover tooltip) so they overlap markers,
        // beacons, PvP/search highlights and every other overlay on the universe overview.
        if (schematic && regionLabelsOnTop)
            DrawSchematicRegions(context, viewport);
        else if (!schematic && miniMapRegionLabelsOnTop)
            DrawStandardRegionLabels(context, viewport);

        DrawHoverTooltip(context);
        DrawSovereigntyHoverHint(context);
        DrawGateJumpHoverHint(context);
        DrawSimulationToast(context);

        if (DebugGridVisible && schematic)
            DrawDebugGrid(context, viewport);
    }

    /// <summary>
    /// Finds the region to grab for a drag at the given screen point: the smallest region whose
    /// (padded) cluster bounding box contains the point, or the nearest region label within a small
    /// screen radius as a fallback. Returns null when nothing is close enough (so the drag pans).
    /// </summary>
    private int? FindRegionAt(Point screen)
    {
        if (_schematicLayout is null || _map is null) return null;
        var world = ScreenToWorld(screen);
        double padWorld = 30.0 / Scale;

        int? best = null;
        double bestArea = double.MaxValue;
        foreach (var (regionId, ids) in _schematicLayout.RegionSystemIds)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (int id in ids)
            {
                if (_map.Get(id) is not { } sys) continue;
                var p = Project(sys);
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            if (minX > maxX) continue;
            minX -= padWorld; minY -= padWorld; maxX += padWorld; maxY += padWorld;
            if (world.X < minX || world.X > maxX || world.Y < minY || world.Y > maxY) continue;
            double area = (maxX - minX) * (maxY - minY);
            if (area < bestArea) { bestArea = area; best = regionId; }
        }
        if (best is not null) return best;

        double bestDistSq = 60 * 60;
        foreach (var (regionId, centroid) in _schematicLayout.RegionCentroids)
        {
            var s = WorldToScreen(centroid);
            double dx = s.X - screen.X, dy = s.Y - screen.Y;
            double d = dx * dx + dy * dy;
            if (d < bestDistSq) { bestDistSq = d; best = regionId; }
        }
        return best;
    }

    /// <summary>
    /// Developer overlay for hand-tuning <c>ingame-region-positions.json</c>: draws the curated
    /// 0-100 coordinate grid mapped through the current layout transform, annotates each region
    /// with its current curated (x, y), and shows a live readout of the curated coordinate under
    /// the pointer so a target position can be read off and typed into the JSON.
    /// </summary>
    private void DrawDebugGrid(DrawingContext context, Rect viewport)
    {
        if (_schematicLayout is null || !_schematicLayout.HasCuratedGrid) return;

        var anchors = _schematicLayout.RegionRawAnchors;

        // Grid extent: the documented 0-100 JSON range, widened to cover any fallback-placed region.
        const int step = 5;
        double minX = 0, minY = 0, maxX = 100, maxY = 100;
        foreach (var p in anchors.Values)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        minX = Math.Floor(minX / step) * step; minY = Math.Floor(minY / step) * step;
        maxX = Math.Ceiling(maxX / step) * step; maxY = Math.Ceiling(maxY / step) * step;

        var linePen = new Pen(DebugGridLineBrush, 1);
        var majorPen = new Pen(DebugGridLabelBrush, 1);
        var typeface = new Typeface(Typeface.Default.FontFamily);

        for (double gx = minX; gx <= maxX; gx += step)
        {
            var a = WorldToScreen(_schematicLayout.CuratedToWorld(new Point(gx, minY)));
            var b = WorldToScreen(_schematicLayout.CuratedToWorld(new Point(gx, maxY)));
            bool major = ((int)Math.Round(gx)) % 10 == 0;
            context.DrawLine(major ? majorPen : linePen, a, b);
            if (major)
            {
                var label = new FormattedText($"{gx:0}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 11, DebugGridLabelBrush);
                context.DrawText(label, new Point(a.X + 2, Math.Clamp(a.Y, 2, viewport.Height - 14)));
            }
        }
        for (double gy = minY; gy <= maxY; gy += step)
        {
            var a = WorldToScreen(_schematicLayout.CuratedToWorld(new Point(minX, gy)));
            var b = WorldToScreen(_schematicLayout.CuratedToWorld(new Point(maxX, gy)));
            bool major = ((int)Math.Round(gy)) % 10 == 0;
            context.DrawLine(major ? majorPen : linePen, a, b);
            if (major)
            {
                var label = new FormattedText($"{gy:0}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 11, DebugGridLabelBrush);
                context.DrawText(label, new Point(Math.Clamp(a.X, 2, viewport.Width - 20), a.Y + 2));
            }
        }

        // Per-region current curated coordinate, drawn just below each region's on-screen anchor.
        foreach (var (regionId, raw) in anchors)
        {
            if (!_schematicLayout.RegionCentroids.TryGetValue(regionId, out var centroid)) continue;
            var screen = WorldToScreen(centroid);
            if (!viewport.Contains(screen)) continue;
            var coord = new FormattedText($"{raw.X:0.0}, {raw.Y:0.0}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 11, DebugRegionCoordBrush);
            context.DrawText(coord, new Point(screen.X - coord.Width / 2, screen.Y + 8));
        }

        // Live readout of the curated coordinate under the pointer.
        if (_lastPointerPos is { } pointer)
        {
            var cur = _schematicLayout.WorldToCurated(ScreenToWorld(pointer));
            var text = new FormattedText($"grid  x={cur.X:0.0}  y={cur.Y:0.0}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.Bold),
                13, DebugReadoutText);
            var box = new Rect(pointer.X + 14, pointer.Y + 14, text.Width + 12, text.Height + 8);
            context.FillRectangle(DebugReadoutBackground, box);
            context.DrawText(text, new Point(box.X + 6, box.Y + 4));
        }
    }

    /// <summary>
    /// Brief on-map notice when a simulation pick would not intersect existing ranges.
    /// </summary>
    private void DrawSimulationToast(DrawingContext context)
    {
        if (IsJumpRangeMiniMap || _simulationToast is not { } toast)
            return;

        double elapsedMs = (DateTime.UtcNow - toast.StartedAt).TotalMilliseconds;
        if (elapsedMs > 2000)
            return;

        double opacity = elapsedMs switch
        {
            < 150 => elapsedMs / 150.0,
            < 1700 => 1.0,
            _ => Math.Max(0, 1.0 - (elapsedMs - 1700) / 300.0),
        };

        const double padX = 10, padY = 6, fontSize = 12;
        byte alpha = (byte)(opacity * 245);
        var textBrush = new SolidColorBrush(Color.FromArgb(alpha, 180, 0, 0));
        var boxFill = new SolidColorBrush(Color.FromArgb(alpha, 255, 248, 220));
        var boxBorder = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 200, 120, 0)), 1);

        var formatted = new FormattedText(toast.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, fontSize, textBrush);
        double boxW = formatted.Width + padX * 2;
        double boxH = formatted.Height + padY * 2;
        double floatUp = Math.Min(elapsedMs / 180.0, 14.0);
        double x = toast.ScreenPos.X - boxW / 2;
        double y = toast.ScreenPos.Y - boxH - 12 - floatUp;
        x = Math.Clamp(x, 4, Math.Max(4, Bounds.Width - boxW - 4));
        y = Math.Clamp(y, 4, Math.Max(4, Bounds.Height - boxH - 4));

        var box = new Rect(x, y, boxW, boxH);
        context.FillRectangle(boxFill, box);
        context.DrawRectangle(null, boxBorder, box, 4, 4);
        context.DrawText(formatted, new Point(x + padX, y + padY));
    }

    /// <summary>
    /// Shrinks fixed-pixel highlights when the map is zoomed out for the universe overview.
    /// At the default zoom level the factor is 1.0; it falls toward 0.12 at extreme zoom-out.
    /// </summary>
    private double ComputeWideZoomHighlightScale(bool schematic, bool regionLabelsOnTop, bool miniMapRegionLabelsOnTop)
    {
        if (schematic && regionLabelsOnTop)
            return Math.Clamp(_zoom / DefaultSchematicZoom, 0.12, 1.0);
        if (miniMapRegionLabelsOnTop)
        {
            double refZoom = HasJumpRangeOverlay ? _jumpRangeFocusZoom : DefaultStandardZoom;
            return Math.Clamp(_zoom / refZoom, 0.12, 1.0);
        }
        return 1.0;
    }

    /// <summary>
    /// Floating name + region tooltip for the hovered system. Always available on the Jump Range
    /// mini-map, and on the main schematic map only at wide zoom where plates collapse to dots and
    /// system names are otherwise hidden.
    /// </summary>
    private void DrawHoverTooltip(DrawingContext context)
    {
        if ((!ShowHoverTooltips && !MainMapHoverHintActive) || _hoveredSystem is not { } system || _lastPointerPos is not { } pointer)
            return;

        const double fontSize = 11;
        var lines = new List<(string Text, double Size, IBrush Brush)>
        {
            (system.Name, fontSize, Brushes.Black),
            (RegionNameProvider?.Invoke(system.RegionId) ?? $"Region {system.RegionId}", fontSize - 1, Brushes.DimGray),
        };
        AppendSecurityStatusLine(lines, system, fontSize);
        AppendTrackedCharacterLines(lines, system.Id, fontSize);
        AppendWormholeConnectionLines(lines, system.Id, fontSize);
        AppendManualWormholeLines(lines, system.Id, fontSize);
        AppendPvPActivityLines(lines, system, fontSize);
        AppendPilotGateJumpLine(lines, system.Id, fontSize);

        DrawFloatingHintBox(context, pointer, lines);
    }

    /// <summary>
    /// On compact/full schematic plates, shows Security status plus IHUB alliance ownership and/or
    /// live-tracked pilots (name/region are already on the plate).
    /// </summary>
    private void DrawSovereigntyHoverHint(DrawingContext context)
    {
        if (!MainMapSovereigntyHintActive || _hoveredSystem is not { } system || _lastPointerPos is not { } pointer)
            return;

        const double fontSize = 11;
        var lines = new List<(string Text, double Size, IBrush Brush)>();
        AppendSecurityStatusLine(lines, system, fontSize);
        string? alliance = IhubAllianceProvider?.Invoke(system.Id);
        if (!string.IsNullOrWhiteSpace(alliance))
            lines.Add((alliance, fontSize, Brushes.Black));
        AppendTrackedCharacterLines(lines, system.Id, fontSize);
        AppendWormholeConnectionLines(lines, system.Id, fontSize);
        AppendManualWormholeLines(lines, system.Id, fontSize);
        AppendPvPActivityLines(lines, system, fontSize);
        AppendPilotGateJumpLine(lines, system.Id, fontSize);

        DrawFloatingHintBox(context, pointer, lines);
    }

    private static void AppendSecurityStatusLine(
        List<(string Text, double Size, IBrush Brush)> lines, SolarSystem system, double fontSize)
    {
        double security = Math.Round(system.Security, 1);
        lines.Add(($"Security status: {security:0.0}", fontSize - 1, Brushes.Black));
    }

    /// <summary>
    /// Gate-jump distance on Standard mode and other views where the name/sovereignty hints are off.
    /// </summary>
    private void DrawGateJumpHoverHint(DrawingContext context)
    {
        if (ShowHoverTooltips || MainMapHoverHintActive || MainMapSovereigntyHintActive)
            return;
        if (_hoveredSystem is not { } system || _lastPointerPos is not { } pointer)
            return;
        if (ResolveMainProfileSystemId() is null)
            return;

        const double fontSize = 11;
        var lines = new List<(string Text, double Size, IBrush Brush)>();
        AppendPilotGateJumpLine(lines, system.Id, fontSize);
        AppendPvPActivityLines(lines, system, fontSize);
        if (lines.Count == 0)
            return;

        DrawFloatingHintBox(context, pointer, lines);
    }

    private void AppendWormholeConnectionLines(
        List<(string Text, double Size, IBrush Brush)> lines, int systemId, double fontSize)
    {
        if (!ShowWormholes) return;
        var connections = WormholeConnectionsProvider?.Invoke(systemId);
        if (connections is not { Count: > 0 })
            return;

        foreach (var connection in connections)
        {
            bool isHub = connection.HubSystemId == systemId;
            string direction = isHub
                ? connection.ExitsOutward ? "→" : "←"
                : connection.ExitsOutward ? "←" : "→";
            string remoteName = isHub ? connection.RemoteSystemName : connection.HubSystemName;
            var brush = connection.Hub == WormholeHubKind.Thera ? TheraWormholeHintBrush : TurnurWormholeHintBrush;
            lines.Add(($"Червоточина {direction} {remoteName} ({connection.HubSystemName})", fontSize - 1, brush));
            lines.Add(($"Тип {connection.WhType}, до {connection.MaxShipSize}", fontSize - 2, Brushes.DimGray));
            if (connection.RemainingHours is int hours)
                lines.Add(($"Осталось ~{hours} ч", fontSize - 2, Brushes.DimGray));
            lines.Add(($"{connection.HubSignature} ↔ {connection.RemoteSignature}", fontSize - 2, Brushes.DimGray));
        }
    }

    private void AppendManualWormholeLines(
        List<(string Text, double Size, IBrush Brush)> lines, int systemId, double fontSize)
    {
        if (!ShowWormholes) return;
        var marker = ManualWormholeProvider?.Invoke(systemId);
        if (marker is null) return;

        string exit = marker.ExitSystemId is int exitId && _map?.Get(exitId) is { } exitSystem
            ? exitSystem.Name
            : string.IsNullOrWhiteSpace(marker.ExitComment) ? "не указана" : marker.ExitComment;
        lines.Add(($"Ручная червоточина → {exit}", fontSize - 1, ManualWormholeHintBrush));
        var remaining = marker.ExpiresAtUtc - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            int hours = Math.Max(1, (int)Math.Ceiling(remaining.TotalHours));
            lines.Add(($"Осталось ~{hours} ч", fontSize - 2, Brushes.DimGray));
        }
    }

    private void AppendPvPActivityLines(
        List<(string Text, double Size, IBrush Brush)> lines, SolarSystem system, double fontSize)
    {
        if (PvPActivityProvider is null || IsJumpRangeMiniMap || !IsSystemMonitoredForPvP(system))
            return;

        var stats = PvPActivityProvider.Invoke(system.Id);
        if (stats.Level == PvPActivityLevel.None)
            return;

        var brush = stats.Level switch
        {
            PvPActivityLevel.NpcCapital => PvPNpcCapitalHighlight,
            PvPActivityLevel.Hot => PvPHotHighlight,
            _ => PvPRecentHighlight,
        };

        if (stats.Level == PvPActivityLevel.NpcCapital)
        {
            lines.Add(("zKillboard: NPC-капитал (30 мин)", fontSize - 1, brush));
            if (stats.ValidHourKillCount > 0)
                lines.Add((FormatHourKillCount(stats.ValidHourKillCount), fontSize - 2, Brushes.DimGray));
            return;
        }

        lines.Add(($"zKillboard: {FormatHourKillCount(stats.ValidHourKillCount)}", fontSize - 1, brush));
    }

    private static string FormatHourKillCount(int count) => count switch
    {
        1 => "1 убийство за час",
        >= 2 and <= 4 => $"{count} убийства за час",
        _ => $"{count} убийств за час",
    };

    private bool IsSystemMonitoredForPvP(SolarSystem system) =>
        PvPScope == ZKillboardScope.GlobalNullsec
            ? system.IsNullSec
            : _reachableByJump.Contains(system.Id) || system.Id == _jumpRangeOriginSystemId;

    private bool HasMonitoredPvPActivity(int? systemId) =>
        systemId is int id
        && _map?.Get(id) is { } system
        && IsSystemMonitoredForPvP(system)
        && PvPActivityProvider?.Invoke(id).Level != PvPActivityLevel.None;

    private void AppendPilotGateJumpLine(
        List<(string Text, double Size, IBrush Brush)> lines, int systemId, double fontSize)
    {
        if (ResolveMainProfileSystemId() is not int pilotId || _map is null)
            return;
        var route = GatePathfinder.FindRoute(_map, pilotId, systemId);
        if (route is null) return;

        int jumps = route.JumpCount;
        string jumpWord = jumps switch
        {
            1 => "прыжок",
            >= 2 and <= 4 => "прыжка",
            _ => "прыжков",
        };
        lines.Add(($"{jumps} {jumpWord}", fontSize - 1, Brushes.DimGray));
    }

    private int? ResolveMainProfileSystemId()
    {
        int? fromProvider = MainProfileSystemIdProvider?.Invoke();
        if (fromProvider is int providerId && _map?.Get(providerId) is not null)
            return providerId;
        if (_pilotSystemId is int pilotId && _map?.Get(pilotId) is not null)
            return pilotId;
        return null;
    }

    private void AppendTrackedCharacterLines(
        List<(string Text, double Size, IBrush Brush)> lines, int systemId, double fontSize)
    {
        var characters = CharactersInSystemProvider?.Invoke(systemId);
        if (characters is not { Count: > 0 })
            return;

        lines.Add((string.Join(", ", characters), fontSize - 1, TrackedCharacterHintBrush));
    }

    private void DrawFloatingHintBox(DrawingContext context, Point pointer,
        IReadOnlyList<(string Text, double Size, IBrush Brush)> lines)
    {
        const double padX = 8, padY = 5, lineGap = 2;
        var typeface = Typeface.Default;

        double boxW = lines.Max(l => MeasureText(l.Text, l.Size, typeface).Width) + padX * 2;
        double contentH = lines.Sum(l => MeasureText(l.Text, l.Size, typeface).Height) + lineGap * (lines.Count - 1);
        double boxH = padY * 2 + contentH;

        double x = pointer.X + 14;
        double y = pointer.Y + 14;
        if (x + boxW > Bounds.Width - 4) x = pointer.X - boxW - 14;
        if (y + boxH > Bounds.Height - 4) y = pointer.Y - boxH - 14;
        x = Math.Max(4, x);
        y = Math.Max(4, y);

        var box = new Rect(x, y, boxW, boxH);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), box);
        context.DrawRectangle(null, new Pen(Brushes.Gray, 1), box, 3, 3);

        double textY = y + padY;
        foreach (var (text, size, brush) in lines)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, size, brush);
            context.DrawText(formatted, new Point(x + padX, textY));
            textY += formatted.Height + lineGap;
        }
    }

    /// <summary>
    /// Region-name labels at wide zoom are drawn on top of systems; when zoomed in past the
    /// default level they are drawn before plates and system labels so later opaque draws paint
    /// over them and system names stay legible.
    /// </summary>
    private void DrawSchematicRegions(DrawingContext context, Rect viewport)
    {
        if (_schematicLayout is null) return;

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
    /// Region-name labels on the Jump Range mini-map: drawn on top at the auto-fit zoom level
    /// or wider, and behind system markers/labels when the user zooms in past that level.
    /// </summary>
    private void DrawStandardRegionLabels(DrawingContext context, Rect viewport)
    {
        if (_standardRegionCentroids.Count == 0) return;

        var typeface = new Typeface(Typeface.Default.FontFamily, FontStyle.Italic, FontWeight.SemiBold);
        double fontSize = Math.Clamp(10 + Scale * 0.06, 10, 17);

        foreach (var (regionId, centroid) in _standardRegionCentroids)
        {
            var screen = WorldToScreen(centroid);
            if (!viewport.Contains(screen)) continue;

            string regionName = RegionNameProvider?.Invoke(regionId) ?? $"Region {regionId}";
            var label = new FormattedText(regionName.ToUpperInvariant(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, fontSize, SchematicRegionLabelBrush);
            var labelPos = new Point(screen.X - label.Width / 2, screen.Y - label.Height / 2);
            context.FillRectangle(StandardLabelHalo, new Rect(labelPos.X - 4, labelPos.Y - 2, label.Width + 8, label.Height + 4));
            context.DrawText(label, labelPos);
        }
    }

    private HashSet<int>? BuildRouteSystemIds() =>
        RouteSteps is null ? null : RouteSteps.SelectMany(s => new[] { s.FromSystemId, s.ToSystemId }).ToHashSet();

    private bool IsPinnedSystem(int systemId, HashSet<int>? routeSystemIds) =>
        systemId == _selectedSystemId ||
        systemId == FromSystemId ||
        systemId == ToSystemId ||
        systemId == _hoveredSystem?.Id ||
        systemId == _linkedHoveredSystemId ||
        WaypointSystemIds?.Contains(systemId) == true ||
        routeSystemIds?.Contains(systemId) == true;

    /// <summary>Jump Range mini-map instance (Standard mode, true LY scale).</summary>
    private bool IsJumpRangeMiniMap => ShowHoverTooltips;

    /// <summary>
    /// True when the main schematic map is zoomed out far enough that plates collapse to dots
    /// (no visible names), so a floating name hint is shown for the hovered system instead.
    /// </summary>
    private bool MainMapHoverHintActive =>
        !ShowHoverTooltips && _displayMode == MapDisplayMode.Schematic && _currentPlateTier == SchematicPlateDetailTier.Dot;

    /// <summary>
    /// Compact/full schematic plates already show the system name; Security status, alliance
    /// ownership, and tracked pilots are hinted separately.
    /// </summary>
    private bool MainMapSovereigntyHintActive =>
        !ShowHoverTooltips
        && _displayMode == MapDisplayMode.Schematic
        && (_currentPlateTier == SchematicPlateDetailTier.Compact || _currentPlateTier == SchematicPlateDetailTier.Full)
        && _hoveredSystem is not null;

    /// <summary>Jump Range mini-map with an active origin and range circle.</summary>
    private bool HasJumpRangeOverlay => _jumpRangeOriginSystemId is not null && _selectedRangeLy > 0;

    /// <summary>Main-map solid jump-range rings are replaced by simulation styling while sim mode is on.</summary>
    private bool UseSimulationJumpRangeStyling => _jumpRangeSimulationActive && !IsJumpRangeMiniMap;

    private bool IsJumpRangeHighlightedSystem(int systemId) =>
        _reachableByJump.Contains(systemId) || systemId == _jumpRangeOriginSystemId;

    private bool ShouldDrawMainJumpRangeOutline(int systemId) =>
        !UseSimulationJumpRangeStyling &&
        (_reachableByJump.Contains(systemId) || systemId == _jumpRangeOriginSystemId);

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
        bool miniMapView = IsJumpRangeMiniMap;
        bool miniMapReachability = HasJumpRangeOverlay;

        // Reserve the space around every visible dot so labels never start on top of a marker,
        // even one whose own label loses the placement race.
        foreach (var (_, screen) in visible)
        {
            OccupyCell(occupied, new Rect(screen.X - 4, screen.Y - 4, 8, 8), LabelCellSizePx);
        }

        bool ShouldForceLabel(int systemId) =>
            miniMapReachability && (systemId == _jumpRangeOriginSystemId || IsPinnedSystem(systemId, routeSystemIds));

        void DrawLabel(SolarSystem system, Point screen, bool force)
        {
            bool pinned = force || IsPinnedSystem(system.Id, routeSystemIds);
            bool inRange = IsJumpRangeHighlightedSystem(system.Id);
            var textBrush = miniMapReachability && !inRange ? Brushes.DimGray : Brushes.Black;
            var haloBrush = miniMapReachability && !inRange ? JumpRangeMiniMapOutOfRangeLabelHalo : StandardLabelHalo;
            var formatted = new FormattedText(system.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, fontSize, textBrush);
            var rect = new Rect(screen.X + 6, screen.Y - formatted.Height / 2, formatted.Width + 3, formatted.Height);

            if (!pinned && OverlapsCell(occupied, rect, LabelCellSizePx)) return;

            context.FillRectangle(haloBrush, new Rect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2));
            context.DrawText(formatted, new Point(rect.X + 1, rect.Y));
            OccupyCell(occupied, rect, LabelCellSizePx);
        }

        if (miniMapView)
        {
            var miniMapOrdered = visible
                .OrderByDescending(v => miniMapReachability && v.System.Id == _jumpRangeOriginSystemId)
                .ThenByDescending(v => IsPinnedSystem(v.System.Id, routeSystemIds))
                .ThenByDescending(v => miniMapReachability && IsJumpRangeHighlightedSystem(v.System.Id))
                .ThenByDescending(v => _map?.GateNeighbors(v.System.Id).Count ?? 0)
                .ThenByDescending(v => v.System.Security)
                .Take(MaxLabelCandidates);

            foreach (var (system, screen) in miniMapOrdered)
                DrawLabel(system, screen, force: ShouldForceLabel(system.Id));

            return;
        }

        var ordered = visible
            .OrderByDescending(v => IsPinnedSystem(v.System.Id, routeSystemIds))
            .ThenByDescending(v => _map?.GateNeighbors(v.System.Id).Count ?? 0)
            .ThenByDescending(v => v.System.Security)
            .Take(MaxLabelCandidates)
            .ToList();

        foreach (var (system, screen) in ordered)
            DrawLabel(system, screen, force: false);
    }

    private const double SchematicDotDiameter = 7.0;

    private const double SchematicFullNameFontBase = 5.8;
    private const double SchematicFullKillFontBase = 4.8;
    private const double SchematicFullPadXBase = 1.5;
    private const double SchematicFullPadYBase = 0.6;
    private const double SchematicFullLineGapBase = 0.0;
    private const double SchematicFullMinWidthBase = 14.0;

    private const double SchematicCompactNameFontBase = 6.5;
    private const double SchematicCompactPadXBase = 2.5;
    private const double SchematicCompactPadYBase = 1.0;
    private const double SchematicCompactMinWidthBase = 12.0;

    /// <summary>
    /// Dotlan-style system plates. Every visible system renders at the same detail tier.
    /// </summary>
    private void DrawSchematicPlates(DrawingContext context, List<(SolarSystem System, Point Screen)> visible)
    {
        if (visible.Count == 0) return;

        double dotDiameter = SchematicDotDiameter * _wideZoomHighlightScale;
        var typeface = Typeface.Default;
        const double cellSize = 12.0;

        Rect ComputeRectAtScale(SchematicPlateDetailTier tier, SolarSystem system, Point screen, double scale)
        {
            double fullNameFont = SchematicFullNameFontBase * scale;
            double fullKillFont = SchematicFullKillFontBase * scale;
            double fullPadX = SchematicFullPadXBase * scale;
            double fullPadY = SchematicFullPadYBase * scale;
            double fullLineGap = SchematicFullLineGapBase * scale;
            double fullMinWidth = SchematicFullMinWidthBase * scale;

            double compactNameFont = SchematicCompactNameFontBase * scale;
            double compactPadX = SchematicCompactPadXBase * scale;
            double compactPadY = SchematicCompactPadYBase * scale;
            double compactMinWidth = SchematicCompactMinWidthBase * scale;

            switch (tier)
            {
                case SchematicPlateDetailTier.Full:
                {
                    var nameText = MeasureText(system.Name, fullNameFont, typeface);
                    if (ShowNpcKillLabels)
                    {
                        int kills = NpcKillsProvider?.Invoke(system.Id) ?? 0;
                        var killText = MeasureText(kills.ToString(CultureInfo.InvariantCulture), fullKillFont, typeface);
                        double width = Math.Max(Math.Max(nameText.Width, killText.Width) + fullPadX * 2, fullMinWidth);
                        double height = fullPadY + nameText.Height + fullLineGap + killText.Height + fullPadY;
                        return new Rect(screen.X - width / 2, screen.Y - height / 2, width, height);
                    }

                    double nameOnlyWidth = Math.Max(nameText.Width + fullPadX * 2, fullMinWidth);
                    double nameOnlyHeight = fullPadY + nameText.Height + fullPadY;
                    return new Rect(screen.X - nameOnlyWidth / 2, screen.Y - nameOnlyHeight / 2, nameOnlyWidth, nameOnlyHeight);
                }
                case SchematicPlateDetailTier.Compact:
                {
                    var nameText = MeasureText(system.Name, compactNameFont, typeface);
                    double width = Math.Max(nameText.Width + compactPadX * 2, compactMinWidth);
                    double height = compactPadY * 2 + nameText.Height;
                    return new Rect(screen.X - width / 2, screen.Y - height / 2, width, height);
                }
                default:
                    return new Rect(screen.X - dotDiameter / 2, screen.Y - dotDiameter / 2, dotDiameter, dotDiameter);
            }
        }

        bool AllFitAtScale(IEnumerable<(SolarSystem System, Point Screen)> systems, SchematicPlateDetailTier tier, double scale)
        {
            var trial = new Dictionary<(long Cx, long Cy), List<Rect>>();
            foreach (var (system, screen) in systems)
            {
                var rect = ComputeRectAtScale(tier, system, screen, scale);
                if (OverlapsCell(trial, rect, cellSize)) return false;
                OccupyCell(trial, rect, cellSize);
            }
            return true;
        }

        SchematicPlateDetailTier tier = SchematicPlateLayoutPolicy.ResolveTier(_zoom, ShowNpcKillLabels);
        _currentPlateTier = tier;
        double plateScale = tier == SchematicPlateDetailTier.Dot
            ? SchematicPlateLayoutPolicy.ComputeTargetPlateScale(tier, _zoom, _wideZoomHighlightScale)
            : SchematicPlateLayoutPolicy.ShrinkUntilFits(
                SchematicPlateLayoutPolicy.ComputeTargetPlateScale(tier, _zoom, _wideZoomHighlightScale),
                scale => AllFitAtScale(visible, tier, scale));

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

        Rect ComputeRect(SchematicPlateDetailTier tier, SolarSystem system, Point screen) =>
            ComputeRectAtScale(tier, system, screen, plateScale);

        _lastPlateRects = new Dictionary<int, Rect>(visible.Count);

        foreach (var (system, screen) in visible)
        {
            var rect = ComputeRect(tier, system, screen);

            int? npcKills = NpcKillsProvider?.Invoke(system.Id);
            var fillBrush = SystemFillBrush(system, npcKills);
            var textBrush = ReadableTextBrush(fillBrush);

            bool isSelected = system.Id == _selectedSystemId;
            bool isFrom = system.Id == FromSystemId;
            bool isTo = system.Id == ToSystemId;
            bool isGateNeighbor = _gateNeighbors.Contains(system.Id);
            bool isLinkedHover = system.Id == _linkedHoveredSystemId;

            IBrush borderBrush = isFrom ? Brushes.LimeGreen
                : isTo ? Brushes.OrangeRed
                : isSelected ? Brushes.Black
                : isGateNeighbor || isLinkedHover ? GateHighlightBrush
                : Brushes.Black;
            double borderWidth = isSelected || isFrom || isTo ? 2.0 : isGateNeighbor || isLinkedHover ? 1.6 : 1.0;

            var jumpRangePen = ShouldDrawMainJumpRangeOutline(system.Id)
                ? new Pen(Brushes.Black, JumpRangeRingWidth)
                : null;
            var simulationPen = CreateSimulationOutlinePen(system.Id);

            switch (tier)
            {
                case SchematicPlateDetailTier.Full:
                {
                    var nameText = new FormattedText(system.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fullNameFont, textBrush);
                    context.DrawRectangle(fillBrush, new Pen(borderBrush, borderWidth), rect, 5, 5);
                    context.DrawText(nameText, new Point(screen.X - nameText.Width / 2, rect.Y + fullPadY));
                    if (ShowNpcKillLabels)
                    {
                        var killText = new FormattedText((npcKills ?? 0).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fullKillFont, textBrush);
                        context.DrawText(killText, new Point(screen.X - killText.Width / 2, rect.Y + fullPadY + nameText.Height + fullLineGap));
                    }
                    DrawNpcStationMarker(context, system.Id, rect);
                    if (jumpRangePen is not null) context.DrawRectangle(null, jumpRangePen, rect, 5, 5);
                    if (simulationPen is not null) context.DrawRectangle(null, simulationPen, rect, 5, 5);
                    break;
                }
                case SchematicPlateDetailTier.Compact:
                {
                    var nameText = new FormattedText(system.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, compactNameFont, textBrush);
                    context.DrawRectangle(fillBrush, new Pen(borderBrush, borderWidth), rect, 4, 4);
                    context.DrawText(nameText, new Point(screen.X - nameText.Width / 2, rect.Y + compactPadY));
                    DrawNpcStationMarker(context, system.Id, rect);
                    if (jumpRangePen is not null) context.DrawRectangle(null, jumpRangePen, rect, 4, 4);
                    if (simulationPen is not null) context.DrawRectangle(null, simulationPen, rect, 4, 4);
                    break;
                }
                default:
                    context.DrawEllipse(fillBrush, new Pen(borderBrush, borderWidth), screen, dotDiameter / 2, dotDiameter / 2);
                    if (jumpRangePen is not null) context.DrawEllipse(null, jumpRangePen, screen, dotDiameter / 2, dotDiameter / 2);
                    if (simulationPen is not null) context.DrawEllipse(null, simulationPen, screen, dotDiameter / 2, dotDiameter / 2);
                    break;
            }

            _lastPlateRects[system.Id] = rect;
        }
    }

    /// <summary>
    /// Standard-mode system markers: mini-map-sized circles filled by security/NPC-kills color,
    /// or NPC-station squares using the same light-green / no-clone diagonal fill as Dotlan plates.
    /// </summary>
    private void DrawStandardSystemMarkers(DrawingContext context, List<(SolarSystem System, Point Screen)> visible)
    {
        foreach (var (system, screen) in visible)
        {
            bool isSelected = system.Id == _selectedSystemId;
            bool isGateNeighbor = _gateNeighbors.Contains(system.Id);
            bool isJumpReachable = _reachableByJump.Contains(system.Id);
            bool isOrigin = system.Id == _jumpRangeOriginSystemId;
            bool highlightedEndpoint = system.Id == FromSystemId || system.Id == ToSystemId || isSelected || isOrigin;
            bool hasStation = HasNpcStationProvider?.Invoke(system.Id) == true;

            double r;
            Pen markerPen;

            if (IsJumpRangeMiniMap && HasJumpRangeOverlay && !isJumpReachable && !isOrigin)
            {
                // Out-of-range context: slightly larger muted marker so it stays readable
                // over the background gate lines drawn underneath.
                r = StandardMarkerRadiusOutOfRange;
                markerPen = new Pen(JumpRangeMiniMapOutOfRangeMarkerBrush, 1.0);
            }
            else if (IsJumpRangeMiniMap)
            {
                r = highlightedEndpoint ? StandardMarkerRadiusSelected : StandardMarkerRadius;
                markerPen = HasJumpRangeOverlay && (isJumpReachable || isOrigin)
                    ? new Pen(Brushes.Black, JumpRangeRingWidth)
                    : new Pen(JumpRangeMiniMapOutOfRangeMarkerBrush, 1.0);
            }
            else
            {
                r = highlightedEndpoint ? StandardMarkerRadiusSelected : StandardMarkerRadius;
                // Dotlan-style jump-range highlight: a bold black outline traced directly on the
                // marker's own edge (not a separate ring floating outside it).
                markerPen = ShouldDrawMainJumpRangeOutline(system.Id)
                    ? new Pen(Brushes.Black, JumpRangeRingWidth)
                    : StandardSystemOutlinePen;
            }

            if (hasStation)
            {
                var marker = new Rect(screen.X - r, screen.Y - r, r * 2, r * 2);
                context.FillRectangle(NpcStationMarkerBrush, marker);
                if (NpcStationNoCloneProvider?.Invoke(system.Id) == true)
                    DrawNpcStationNoCloneDiagonal(context, marker);
                context.DrawRectangle(null, markerPen, marker);

                if (!IsJumpRangeMiniMap)
                {
                    var simulationPen = CreateSimulationOutlinePen(system.Id);
                    if (simulationPen is not null)
                        context.DrawRectangle(null, simulationPen, marker);
                }

                if (isSelected)
                    context.DrawRectangle(null, new Pen(Brushes.Black, 2.0), marker.Inflate(3));
                else if (isGateNeighbor)
                    context.DrawRectangle(null, new Pen(GateHighlightBrush, 2.0), marker.Inflate(2.5));
            }
            else
            {
                var brush = SystemFillBrush(system, NpcKillsProvider?.Invoke(system.Id));
                context.DrawEllipse(brush, markerPen, screen, r, r);

                if (!IsJumpRangeMiniMap)
                {
                    var simulationPen = CreateSimulationOutlinePen(system.Id);
                    if (simulationPen is not null)
                        context.DrawEllipse(null, simulationPen, screen, r, r);
                }

                if (isSelected)
                    context.DrawEllipse(null, new Pen(Brushes.Black, 2.0), screen, r + 3, r + 3);
                else if (isGateNeighbor)
                    context.DrawEllipse(null, new Pen(GateHighlightBrush, 2.0), screen, r + 2.5, r + 2.5);
            }
        }
    }

    private static double ClampStandardJumpRangeRadiusPx(double radiusPx, double viewportWidth, double viewportHeight) =>
        Math.Min(radiusPx, Math.Max(viewportWidth, viewportHeight) * StandardJumpRangeRadiusCapFactor);

    /// <summary>
    /// Cohen–Sutherland clip so gate stubs with far off-screen endpoints stay within a safe
    /// draw rect (Skia can drop geometry with extreme pixel coordinates at high zoom).
    /// </summary>
    private static bool TryClipLineToRect(Point p0, Point p1, Rect rect, out Point a, out Point b)
    {
        const int Inside = 0;
        const int Left = 1;
        const int Right = 2;
        const int Bottom = 4;
        const int Top = 8;

        static int Code(Point p, Rect r)
        {
            int c = Inside;
            if (p.X < r.X) c |= Left;
            else if (p.X > r.Right) c |= Right;
            if (p.Y < r.Y) c |= Top;
            else if (p.Y > r.Bottom) c |= Bottom;
            return c;
        }

        double x0 = p0.X, y0 = p0.Y, x1 = p1.X, y1 = p1.Y;
        int code0 = Code(p0, rect);
        int code1 = Code(p1, rect);

        for (int i = 0; i < 8; i++)
        {
            if ((code0 | code1) == 0)
            {
                a = new Point(x0, y0);
                b = new Point(x1, y1);
                return true;
            }

            if ((code0 & code1) != 0)
            {
                a = default;
                b = default;
                return false;
            }

            int codeOut = code0 != 0 ? code0 : code1;
            double x, y;
            if ((codeOut & Top) != 0)
            {
                x = x0 + (x1 - x0) * (rect.Y - y0) / (y1 - y0);
                y = rect.Y;
            }
            else if ((codeOut & Bottom) != 0)
            {
                x = x0 + (x1 - x0) * (rect.Bottom - y0) / (y1 - y0);
                y = rect.Bottom;
            }
            else if ((codeOut & Right) != 0)
            {
                y = y0 + (y1 - y0) * (rect.Right - x0) / (x1 - x0);
                x = rect.Right;
            }
            else
            {
                y = y0 + (y1 - y0) * (rect.X - x0) / (x1 - x0);
                x = rect.X;
            }

            if (codeOut == code0)
            {
                x0 = x;
                y0 = y;
                code0 = Code(new Point(x0, y0), rect);
            }
            else
            {
                x1 = x;
                y1 = y;
                code1 = Code(new Point(x1, y1), rect);
            }
        }

        a = default;
        b = default;
        return false;
    }

    /// <summary>
    /// Flags a dockable NPC-station system with a small light-green (салатовый) square tucked into
    /// the plate's bottom-right corner. When no station in the system offers cloning, the
    /// top-left triangle is filled red along the square's diagonal.
    /// </summary>
    private void DrawNpcStationMarker(DrawingContext context, int systemId, Rect plate)
    {
        if (HasNpcStationProvider?.Invoke(systemId) != true) return;

        double size = Math.Clamp(plate.Height * 0.42, 2.0, plate.Width * 0.4);
        var marker = new Rect(plate.Right - size, plate.Bottom - size, size, size);
        context.FillRectangle(NpcStationMarkerBrush, marker);

        if (NpcStationNoCloneProvider?.Invoke(systemId) == true)
            DrawNpcStationNoCloneDiagonal(context, marker);

        context.DrawRectangle(null, NpcStationMarkerPen, marker);
    }

    private static void DrawNpcStationNoCloneDiagonal(DrawingContext context, Rect marker)
    {
        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(new Point(marker.Left, marker.Top), true);
            gc.LineTo(new Point(marker.Right, marker.Bottom));
            gc.LineTo(new Point(marker.Left, marker.Bottom));
            gc.EndFigure(true);
        }
        context.DrawGeometry(NpcStationNoCloneMarkerBrush, null, geometry);
    }

    private static FormattedText MeasureText(string text, double fontSize, Typeface typeface) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);

    /// <summary>Null-sec and red low-sec plates are white (Dotlan-style); others use security color.</summary>
    private static IBrush PlateFillBrush(double security) =>
        Math.Round(security, 1) < 0.2 ? Brushes.White : SecurityBrush(security);

    private bool ShowNpcKillLabels => _plateColorMode == MapPlateColorMode.NpcKills;

    private IBrush SystemFillBrush(SolarSystem system, int? npcKills)
    {
        if (_plateColorMode == MapPlateColorMode.Security)
            return PlateFillBrush(system.Security);
        return npcKills is int kills ? NpcKillsFillBrush(kills) : PlateFillBrush(system.Security);
    }

    /// <summary>
    /// Dotlan "NPC Kills" style gradient, toned down so busy ratting systems do not glare on
    /// the white schematic map. Stops follow ESI's last-hour distribution but are desaturated.
    /// </summary>
    private static readonly (double Kills, Color Color)[] NpcKillsColorStops =
    {
        (0,    Color.FromRgb(0xFF, 0xFF, 0xFF)),
        (25,   Color.FromRgb(0xE8, 0xF0, 0xDC)),
        (75,   Color.FromRgb(0xB8, 0xD4, 0x98)),
        (200,  Color.FromRgb(0xD8, 0xD0, 0x90)),
        (500,  Color.FromRgb(0xD4, 0xB0, 0x78)),
        (1200, Color.FromRgb(0xC8, 0x88, 0x80)),
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
                var pos = new Point(screen.X + Math.Cos(angle) * offset * _wideZoomHighlightScale, screen.Y + Math.Sin(angle) * offset * _wideZoomHighlightScale);
                DrawStructureIcon(context, structures[i].Kind, pos, _wideZoomHighlightScale);
            }
        }
    }

    private static readonly IBrush StructureIconBorder = new SolidColorBrush(Color.FromArgb(220, 20, 20, 22));

    private static void DrawStructureIcon(DrawingContext context, StructureKind kind, Point center, double scale = 1.0)
    {
        double size = 4.5 * scale;
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
        double s = _wideZoomHighlightScale;
        var pen = new Pen(brush, (schematic ? 3.0 : 2.5) * s);
        double r = (schematic ? 10 : 9) * s;
        context.DrawEllipse(null, pen, screen, r, r);
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11 * s, new SolidColorBrush(((ISolidColorBrush)brush).Color));
        context.DrawText(text, new Point(screen.X + 11 * s, screen.Y - 22 * s));
    }

    /// <summary>
    /// Red, yellow or purple outline on jump-reachable systems with recent zKillboard activity.
    /// Drawn after plates so it stays visible on top of the black jump-range ring.
    /// </summary>
    private void DrawPvPActivityHighlights(DrawingContext context, bool schematic, List<(SolarSystem System, Point Screen)> visible)
    {
        if (PvPActivityProvider is null || IsJumpRangeMiniMap) return;

        double s = _wideZoomHighlightScale;
        foreach (var (system, screen) in visible)
        {
            bool monitored = PvPScope == ZKillboardScope.GlobalNullsec
                ? system.IsNullSec
                : _reachableByJump.Contains(system.Id) || system.Id == _jumpRangeOriginSystemId;
            if (!monitored) continue;

            var stats = PvPActivityProvider.Invoke(system.Id);
            if (stats.Level == PvPActivityLevel.None) continue;

            var (brush, fill) = stats.Level switch
            {
                PvPActivityLevel.NpcCapital => (PvPNpcCapitalHighlight, PvPNpcCapitalFill),
                PvPActivityLevel.Hot => (PvPHotHighlight, PvPHotFill),
                _ => (PvPRecentHighlight, PvPRecentFill),
            };
            var pen = new Pen(brush, (schematic ? 6.0 : 5.0) * s);

            if (schematic && _lastPlateRects.TryGetValue(system.Id, out var rect))
            {
                double expand = 5.0 * s;
                var expanded = new Rect(rect.X - expand, rect.Y - expand, rect.Width + expand * 2, rect.Height + expand * 2);
                context.DrawRectangle(fill, pen, expanded, 5, 5);
            }
            else
            {
                double r = system.Id == _selectedSystemId || system.Id == FromSystemId || system.Id == ToSystemId ? 5.0 : 2.4;
                context.DrawEllipse(fill, pen, screen, (r + 5.0) * s, (r + 5.0) * s);
            }
        }
    }

    /// <summary>
    /// Soft salad-green glow on solar systems infested by Sansha Nation incursions. Animated only
    /// when zoomed in past 5.00; at overview zoom a faint static halo is shown instead.
    /// </summary>
    private void DrawSanshaIncursionHighlights(DrawingContext context, bool schematic, List<(SolarSystem System, Point Screen)> visible)
    {
        if (SanshaIncursionProvider is null || IsJumpRangeMiniMap) return;

        double s = _wideZoomHighlightScale;
        bool animated = _zoom > SanshaIncursionAnimMinZoom;
        double pulse = animated ? 0.55 + 0.45 * Math.Sin(_incursionAnimPhase) : 0.7;

        foreach (var (system, screen) in visible)
        {
            if (!SanshaIncursionProvider.Invoke(system.Id)) continue;

            if (schematic && _lastPlateRects.TryGetValue(system.Id, out var rect))
                DrawSanshaIncursionPlateGlow(context, rect, s, pulse, animated);
            else
                DrawSanshaIncursionMarkerGlow(context, screen, system, s, pulse, animated);
        }
    }

    private void DrawSanshaIncursionPlateGlow(DrawingContext context, Rect rect, double s, double pulse, bool animated)
    {
        double baseExpand = (animated ? 2.0 + pulse * 2.5 : 1.5) * s;
        for (int layer = 3; layer >= 1; layer--)
        {
            double expand = baseExpand + layer * 2.5 * s;
            byte alpha = (byte)((animated ? 32 : 26) + pulse * 16 / layer);
            var halo = new SolidColorBrush(Color.FromArgb(alpha, SanshaIncursionColor.R, SanshaIncursionColor.G, SanshaIncursionColor.B));
            var expanded = new Rect(rect.X - expand, rect.Y - expand, rect.Width + expand * 2, rect.Height + expand * 2);
            context.DrawRectangle(halo, null, expanded, 5, 5);
        }

        byte strokeAlpha = SanshaIncursionStrokeAlpha(pulse, animated);
        var pen = new Pen(
            new SolidColorBrush(Color.FromArgb(strokeAlpha, SanshaIncursionColor.R, SanshaIncursionColor.G, SanshaIncursionColor.B)),
            Math.Max(1.0, 1.7 * s));
        context.DrawRectangle(null, pen, rect, 5, 5);
    }

    private void DrawSanshaIncursionMarkerGlow(DrawingContext context, Point screen, SolarSystem system, double s, double pulse, bool animated)
    {
        double baseR = system.Id == _selectedSystemId || system.Id == FromSystemId || system.Id == ToSystemId ? 5.0 : 2.4;
        double centerR = (baseR + 3.0) * s;

        for (int layer = 3; layer >= 1; layer--)
        {
            double r = centerR + (animated ? 3.0 + pulse * 4.0 : 2.5) * s + layer * 2.0 * s;
            byte alpha = (byte)((animated ? 32 : 26) + pulse * 16 / layer);
            var halo = new SolidColorBrush(Color.FromArgb(alpha, SanshaIncursionColor.R, SanshaIncursionColor.G, SanshaIncursionColor.B));
            context.DrawEllipse(halo, null, screen, r, r);
        }

        byte strokeAlpha = SanshaIncursionStrokeAlpha(pulse, animated);
        var pen = new Pen(
            new SolidColorBrush(Color.FromArgb(strokeAlpha, SanshaIncursionColor.R, SanshaIncursionColor.G, SanshaIncursionColor.B)),
            Math.Max(1.0, 1.6 * s));
        context.DrawEllipse(null, pen, screen, centerR + 1.5 * s, centerR + 1.5 * s);
    }

    /// <summary>Contour opacity: 80–98% when animated, 88% static at overview zoom.</summary>
    private static byte SanshaIncursionStrokeAlpha(double pulse, bool animated)
    {
        if (!animated) return 88;
        double t = Math.Clamp((pulse - 0.55) / 0.45, 0, 1);
        return (byte)(80 + t * 18);
    }

    /// <summary>
    /// Thera/Turnur wormhole markers: ripple rings at overview zoom; breathing plate/marker glow
    /// (Sansha-style) when zoomed in past 5.00. Thera itself is not drawn — only remote exits.
    /// </summary>
    private void DrawEveScoutWormholeHighlights(DrawingContext context, bool schematic, List<(SolarSystem System, Point Screen)> visible)
    {
        if (!ShowWormholes || WormholeConnectionsProvider is null || IsJumpRangeMiniMap) return;

        var hubKindsBySystem = BuildWormholeKindsBySystem();

        foreach (var (system, screen) in visible)
        {
            if (!hubKindsBySystem.TryGetValue(system.Id, out var kinds)) continue;
            bool isHub = WormholeHubCatalog.IsHubSystem(system.Id);
            foreach (var kind in kinds)
            {
                if (schematic && _lastPlateRects.TryGetValue(system.Id, out var rect))
                    DrawWormholePlateMarker(context, rect, kind, isHub);
                else
                    DrawWormholeDotMarker(context, screen, system, kind, isHub);
            }
        }
    }

    private Dictionary<int, HashSet<WormholeHubKind>> BuildWormholeKindsBySystem()
    {
        var result = new Dictionary<int, HashSet<WormholeHubKind>>();
        if (WormholeConnectionsProvider is null) return result;

        void Add(int systemId, WormholeHubKind kind)
        {
            if (systemId == WormholeHubCatalog.TheraSystemId || _map?.Get(systemId) is null) return;
            if (!result.TryGetValue(systemId, out var kinds))
            {
                kinds = new HashSet<WormholeHubKind>();
                result[systemId] = kinds;
            }
            kinds.Add(kind);
        }

        foreach (int hubId in new[] { WormholeHubCatalog.TheraSystemId, WormholeHubCatalog.TurnurSystemId })
        {
            foreach (var connection in WormholeConnectionsProvider.Invoke(hubId))
            {
                Add(connection.HubSystemId, connection.Hub);
                Add(connection.RemoteSystemId, connection.Hub);
            }
        }

        return result;
    }

    /// <summary>
    /// User-placed wormhole markers: same ripple/glow animation as Thera/Turnur but dark gray and
    /// always visible (no Map-menu toggle). Not drawn on the Jump Range mini-map.
    /// </summary>
    private void DrawManualWormholeHighlights(DrawingContext context, bool schematic, List<(SolarSystem System, Point Screen)> visible)
    {
        if (!ShowWormholes || ManualWormholeProvider is null || IsJumpRangeMiniMap) return;

        foreach (var (system, screen) in visible)
        {
            if (ManualWormholeProvider.Invoke(system.Id) is null) continue;
            if (schematic && _lastPlateRects.TryGetValue(system.Id, out var rect))
                DrawManualWormholePlateMarker(context, rect);
            else
                DrawManualWormholeDotMarker(context, screen, system);
        }
    }

    private void DrawManualWormholePlateMarker(DrawingContext context, Rect rect)
    {
        var color = ManualWormholeColor;
        if (_zoom > SanshaIncursionAnimMinZoom)
        {
            double closeScale = _wideZoomHighlightScale;
            double pulse = 0.55 + 0.45 * Math.Sin(_wormholeAnimPhase);
            DrawWormholePlateGlow(context, rect, color, closeScale, pulse, animated: true);
            return;
        }

        double zoomT = ComputeWormholeCloseZoomT();
        double s = WormholeHighlightScale(zoomT);
        var center = rect.Center;
        DrawWormholeRippleRings(context, center, color, isHub: false, zoomT, s);

        byte borderAlpha = (byte)Lerp(150, 55, zoomT);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(borderAlpha, color.R, color.G, color.B)),
            Math.Max(1.2, Lerp(2.2, 1.0, zoomT) * s));
        context.DrawRectangle(null, pen, rect, 5, 5);
    }

    private void DrawManualWormholeDotMarker(DrawingContext context, Point screen, SolarSystem system)
    {
        var color = ManualWormholeColor;
        if (_zoom > SanshaIncursionAnimMinZoom)
        {
            double closeScale = _wideZoomHighlightScale;
            double pulse = 0.55 + 0.45 * Math.Sin(_wormholeAnimPhase);
            DrawWormholeMarkerGlow(context, screen, system, color, closeScale, pulse, animated: true, isHub: false);
            return;
        }

        double zoomT = ComputeWormholeCloseZoomT();
        double s = WormholeHighlightScale(zoomT);
        DrawWormholeRippleRings(context, screen, color, isHub: false, zoomT, s);

        double baseR = system.Id == _selectedSystemId || system.Id == FromSystemId || system.Id == ToSystemId ? 5.0 : 2.4;
        double centerR = (baseR + 2.0) * s;
        byte strokeAlpha = (byte)Lerp(145, 50, zoomT);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(strokeAlpha, color.R, color.G, color.B)),
            Math.Max(1.2, Lerp(1.8, 1.0, zoomT) * s));
        context.DrawEllipse(null, pen, screen, centerR + 1.5 * s, centerR + 1.5 * s);
    }

    private void DrawWormholePlateMarker(DrawingContext context, Rect rect, WormholeHubKind kind, bool isHub)
    {
        var color = WormholeColor(kind);
        if (_zoom > SanshaIncursionAnimMinZoom)
        {
            double closeScale = _wideZoomHighlightScale;
            double pulse = 0.55 + 0.45 * Math.Sin(_wormholeAnimPhase);
            DrawWormholePlateGlow(context, rect, color, closeScale, pulse, animated: true);
            return;
        }

        double zoomT = ComputeWormholeCloseZoomT();
        double s = WormholeHighlightScale(zoomT);
        var center = rect.Center;
        DrawWormholeRippleRings(context, center, color, isHub, zoomT, s);

        byte borderAlpha = (byte)Lerp(isHub ? 175 : 150, isHub ? 75 : 55, zoomT);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(borderAlpha, color.R, color.G, color.B)),
            Math.Max(1.2, Lerp(isHub ? 2.6 : 2.2, isHub ? 1.4 : 1.0, zoomT) * s));
        context.DrawRectangle(null, pen, rect, 5, 5);
    }

    private void DrawWormholeDotMarker(DrawingContext context, Point screen, SolarSystem system, WormholeHubKind kind, bool isHub)
    {
        var color = WormholeColor(kind);
        if (_zoom > SanshaIncursionAnimMinZoom)
        {
            double closeScale = _wideZoomHighlightScale;
            double pulse = 0.55 + 0.45 * Math.Sin(_wormholeAnimPhase);
            DrawWormholeMarkerGlow(context, screen, system, color, closeScale, pulse, animated: true, isHub);
            return;
        }

        double zoomT = ComputeWormholeCloseZoomT();
        double s = WormholeHighlightScale(zoomT);
        DrawWormholeRippleRings(context, screen, color, isHub, zoomT, s);

        double baseR = system.Id == _selectedSystemId || system.Id == FromSystemId || system.Id == ToSystemId ? 5.0 : 2.4;
        double centerR = (baseR + (isHub ? 3.0 : 2.0)) * s;
        byte strokeAlpha = (byte)Lerp(isHub ? 170 : 145, isHub ? 70 : 50, zoomT);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(strokeAlpha, color.R, color.G, color.B)),
            Math.Max(1.2, Lerp(isHub ? 2.2 : 1.8, 1.0, zoomT) * s));
        context.DrawEllipse(null, pen, screen, centerR + 1.5 * s, centerR + 1.5 * s);
    }

    private void DrawWormholePlateGlow(DrawingContext context, Rect rect, Color color, double s, double pulse, bool animated)
    {
        double baseExpand = (animated ? 2.0 + pulse * 2.5 : 1.5) * s;
        for (int layer = 3; layer >= 1; layer--)
        {
            double expand = baseExpand + layer * 2.5 * s;
            byte alpha = (byte)((animated ? 32 : 26) + pulse * 16 / layer);
            var halo = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            var expanded = new Rect(rect.X - expand, rect.Y - expand, rect.Width + expand * 2, rect.Height + expand * 2);
            context.DrawRectangle(halo, null, expanded, 5, 5);
        }

        byte strokeAlpha = WormholeGlowStrokeAlpha(pulse, animated);
        var pen = new Pen(
            new SolidColorBrush(Color.FromArgb(strokeAlpha, color.R, color.G, color.B)),
            Math.Max(1.0, 1.7 * s));
        context.DrawRectangle(null, pen, rect, 5, 5);
    }

    private void DrawWormholeMarkerGlow(
        DrawingContext context, Point screen, SolarSystem system, Color color, double s, double pulse, bool animated, bool isHub)
    {
        double baseR = system.Id == _selectedSystemId || system.Id == FromSystemId || system.Id == ToSystemId ? 5.0 : 2.4;
        double centerR = (baseR + (isHub ? 3.0 : 2.0)) * s;

        for (int layer = 3; layer >= 1; layer--)
        {
            double r = centerR + (animated ? 3.0 + pulse * 4.0 : 2.5) * s + layer * 2.0 * s;
            byte alpha = (byte)((animated ? 32 : 26) + pulse * 16 / layer);
            var halo = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            context.DrawEllipse(halo, null, screen, r, r);
        }

        byte strokeAlpha = WormholeGlowStrokeAlpha(pulse, animated);
        var pen = new Pen(
            new SolidColorBrush(Color.FromArgb(strokeAlpha, color.R, color.G, color.B)),
            Math.Max(1.0, 1.6 * s));
        context.DrawEllipse(null, pen, screen, centerR + 1.5 * s, centerR + 1.5 * s);
    }

    private static byte WormholeGlowStrokeAlpha(double pulse, bool animated) => SanshaIncursionStrokeAlpha(pulse, animated);

    private void DrawWormholeRippleRings(
        DrawingContext context, Point center, Color color, bool isHub, double zoomT, double s)
    {
        int ringCount = zoomT < 0.75 ? 2 : zoomT < 0.95 ? 1 : 0;
        for (int ring = 0; ring < ringCount; ring++)
        {
            double phase = _wormholeAnimPhase + ring * 0.9;
            double wave = 0.5 + 0.5 * Math.Sin(phase);
            double radius = (Lerp(isHub ? 16.0 : 13.0, isHub ? 8.0 : 6.5, zoomT) + wave * Lerp(6.0, 1.5, zoomT)) * s;

            byte haloAlpha = (byte)(Lerp(isHub ? 62 : 52, isHub ? 24 : 18, zoomT) * (0.65 + wave * 0.35) * (1.0 - ring * 0.12));
            var halo = new SolidColorBrush(Color.FromArgb(haloAlpha, color.R, color.G, color.B));
            context.DrawEllipse(halo, null, center, radius * 0.75, radius * 0.75);

            byte strokeAlpha = (byte)(Lerp(isHub ? 175 : 155, isHub ? 65 : 48, zoomT) * (0.7 + wave * 0.3) * (1.0 - ring * 0.12));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(strokeAlpha, color.R, color.G, color.B)),
                Math.Max(1.2, Lerp(2.4, 1.3, zoomT) * s));
            context.DrawEllipse(null, pen, center, radius, radius);
        }

        if (zoomT >= 0.35)
        {
            double shimmer = 0.5 + 0.5 * Math.Sin(_wormholeAnimPhase * 1.4);
            byte alpha = (byte)(Lerp(32, 16, zoomT) * (0.75 + shimmer * 0.25));
            double radius = (Lerp(isHub ? 7.0 : 5.5, isHub ? 4.5 : 3.5, zoomT) + shimmer * 0.8) * s;
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            context.DrawEllipse(brush, null, center, radius, radius);
        }
    }

    private double WormholeHighlightScale(double zoomT)
    {
        double baseScale = Math.Max(_wideZoomHighlightScale, 0.35);
        return Math.Max(baseScale * Lerp(2.0, 1.0, zoomT), Lerp(0.95, 0.35, zoomT));
    }

    private double ComputeWormholeCloseZoomT() =>
        Math.Clamp((Math.Log(Math.Max(_zoom, RegionLabelOverlayMaxZoom)) - Math.Log(RegionLabelOverlayMaxZoom))
            / (Math.Log(22.0) - Math.Log(RegionLabelOverlayMaxZoom)), 0, 1);

    private static Color WormholeColor(WormholeHubKind kind) =>
        kind == WormholeHubKind.Thera ? TheraWormholeColor : TurnurWormholeColor;

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    /// <summary>
    /// Red pin and frame for the system chosen in the right-panel search box. On Schematic plates
    /// the pin sits above the plate and a red frame wraps the plate border so the highlight does
    /// not blend into the plate fill.
    /// </summary>
    private void DrawSearchedSystemHighlight(DrawingContext context, bool schematic)
    {
        if (_searchedSystemId is not int id || _map?.Get(id) is not { } system)
            return;

        double s = _wideZoomHighlightScale;
        var screen = WorldToScreen(Project(system));
        var markerPen = new Pen(SearchedSystemMarkerBrush, 3.5 * s);
        var pinOutline = new Pen(Brushes.White, 2.0 * s);

        if (schematic)
        {
            var rect = _lastPlateRects.TryGetValue(id, out var drawnRect)
                ? drawnRect
                : EstimateSchematicPlateRect(system, screen);

            double expand = 4.0 * s;
            var frame = new Rect(rect.X - expand, rect.Y - expand, rect.Width + expand * 2, rect.Height + expand * 2);
            context.DrawRectangle(SearchedSystemMarkerFill, markerPen, frame, 5, 5);

            double pinR = 7.0 * s;
            var pinCenter = new Point(rect.X + rect.Width / 2, rect.Y - pinR - 3.0 * s);
            context.DrawEllipse(SearchedSystemMarkerBrush, pinOutline, pinCenter, pinR, pinR);
            return;
        }

        double r = 10 * s;
        context.DrawEllipse(SearchedSystemMarkerBrush, pinOutline, screen, r, r);
    }

    /// <summary>
    /// Green outline for a system hovered on the linked Jump Range mini-map. Drawn last so it
    /// stays visible even when jump-range or selection styling would otherwise cover the plate border.
    /// </summary>
    private void DrawLinkedHoverHighlight(DrawingContext context, bool schematic)
    {
        if (_linkedHoveredSystemId is not int id || _map?.Get(id) is not { } system)
            return;

        double s = _wideZoomHighlightScale;
        var pen = new Pen(GateHighlightBrush, 2.8 * s);
        var screen = WorldToScreen(Project(system));

        if (schematic && _lastPlateRects.TryGetValue(id, out var rect))
        {
            double expand = 3.0 * s;
            var expanded = new Rect(rect.X - expand, rect.Y - expand, rect.Width + expand * 2, rect.Height + expand * 2);
            context.DrawRectangle(null, pen, expanded, 5, 5);
        }
        else
        {
            double r = system.Id == _selectedSystemId || system.Id == FromSystemId || system.Id == ToSystemId ? 5.0 : 2.4;
            context.DrawEllipse(null, pen, screen, (r + 4.0) * s, (r + 4.0) * s);
        }
    }

    /// <summary>
    /// Animated green outline on the jump-range origin when the tracked pilot is not in that system.
    /// </summary>
    private void DrawJumpOriginPulse(DrawingContext context, bool schematic)
    {
        if (_jumpRangeOriginSystemId is not int originId || _map?.Get(originId) is not { } system)
            return;
        if (_pilotSystemId == originId)
            return;

        double s = _wideZoomHighlightScale;
        double pulse = 0.5 + 0.5 * Math.Sin(_jumpOriginPulsePhase);
        byte alpha = (byte)(110 + pulse * 145);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0x22, 0xC5, 0x5E)), (2.0 + pulse * 2.5) * s);
        var screen = WorldToScreen(Project(system));

        if (schematic && _lastPlateRects.TryGetValue(originId, out var rect))
        {
            double expand = (2.0 + pulse * 4.0) * s;
            var expanded = new Rect(rect.X - expand, rect.Y - expand, rect.Width + expand * 2, rect.Height + expand * 2);
            context.DrawRectangle(null, pen, expanded, 5, 5);
        }
        else
        {
            double baseR = system.Id == _selectedSystemId || system.Id == FromSystemId || system.Id == ToSystemId ? 5.0 : 2.4;
            double r = (baseR + 4.0 + pulse * 5.0) * s;
            context.DrawEllipse(null, pen, screen, r, r);
        }
    }

    /// <summary>
    /// At close zoom on the main map, location beacons are painted before plates so halos stay in
    /// the background and crosshair ticks never obscure neighboring system names.
    /// </summary>
    private bool LocationBeaconsBehindPlates(bool schematic) =>
        !IsJumpRangeMiniMap && _zoom > (schematic ? DefaultSchematicZoom : DefaultStandardZoom);

    private void DrawAllLocationBeacons(DrawingContext context, bool schematic)
    {
        if (_pilotSystemId is int pilotId)
            DrawLocationBeaconForSystem(context, schematic, pilotId,
                PilotBeaconHalo, PilotBeaconRing, PilotBeaconCore);

        foreach (int cynoId in _cynoSystemIds)
            DrawLocationBeaconForSystem(context, schematic, cynoId,
                CynoBeaconHalo, CynoBeaconRing, CynoBeaconCore);

        foreach (int scId in _scSystemIds)
            DrawLocationBeaconForSystem(context, schematic, scId,
                ScBeaconHalo, ScBeaconRing, ScBeaconCore);
    }

    private void DrawLocationBeaconForSystem(DrawingContext context, bool schematic, int systemId,
        IBrush haloBrush, IBrush ringBrush, IBrush coreBrush)
    {
        if (_map?.Get(systemId) is not { } system)
            return;

        var screen = WorldToScreen(Project(system));
        Rect? plateRect = null;
        if (schematic)
        {
            plateRect = _lastPlateRects.TryGetValue(systemId, out var drawnRect)
                ? drawnRect
                : EstimateSchematicPlateRect(system, screen);
        }

        DrawLocationBeacon(context, screen, plateRect,
            haloBrush, ringBrush, coreBrush, _wideZoomHighlightScale);
    }

    /// <summary>
    /// Approximate schematic plate bounds for beacon sizing before plates are painted this frame.
    /// </summary>
    private Rect EstimateSchematicPlateRect(SolarSystem system, Point screen)
    {
        double plateScale = SchematicPlateLayoutPolicy.ComputeTargetPlateScale(
            SchematicPlateLayoutPolicy.ResolveTier(_zoom, ShowNpcKillLabels),
            _zoom,
            _wideZoomHighlightScale);

        double nameFont = SchematicCompactNameFontBase * plateScale;
        double padX = SchematicCompactPadXBase * plateScale;
        double padY = SchematicCompactPadYBase * plateScale;
        double minWidth = SchematicCompactMinWidthBase * plateScale;

        var nameText = MeasureText(system.Name, nameFont, Typeface.Default);
        double width = Math.Max(nameText.Width + padX * 2, minWidth);
        double height = padY * 2 + nameText.Height;
        return new Rect(screen.X - width / 2, screen.Y - height / 2, width, height);
    }

    /// <summary>
    /// Crosshair beacon for a live-tracked location (main pilot or cyno). Sized in constant screen
    /// pixels so it stays visible at any zoom level; grows to encircle an oversized Schematic plate.
    /// </summary>
    private static void DrawLocationBeacon(DrawingContext context, Point screen, Rect? plateRect,
        IBrush haloBrush, IBrush ringBrush, IBrush coreBrush, double scale = 1.0)
    {
        const double haloR = 17.0;
        const double ringR = 10.0;
        const double coreR = 4.5;
        const double tickGap = 3.0;
        const double tickLen = 6.0;

        double ringRadius = ringR * scale;
        if (plateRect is { } rect)
        {
            double halfDiagonal = Math.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) / 2;
            ringRadius = Math.Max(ringR * scale, halfDiagonal + 5.0 * scale);
        }
        double haloRadius = haloR * scale + (ringRadius - ringR * scale);

        context.DrawEllipse(haloBrush, null, screen, haloRadius, haloRadius);
        context.DrawEllipse(null, new Pen(ringBrush, 2.2 * scale), screen, ringRadius, ringRadius);

        var tickPen = new Pen(Brushes.Black, 1.4 * scale);
        context.DrawLine(tickPen, new Point(screen.X - ringRadius - tickGap * scale - tickLen * scale, screen.Y), new Point(screen.X - ringRadius - tickGap * scale, screen.Y));
        context.DrawLine(tickPen, new Point(screen.X + ringRadius + tickGap * scale, screen.Y), new Point(screen.X + ringRadius + tickGap * scale + tickLen * scale, screen.Y));
        context.DrawLine(tickPen, new Point(screen.X, screen.Y - ringRadius - tickGap * scale - tickLen * scale), new Point(screen.X, screen.Y - ringRadius - tickGap * scale));
        context.DrawLine(tickPen, new Point(screen.X, screen.Y + ringRadius + tickGap * scale), new Point(screen.X, screen.Y + ringRadius + tickGap * scale + tickLen * scale));

        context.DrawEllipse(coreBrush, new Pen(Brushes.White, 1.4 * scale), screen, coreR * scale, coreR * scale);
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
