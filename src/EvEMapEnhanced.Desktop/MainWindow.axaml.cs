using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EvEMapEnhanced.Core.Auth;
using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Stats;
using EvEMapEnhanced.Core.Ships;
using EvEMapEnhanced.Data.Auth;

namespace EvEMapEnhanced.Desktop;

public partial class MainWindow : Window
{
    private readonly AppServices _services = new();

    private List<AuthenticatedCharacter> _characters = new();
    private CancellationTokenSource? _locationPollCts;
    private readonly Dictionary<long, CancellationTokenSource> _cynoPollCtsByCharacter = new();
    private readonly Dictionary<long, CancellationTokenSource> _scPollCtsByCharacter = new();
    private CancellationTokenSource? _pvpRefreshCts;
    private CancellationTokenSource? _pvpDebounceCts;
    private HashSet<int>? _pendingPvpSystems;
    private int _pvpRefreshGeneration;
    private int _shipTypeAutoSelectGeneration;
    private bool _isApplyingDetectedShip;

    public MainWindow()
    {
        InitializeComponent();
        PopulateStaticLookups();
        SyncZKillboardRequestModeMenu();
        SyncZKillboardScopeMenu();
        SyncShowEveScoutWormholesMenu();
        RouteMap.PvPScope = _services.ZKillboardScope;
        RouteMap.ShowEveScoutWormholes = _services.ShowEveScoutWormholes;
        RouteMap.RouteFromRequested += OnMapRouteFromRequested;
        RouteMap.RouteToRequested += OnMapRouteToRequested;
        RouteMap.ZKillboardOpenRequested += OnOpenZKillboardSystem;
        RouteMap.RouteContextProvider = () => (GetSelectedHull(), GetSelectedRouteSkills(), GetSelectedJumpMethod());
        RouteMap.RegionNameProvider = id => _services.RegionNames?.GetValueOrDefault(id);
        RouteMap.IhubAllianceProvider = id => _services.IhubAllianceBySystem.GetValueOrDefault(id);
        RouteMap.CharactersInSystemProvider = GetTrackedCharactersInSystem;
        RouteMap.NpcKillsProvider = id => _services.NpcKills?.GetValueOrDefault(id);
        RouteMap.HasNpcStationProvider = id => _services.NpcStationSystems.Contains(id);
        RouteMap.PvPActivityProvider = id => _services.JumpRangePvPActivity.GetValueOrDefault(id);
        RouteMap.SanshaIncursionProvider = id => _services.SanshaIncursionSystems.Contains(id);
        RouteMap.WormholeConnectionsProvider = id =>
            _services.EveScoutWormholesBySystem.TryGetValue(id, out var connections)
                ? connections
                : Array.Empty<WormholeConnection>();
        RouteMap.JumpRangeOriginChanged += OnRouteMapJumpRangeOriginChanged;
        RouteMap.JumpReachabilityChanged += OnJumpReachabilityChanged;
        RouteMap.JumpRangeSimulationExhausted += OnJumpRangeSimulationExhausted;
        RouteMap.ZoomLevelChanged += OnRouteMapZoomLevelChanged;
        SyncMapZoomSliderFromMap();
        JumpRangeMiniMap.JumpReachabilityChanged += OnJumpReachabilityChanged;

        // Jump Range mini-map: always Standard mode (true-to-scale) and always shows Black Ops
        // range specifically, regardless of whatever ship class the main map's "Дальность
        // прыжка" combo is set to -- Black Ops is the class actually used to scout/plan chained
        // covert-cyno jumps, which is what this panel exists for.
        JumpRangeMiniMap.DisplayMode = MapDisplayMode.Standard;
        JumpRangeMiniMap.JumpRangeShipClass = CapitalShipClass.BlackOps;
        JumpRangeMiniMap.ShowHoverTooltips = true;
        JumpRangeMiniMap.RegionNameProvider = id => _services.RegionNames?.GetValueOrDefault(id);
        JumpRangeMiniMap.IhubAllianceProvider = id => _services.IhubAllianceBySystem.GetValueOrDefault(id);
        JumpRangeMiniMap.CharactersInSystemProvider = GetTrackedCharactersInSystem;
        JumpRangeMiniMap.HoveredSystemChanged += OnJumpRangeMiniMapHoverChanged;
        JumpRangeMiniMap.RouteFromRequested += OnMapRouteFromRequested;
        JumpRangeMiniMap.RouteToRequested += OnMapRouteToRequested;
        JumpRangeMiniMap.ZKillboardOpenRequested += OnOpenZKillboardSystem;
        JumpRangeMiniMap.RouteContextProvider = () => (GetSelectedHull(), GetSelectedRouteSkills(), GetSelectedJumpMethod());

        CynoProfileSelector.Configure("(нет)", 170);
        CynoProfileSelector.SelectionChanged += OnCynoProfileSelectionChanged;
        ScProfileSelector.Configure("(нет)", 170);
        ScProfileSelector.SelectionChanged += OnScProfileSelectionChanged;

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
                if (_services.Map is not null)
                {
                    RouteMap.SetMap(_services.Map);
                    JumpRangeMiniMap.SetMap(_services.Map);
                    TriggerPvpRefresh();
                    _ = EnsureNpcStationDataAsync();
                }
            }
            catch (Exception ex)
            {
                ShowStatusToast($"SDE: ошибка загрузки кэша: {ex.Message}", ToastKind.Warning);
            }
        }

        if (!_services.IsMapLoaded)
            RefreshSdeStatus(notify: true);

        LoadCharacters();
        _ = RefreshNpcKillsLoopAsync();
        _ = RefreshSanshaIncursionsLoopAsync();
        _ = RefreshEveScoutWormholesLoopAsync();
        _ = RefreshIhubAlliancesLoopAsync();
        _ = RefreshAllCharacterSkillsLoopAsync();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Backfills NPC-station data for caches imported before that table existed, then repaints so
    /// the gold station markers appear without the user having to re-download the SDE.
    /// </summary>
    private async Task EnsureNpcStationDataAsync()
    {
        try
        {
            if (await _services.EnsureNpcStationDataAsync())
            {
                RouteMap.InvalidateVisual();
                JumpRangeMiniMap.InvalidateVisual();
            }
        }
        catch
        {
            // Best-effort backfill; a full "Обновить SDE" still repopulates station data.
        }
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

        JumpRangeClassCombo.Items.Add(new ComboBoxItem { Content = "Свой корабль", Tag = null });
        foreach (var shipClass in Enum.GetValues<CapitalShipClass>())
        {
            JumpRangeClassCombo.Items.Add(new ComboBoxItem { Content = shipClass.ToDisplayLabel(), Tag = shipClass });
        }

        RouteMap.JumpRangeShipClass = CapitalShipClass.BlackOps;
        JumpRangeClassCombo.SelectedIndex = Array.IndexOf(Enum.GetValues<CapitalShipClass>(), CapitalShipClass.BlackOps) + 1;
    }

    private void OnJumpRangeClassChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RouteMap is null) return;
        if (JumpRangeClassCombo.SelectedItem is ComboBoxItem item)
        {
            RouteMap.JumpRangeShipClass = item.Tag as CapitalShipClass?;
        }
    }

    private void SetJumpRangeShipClass(CapitalShipClass shipClass)
    {
        int targetIndex = Array.IndexOf(Enum.GetValues<CapitalShipClass>(), shipClass) + 1;
        if (targetIndex < 1 || targetIndex >= JumpRangeClassCombo.Items.Count) return;

        JumpRangeClassCombo.SelectedIndex = targetIndex;
        if (RouteMap is not null)
            RouteMap.JumpRangeShipClass = shipClass;
    }

    private void SetRouteShipClass(CapitalShipClass shipClass, int? preferredTypeId = null)
    {
        int classIndex = Array.IndexOf(Enum.GetValues<CapitalShipClass>(), shipClass);
        if (classIndex < 0 || classIndex >= ShipClassCombo.Items.Count) return;

        _isApplyingDetectedShip = true;
        try
        {
            ShipClassCombo.SelectedIndex = classIndex;
            PopulateShipHullsForClass(shipClass);
            if (preferredTypeId is int typeId)
                SelectRouteHullByTypeId(typeId);
        }
        finally
        {
            _isApplyingDetectedShip = false;
        }
    }

    private void SelectRouteHullByTypeId(int typeId)
    {
        if (_services.ShipCatalog?.HullsByTypeId.TryGetValue(typeId, out ShipHull? catalogHull) != true
            || catalogHull is null)
            return;

        for (int i = 0; i < ShipHullCombo.Items.Count; i++)
        {
            if (ShipHullCombo.Items[i] is ComboBoxItem { Tag: ShipHull hull }
                && (hull.TypeId == typeId
                    || string.Equals(hull.Name, catalogHull.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ShipHullCombo.SelectedIndex = i;
                return;
            }
        }
    }

    private void ApplyDetectedShipSelection(int shipTypeId, CapitalShipClass? shipClass)
    {
        if (shipClass is CapitalShipClass resolved)
        {
            SetJumpRangeShipClass(resolved);
            SetRouteShipClass(resolved, shipTypeId);
            RouteMap.RefreshJumpRangeHighlights();
            return;
        }

        SetJumpRangeShipClassToDefault();
    }

    private void ShowStatusToast(string message, ToastKind kind = ToastKind.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        DiagnosticToast.Show(message, kind);
    }

    private void OnJumpRangeSimulationExhausted()
    {
        ShowStatusToast("Симуляция завершена, доступные системы отсутствуют", ToastKind.Warning);
    }

    private void SetJumpRangeShipClassToDefault() => SetJumpRangeShipClass(CapitalShipClass.BlackOps);

    /// <summary>
    /// When the main profile changes, query ESI once for the pilot's current ship and align the
    /// jump-range ship-class selector. Capsules and non-jump hulls leave Black Ops selected.
    /// </summary>
    private async Task AutoSelectJumpRangeShipForActivePilotAsync(int generation)
    {
        var character = GetActiveCharacter();
        if (character is null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _shipTypeAutoSelectGeneration) return;
                SetJumpRangeShipClassToDefault();
            });
            return;
        }

        var settings = EsiAuthConfig.TryLoad();
        if (settings is null)
        {
            SetShipTypeAutoSelectStatus(generation, character.CharacterId,
                "Тип корабля: ESI Client ID не настроен.");
            return;
        }

        if (!_services.CharacterHasShipTypeScope(character.CharacterId))
        {
            SetShipTypeAutoSelectStatus(generation, character.CharacterId,
                $"Тип корабля: перевойдите {character.Name} через «Войти» — нужен доступ к текущему кораблю.");
            return;
        }

        try
        {
            int shipTypeId = await _services.RefreshCharacterShipTypeAsync(character.CharacterId, settings);
            if (generation != _shipTypeAutoSelectGeneration) return;

            CapitalShipClass? shipClass = await _services.ResolveJumpShipClassAsync(shipTypeId);
            if (generation != _shipTypeAutoSelectGeneration) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _shipTypeAutoSelectGeneration) return;
                if (GetActiveCharacter()?.CharacterId != character.CharacterId) return;

                ApplyDetectedShipSelection(shipTypeId, shipClass);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string message = ex.Message.Contains("401", StringComparison.Ordinal)
                || ex.Message.Contains("403", StringComparison.Ordinal)
                ? $"Тип корабля: перевойдите {character.Name} через «Войти» — нужен доступ к текущему кораблю."
                : $"Тип корабля: не удалось определить корабль ({ex.Message}).";
            SetShipTypeAutoSelectStatus(generation, character.CharacterId, message);
        }
    }

    private void SetShipTypeAutoSelectStatus(int generation, long characterId, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _shipTypeAutoSelectGeneration) return;
            if (GetActiveCharacter()?.CharacterId != characterId) return;
            ShowStatusToast(message, ToastKind.Warning);
        });
    }

    private void RequestJumpRangeShipAutoSelect()
    {
        int generation = Interlocked.Increment(ref _shipTypeAutoSelectGeneration);
        _ = AutoSelectJumpRangeShipForActivePilotAsync(generation);
    }

    private void OnMapModeSchematicClick(object? sender, RoutedEventArgs e) => SetMapDisplayMode(MapDisplayMode.Schematic);

    private void OnMapModeStandardClick(object? sender, RoutedEventArgs e) => SetMapDisplayMode(MapDisplayMode.Standard);

    private void SetMapDisplayMode(MapDisplayMode mode)
    {
        RouteMap.DisplayMode = mode;
        MapModeSchematicMenuItem.IsChecked = mode == MapDisplayMode.Schematic;
        MapModeStandardMenuItem.IsChecked = mode == MapDisplayMode.Standard;
    }

    private void OnPlateColorNpcKillsClick(object? sender, RoutedEventArgs e) => SetPlateColorMode(MapPlateColorMode.NpcKills);

    private void OnPlateColorSecurityClick(object? sender, RoutedEventArgs e) => SetPlateColorMode(MapPlateColorMode.Security);

    private void SetPlateColorMode(MapPlateColorMode mode)
    {
        RouteMap.PlateColorMode = mode;
        ColorNpcKillsMenuItem.IsChecked = mode == MapPlateColorMode.NpcKills;
        ColorSecurityMenuItem.IsChecked = mode == MapPlateColorMode.Security;
    }

    private void OnZKillboardPoliteClick(object? sender, RoutedEventArgs e) => SetZKillboardRequestMode(ZKillboardRequestMode.Polite);

    private void OnZKillboardFasterClick(object? sender, RoutedEventArgs e) => SetZKillboardRequestMode(ZKillboardRequestMode.Faster);

    private void OnDebugGridToggled(object? sender, RoutedEventArgs e) =>
        RouteMap.ShowDebugGrid = DebugGridMenuItem.IsChecked;

    private void OnShowEveScoutWormholesToggled(object? sender, RoutedEventArgs e)
    {
        bool enabled = ShowEveScoutWormholesMenuItem.IsChecked;
        _services.SetShowEveScoutWormholes(enabled);
        RouteMap.ShowEveScoutWormholes = enabled;
        RouteMap.SyncWormholeAnimation(enabled && _services.EveScoutWormholes.Count > 0);
        RouteMap.InvalidateVisual();
    }

    private void SyncShowEveScoutWormholesMenu() =>
        ShowEveScoutWormholesMenuItem.IsChecked = _services.ShowEveScoutWormholes;

    private void OnRegionEditToggled(object? sender, RoutedEventArgs e)
    {
        RouteMap.RegionEditMode = RegionEditMenuItem.IsChecked;
        if (RegionEditMenuItem.IsChecked == true)
        {
            ShowStatusToast(
                "Режим правки регионов: тяните регион мышью, затем «Экспортировать координаты регионов...».",
                ToastKind.Info);
        }
    }

    private async void OnExportRegionPositionsClick(object? sender, RoutedEventArgs e)
    {
        string? json = RouteMap.BuildRegionPositionsJson();
        if (string.IsNullOrEmpty(json))
        {
            ShowStatusToast("Экспорт: сетка регионов недоступна (загрузите SDE, режим «Схема»).", ToastKind.Warning);
            return;
        }

        string path = System.IO.Path.Combine(
            EvEMapEnhanced.Data.Paths.AppPaths.AppDataDir, "ingame-region-positions.json");
        try
        {
            await System.IO.File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            ShowStatusToast($"Экспорт: не удалось записать файл ({ex.Message}).", ToastKind.Warning);
            return;
        }

        string clipboardNote = "";
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            try
            {
                var data = new Avalonia.Input.DataTransfer();
                data.Add(Avalonia.Input.DataTransferItem.CreateText(json));
                await clipboard.SetDataAsync(data);
                clipboardNote = " и скопировано в буфер обмена";
            }
            catch
            {
                // Clipboard may be unavailable (e.g. headless); the saved file is enough.
            }
        }

        ShowStatusToast($"Координаты регионов сохранены: {path}{clipboardNote}.", ToastKind.Success);
    }

    private void OnZKillboardJumpRangeScopeClick(object? sender, RoutedEventArgs e) => SetZKillboardScope(ZKillboardScope.JumpRange);

    private void OnZKillboardGlobalScopeClick(object? sender, RoutedEventArgs e) => SetZKillboardScope(ZKillboardScope.GlobalNullsec);

    private void SetZKillboardRequestMode(ZKillboardRequestMode mode)
    {
        _services.SetZKillboardRequestMode(mode);
        SyncZKillboardRequestModeMenu();
        TriggerPvpRefresh();
    }

    private void SetZKillboardScope(ZKillboardScope scope)
    {
        _services.SetZKillboardScope(scope);
        RouteMap.PvPScope = scope;
        SyncZKillboardScopeMenu();
        TriggerPvpRefresh();
    }

    private void SyncZKillboardScopeMenu()
    {
        ZKillboardJumpRangeScopeMenuItem.IsChecked = _services.ZKillboardScope == ZKillboardScope.JumpRange;
        ZKillboardGlobalScopeMenuItem.IsChecked = _services.ZKillboardScope == ZKillboardScope.GlobalNullsec;
    }

    private void SyncZKillboardRequestModeMenu()
    {
        ZKillboardPoliteMenuItem.IsChecked = _services.ZKillboardRequestMode == ZKillboardRequestMode.Polite;
        ZKillboardFasterMenuItem.IsChecked = _services.ZKillboardRequestMode == ZKillboardRequestMode.Faster;
    }

    private void OnShipClassChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingDetectedShip) return;
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

    private ShipHull? GetSelectedHull()
    {
        if (ShipHullCombo.SelectedItem is ComboBoxItem { Tag: ShipHull hull })
            return hull;

        if (ShipClassCombo.SelectedItem is ComboBoxItem { Tag: CapitalShipClass shipClass })
            return DefaultHullForClass(shipClass);

        return null;
    }

    private static ShipHull? DefaultHullForClass(CapitalShipClass shipClass)
    {
        var hulls = shipClass == CapitalShipClass.CommandCarrier
            ? ShipHulls.ByClass(CapitalShipClass.Carrier).Concat(ShipHulls.ByClass(CapitalShipClass.ForceAuxiliary))
            : ShipHulls.ByClass(shipClass);
        return hulls.FirstOrDefault();
    }

    // ============================================================
    // SDE download / status
    // ============================================================

    private void RefreshSdeStatus(bool notify = false)
    {
        string message;
        ToastKind kind;
        if (_services.IsMapLoaded && _services.Map is not null)
        {
            message = $"SDE: загружен, систем: {_services.Map.Systems.Count}";
            kind = ToastKind.Success;
        }
        else if (_services.SdeService.IsCached())
        {
            message = "SDE: закэширован (не загружен в память)";
            kind = ToastKind.Warning;
        }
        else
        {
            message = "SDE: не загружен — меню «Данные» → «Скачать / обновить SDE»";
            kind = ToastKind.Warning;
        }

        if (notify)
            ShowStatusToast(message, kind);
    }

    private async void OnDownloadSdeClick(object? sender, RoutedEventArgs e)
    {
        DownloadSdeMenuItem.IsEnabled = false;
        SdeProgressBar.IsVisible = true;
        SdeProgressBar.Value = 0;
        ShowStatusToast("SDE: скачивание...", ToastKind.Info);

        try
        {
            var progress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() => SdeProgressBar.Value = p));

            var summary = await _services.SdeService.DownloadAndImportAsync(progress);
            _services.ReloadMapFromCache();
            RefreshSystemNameLookups();
            if (_services.Map is not null)
            {
                RouteMap.SetMap(_services.Map);
                JumpRangeMiniMap.SetMap(_services.Map);
                TriggerPvpRefresh();
            }
            ShowStatusToast(
                $"SDE: обновлён — регионов={summary.Regions}, систем={summary.SolarSystems}, stargate-пар={summary.Stargates}, типов кораблей={summary.ShipTypesResolved}",
                ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowStatusToast($"SDE: ошибка скачивания: {ex.Message}", ToastKind.Warning);
        }
        finally
        {
            DownloadSdeMenuItem.IsEnabled = true;
            SdeProgressBar.IsVisible = false;
        }
    }

    private void RefreshSystemNameLookups()
    {
        if (_services.Map is null) return;

        var names = _services.Map.Systems.Values.Select(s => s.Name).OrderBy(n => n).ToList();
        RouteFromBox.ItemsSource = names;
        RouteToBox.ItemsSource = names;
        SystemSearchBox.ItemsSource = names;
    }

    private void OnSystemSearchSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        FocusSearchedSystem(SystemSearchBox.Text);

    private void OnSystemSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SystemSearchBox.Text))
            RouteMap.SearchedSystemId = null;
    }

    private void OnSystemSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        FocusSearchedSystem(SystemSearchBox.Text);
        e.Handled = true;
    }

    private void FocusSearchedSystem(string? name)
    {
        if (_services.Map is null) return;

        if (string.IsNullOrWhiteSpace(name))
        {
            RouteMap.SearchedSystemId = null;
            return;
        }

        var system = _services.Map.FindByName(name.Trim());
        if (system is null) return;

        RouteMap.CenterOnSystem(system.Id);
        RouteMap.SearchedSystemId = system.Id;
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

    /// <summary>
    /// Keeps the Jump Range mini-map centered on the jump-range origin (not mere click selection),
    /// zoomed so the full Black Ops range circle is visible at true LY scale.
    /// </summary>
    private void OnRouteMapJumpRangeOriginChanged(int? systemId)
    {
        JumpRangeMiniMap.FocusJumpRange(systemId);

        if (systemId is int id && _services.Map?.Get(id) is { } system)
        {
            double rangeLy = JumpSimulator.MaxRangeLy(CapitalShipClass.BlackOps, GetSelectedRouteSkills());
            JumpRangeMiniMapLabel.Text = $"Дальность прыжка (Black Ops) от {system.Name}: {rangeLy:F1} LY";
        }
        else
        {
            JumpRangeMiniMapLabel.Text = "Дальность прыжка (Black Ops): выберите систему на карте";
        }
    }

    private void OnJumpRangeMiniMapHoverChanged(int? systemId) =>
        RouteMap.LinkedHoveredSystemId = systemId;

    private static void OnOpenZKillboardSystem(int systemId) =>
        AppServices.OpenZKillboardSystemPage(systemId);

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

    private void OnClearRouteClick(object? sender, RoutedEventArgs e) => ClearRoute();

    private void ClearRoute()
    {
        RouteFromBox.Text = string.Empty;
        RouteToBox.Text = string.Empty;
        RouteStepsList.ItemsSource = null;
        RouteSummaryText.Text = string.Empty;

        if (RouteMap is null) return;

        RouteMap.FromSystemId = null;
        RouteMap.ToSystemId = null;
        RouteMap.RouteSteps = null;
        RouteMap.InvalidateVisual();
    }

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
            RouteMap.RouteSteps = null;
            RouteMap.InvalidateVisual();
            return;
        }

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
        RefreshCynoProfileSelector(_services.Characters.GetActiveCynoCharacterIds());
        RefreshScProfileSelector(_services.Characters.GetActiveScCharacterIds());

        if (GetActiveCharacter() is not null)
            RequestJumpRangeShipAutoSelect();
    }

    private void RefreshPilotCombo(long? selectId = null)
    {
        _isRefreshingPilotCombo = true;
        try
        {
            long? wantId = selectId ?? (PilotCombo.SelectedItem is ComboBoxItem { Tag: long id } ? id : null);

            PilotCombo.Items.Clear();
            PilotCombo.Items.Add(new ComboBoxItem { Content = "(нет, макс. навыки 5)", Tag = null });
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

    private bool _isRefreshingCynoCombo;

    private void RefreshCynoProfileSelector(IReadOnlyList<long>? selectIds = null)
    {
        _isRefreshingCynoCombo = true;
        try
        {
            var wantIds = selectIds?.ToHashSet()
                ?? CynoProfileSelector.GetSelectedIds().ToHashSet();
            var items = _characters.Select(c => (c.CharacterId, c.Name)).ToList();
            CynoProfileSelector.SetItems(items, wantIds);
        }
        finally
        {
            _isRefreshingCynoCombo = false;
        }

        _services.Characters.SetActiveCynoCharacterIds(CynoProfileSelector.GetSelectedIds());
        RestartCynoLocationPolling();
    }

    private IReadOnlyList<AuthenticatedCharacter> GetSelectedCynoCharacters()
    {
        var ids = CynoProfileSelector.GetSelectedIds().ToHashSet();
        return _characters.Where(c => ids.Contains(c.CharacterId)).ToList();
    }

    private void OnCynoProfileSelectionChanged(object? sender, EventArgs e)
    {
        if (_isRefreshingCynoCombo || RouteMap is null) return;

        _services.Characters.SetActiveCynoCharacterIds(CynoProfileSelector.GetSelectedIds());
        RestartCynoLocationPolling();
    }

    private bool _isRefreshingScCombo;

    private void RefreshScProfileSelector(IReadOnlyList<long>? selectIds = null)
    {
        _isRefreshingScCombo = true;
        try
        {
            var wantIds = selectIds?.ToHashSet()
                ?? ScProfileSelector.GetSelectedIds().ToHashSet();
            var items = _characters.Select(c => (c.CharacterId, c.Name)).ToList();
            ScProfileSelector.SetItems(items, wantIds);
        }
        finally
        {
            _isRefreshingScCombo = false;
        }

        _services.Characters.SetActiveScCharacterIds(ScProfileSelector.GetSelectedIds());
        RestartScLocationPolling();
    }

    private IReadOnlyList<AuthenticatedCharacter> GetSelectedScCharacters()
    {
        var ids = ScProfileSelector.GetSelectedIds().ToHashSet();
        return _characters.Where(c => ids.Contains(c.CharacterId)).ToList();
    }

    private void OnScProfileSelectionChanged(object? sender, EventArgs e)
    {
        if (_isRefreshingScCombo || RouteMap is null) return;

        _services.Characters.SetActiveScCharacterIds(ScProfileSelector.GetSelectedIds());
        RestartScLocationPolling();
    }

    private PilotSkills GetSelectedRouteSkills() => GetActiveCharacter()?.Skills ?? PilotSkills.MaxSkills();

    /// <summary>
    /// Names of characters whose live location is tracked and matches <paramref name="systemId"/>.
    /// </summary>
    private IReadOnlyList<string> GetTrackedCharactersInSystem(int systemId)
    {
        var names = new List<string>();
        var seen = new HashSet<long>();

        void Add(AuthenticatedCharacter? character)
        {
            if (character is null || character.LastKnownSystemId != systemId)
                return;
            if (seen.Add(character.CharacterId))
                names.Add(character.Name);
        }

        if (JumpRangeOnlineCheck.IsChecked == true)
            Add(GetActiveCharacter());

        foreach (var character in GetSelectedCynoCharacters())
            Add(character);

        foreach (var character in GetSelectedScCharacters())
            Add(character);

        return names;
    }

    private void OnPilotSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isRefreshingPilotCombo)
        {
            _services.Characters.SetActiveCharacterId(GetActiveCharacter()?.CharacterId);
            RequestJumpRangeShipAutoSelect();
        }

        RouteMap.RefreshJumpRangeHighlights();

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

        SignInMenuItem.IsEnabled = false;
        RouteSummaryText.Text = "Запускаю локальный приёмник для входа EVE...";
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        string? pendingAuthUrl = null;
        try
        {
            var character = await _services.SignInWithEveOnlineAsync(
                settings,
                url =>
                {
                    pendingAuthUrl = url;
                    RouteSummaryText.Text = "Открываю браузер EVE Online... Завершите вход там.";
                    return OpenAuthUrlAsync(url);
                },
                timeoutCts.Token);
            LoadCharacters(character.CharacterId);
            RouteSummaryText.Text = $"Выполнен вход как {character.Name}.";
        }
        catch (OperationCanceledException)
        {
            RouteSummaryText.Text = pendingAuthUrl is null
                ? "Вход отменён или истекло время ожидания (10 мин). Повторите попытку."
                : $"Вход не завершён за 10 мин. Если браузер не открылся — скопируйте ссылку вручную:\n{pendingAuthUrl}";
        }
        catch (Exception ex)
        {
            string hint = ex.Message.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                ? " Если в браузере после входа «connection refused» — разрешите EvE Map Enhanced в брандмауэре Windows."
                : string.Empty;
            RouteSummaryText.Text = pendingAuthUrl is null
                ? $"Ошибка входа: {ex.Message}{hint}"
                : $"Ошибка входа: {ex.Message}{hint}\nСсылка для ручного входа:\n{pendingAuthUrl}";
        }
        finally
        {
            SignInMenuItem.IsEnabled = true;
        }
    }

    private async Task OpenAuthUrlAsync(string url)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
        {
            try
            {
                if (await launcher.LaunchUriAsync(new Uri(url)))
                    return;
            }
            catch
            {
                // Fall through to OS-specific browser launchers.
            }
        }

        BrowserLauncher.OpenOrThrow(url);
    }

    private async void OnRefreshSkillsClick(object? sender, RoutedEventArgs e)
    {
        var character = GetActiveCharacter();
        var settings = EsiAuthConfig.TryLoad();
        if (character is null || settings is null) return;

        RefreshSkillsMenuItem.IsEnabled = false;
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
            RefreshSkillsMenuItem.IsEnabled = true;
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
        bool online = JumpRangeOnlineCheck.IsChecked == true;

        if (online)
        {
            RestartLocationPollingForActiveCharacter();
        }
        else
        {
            StopLocationPolling();
            RouteMap.SelectSystemExternally(null);
        }
    }

    /// <summary>
    /// When checked, left-clicks on the map no longer move the jump-range origin; only the
    /// right-click "Дальность прыжка" menu may re-anchor it.
    /// </summary>
    private void OnFocusCheckToggled(object? sender, RoutedEventArgs e)
    {
        RouteMap.PinJumpRangeOrigin = FocusCheck.IsChecked == true;
    }

    private void OnJumpRangeSimulationToggled(object? sender, RoutedEventArgs e)
    {
        if (RouteMap is null) return;
        RouteMap.JumpRangeSimulationActive = JumpRangeSimulationToggle.IsChecked == true;
    }

    private void OnCenterPilotClick(object? sender, RoutedEventArgs e)
    {
        var character = GetActiveCharacter();
        if (character?.LastKnownSystemId is not int systemId || _services.Map?.Get(systemId) is null)
        {
            ShowStatusToast("Центрирование: выберите основной профиль с известной позицией.", ToastKind.Warning);
            return;
        }

        RouteMap.CenterOnSystem(systemId);
    }

    private void RestartLocationPollingForActiveCharacter()
    {
        StopLocationPolling();

        var character = GetActiveCharacter();
        var settings = EsiAuthConfig.TryLoad();
        RouteMap.SelectSystemExternally(character?.LastKnownSystemId);

        if (character is null)
        {
            ShowStatusToast("Слежение: выберите пилота.", ToastKind.Warning);
            return;
        }
        if (settings is null)
        {
            ShowStatusToast("Слежение: ESI Client ID не настроен.", ToastKind.Warning);
            return;
        }

        if (character.LastKnownSystemId is int cachedId)
        {
            string sysName = _services.Map?.Get(cachedId)?.Name ?? $"#{cachedId}";
            ShowStatusToast($"Слежение: {sysName} (кэш, обновляю...)", ToastKind.Info);
        }
        else
        {
            ShowStatusToast("Слежение: определяю местоположение...", ToastKind.Info);
        }

        var cts = new CancellationTokenSource();
        _locationPollCts = cts;
        _ = PollLocationLoopAsync(character.CharacterId, settings, cts.Token);
    }

    private void StopLocationPolling()
    {
        _locationPollCts?.Cancel();
        _locationPollCts = null;
    }

    /// <summary>
    /// Polls ESI for the tracked pilot's current system and re-selects it on the map every tick
    /// so the jump-range highlight always follows real, live movement (not just wherever the
    /// pilot happened to be when tracking was turned on). Errors are surfaced as bottom-left
    /// toasts instead of being silently swallowed — a previous version of this loop ate every
    /// exception, which made an expired/under-scoped token (a character signed in before the
    /// location scope was added, say) look exactly like "the jump range just doesn't update",
    /// with no indication of why.
    /// </summary>
    private async Task PollLocationLoopAsync(long characterId, EsiAuthSettings settings, CancellationToken ct)
    {
        // ESI's own location endpoint is only cached for a few seconds server-side, so polling
        // this much more slowly than that (45s, previously) meant real in-game movement could sit
        // unreflected on the map for the better part of a minute.
        var pollInterval = TimeSpan.FromSeconds(12);

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
                        JumpRangeMiniMap.InvalidateVisual();
                    }
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (GetActiveCharacter()?.CharacterId == characterId)
                    {
                        ShowStatusToast($"Слежение: ошибка обновления ({ex.Message})", ToastKind.Warning);
                    }
                });
            }

            try { await Task.Delay(pollInterval, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    // ============================================================
    // Live cyno pilot location tracking
    // ============================================================

    private void RestartCynoLocationPolling()
    {
        StopCynoLocationPolling();

        if (RouteMap is null) return;

        RefreshCynoBeaconsOnMap();

        var settings = EsiAuthConfig.TryLoad();
        if (settings is null) return;

        foreach (var character in GetSelectedCynoCharacters())
        {
            var cts = new CancellationTokenSource();
            _cynoPollCtsByCharacter[character.CharacterId] = cts;
            _ = PollCynoLocationLoopAsync(character.CharacterId, settings, cts.Token);
        }
    }

    private void RefreshCynoBeaconsOnMap()
    {
        if (RouteMap is null) return;

        var systemIds = GetSelectedCynoCharacters()
            .Select(c => c.LastKnownSystemId)
            .Where(id => id is not null)
            .Select(id => id!.Value);
        RouteMap.SetCynoLocations(systemIds);
        JumpRangeMiniMap.InvalidateVisual();
    }

    private void StopCynoLocationPolling()
    {
        foreach (var cts in _cynoPollCtsByCharacter.Values)
        {
            cts.Cancel();
        }
        _cynoPollCtsByCharacter.Clear();
    }

    private async Task PollCynoLocationLoopAsync(long characterId, EsiAuthSettings settings, CancellationToken ct)
    {
        var pollInterval = TimeSpan.FromSeconds(12);

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
                    if (GetSelectedCynoCharacters().Any(c => c.CharacterId == characterId))
                    {
                        RefreshCynoBeaconsOnMap();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep last-known cyno location; retry on next tick.
            }

            try { await Task.Delay(pollInterval, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    // ============================================================
    // Live SC pilot location tracking
    // ============================================================

    private void RestartScLocationPolling()
    {
        StopScLocationPolling();

        if (RouteMap is null) return;

        RefreshScBeaconsOnMap();

        var settings = EsiAuthConfig.TryLoad();
        if (settings is null) return;

        foreach (var character in GetSelectedScCharacters())
        {
            var cts = new CancellationTokenSource();
            _scPollCtsByCharacter[character.CharacterId] = cts;
            _ = PollScLocationLoopAsync(character.CharacterId, settings, cts.Token);
        }
    }

    private void RefreshScBeaconsOnMap()
    {
        if (RouteMap is null) return;

        var systemIds = GetSelectedScCharacters()
            .Select(c => c.LastKnownSystemId)
            .Where(id => id is not null)
            .Select(id => id!.Value);
        RouteMap.SetScLocations(systemIds);
        JumpRangeMiniMap.InvalidateVisual();
    }

    private void StopScLocationPolling()
    {
        foreach (var cts in _scPollCtsByCharacter.Values)
        {
            cts.Cancel();
        }
        _scPollCtsByCharacter.Clear();
    }

    private async Task PollScLocationLoopAsync(long characterId, EsiAuthSettings settings, CancellationToken ct)
    {
        var pollInterval = TimeSpan.FromSeconds(12);

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
                    if (GetSelectedScCharacters().Any(c => c.CharacterId == characterId))
                    {
                        RefreshScBeaconsOnMap();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep last-known SC location; retry on next tick.
            }

            try { await Task.Delay(pollInterval, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    // ============================================================
    // Structures (modal)
    // ============================================================

    private async void OnStructuresClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new StructuresWindow(_services, () => RouteMap.InvalidateVisual());
        await dialog.ShowDialog(this);
    }

    // ============================================================
    // NPC-kill map coloring
    // ============================================================

    private void OnJumpReachabilityChanged(IReadOnlyCollection<int> systemIds)
    {
        if (_services.ZKillboardScope != ZKillboardScope.JumpRange)
            return;

        TriggerPvpRefresh();
    }

    private HashSet<int> ComputeMonitoredSystems() =>
        _services.ZKillboardScope == ZKillboardScope.GlobalNullsec
            ? _services.GetNullsecSystemIds()
            : ComputeBlackOpsMonitoredSystems();

    /// <summary>
    /// zKillboard overlays follow the Jump Range mini-map: Black Ops reachability from the
    /// anchored origin, plus the origin system itself (kills often happen where you are sitting).
    /// </summary>
    private HashSet<int> ComputeBlackOpsMonitoredSystems()
    {
        if (RouteMap.JumpRangeOriginSystemId is not int originId || _services.Map?.Get(originId) is not { } origin)
            return new HashSet<int>();

        double rangeLy = JumpSimulator.MaxRangeLy(CapitalShipClass.BlackOps, GetSelectedRouteSkills());
        var set = _services.Map.SystemsWithinRange(origin, rangeLy)
            .Where(t => JumpRules.IsValidJumpLanding(t.System, JumpMethod.Cyno))
            .Select(t => t.System.Id)
            .ToHashSet();
        set.Add(originId);
        return set;
    }

    private void TriggerPvpRefresh()
    {
        if (_services.KillVictimFilter is null)
        {
            SetPvpStatusText("zKillboard: загрузите SDE (меню «Данные»)");
            return;
        }

        _pendingPvpSystems = ComputeMonitoredSystems();
        if (_pendingPvpSystems.Count == 0)
        {
            SetPvpStatusText(_services.ZKillboardScope == ZKillboardScope.GlobalNullsec
                ? "zKillboard: нет nullsec систем в SDE"
                : "zKillboard: нет систем в jump range — кликните систему на карте");
            return;
        }

        SetPvpStatusText(FormatPvpPreparingMessage(
            _pendingPvpSystems.Count,
            CountPendingRegions(),
            _services.ZKillboardScope,
            _services.ZKillboardRequestMode));

        _pvpDebounceCts?.Cancel();
        _pvpDebounceCts?.Dispose();
        _pvpDebounceCts = new CancellationTokenSource();
        _ = DebouncedPvpRefreshAsync(_pvpDebounceCts.Token);
    }

    private async Task DebouncedPvpRefreshAsync(CancellationToken debounceCt)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), debounceCt);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var set = _pendingPvpSystems is not null ? new HashSet<int>(_pendingPvpSystems) : new HashSet<int>();
        if (set.Count == 0) return;

        int generation = Interlocked.Increment(ref _pvpRefreshGeneration);

        _pvpRefreshCts?.Cancel();
        _pvpRefreshCts?.Dispose();
        _pvpRefreshCts = new CancellationTokenSource();

        _ = RefreshJumpRangePvPLoopAsync(set, generation, _pvpRefreshCts.Token, RouteMap.JumpRangeOriginSystemId);
    }

    /// <summary>
    /// Keeps red/yellow PvP overlays on jump-reachable systems fresh by querying zKillboard
    /// for each system in range (throttled). Re-runs whenever reachability changes and every
    /// few minutes while the same range stays active.
    /// </summary>
    private async Task RefreshJumpRangePvPLoopAsync(
        IReadOnlyCollection<int> systemIds,
        int generation,
        CancellationToken ct,
        int? originSystemId)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (generation != _pvpRefreshGeneration) return;

                if (systemIds.Count == 0)
                {
                    await _services.RefreshJumpRangePvPAsync(
                        Array.Empty<int>(),
                        originSystemId,
                        onProgress: () =>
                        {
                            if (generation == _pvpRefreshGeneration)
                                Dispatcher.UIThread.Post(UpdatePvpStatusText);
                        },
                        ct: CancellationToken.None);
                    return;
                }

                await _services.RefreshJumpRangePvPAsync(
                    systemIds,
                    originSystemId,
                    onProgress: () =>
                    {
                        if (generation != _pvpRefreshGeneration) return;
                        Dispatcher.UIThread.Post(() =>
                        {
                            UpdatePvpStatusText();
                            RouteMap.InvalidateVisual();
                            JumpRangeMiniMap.InvalidateVisual();
                        });
                    },
                    ct: CancellationToken.None);

                if (generation != _pvpRefreshGeneration) return;

                Dispatcher.UIThread.Post(UpdatePvpStatusText);
                await Task.Delay(TimeSpan.FromMinutes(3), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Reachability changed or window closed — expected.
        }
    }

    private void UpdatePvpStatusText()
    {
        var (completed, total, hot, recent, npcCapital, failed, cached, remainingNetwork) = _services.JumpRangePvPProgress;
        if (total == 0)
            return;

        if (completed < total)
        {
            SetPvpStatusText(FormatPvpLoadingMessage(
                completed, total, cached, remainingNetwork, _services.ZKillboardScope, _services.ZKillboardRequestMode));
            return;
        }

        if (failed == total)
        {
            SetPvpStatusText("zKillboard: ошибка сети — проверьте доступ к zkillboard.com");
            return;
        }

        string scopeNote = _services.ZKillboardScope == ZKillboardScope.GlobalNullsec ? " (глобальный)" : "";
        SetPvpStatusText(hot > 0 || recent > 0 || npcCapital > 0
            ? $"zKillboard{scopeNote}: готово — фиолетовых {npcCapital}, красных {hot}, жёлтых {recent}"
            : $"zKillboard{scopeNote}: готово — активности нет ({total} систем)");
    }

    private int CountPendingRegions()
    {
        if (_services.Map is null) return 0;
        if (_services.ZKillboardScope == ZKillboardScope.GlobalNullsec)
            return _services.CountNullsecRegionsNeedingFetch();

        if (_pendingPvpSystems is null) return 0;
        return _pendingPvpSystems
            .Select(id => _services.Map.Get(id)?.RegionId)
            .Where(r => r is int)
            .Distinct()
            .Count(regionId => !_services.IsRegionCacheFresh(regionId!.Value));
    }

    private static string FormatPvpPreparingMessage(
        int totalSystems, int totalRegions, ZKillboardScope scope, ZKillboardRequestMode mode)
    {
        string scopeLabel = scope == ZKillboardScope.GlobalNullsec ? "глобальный nullsec" : "jump range";
        string pace = mode == ZKillboardRequestMode.Faster
            ? "~2 запроса/с, 2 параллельно"
            : "~1 запрос/с";
        int etaSeconds = mode == ZKillboardRequestMode.Faster
            ? (int)Math.Ceiling(totalRegions / 2.0)
            : totalRegions;
        return $"zKillboard ({scopeLabel}): {totalSystems} систем, {totalRegions} регион(ов), ~{etaSeconds} с ({pace})";
    }

    private static string FormatPvpLoadingMessage(
        int completed, int total, int cached, int remainingNetwork, ZKillboardScope scope, ZKillboardRequestMode mode)
    {
        int etaSeconds = mode == ZKillboardRequestMode.Faster
            ? (int)Math.Ceiling(remainingNetwork / 2.0)
            : remainingNetwork;
        string eta = remainingNetwork > 0 ? $"~{etaSeconds} с" : "кэш";
        string cacheNote = cached > 0 ? $", {cached} из кэша" : "";
        string scopeLabel = scope == ZKillboardScope.GlobalNullsec ? "глобальный" : "jump range";
        return $"zKillboard ({scopeLabel}): {completed}/{total}{cacheNote}, осталось {eta}";
    }

    private void SetPvpStatusText(string text)
    {
        PvpStatusText.Text = text;
    }

    private bool _syncingMapZoomSlider;

    private void SyncMapZoomSliderFromMap()
    {
        _syncingMapZoomSlider = true;
        MapZoomSlider.Value = MapControl.SliderFromZoom(RouteMap.ZoomLevel);
        MapZoomLevelText.Text = MapControl.FormatZoomLevel(RouteMap.ZoomLevel);
        _syncingMapZoomSlider = false;
    }

    private void OnRouteMapZoomLevelChanged(object? sender, double zoom)
    {
        if (_syncingMapZoomSlider) return;
        SyncMapZoomSliderFromMap();
    }

    private void OnMapZoomSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingMapZoomSlider) return;
        RouteMap.ZoomLevel = MapControl.ZoomFromSlider(e.NewValue);
    }

    private void OnMapZoomInClick(object? sender, RoutedEventArgs e) =>
        RouteMap.ZoomLevel = Math.Min(RouteMap.ZoomLevel * 1.25, MapControl.MaxZoomLevel);

    private void OnMapZoomOutClick(object? sender, RoutedEventArgs e) =>
        RouteMap.ZoomLevel = Math.Max(RouteMap.ZoomLevel / 1.25, MapControl.MinZoomLevel);

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

    /// <summary>
    /// Keeps Sansha incursion highlights fresh from ESI's public incursions feed. Polls every few
    /// minutes; incursion state changes slowly so aggressive polling is unnecessary.
    /// </summary>
    private async Task RefreshSanshaIncursionsLoopAsync()
    {
        while (true)
        {
            await _services.RefreshSanshaIncursionsAsync();
            Dispatcher.UIThread.Post(() =>
            {
                RouteMap.SyncIncursionAnimation(_services.SanshaIncursionSystems.Count > 0);
                RouteMap.InvalidateVisual();
            });
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }

    /// <summary>
    /// Keeps Thera/Turnur wormhole markers fresh from the public EvE-Scout API.
    /// </summary>
    private async Task RefreshEveScoutWormholesLoopAsync()
    {
        while (true)
        {
            await _services.RefreshEveScoutWormholesAsync();
            Dispatcher.UIThread.Post(() =>
            {
                RouteMap.SyncWormholeAnimation(_services.ShowEveScoutWormholes && _services.EveScoutWormholes.Count > 0);
                RouteMap.InvalidateVisual();
            });
            await Task.Delay(TimeSpan.FromMinutes(10));
        }
    }

    /// <summary>
    /// Keeps IHUB alliance names fresh from ESI's public sovereignty map (cached server-side for
    /// about ten minutes, so polling every ten minutes is enough).
    /// </summary>
    private async Task RefreshIhubAlliancesLoopAsync()
    {
        while (true)
        {
            await _services.RefreshIhubAlliancesAsync();
            Dispatcher.UIThread.Post(() =>
            {
                RouteMap.InvalidateVisual();
                JumpRangeMiniMap.InvalidateVisual();
            });
            await Task.Delay(TimeSpan.FromMinutes(10));
        }
    }
}
