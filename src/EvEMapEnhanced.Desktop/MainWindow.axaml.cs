using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using EvEMapEnhanced.Core.Structures;
using EvEMapEnhanced.Data.Stats;

namespace EvEMapEnhanced.Desktop;

public partial class MainWindow : Window
{
    private readonly AppServices _services = new();

    private List<PilotProfile> _profiles = new();
    private PilotProfile? _currentEditingProfile;
    private List<UserStructure> _structures = new();
    private List<RouteStep>? _lastRouteSteps;

    public MainWindow()
    {
        InitializeComponent();
        PopulateStaticLookups();
        RouteMap.RouteFromRequested += OnMapRouteFromRequested;
        RouteMap.RouteToRequested += OnMapRouteToRequested;
        RouteMap.PilotLocationSetRequested += OnMapPilotLocationSetRequested;
        RouteMap.RouteContextProvider = () => (GetSelectedHull(), GetSelectedRouteSkills(), GetSelectedJumpMethod());
        RouteMap.StatsProvider = id => _services.StatsCache.Get(id);
        RouteMap.RegionNameProvider = id => _services.RegionNames?.GetValueOrDefault(id);
        RouteMap.NpcKillsProvider = id => _services.NpcKills?.GetValueOrDefault(id);
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        RefreshSdeStatus();

        if (_services.SdeService.IsCached())
        {
            try
            {
                _services.ReloadMapFromCache();
                RefreshSystemNameLookups();
                if (_services.Map is not null) RouteMap.SetMap(_services.Map);
            }
            catch (Exception ex)
            {
                SdeStatusText.Text = $"ошибка загрузки кэша: {ex.Message}";
            }
        }

        LoadProfiles();
        LoadStructuresList();
        _ = RefreshNpcKillsLoopAsync();
        await Task.CompletedTask;
    }

    // ============================================================
    // Static lookups (ship classes, structure kinds) - independent of SDE.
    // ============================================================

    private void PopulateStaticLookups()
    {
        foreach (var shipClass in Enum.GetValues<CapitalShipClass>())
        {
            ShipClassCombo.Items.Add(new ComboBoxItem { Content = shipClass.ToString(), Tag = shipClass });
        }
        ShipClassCombo.SelectedIndex = 0;
        PopulateShipHullsForClass((CapitalShipClass)((ComboBoxItem)ShipClassCombo.SelectedItem!).Tag!);

        foreach (var kind in Enum.GetValues<StructureKind>())
        {
            StructureKindCombo.Items.Add(new ComboBoxItem { Content = kind.ToString(), Tag = kind });
        }
        StructureKindCombo.SelectedIndex = 0;
        UpdateLinkedSystemVisibility((StructureKind)((ComboBoxItem)StructureKindCombo.SelectedItem!).Tag!);

        JumpRangeClassCombo.Items.Add(new ComboBoxItem { Content = "Свой корабль (вкладка Маршрут)", Tag = null });
        foreach (var shipClass in Enum.GetValues<CapitalShipClass>())
        {
            JumpRangeClassCombo.Items.Add(new ComboBoxItem { Content = shipClass.ToRussianLabel(), Tag = shipClass });
        }
        JumpRangeClassCombo.SelectedIndex = 0;
    }

    private void OnJumpRangeClassChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RouteMap is null) return;
        if (JumpRangeClassCombo.SelectedItem is ComboBoxItem item)
        {
            RouteMap.JumpRangeShipClass = item.Tag as CapitalShipClass?;
        }
    }

    private void OnJumpRangeOnlineToggled(object? sender, RoutedEventArgs e)
    {
        if (JumpRangeOnlineCheck.IsChecked == true)
        {
            var profile = GetActiveRouteProfile();
            RouteMap.SelectSystemExternally(profile?.CurrentSystemId);
        }
    }

    /// <summary>The pilot profile currently selected in the Route tab's "Pilot profile" combo.</summary>
    private PilotProfile? GetActiveRouteProfile() =>
        ProfileCombo.SelectedItem is ComboBoxItem { Tag: int profileId }
            ? _profiles.FirstOrDefault(p => p.Id == profileId)
            : null;

    /// <summary>
    /// If the "online" jump-range toggle is on and the given profile is the one currently
    /// active on the Route tab, moves the map's jump-range overlay to its current location.
    /// </summary>
    private void ApplyOnlineJumpRangeIfActive(PilotProfile profile)
    {
        if (JumpRangeOnlineCheck.IsChecked != true) return;
        if (GetActiveRouteProfile()?.Id != profile.Id) return;
        RouteMap.SelectSystemExternally(profile.CurrentSystemId);
    }

    private void OnMapPilotLocationSetRequested(int systemId)
    {
        var profile = GetActiveRouteProfile() ?? _currentEditingProfile;
        if (profile is null) return;

        profile.CurrentSystemId = systemId;
        _services.PilotProfiles.Save(profile);

        if (_currentEditingProfile?.Id == profile.Id)
        {
            ProfileCurrentSystemBox.Text = _services.Map?.Get(systemId)?.Name;
        }
    }

    private void OnUpdatePilotLocationClick(object? sender, RoutedEventArgs e)
    {
        if (_currentEditingProfile is null || _services.Map is null) return;

        var system = _services.Map.FindByName(ProfileCurrentSystemBox.Text ?? string.Empty);
        if (system is null) return;

        _currentEditingProfile.CurrentSystemId = system.Id;
        _services.PilotProfiles.Save(_currentEditingProfile);
        ApplyOnlineJumpRangeIfActive(_currentEditingProfile);
    }

    private void OnShipClassChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ShipClassCombo.SelectedItem is ComboBoxItem item && item.Tag is CapitalShipClass shipClass)
        {
            PopulateShipHullsForClass(shipClass);
        }
    }

    private void PopulateShipHullsForClass(CapitalShipClass shipClass)
    {
        ShipHullCombo.Items.Clear();

        // "Command Carrier" is an operational role flown on Carrier/FAX hulls, not a distinct hull.
        var hulls = shipClass == CapitalShipClass.CommandCarrier
            ? ShipHulls.ByClass(CapitalShipClass.Carrier).Concat(ShipHulls.ByClass(CapitalShipClass.ForceAuxiliary))
            : ShipHulls.ByClass(shipClass);

        foreach (var hull in hulls)
        {
            ShipHullCombo.Items.Add(new ComboBoxItem { Content = $"{hull.Name} ({hull.Faction})", Tag = hull });
        }

        ShipHullCombo.IsEnabled = ShipHullCombo.Items.Count > 0;
        if (ShipHullCombo.Items.Count > 0) ShipHullCombo.SelectedIndex = 0;
    }

    private JumpMethod GetSelectedJumpMethod() =>
        JumpMethodCombo.SelectedIndex == 1 ? JumpMethod.CovertCyno : JumpMethod.Cyno;

    private ShipHull? GetSelectedHull() =>
        ShipHullCombo.SelectedItem is ComboBoxItem { Tag: ShipHull hull } ? hull : null;

    private void OnStructureKindChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (StructureKindCombo.SelectedItem is ComboBoxItem item && item.Tag is StructureKind kind)
        {
            UpdateLinkedSystemVisibility(kind);
        }
    }

    private void UpdateLinkedSystemVisibility(StructureKind kind)
    {
        bool needsLink = kind.IsJumpEdge();
        LinkedSystemLabel.IsVisible = needsLink;
        StructureLinkedSystemBox.IsVisible = needsLink;
    }

    // ============================================================
    // SDE download / status
    // ============================================================

    private void RefreshSdeStatus()
    {
        if (_services.IsMapLoaded && _services.Map is not null)
        {
            SdeStatusText.Text = $"загружен, систем: {_services.Map.Systems.Count}";
        }
        else if (_services.SdeService.IsCached())
        {
            SdeStatusText.Text = "закэширован (не загружен в память)";
        }
        else
        {
            SdeStatusText.Text = "не загружен - нажмите \"Скачать / обновить SDE\"";
        }
    }

    private async void OnDownloadSdeClick(object? sender, RoutedEventArgs e)
    {
        DownloadSdeButton.IsEnabled = false;
        SdeProgressBar.IsVisible = true;
        SdeProgressBar.Value = 0;
        SdeStatusText.Text = "скачивание...";

        try
        {
            var progress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() => SdeProgressBar.Value = p));

            var summary = await _services.SdeService.DownloadAndImportAsync(progress);
            _services.ReloadMapFromCache();
            RefreshSystemNameLookups();
            if (_services.Map is not null) RouteMap.SetMap(_services.Map);
            SdeStatusText.Text = $"обновлён: регионов={summary.Regions}, систем={summary.SolarSystems}, стargate-пар={summary.Stargates}, типов кораблей={summary.ShipTypesResolved}";
        }
        catch (Exception ex)
        {
            SdeStatusText.Text = $"ошибка скачивания: {ex.Message}";
        }
        finally
        {
            DownloadSdeButton.IsEnabled = true;
            SdeProgressBar.IsVisible = false;
        }
    }

    private void RefreshSystemNameLookups()
    {
        if (_services.Map is null) return;

        var names = _services.Map.Systems.Values.Select(s => s.Name).OrderBy(n => n).ToList();
        RouteFromBox.ItemsSource = names;
        RouteToBox.ItemsSource = names;
        StructureSystemBox.ItemsSource = names;
        StructureLinkedSystemBox.ItemsSource = names;
        StatsSystemBox.ItemsSource = names;
        ProfileCurrentSystemBox.ItemsSource = names;

        RefreshShipRangePreview();
    }

    // ============================================================
    // Route planning
    // ============================================================

    private PilotSkills GetSelectedRouteSkills()
    {
        if (ProfileCombo.SelectedItem is ComboBoxItem { Tag: int profileId })
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is not null) return profile.Skills;
        }
        return new PilotSkills();
    }

    private RouteFilterOptions BuildRouteFilterOptions()
    {
        var options = new RouteFilterOptions
        {
            AvoidLowSec = AvoidLowSecCheck.IsChecked == true,
            AvoidNullSec = AvoidNullSecCheck.IsChecked == true,
            Preference = RoutePreferenceCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<GateRoutePreference>(tag, out var pref)
                ? pref
                : GateRoutePreference.Shorter,
        };

        if (ProfileCombo.SelectedItem is ComboBoxItem { Tag: int profileId })
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is not null)
            {
                options.AvoidSystemIds = new HashSet<int>(profile.AvoidSystemIds);
            }
        }

        if (AvoidActivityCheck.IsChecked == true)
        {
            options.SystemPenalty = systemId => _services.StatsCache.Get(systemId)?.ActivityScore ?? 0.0;
        }

        return options;
    }

    private void OnMapModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RouteMap is null || MapModeCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (!Enum.TryParse<MapDisplayMode>(tag, out var mode)) return;
        RouteMap.DisplayMode = mode;
    }

    private void OnMapRouteFromRequested(int systemId)
    {
        var name = _services.Map?.Get(systemId)?.Name;
        if (name is null) return;
        RouteFromBox.Text = name;
        RouteMap.FromSystemId = systemId;
        RouteMap.InvalidateVisual();
        TryAutoBuildRouteFromMap();
    }

    private void OnMapRouteToRequested(int systemId)
    {
        var name = _services.Map?.Get(systemId)?.Name;
        if (name is null) return;
        RouteToBox.Text = name;
        RouteMap.ToSystemId = systemId;
        RouteMap.InvalidateVisual();
        TryAutoBuildRouteFromMap();
    }

    private void TryAutoBuildRouteFromMap()
    {
        if (RouteMap.FromSystemId is not null && RouteMap.ToSystemId is not null)
        {
            BuildRoute();
        }
    }

    private void OnBuildRouteClick(object? sender, RoutedEventArgs e) => BuildRoute();

    private void BuildRoute()
    {
        if (_services.Map is null)
        {
            RouteSummaryText.Text = "Сначала скачайте SDE.";
            return;
        }

        var map = _services.Map;
        var from = map.FindByName(RouteFromBox.Text ?? string.Empty);
        var to = map.FindByName(RouteToBox.Text ?? string.Empty);
        if (from is null || to is null)
        {
            RouteSummaryText.Text = "Система отправления или назначения не найдена.";
            return;
        }

        int mode = RouteModeCombo.SelectedIndex;
        var skills = GetSelectedRouteSkills();
        var options = BuildRouteFilterOptions();
        var method = JumpMethodCombo.SelectedIndex == 1 ? JumpMethod.CovertCyno : JumpMethod.Cyno;
        var hull = GetSelectedHull();

        List<RouteStep>? steps = null;
        RouteSimulationResult? simulation = null;

        try
        {
            switch (mode)
            {
                case 0: // Gate only
                {
                    var gateRoute = GatePathfinder.FindRoute(map, from.Id, to.Id, options);
                    if (gateRoute is not null) steps = gateRoute.ToSteps().ToList();
                    break;
                }
                case 1: // Jump only
                {
                    if (hull is null)
                    {
                        RouteSummaryText.Text = "Выберите корабль для прыжкового маршрута.";
                        return;
                    }
                    var jumpRoute = JumpPathfinder.FindRoute(map, hull, skills, from.Id, to.Id, method, options);
                    if (jumpRoute is not null)
                    {
                        steps = jumpRoute.ToSteps().ToList();
                        simulation = RouteSimulator.SimulateJumpRoute(jumpRoute, hull, skills);
                    }
                    break;
                }
                default: // Hybrid
                {
                    if (hull is null)
                    {
                        RouteSummaryText.Text = "Выберите корабль для гибридного маршрута.";
                        return;
                    }
                    var combined = HybridRouter.FindRoute(map, hull, skills, from.Id, to.Id, method, options);
                    if (combined is not null) steps = combined.Steps.ToList();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            RouteSummaryText.Text = $"Ошибка построения маршрута: {ex.Message}";
            return;
        }

        RouteMap.FromSystemId = from.Id;
        RouteMap.ToSystemId = to.Id;

        if (steps is null)
        {
            RouteStepsList.ItemsSource = null;
            RouteSummaryText.Text = "Маршрут не найден.";
            _lastRouteSteps = null;
            RouteMap.RouteSteps = null;
            RouteMap.InvalidateVisual();
            return;
        }

        _lastRouteSteps = steps;
        RenderRouteSteps(map, steps, hull, skills);
        RenderRouteSummary(steps, simulation, hull, skills);

        RouteMap.RouteSteps = steps;
        RouteMap.FitToSystems(steps.SelectMany(s => new[] { s.FromSystemId, s.ToSystemId }).Append(from.Id).Append(to.Id));
        RouteMap.InvalidateVisual();
    }

    private void RenderRouteSteps(UniverseMap map, List<RouteStep> steps, ShipHull? hull, PilotSkills skills)
    {
        var lines = new List<string>();
        var state = JumpState.Fresh();

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var fromSys = map.Get(step.FromSystemId)!;
            var toSys = map.Get(step.ToSystemId)!;

            if (step.Kind == RouteStepKind.Gate)
            {
                lines.Add($"{i + 1}. {RouteDisplayFormat.SystemName(fromSys)} → {RouteDisplayFormat.SystemName(toSys)}  gate");
            }
            else
            {
                string methodLabel = RouteDisplayFormat.JumpMethodLabel(step.Method);
                string extra = "";
                if (hull is not null)
                {
                    var result = JumpSimulator.SimulateJump(hull, skills, step.Method ?? JumpMethod.Cyno, step.DistanceLy ?? 0, state);
                    string warn = result.WithinRange ? "" : "  OUT OF RANGE";
                    extra = $"  {result.IsotopesUsed:F0} fuel, {result.CooldownMinutes:F0}m cd{warn}";
                }
                lines.Add($"{i + 1}. {RouteDisplayFormat.SystemName(fromSys)} → {RouteDisplayFormat.SystemName(toSys)}  {methodLabel} {step.DistanceLy:F2} LY{extra}");
            }
        }

        RouteStepsList.ItemsSource = lines;
    }

    private void RenderRouteSummary(List<RouteStep> steps, RouteSimulationResult? simulation, ShipHull? hull, PilotSkills skills)
    {
        int gates = steps.Count(s => s.Kind == RouteStepKind.Gate);
        int jumps = steps.Count(s => s.Kind == RouteStepKind.Jump);

        string text = $"Итого: {gates} gate(s), {jumps} jump(s).";
        double jumpLy = steps.Where(s => s.DistanceLy.HasValue).Sum(s => s.DistanceLy!.Value);
        if (jumpLy > 0) text += $" Jump total: {jumpLy:F1} LY.";
        if (hull is not null && jumps > 0)
            text += $" Ship range: {JumpSimulator.MaxRangeLy(hull, skills):F1} LY.";
        if (simulation is not null)
        {
            text += $" Fuel: {simulation.TotalFuel:F0} isotopes. Peak fatigue: {simulation.PeakFatigueMinutes:F0} min.";
            if (simulation.AnyLegOutOfRange) text += " WARNING: some jumps out of range!";
        }
        else if (jumps > 0 && hull is not null)
        {
            var route = new JumpRoute(steps.Where(s => s.Kind == RouteStepKind.Jump)
                .Select(s => new JumpRouteLeg(s.FromSystemId, s.ToSystemId, s.DistanceLy ?? 0, s.Method ?? JumpMethod.Cyno)).ToList());
            var sim = RouteSimulator.SimulateJumpRoute(route, hull, skills);
            text += $" Fuel: {sim.TotalFuel:F0} isotopes. Peak fatigue: {sim.PeakFatigueMinutes:F0} min.";
            if (sim.AnyLegOutOfRange) text += " WARNING: some jumps out of range!";
        }

        RouteSummaryText.Text = text;
    }

    private void OnSaveRouteClick(object? sender, RoutedEventArgs e)
    {
        if (_lastRouteSteps is null || _lastRouteSteps.Count == 0 || _services.Map is null)
        {
            RouteSummaryText.Text = "Нечего сохранять - сначала постройте маршрут.";
            return;
        }

        var map = _services.Map;
        var fromName = map.Get(_lastRouteSteps[0].FromSystemId)?.Name ?? "?";
        var toName = map.Get(_lastRouteSteps[^1].ToSystemId)?.Name ?? "?";

        var saved = new SavedRoute
        {
            Name = $"{fromName} -> {toName} ({DateTime.Now:dd.MM.yyyy HH:mm})",
            Steps = _lastRouteSteps,
        };

        _services.SavedRoutes.Save(saved);
        RouteSummaryText.Text += $"  [Сохранено как \"{saved.Name}\"]";
    }

    private void OnProfileSelectionChangedForRoute(object? sender, SelectionChangedEventArgs e)
    {
        if (JumpRangeOnlineCheck?.IsChecked == true)
        {
            RouteMap.SelectSystemExternally(GetActiveRouteProfile()?.CurrentSystemId);
        }
    }

    private void OnJdcChanged(object? sender, NumericUpDownValueChangedEventArgs e) => RefreshShipRangePreview();

    private void RefreshShipRangePreview()
    {
        var jdc = (int)(JdcUpDown.Value ?? 0);
        var skills = new PilotSkills { JumpDriveCalibration = jdc };

        var lines = ShipHulls.All
            .GroupBy(h => h.ShipClass)
            .OrderBy(g => g.Key.ToString())
            .SelectMany(g => g.Select(h => $"{g.Key.ToRussianLabel()} / {h.Name}: {JumpSimulator.MaxRangeLy(h, skills):F1} LY (макс {h.Mechanics.MaxRangeLy:F1} LY)"))
            .ToList();

        ShipRangePreviewList.ItemsSource = lines;
    }

    // ============================================================
    // Pilot profiles
    // ============================================================

    private void LoadProfiles()
    {
        _profiles = _services.PilotProfiles.LoadAll().ToList();
        if (_profiles.Count == 0)
        {
            var defaultProfile = new PilotProfile();
            _services.PilotProfiles.Save(defaultProfile);
            _profiles.Add(defaultProfile);
        }

        RefreshProfileCombos(_profiles[0].Id);
    }

    private void RefreshProfileCombos(int selectId)
    {
        ProfileCombo.Items.Clear();
        ProfileEditorCombo.Items.Clear();

        int selectedIndex = 0;
        for (int i = 0; i < _profiles.Count; i++)
        {
            var profile = _profiles[i];
            ProfileCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
            ProfileEditorCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
            if (profile.Id == selectId) selectedIndex = i;
        }

        if (_profiles.Count > 0)
        {
            ProfileCombo.SelectedIndex = selectedIndex;
            ProfileEditorCombo.SelectedIndex = selectedIndex;
        }
    }

    private void OnProfileEditorSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProfileEditorCombo.SelectedItem is not ComboBoxItem { Tag: int profileId }) return;
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return;

        _currentEditingProfile = profile;
        ProfileNameBox.Text = profile.Name;
        JdcUpDown.Value = profile.Skills.JumpDriveCalibration;
        JfcUpDown.Value = profile.Skills.JumpFuelConservation;
        JfUpDown.Value = profile.Skills.JumpFreighters;
        CapitalShipsUpDown.Value = profile.Skills.CapitalShips;
        BlackOpsUpDown.Value = profile.Skills.BlackOps;
        EconomizerCombo.SelectedIndex = (int)profile.Skills.Economizer;
        ProfileAvoidLowSecCheck.IsChecked = profile.AvoidLowSec;
        ProfileAvoidNullSecCheck.IsChecked = profile.AvoidNullSec;
        ProfileAvoidActivityCheck.IsChecked = profile.AvoidRecentKillActivity;
        AvoidSystemsBox.Text = string.Join(",", profile.AvoidSystemIds);
        ProfileCurrentSystemBox.Text = profile.CurrentSystemId is int sysId ? _services.Map?.Get(sysId)?.Name : null;

        RefreshShipRangePreview();
    }

    private void OnNewProfileClick(object? sender, RoutedEventArgs e)
    {
        var profile = new PilotProfile { Name = "Новый профиль" };
        _services.PilotProfiles.Save(profile);
        _profiles.Add(profile);
        RefreshProfileCombos(profile.Id);
    }

    private void OnSaveProfileClick(object? sender, RoutedEventArgs e)
    {
        var profile = _currentEditingProfile ?? _profiles.FirstOrDefault();
        if (profile is null) return;

        profile.Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? profile.Name : ProfileNameBox.Text!;
        profile.Skills.JumpDriveCalibration = (int)(JdcUpDown.Value ?? 0);
        profile.Skills.JumpFuelConservation = (int)(JfcUpDown.Value ?? 0);
        profile.Skills.JumpFreighters = (int)(JfUpDown.Value ?? 0);
        profile.Skills.CapitalShips = (int)(CapitalShipsUpDown.Value ?? 0);
        profile.Skills.BlackOps = (int)(BlackOpsUpDown.Value ?? 0);
        profile.Skills.Economizer = (JumpDriveEconomizerTier)EconomizerCombo.SelectedIndex;
        profile.AvoidLowSec = ProfileAvoidLowSecCheck.IsChecked == true;
        profile.AvoidNullSec = ProfileAvoidNullSecCheck.IsChecked == true;
        profile.AvoidRecentKillActivity = ProfileAvoidActivityCheck.IsChecked == true;
        profile.AvoidSystemIds = ParseAvoidSystems(AvoidSystemsBox.Text);

        if (string.IsNullOrWhiteSpace(ProfileCurrentSystemBox.Text))
        {
            profile.CurrentSystemId = null;
        }
        else if (_services.Map?.FindByName(ProfileCurrentSystemBox.Text) is { } currentSystem)
        {
            profile.CurrentSystemId = currentSystem.Id;
        }

        _services.PilotProfiles.Save(profile);
        RefreshProfileCombos(profile.Id);
        RefreshShipRangePreview();
        ApplyOnlineJumpRangeIfActive(profile);
    }

    private HashSet<int> ParseAvoidSystems(string? text)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, out int id))
            {
                result.Add(id);
            }
            else if (_services.Map?.FindByName(token) is { } system)
            {
                result.Add(system.Id);
            }
        }
        return result;
    }

    private void OnDeleteProfileClick(object? sender, RoutedEventArgs e)
    {
        if (_currentEditingProfile is null || _profiles.Count <= 1) return;

        _services.PilotProfiles.Delete(_currentEditingProfile.Id);
        _profiles.Remove(_currentEditingProfile);
        _currentEditingProfile = null;
        RefreshProfileCombos(_profiles[0].Id);
    }

    // ============================================================
    // Structures
    // ============================================================

    private void LoadStructuresList()
    {
        _structures = _services.UserStructures.LoadAll().ToList();
        RenderStructuresList();
    }

    private void RenderStructuresList()
    {
        var map = _services.Map;
        var lines = _structures.Select(s =>
        {
            string sysName = map?.Get(s.SolarSystemId)?.Name ?? $"#{s.SolarSystemId}";
            string linked = s.LinkedSystemId is int linkedId ? $" <-> {map?.Get(linkedId)?.Name ?? $"#{linkedId}"}" : "";
            return $"{s.Kind}: {s.Name} @ {sysName}{linked}  [{s.Access}]";
        }).ToList();

        StructuresList.ItemsSource = lines;
    }

    private void OnAddStructureClick(object? sender, RoutedEventArgs e)
    {
        if (_services.Map is null)
        {
            return;
        }
        var map = _services.Map;

        var system = map.FindByName(StructureSystemBox.Text ?? string.Empty);
        if (system is null) return;

        if (StructureKindCombo.SelectedItem is not ComboBoxItem { Tag: StructureKind kind }) return;

        int? linkedId = null;
        if (kind.IsJumpEdge())
        {
            var linked = map.FindByName(StructureLinkedSystemBox.Text ?? string.Empty);
            if (linked is null) return;
            linkedId = linked.Id;
        }

        var structure = new UserStructure
        {
            SolarSystemId = system.Id,
            Kind = kind,
            Name = string.IsNullOrWhiteSpace(StructureNameBox.Text) ? kind.ToString() : StructureNameBox.Text!,
            OwnerTag = string.IsNullOrWhiteSpace(StructureOwnerBox.Text) ? null : StructureOwnerBox.Text,
            Access = (StructureAccessLevel)StructureAccessCombo.SelectedIndex,
            LinkedSystemId = linkedId,
            StrontHours = StrontUpDown.Value is { } stront && stront > 0 ? (double)stront : null,
            Notes = string.IsNullOrWhiteSpace(StructureNotesBox.Text) ? null : StructureNotesBox.Text,
        };

        _services.UserStructures.Save(structure);
        _structures.Add(structure);
        _services.ReloadStructuresOnly();
        RenderStructuresList();
        RouteMap.InvalidateVisual();
    }

    private void OnDeleteStructureClick(object? sender, RoutedEventArgs e)
    {
        int index = StructuresList.SelectedIndex;
        if (index < 0 || index >= _structures.Count) return;

        var structure = _structures[index];
        _services.UserStructures.Delete(structure.Id);
        _structures.RemoveAt(index);
        _services.ReloadStructuresOnly();
        RenderStructuresList();
        RouteMap.InvalidateVisual();
    }

    // ============================================================
    // System stats
    // ============================================================

    private async void OnStatsQueryClick(object? sender, RoutedEventArgs e)
    {
        if (_services.Map is null)
        {
            StatsResultText.Text = "Сначала скачайте SDE.";
            return;
        }

        var system = _services.Map.FindByName(StatsSystemBox.Text ?? string.Empty);
        if (system is null)
        {
            StatsResultText.Text = "Система не найдена.";
            return;
        }

        StatsQueryButton.IsEnabled = false;
        StatsResultText.Text = "Запрос...";

        try
        {
            var service = new SystemStatsService(new ZkillClient(), new EsiKillmailClient(), _services.ShipCatalog);
            var stats = await service.ComputeAsync(system.Id);
            _services.StatsCache.Upsert(stats);

            StatsResultText.Text =
                $"Система: {system.Name} (sec {system.Security:F1})\n" +
                $"Убийств за 1ч: {stats.KillsLastHour}\n" +
                $"Убийств за 24ч: {stats.KillsLast24H}\n" +
                $"  из них капитальных (по первым {service.HydrateTopN}): {stats.CapitalKillsLast24H}\n" +
                $"  из них капсул (по первым {service.HydrateTopN}): {stats.PodKillsLast24H}\n" +
                $"ISK уничтожено за 24ч: {stats.IskDestroyedLast24H:N0}\n" +
                $"Activity score (штраф маршрутизации): {stats.ActivityScore:F1}";
        }
        catch (Exception ex)
        {
            var cached = _services.StatsCache.Get(system.Id);
            StatsResultText.Text = cached is not null
                ? $"Не удалось обновить ({ex.Message}). Последние известные данные: Activity score {cached.ActivityScore:F1}, обновлено {cached.LastUpdatedUtc:g} UTC."
                : $"Ошибка запроса статистики: {ex.Message}";
        }
        finally
        {
            StatsQueryButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Keeps the schematic map's Dotlan-style "NPC Kills" plate coloring fresh: fetches once at
    /// startup, then re-fetches periodically (ESI's system_kills feed itself only updates
    /// hourly, so there's no benefit polling faster). Runs for the lifetime of the window.
    /// </summary>
    private async Task RefreshNpcKillsLoopAsync()
    {
        while (true)
        {
            await _services.RefreshNpcKillsAsync();
            Dispatcher.UIThread.Post(() => RouteMap.InvalidateVisual());
            await Task.Delay(TimeSpan.FromMinutes(15));
        }
    }
}
