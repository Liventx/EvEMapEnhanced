using System.IO.Compression;
using System.Text;

namespace EvEMapEnhanced.Data.Tests;

/// <summary>
/// Builds a minimal, schema-correct SDE JSON Lines zip (matching the official CCP
/// developers.eveonline.com format) with a handful of systems/gates/regions and two
/// ship types (Archon, Capsule), so importer/repository tests don't need network access
/// or the full ~94MB production SDE archive.
/// </summary>
internal static class MiniSdeFixture
{
    public const int RegionId = 10000001;
    public const int ConstellationId = 20000001;
    public const int SystemAId = 30000001; // "Alpha"
    public const int SystemBId = 30000002; // "Bravo"
    public const int SystemCId = 30000003; // "Charlie"
    public const int ArchonTypeId = 23757;
    public const int CapsuleTypeId = 670;
    public const int ShuttleTypeId = 67000;
    public const int CorvetteTypeId = 67001;

    public static string CreateZip(string path)
    {
        if (File.Exists(path)) File.Delete(path);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        AddEntry(zip, "mapRegions.jsonl", Line(
            "{\"_key\": {0}, \"name\": {\"en\": \"TestRegion\"}, \"position\": {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0}}",
            RegionId));

        AddEntry(zip, "mapConstellations.jsonl", Line(
            "{\"_key\": {0}, \"name\": {\"en\": \"TestConstellation\"}, \"regionID\": {1}, \"position\": {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0}}",
            ConstellationId, RegionId));

        AddEntry(zip, "mapSolarSystems.jsonl", string.Join('\n', new[]
        {
            Line("{\"_key\": {0}, \"name\": {\"en\": \"Alpha\"}, \"constellationID\": {1}, \"regionID\": {2}, \"securityStatus\": 0.9, \"position\": {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0}}", SystemAId, ConstellationId, RegionId),
            Line("{\"_key\": {0}, \"name\": {\"en\": \"Bravo\"}, \"constellationID\": {1}, \"regionID\": {2}, \"securityStatus\": 0.8, \"position\": {\"x\": 9.4607e15, \"y\": 0.0, \"z\": 0.0}}", SystemBId, ConstellationId, RegionId),
            Line("{\"_key\": {0}, \"name\": {\"en\": \"Charlie\"}, \"constellationID\": {1}, \"regionID\": {2}, \"securityStatus\": 0.3, \"position\": {\"x\": 1.89214e16, \"y\": 0.0, \"z\": 0.0}}", SystemCId, ConstellationId, RegionId),
        }));

        AddEntry(zip, "mapStargates.jsonl", string.Join('\n', new[]
        {
            Line("{\"_key\": 50000001, \"solarSystemID\": {0}, \"destination\": {\"solarSystemID\": {1}, \"stargateID\": 50000002}, \"position\": {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0}, \"typeID\": 16}", SystemAId, SystemBId),
            Line("{\"_key\": 50000002, \"solarSystemID\": {0}, \"destination\": {\"solarSystemID\": {1}, \"stargateID\": 50000001}, \"position\": {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0}, \"typeID\": 16}", SystemBId, SystemAId),
            Line("{\"_key\": 50000003, \"solarSystemID\": {0}, \"destination\": {\"solarSystemID\": {1}, \"stargateID\": 50000004}, \"position\": {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0}, \"typeID\": 16}", SystemBId, SystemCId),
            Line("{\"_key\": 50000004, \"solarSystemID\": {0}, \"destination\": {\"solarSystemID\": {1}, \"stargateID\": 50000003}, \"position\": {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0}, \"typeID\": 16}", SystemCId, SystemBId),
        }));

        AddEntry(zip, "types.jsonl", string.Join('\n', new[]
        {
            Line("{\"_key\": {0}, \"name\": {\"en\": \"Archon\"}, \"groupID\": 547, \"mass\": 1260000000.0, \"published\": true}", ArchonTypeId),
            Line("{\"_key\": {0}, \"name\": {\"en\": \"Capsule\"}, \"groupID\": 29, \"mass\": 32000.0, \"published\": true}", CapsuleTypeId),
            Line("{\"_key\": {0}, \"name\": {\"en\": \"Ibis\"}, \"groupID\": 31, \"mass\": 1049000.0, \"published\": true}", ShuttleTypeId),
            Line("{\"_key\": {0}, \"name\": {\"en\": \"Velator\"}, \"groupID\": 237, \"mass\": 1049000.0, \"published\": true}", CorvetteTypeId),
            "{\"_key\": 587, \"name\": {\"en\": \"Rifter\"}, \"groupID\": 25, \"mass\": 1067000.0, \"published\": true}",
        }));

        return path;
    }

    /// <summary>Formats a JSON template using {0},{1},... placeholders while leaving literal JSON braces untouched.</summary>
    private static string Line(string template, params object[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            template = template.Replace("{" + i + "}", args[i].ToString());
        }
        return template;
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
