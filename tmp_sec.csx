using EvEMapEnhanced.Data.Sde;
using EvEMapEnhanced.Core.Routing;
var repo = new SdeRepository(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EvEMapEnhanced", "sde", "sde.sqlite"));
var systems = repo.LoadSolarSystems();
var map = new UniverseMap(systems, repo.LoadStargates());
foreach (var name in new[] { "4-HWWF", "IPAY-2", "Erila", "Alikara", "Arvasaras", "Manjonakko" })
{
    var s = map.FindByName(name);
    if (s is null) Console.WriteLine($"{name}: NOT FOUND");
    else Console.WriteLine($"{name}: sec {s.Security:F2} high={s.IsHighSec} low={s.IsLowSec}");
}
