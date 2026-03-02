using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TheSportsDB.Providers
{
    public class DbUpdateService : IHostedService
    {
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/retrorat1/Jellyfin.Plugin.TheSportsDB/releases/tags/db-latest";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<DbUpdateService> _logger;

        public DbUpdateService(
            IHttpClientFactory httpClientFactory,
            IApplicationPaths appPaths,
            ILogger<DbUpdateService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _appPaths = appPaths;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnableDbAutoUpdate)
            {
                _logger.LogInformation("TheSportsDB: DB auto-update is disabled.");
                return Task.CompletedTask;
            }
            _ = Task.Run(() => CheckAndUpdateAsync(cancellationToken), cancellationToken);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task CheckAndUpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-TheSportsDB-Plugin/1.0");
                client.Timeout = TimeSpan.FromSeconds(10);

                // Fetch release metadata
                var release = await client
                    .GetFromJsonAsync<GithubRelease>(ReleasesApiUrl, cancellationToken)
                    .ConfigureAwait(false);

                if (release == null)
                {
                    _logger.LogWarning("TheSportsDB: DB update check returned null response.");
                    return;
                }

                string remoteTag = release.TagName ?? "";

                // Find the DB asset
                GithubAsset? asset = null;
                if (release.Assets != null)
                {
                    foreach (var a in release.Assets)
                    {
                        if (string.Equals(a.Name, "sports_resolver.db",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            asset = a;
                            break;
                        }
                    }
                }

                if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                {
                    _logger.LogWarning(
                        "TheSportsDB: DB update check found tag {T} but no sports_resolver.db asset.",
                        remoteTag);
                    return;
                }

                // Use asset updated_at timestamp as the version key (not tag name)
                if (asset.UpdatedAt == null)
                    _logger.LogWarning("TheSportsDB: Asset has no updated_at timestamp; falling back to tag name for version check.");
                string remoteTimestamp = asset.UpdatedAt ?? remoteTag;
                string versionFile = Path.Combine(_appPaths.DataPath, "sports_resolver_version.txt");
                string localTimestamp = File.Exists(versionFile)
                    ? File.ReadAllText(versionFile).Trim()
                    : "";

                if (string.Equals(remoteTimestamp, localTimestamp, StringComparison.Ordinal))
                {
                    _logger.LogInformation("TheSportsDB: DB is up to date (timestamp: {V}).", remoteTimestamp);
                    return;
                }

                _logger.LogInformation("TheSportsDB: Downloading DB update (timestamp: {T})", remoteTimestamp);

                var bytes = await client.GetByteArrayAsync(asset.BrowserDownloadUrl, cancellationToken)
                    .ConfigureAwait(false);

                string destPath = Path.Combine(_appPaths.DataPath, "sports_resolver.db");
                string tempPath = destPath + ".tmp";
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken)
                    .ConfigureAwait(false);
                File.Move(tempPath, destPath, overwrite: true);
                await File.WriteAllTextAsync(versionFile, remoteTimestamp, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation("TheSportsDB: DB updated successfully (timestamp: {T}).", remoteTimestamp);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "TheSportsDB: DB update check failed: {E}", ex.Message);
            }
        }

        private class GithubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("assets")]
            public GithubAsset[]? Assets { get; set; }
        }

        private class GithubAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }

            [JsonPropertyName("updated_at")]
            public string? UpdatedAt { get; set; }
        }
    }
}
