namespace EvEMapEnhanced.Data.Paths;

/// <summary>Filesystem locations for cached data and the user database.</summary>
public static class AppPaths
{
    public static string AppDataDir
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EvEMapEnhanced");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SdeCacheDir
    {
        get
        {
            string dir = Path.Combine(AppDataDir, "sde");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SdeZipPath => Path.Combine(SdeCacheDir, "sde-latest.zip");
    public static string SdeSqlitePath => Path.Combine(SdeCacheDir, "sde.sqlite");
    public static string UserDbPath => Path.Combine(AppDataDir, "user.sqlite");
}
