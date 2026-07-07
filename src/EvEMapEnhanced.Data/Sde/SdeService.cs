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

    public async Task<ImportSummary> DownloadAndImportAsync(IProgress<double>? downloadProgress = null, CancellationToken ct = default)
    {
        var downloader = new SdeDownloader();
        await downloader.DownloadLatestAsync(_zipPath, downloadProgress, ct);

        var importer = new SdeImporter();
        return importer.ImportFromZip(_zipPath, _sqlitePath, ShipTypeCatalog.NamesToResolve());
    }

    public SdeRepository GetRepository() => new(_sqlitePath);
}
