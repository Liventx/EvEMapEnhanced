using EvEMapEnhanced.Core.Jump;
using EvEMapEnhanced.Core.Routing;
using EvEMapEnhanced.Core.Ships;
using EvEMapEnhanced.Data.Paths;
using EvEMapEnhanced.Data.Sde;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

switch (args[0])
{
    case "sde-import":
        return await SdeImportCommand(args);
    case "sde-download":
        return await SdeDownloadCommand();
    case "ships":
        return ShipsCommand();
    case "route-gate":
        return RouteGateCommand(args);
    case "route-jump":
        return RouteJumpCommand(args);
    case "route-hybrid":
        return RouteHybridCommand(args);
    default:
        PrintUsage();
        return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        EvEMapEnhanced CLI

        Команды:
          sde-import <путь-к-zip>              Импортировать SDE из локального zip
          sde-download                          Скачать актуальный SDE и импортировать
          ships                                 Список известных капитальных корпусов
          route-gate <откуда> <куда> [--prefer shorter|safer|lowsec] [--avoid-lowsec] [--avoid-nullsec]
          route-jump <откуда> <куда> <корабль> [--jdc N] [--jfc N] [--covert]
          route-hybrid <откуда> <куда> <корабль> [--jdc N] [--jfc N] [--prefer shorter|safer|lowsec]
        """);
}

static async Task<int> SdeImportCommand(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Использование: sde-import <путь-к-zip>");
        return 1;
    }

    var importer = new SdeImporter();
    var summary = importer.ImportFromZip(args[1], AppPaths.SdeSqlitePath, ShipTypeCatalog.NamesToResolve());
    Console.WriteLine($"Импортировано: регионов={summary.Regions}, созвездий={summary.Constellations}, систем={summary.SolarSystems}, стargate-пар={summary.Stargates}, типов кораблей={summary.ShipTypesResolved}");
    Console.WriteLine($"Кэш: {AppPaths.SdeSqlitePath}");
    await Task.CompletedTask;
    return 0;
}

static async Task<int> SdeDownloadCommand()
{
    var service = new SdeService();
    Console.WriteLine("Скачивание актуального SDE с developers.eveonline.com...");
    var progress = new Progress<double>(p => Console.Write($"\rЗагрузка: {p:P0}   "));
    var summary = await service.DownloadAndImportAsync(progress);
    Console.WriteLine();
    Console.WriteLine($"Импортировано: регионов={summary.Regions}, созвездий={summary.Constellations}, систем={summary.SolarSystems}, стargate-пар={summary.Stargates}");
    return 0;
}

static int ShipsCommand()
{
    foreach (var group in ShipHulls.All.GroupBy(h => h.ShipClass))
    {
        Console.WriteLine($"{group.Key.ToRussianLabel()} (база {JumpMechanics.Get(group.Key).BaseRangeLy} LY, макс {JumpMechanics.Get(group.Key).MaxRangeLy} LY):");
        foreach (var hull in group)
        {
            Console.WriteLine($"  - {hull.Name} ({hull.Faction}), топливо {hull.BaseFuelPerLyIsotopes}/LY");
        }
    }
    return 0;
}

static UniverseMap LoadMap()
{
    if (!File.Exists(AppPaths.SdeSqlitePath))
    {
        throw new InvalidOperationException("SDE не импортирован. Выполните сначала sde-import или sde-download.");
    }
    var repo = new SdeRepository(AppPaths.SdeSqlitePath);
    return repo.BuildUniverseMap();
}

static RouteFilterOptions ParseRouteOptions(string[] args)
{
    var options = new RouteFilterOptions
    {
        AvoidLowSec = args.Contains("--avoid-lowsec"),
        AvoidNullSec = args.Contains("--avoid-nullsec"),
    };

    int preferIdx = Array.IndexOf(args, "--prefer");
    if (preferIdx >= 0 && preferIdx + 1 < args.Length)
    {
        options.Preference = args[preferIdx + 1].ToLowerInvariant() switch
        {
            "safer" or "highsec" => GateRoutePreference.Safer,
            "lowsec" or "lesssecure" => GateRoutePreference.LessSecure,
            _ => GateRoutePreference.Shorter,
        };
    }

    return options;
}

static int RouteGateCommand(string[] args)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Использование: route-gate <откуда> <куда> [--prefer shorter|safer|lowsec] [--avoid-lowsec] [--avoid-nullsec]");
        return 1;
    }

    var map = LoadMap();
    var from = map.FindByName(args[1]);
    var to = map.FindByName(args[2]);
    if (from is null || to is null)
    {
        Console.WriteLine("Система не найдена.");
        return 1;
    }

    var options = ParseRouteOptions(args);

    var route = GatePathfinder.FindRoute(map, from.Id, to.Id, options);
    if (route is null)
    {
        Console.WriteLine("Маршрут не найден.");
        return 1;
    }

    Console.WriteLine($"Маршрут по гейтам ({options.Preference}): {route.JumpCount} прыжков");
    foreach (int systemId in route.SystemIds)
    {
        var system = map.Get(systemId)!;
        Console.WriteLine($"  {system.Name}  (sec {system.Security:F1})");
    }
    return 0;
}

static (ShipHull Hull, PilotSkills Skills)? ParseShipAndSkills(string[] args, int shipArgIndex)
{
    var hull = ShipHulls.FindByName(args[shipArgIndex]);
    if (hull is null)
    {
        Console.WriteLine($"Неизвестный корабль: {args[shipArgIndex]}. Используйте команду 'ships' для списка.");
        return null;
    }

    var skills = new PilotSkills();
    skills.JumpDriveCalibration = GetIntOption(args, "--jdc", 0);
    skills.JumpFuelConservation = GetIntOption(args, "--jfc", 0);
    skills.JumpFreighters = GetIntOption(args, "--jf", 0);
    return (hull, skills);
}

static int GetIntOption(string[] args, string name, int fallback)
{
    int idx = Array.IndexOf(args, name);
    if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int value)) return value;
    return fallback;
}

static int RouteJumpCommand(string[] args)
{
    if (args.Length < 4)
    {
        Console.WriteLine("Использование: route-jump <откуда> <куда> <корабль> [--jdc N] [--jfc N] [--covert]");
        return 1;
    }

    var map = LoadMap();
    var from = map.FindByName(args[1]);
    var to = map.FindByName(args[2]);
    if (from is null || to is null)
    {
        Console.WriteLine("Система не найдена.");
        return 1;
    }

    var parsed = ParseShipAndSkills(args, 3);
    if (parsed is null) return 1;
    var (hull, skills) = parsed.Value;

    var method = args.Contains("--covert") ? JumpMethod.CovertCyno : JumpMethod.Cyno;
    var route = JumpPathfinder.FindRoute(map, hull, skills, from.Id, to.Id, method);
    if (route is null)
    {
        Console.WriteLine("Прыжковый маршрут не найден (возможно, нужна дозаправка через гейты).");
        return 1;
    }

    var sim = RouteSimulator.SimulateJumpRoute(route, hull, skills);
    Console.WriteLine($"Прыжковый маршрут на {hull.Name}: {route.JumpCount} прыжков, {route.TotalDistanceLy:F1} LY суммарно, дальность до {JumpSimulator.MaxRangeLy(hull, skills):F1} LY");
    foreach (var leg in sim.Legs)
    {
        var fromSys = map.Get(leg.Leg.FromSystemId)!;
        var toSys = map.Get(leg.Leg.ToSystemId)!;
        Console.WriteLine($"  {fromSys.Name} -> {toSys.Name}: {leg.Leg.DistanceLy:F2} LY, топливо {leg.Result.IsotopesUsed:F0}, cooldown {leg.Result.CooldownMinutes:F1} мин, фатига после {leg.Result.FatigueAfterMinutes:F1} мин");
    }
    Console.WriteLine($"Итого топливо: {sim.TotalFuel:F0}, пик фатиги: {sim.PeakFatigueMinutes:F1} мин");
    return 0;
}

static int RouteHybridCommand(string[] args)
{
    if (args.Length < 4)
    {
        Console.WriteLine("Использование: route-hybrid <откуда> <куда> <корабль> [--jdc N] [--jfc N]");
        return 1;
    }

    var map = LoadMap();
    var from = map.FindByName(args[1]);
    var to = map.FindByName(args[2]);
    if (from is null || to is null)
    {
        Console.WriteLine("Система не найдена.");
        return 1;
    }

    var parsed = ParseShipAndSkills(args, 3);
    if (parsed is null) return 1;
    var (hull, skills) = parsed.Value;

    var options = ParseRouteOptions(args);
    var route = HybridRouter.FindRoute(map, hull, skills, from.Id, to.Id, JumpMethod.Cyno, options);
    if (route is null)
    {
        Console.WriteLine("Маршрут не найден.");
        return 1;
    }

    double jumpLy = route.Steps.Where(s => s.DistanceLy.HasValue).Sum(s => s.DistanceLy!.Value);
    Console.WriteLine($"Гибридный маршрут: {route.GateJumps} гейтов + {route.CapitalJumps} прыжков ({jumpLy:F1} LY суммарно)");
    foreach (var step in route.Steps)
    {
        var fromSys = map.Get(step.FromSystemId)!;
        var toSys = map.Get(step.ToSystemId)!;
        string kind = step.Kind == RouteStepKind.Gate ? "гейт" : $"прыжок {step.DistanceLy:F2} LY";
        Console.WriteLine($"  {fromSys.Name} -> {toSys.Name}  [{kind}]");
    }
    return 0;
}
