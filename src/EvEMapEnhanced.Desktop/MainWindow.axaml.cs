using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EvEMapEnhanced.Core.Auth;
using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using EvEMapEnhanced.Core.Structures;
using EvEMapEnhanced.Data.Auth;

namespace EvEMapEnhanced.Desktop;

public partial class MainWindow : Window
{
    private readonly AppServices _services = new();

    private List<AuthenticatedCharacter> _characters = new();
    private List<UserStructure> _structures = new();
    private List<RouteStep>? _lastRouteSteps;
    private CancellationTokenSource? _locationPollCts;

    public MainWindow()
    {
        InitializeComponent();
        PopulateStaticLookups();
        RouteMap.RouteFromRequested += OnMapRouteFromRequested;
        RouteMap.RouteToRequested += OnMapRouteToRequested;
        RouteMap.RouteContextProvider = () => (GetSelectedHull(), GetSelectedRouteSkills(), GetSelectedJumpMethod());
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

        LoadCharacters();
        LoadStructuresList();
        _ = RefreshNpcKillsLoopAsync();
        _ = RefreshAllCharacterSkillsLoopAsync();
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
    }

    // ============================================================
    // Route planning
    // ============================================================

    private RouteFilterOptions BuildRouteFilterOptions() => new()
    {
        Preference = RoutePreferenceCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<GateRoutePreference>(tag, out var pref)
            ? pref
            : GateRoutePreference.Shorter,
    };

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

    // ============================================================
    // ESI-authenticated pilots
    // ============================================================

    /// <summary>
    /// True while <see cref="RefreshPilotCombo"/> is rebuilding the combo's items, so the
    /// intermediate selection changes that causes (e.g. dropping to no selection during
    /// <c>Items.Clear()</c>) don't get individually persisted as "the active pilot" via
    /// <see cref="OnPilotSelectionChanged"/> -- only the final, deliberate selection is.
    /// </summary>
    private bool _isRefreshingPilotCombo;

    /// <summary>
    /// Reloads the signed-in character list. With no explicit <paramref name="preferredId"/>,
    /// restores whichever pilot was last active (persisted across restarts) instead of
    /// defaulting to "no pilot" -- so a previously-authenticated user never has to re-sign-in or
    /// re-pick their pilot just because the app was restarted.
    /// </summary>
    private void LoadCharacters(long? preferredId = null)
    {
        _characters = _services.LoadCharacters().ToList();
        RefreshPilotCombo(preferredId ?? _services.Characters.GetActiveCharacterId());
    }

    private void RefreshPilotCombo(long? selectId = null)
    {
        _isRefreshingPilotCombo = true;
        try
        {
            long? wantId = selectId ?? (PilotCombo.SelectedItem is ComboBoxItem { Tag: long id } ? id : null);

            PilotCombo.Items.Clear();
            PilotCombo.Items.Add(new ComboBoxItem { Content = "(нет, дальность по базовым навыкам)", Tag = null });
            foreach (var character in _characters)
            {
                PilotCombo.Items.Add(new ComboBoxItem { Content = character.Name, Tag = character.CharacterId });
            }

            int indexToSelect = 0;
            if (wantId is long id2)
            {
                for (int i = 1; i < PilotCombo.Items.Count; i++)
                {
                    if (PilotCombo.Items[i] is ComboBoxItem { Tag: long tagId } && tagId == id2) { indexToSelect = i; break; }
                }
            }
            PilotCombo.SelectedIndex = indexToSelect;
        }
        finally
        {
            _isRefreshingPilotCombo = false;
        }

        // Persist whichever pilot the combo actually landed on (including "none" if the
        // requested one wasn't found, e.g. it was just signed out) so the next launch matches.
        _services.Characters.SetActiveCharacterId(GetActiveCharacter()?.CharacterId);
    }

    private AuthenticatedCharacter? GetActiveCharacter() =>
        PilotCombo.SelectedItem is ComboBoxItem { Tag: long characterId }
            ? _characters.FirstOrDefault(c => c.CharacterId == characterId)
            : null;

    private PilotSkills GetSelectedRouteSkills() => GetActiveCharacter()?.Skills ?? new PilotSkills();

    private void OnPilotSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isRefreshingPilotCombo)
        {
            _services.Characters.SetActiveCharacterId(GetActiveCharacter()?.CharacterId);
        }

        if (JumpRangeOnlineCheck?.IsChecked == true)
        {
            RestartLocationPollingForActiveCharacter();
        }
    }

    private async void OnSignInClick(object? sender, RoutedEventArgs e)
    {
        var settings = EsiAuthConfig.TryLoad();
        if (settings is null)
        {
            RouteSummaryText.Text = $"ESI Client ID не настроен. Создайте файл \"{EsiAuthConfig.ConfigPath}\" с Client ID вашего приложения на developers.eveonline.com и попробуйте снова.";
            return;
        }

        SignInButton.IsEnabled = false;
        RouteSummaryText.Text = "Открываю браузер для входа через EVE Online...";
        try
        {
            var character = await _services.SignInWithEveOnlineAsync(settings);
            LoadCharacters(character.CharacterId);
            RouteSummaryText.Text = $"Выполнен вход как {character.Name}.";
        }
        catch (Exception ex)
        {
            RouteSummaryText.Text = $"Ошибка входа: {ex.Message}";
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void OnSignOutClick(object? sender, RoutedEventArgs e)
    {
        var character = GetActiveCharacter();
        if (character is null) return;

        StopLocationPolling();
        _services.SignOutCharacter(character.CharacterId);
        LoadCharacters();
    }

    private async void OnRefreshSkillsClick(object? sender, RoutedEventArgs e)
    {
        var character = GetActiveCharacter();
        var settings = EsiAuthConfig.TryLoad();
        if (character is null || settings is null) return;

        RefreshSkillsButton.IsEnabled = false;
        try
        {
            var skills = await _services.RefreshCharacterSkillsAsync(character.CharacterId, settings);
            character.Skills = skills;
            character.SkillsUpdatedUtc = DateTime.UtcNow;
            RouteSummaryText.Text = $"Навыки обновлены для {character.Name}.";
        }
        catch (Exception ex)
        {
            RouteSummaryText.Text = $"Не удалось обновить навыки: {ex.Message}";
        }
        finally
        {
            RefreshSkillsButton.IsEnabled = true;
        }
    }

    /// <summary>Periodically refreshes skills for every signed-in character so trained levels stay current without requiring a manual click.</summary>
    private async Task RefreshAllCharacterSkillsLoopAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(30));

            var settings = EsiAuthConfig.TryLoad();
            if (settings is null) continue;

            foreach (var character in _characters.ToList())
            {
                try
                {
                    var skills = await _services.RefreshCharacterSkillsAsync(character.CharacterId, settings);
                    character.Skills = skills;
                    character.SkillsUpdatedUtc = DateTime.UtcNow;
                }
                catch
                {
                    // Offline / token expired -- keep last-known skills, retry next tick.
                }
            }
        }
    }

    // ============================================================
    // Live "follow pilot" location tracking (task 7)
    // ============================================================

    private void OnJumpRangeOnlineToggled(object? sender, RoutedEventArgs e)
    {
        if (JumpRangeOnlineCheck.IsChecked == true)
        {
            RestartLocationPollingForActiveCharacter();
        }
        else
        {
            StopLocationPolling();
            RouteMap.SelectSystemExternally(null);
        }
    }

    private void RestartLocationPollingForActiveCharacter()
    {
        StopLocationPolling();

        var character = GetActiveCharacter();
        var settings = EsiAuthConfig.TryLoad();
        RouteMap.SelectSystemExternally(character?.LastKnownSystemId);

        if (character is null || settings is null) return;

        var cts = new CancellationTokenSource();
        _locationPollCts = cts;
        _ = PollLocationLoopAsync(character.CharacterId, settings, cts.Token);
    }

    private void StopLocationPolling()
    {
        _locationPollCts?.Cancel();
        _locationPollCts = null;
    }

    private async Task PollLocationLoopAsync(long characterId, EsiAuthSettings settings, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int systemId = await _services.RefreshCharacterLocationAsync(characterId, settings, ct);
                Dispatcher.UIThread.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    var character = _characters.FirstOrDefault(c => c.CharacterId == characterId);
                    if (character is not null) character.LastKnownSystemId = systemId;
                    if (GetActiveCharacter()?.CharacterId == characterId)
                    {
                        RouteMap.SelectSystemExternally(systemId);
                    }
                });
            }
            catch
            {
                // Offline / ESI hiccup -- keep the last known location and retry on the next tick.
            }

            try { await Task.Delay(TimeSpan.FromSeconds(45), ct); }
            catch (TaskCanceledException) { break; }
        }
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
    // NPC-kill map coloring
    // ============================================================

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
