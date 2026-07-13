using EvEMapEnhanced.Data.Paths;

namespace EvEMapEnhanced.Data.Sde;

/// <summary>
/// High-level orchestration: ensures a local SDE cache exists (downloading and importing
/// on first run, per the "download on first launch" strategy), and hands back a ready
/// <see cref="SdeRepository"/> for building the routing <c>UniverseMap</c>.
/// </summary>
public sealed class SdeService
{
    private readonly string _zipPath;
    private readonly string _sqlitePath;

    public SdeService(string? zipPath = null, string? sqlitePath = null)
    {
        _zipPath = zipPath ?? AppPaths.SdeZipPath;
        _sqlitePath = sqlitePath ?? AppPaths.SdeSqlitePath;
    }

    public bool IsCached()
    {
        return File.Exists(_sqlitePath) && new SdeRepository(_sqlitePath).HasData();
    }

    /// <summary>
    /// Ensures a usable SDE SQLite cache exists: returns immediately when already cached,
    /// otherwise imports from a previously downloaded archive or downloads the latest SDE.
    /// </summary>
    public async Task<(bool AlreadyCached, ImportSummary? Summary)> EnsureCachedAsync(
        IProgress<double>? downloadProgress = null,
        CancellationToken ct = default)
    {
        if (IsCached())
            return (true, null);

        if (File.Exists(_zipPath))
        {
            try
            {
                var summary = await Task.Run(() =>
                {
                    var importer = new SdeImporter();
                    return importer.ImportFromZip(_zipPath, _sqlitePath, ShipTypeCatalog.NamesToResolve());
                }, ct);

                if (IsCached())
                    return (false, summary);
            }
            catch
            {
                // Corrupt or incomplete archive — fall through to a fresh download.
            }
        }

        var downloaded = await DownloadAndImportAsync(downloadProgress, ct);
        return (false, downloaded);
    }

    public async Task<ImportSummary> DownloadAndImportAsync(IProgress<double>? downloadProgress = null, CancellationToken ct = default)
    {
        var downloader = new SdeDownloader();
        await downloader.DownloadLatestAsync(_zipPath, downloadProgress, ct);

        var importer = new SdeImporter();
        return importer.ImportFromZip(_zipPath, _sqlitePath, ShipTypeCatalog.NamesToResolve());
    }

    /// <summary>
    /// Re-imports from the already-downloaded SDE archive on disk (no network), used to backfill
    /// newly-tracked tables (e.g. NPC stations) into an existing cache without forcing a full
    /// re-download. Returns false when no cached archive is available.
    /// </summary>
    public async Task<bool> TryReimportFromCachedZipAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_zipPath)) return false;

        await Task.Run(() =>
        {
            var importer = new SdeImporter();
            importer.ImportFromZip(_zipPath, _sqlitePath, ShipTypeCatalog.NamesToResolve());
        }, ct);
        return true;
    }

    public SdeRepository GetRepository() => new(_sqlitePath);
}
