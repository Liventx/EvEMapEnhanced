namespace EvEMapEnhanced.Data.Sde;

/// <summary>
/// Downloads the official CCP Static Data Export (JSON Lines format) from
/// developers.eveonline.com. This is the officially documented, always-current
/// download endpoint (see https://developers.eveonline.com/docs/services/static-data/).
/// </summary>
public sealed class SdeDownloader
{
    public const string LatestJsonlZipUrl = "https://developers.eveonline.com/static-data/eve-online-static-data-latest-jsonl.zip";

    private readonly HttpClient _httpClient;

    public SdeDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task DownloadLatestAsync(string destinationZipPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(LatestJsonlZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        string tempPath = destinationZipPath + ".part";

        await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
        await using (var fileStream = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;
                if (totalBytes is > 0)
                {
                    progress?.Report((double)totalRead / totalBytes.Value);
                }
            }
        }

        File.Move(tempPath, destinationZipPath, overwrite: true);
    }
}
