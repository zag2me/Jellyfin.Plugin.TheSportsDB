using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TheSportsDB.Providers
{
    public class TheSportsDBEpisodeProvider
        : IRemoteMetadataProvider<Episode, EpisodeInfo>, IRemoteImageProvider
    {
        private readonly TheSportsDbClient _client;
        private readonly ILogger<TheSportsDBEpisodeProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SportsResolverDb _sportsResolverDb;

        // For CleanFilename() only — never used to gate lookups
        // Order matters: longest/most-specific first
        private static readonly string[] LeagueNameStrips = new[]
        {
            "English Premier League",
            "Spanish La Liga",
            "ICC Mens T20 World Cup",
            "ICC Womens T20 World Cup",
            "ICC Cricket World Cup",
            "La Liga", "Spanish",
            "NHL", "EPL", "NFL", "NBA", "MLB", "UFC", "ICC",
        };
        private static readonly string[] SuffixStrips = new[]
        {
            "Early Prelims", "Early Card", "Main Card", "Main Event", "Prelims", "Fight-BB",
            "Kickoff", "Pre Show", "Post Show", "Weigh-in", "Face Off"
        };
        private static readonly string[] NoiseTags = new[]
        {
            "Fubo", "Peacock", "Sky", "TNT", "Amazon", "BBC", "ITV",
            "HDTV", "WEB-DL", "WEBRip", "X264", "H264", "X265", "H265", "HEVC", "AAC", "MKV", "MP4",
            "Full Match Replay", "Full Match", "Match Replay",
            "SkySportsOne", "SkySports", "SkySport",
            "MWR", "F1TV", "DDP51", "DDP5",
            "English", "HQ"
        };

        public string Name => "TheSportsDB";
        public bool Supports(BaseItem item) => item is Episode;

        public TheSportsDBEpisodeProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<TheSportsDBEpisodeProvider> logger,
            ILogger<TheSportsDbClient> clientLogger,
            IApplicationPaths applicationPaths)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _client = new TheSportsDbClient(httpClientFactory, clientLogger);
            string pluginLocation = typeof(TheSportsDBEpisodeProvider).Assembly.Location;
            string dbPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(pluginLocation) ?? "", "sports_resolver.db");
            _sportsResolverDb = new SportsResolverDb(dbPath);
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
            EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var apiResult = await _client.SearchEventsAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
            var list = new List<RemoteSearchResult>();
            foreach (var ev in apiResult?.events ?? apiResult?.@event ?? new List<Event>())
            {
                list.Add(new RemoteSearchResult
                {
                    Name = ev.strEvent,
                    ProviderIds = { { "TheSportsDB", ev.idEvent } },
                    ProductionYear = DateTime.TryParse(ev.dateEvent, out var d) ? d.Year : null,
                    ImageUrl = ev.strThumb,
                    PremiereDate = DateTime.TryParse(ev.dateEvent, out var dt) ? dt : null
                });
            }
            return list;
        }

        public async Task<MetadataResult<Episode>> GetMetadata(
            EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();

            // STEP 1: series name from path
            string? seriesName = GetSeriesNameFromPath(info.Path);
            _logger.LogInformation("TheSportsDB: item=\"{N}\" series=\"{S}\" path=\"{P}\"",
                info.Name, seriesName, info.Path);

            if (string.IsNullOrEmpty(seriesName))
            {
                _logger.LogWarning("TheSportsDB: Cannot determine series name from path.");
                return result;
            }

            // STEP 2: resolve leagueId — config first, DB alias second
            string? leagueId = null;
            var config = Plugin.Instance?.Configuration;

            if (config != null)
            {
                var mapping = config.LeagueMappings.FirstOrDefault(m =>
                    string.Equals(m.Name, seriesName, StringComparison.OrdinalIgnoreCase));
                if (mapping != null)
                {
                    leagueId = mapping.LeagueId;
                    _logger.LogInformation("TheSportsDB: Config mapping: \"{S}\" → \"{Id}\"", seriesName, leagueId);
                }
            }

            if (string.IsNullOrEmpty(leagueId))
            {
                leagueId = _sportsResolverDb.GetLeagueIdFromAlias(seriesName);
                if (!string.IsNullOrEmpty(leagueId))
                    _logger.LogInformation("TheSportsDB: DB alias: \"{S}\" → \"{Id}\"", seriesName, leagueId);
            }

            if (string.IsNullOrEmpty(leagueId))
            {
                _logger.LogWarning(
                    "TheSportsDB: No league mapping for \"{S}\". Add a mapping in Dashboard > Plugins > TheSportsDB.",
                    seriesName);
                return result;
            }

            // STEP 3: league slug
            string? leagueSlug = _sportsResolverDb.GetLeagueSlug(leagueId);
            _logger.LogInformation("TheSportsDB: Slug: \"{Slug}\"", leagueSlug ?? "(none)");

            // STEP 4: raw filename (always from path)
            string rawFilename = string.IsNullOrEmpty(info.Path)
                ? (info.Name ?? "")
                : System.IO.Path.GetFileNameWithoutExtension(info.Path);
            _logger.LogInformation("TheSportsDB: Raw filename: \"{Raw}\"", rawFilename);

            // STEP 5: extract date
            DateTime? fileDate = ExtractDate(rawFilename);
            _logger.LogInformation("TheSportsDB: Date: \"{D}\"", fileDate?.ToString("yyyy-MM-dd") ?? "none");

            if (!fileDate.HasValue)
            {
                _logger.LogWarning("TheSportsDB: No date in \"{Raw}\".", rawFilename);
                return result;
            }

            // STEP 6: find event
            Event? matched = await FindEventAsync(rawFilename, leagueId, leagueSlug, fileDate.Value, cancellationToken).ConfigureAwait(false);
            if (matched == null)
            {
                _logger.LogWarning("TheSportsDB: No match for \"{Raw}\".", rawFilename);
                return result;
            }

            // STEP 7: lookupevent
            _logger.LogInformation("TheSportsDB: Matched \"{E}\" id={Id}. Calling lookupevent.", matched.strEvent, matched.idEvent);
            var lr = await _client.GetEventAsync(matched.idEvent, cancellationToken).ConfigureAwait(false);
            var full = lr?.events?.FirstOrDefault() ?? lr?.@event?.FirstOrDefault() ?? matched;

            // STEP 8: build metadata
            result.HasMetadata = true;
            string title = full.strEvent ?? rawFilename;
            foreach (var sfx in new[] { "Early Prelims", "Prelims", "Main Card", "Weigh-in", "Post Show", "Pre Show", "Kickoff", "Press Conference" })
            {
                if (rawFilename.IndexOf(sfx, StringComparison.OrdinalIgnoreCase) >= 0
                    && title.IndexOf(sfx, StringComparison.OrdinalIgnoreCase) < 0)
                { title += $" ({sfx})"; break; }
            }

            result.Item = new Episode
            {
                Name = title,
                Overview = BuildOverview(full.strDescriptionEN),
                PremiereDate = DateTime.TryParse(full.dateEvent, out var pd) ? pd : null,
                ProductionYear = DateTime.TryParse(full.dateEvent, out var py) ? py.Year : null,
            };
            result.Item.ProviderIds["TheSportsDB"] = full.idEvent;

            if (!string.IsNullOrEmpty(full.strThumb))
                result.Item.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = full.strThumb }, 0);
            else if (!string.IsNullOrEmpty(full.strPoster))
                result.Item.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = full.strPoster }, 0);
            if (!string.IsNullOrEmpty(full.strFanart))
                result.Item.SetImage(new ItemImageInfo { Type = ImageType.Backdrop, Path = full.strFanart }, 0);

            return result;
        }

        private async Task<Event?> FindEventAsync(
            string rawFilename, string leagueId, string? leagueSlug,
            DateTime fileDate, CancellationToken cancellationToken)
        {
            string cleanName = CleanFilename(rawFilename);
            _logger.LogInformation("TheSportsDB: Clean: \"{C}\"", cleanName);

            string? teamA = null, teamB = null;
            bool hyphen = false;

            var vsIdx = cleanName.IndexOf(" vs ", StringComparison.OrdinalIgnoreCase);
            if (vsIdx > 0)
            {
                var rA = cleanName.Substring(0, vsIdx).Trim();
                var rB = cleanName.Substring(vsIdx + 4).Trim();
                teamA = _sportsResolverDb.GetTeamFullName(rA, leagueId) ?? rA;
                teamB = _sportsResolverDb.GetTeamFullName(rB, leagueId) ?? rB;
                _logger.LogInformation("TheSportsDB: Teams (vs): \"{A}\" vs \"{B}\"", teamA, teamB);
            }
            else
            {
                var parts = cleanName.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var abA = parts[parts.Length - 2].Trim();
                    var abB = parts[parts.Length - 1].Trim();
                    if (abA.Length <= 5 && abB.Length <= 5)
                    {
                        var xA = _sportsResolverDb.GetTeamFullName(abA, leagueId);
                        var xB = _sportsResolverDb.GetTeamFullName(abB, leagueId);
                        if (!string.IsNullOrEmpty(xA) && !string.IsNullOrEmpty(xB))
                        { teamA = xA; teamB = xB; hyphen = true;
                          _logger.LogInformation("TheSportsDB: Teams (hyphen): \"{A}\" vs \"{B}\"", teamA, teamB); }
                    }
                }
            }

            foreach (int offset in new[] { 0, 1, -1, 2, -2 })
            {
                var dp = fileDate.AddDays(offset);
                var dr = await _client.GetEventsByDayAsync(dp, null, leagueId, leagueSlug, cancellationToken).ConfigureAwait(false);
                var evList = dr?.events ?? dr?.@event;
                if (evList == null || evList.Count == 0) continue;

                // P1: strFilename
                foreach (var ev in evList)
                {
                    if (!string.IsNullOrEmpty(ev.strFilename)
                        && string.Equals(ev.strFilename.Trim(), rawFilename.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("TheSportsDB: P1 (strFilename): \"{F}\"", ev.strFilename);
                        return ev;
                    }
                }

                // P2/P3: team + date
                if (!string.IsNullOrEmpty(teamA) && !string.IsNullOrEmpty(teamB))
                {
                    static bool TeamMatch(string evTeam, string fileTeam) =>
                        (evTeam.Length >= 3 && fileTeam.Length >= 3)
                            ? (evTeam.Contains(fileTeam, StringComparison.OrdinalIgnoreCase) ||
                               fileTeam.Contains(evTeam, StringComparison.OrdinalIgnoreCase))
                            : string.Equals(evTeam, fileTeam, StringComparison.OrdinalIgnoreCase);

                    foreach (var ev in evList)
                    {
                        if (string.IsNullOrEmpty(ev.strHomeTeam) || string.IsNullOrEmpty(ev.strAwayTeam)) continue;
                        bool tm = (TeamMatch(ev.strHomeTeam, teamA) && TeamMatch(ev.strAwayTeam, teamB))
                               || (TeamMatch(ev.strHomeTeam, teamB) && TeamMatch(ev.strAwayTeam, teamA));
                        if (tm)
                        {
                            _logger.LogInformation("TheSportsDB: P{P} (teams): {H} vs {A}", hyphen ? "3" : "2", ev.strHomeTeam, ev.strAwayTeam);
                            return ev;
                        }
                    }
                }
            }

            _logger.LogWarning("TheSportsDB: No match in ±2d for \"{Raw}\".", rawFilename);
            return null;
        }

        private static string? GetSeriesNameFromPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (dir == null) return null;
            var folderName = System.IO.Path.GetFileName(dir);
            if (Regex.IsMatch(folderName ?? "", @"\d{4}|Season", RegexOptions.IgnoreCase))
                dir = System.IO.Path.GetDirectoryName(dir);
            return dir != null ? System.IO.Path.GetFileName(dir) : null;
        }

        private static DateTime? ExtractDate(string raw)
        {
            // YYYY-MM-DD
            var m1 = Regex.Match(raw, @"(\d{4})[\.\-_](\d{2})[\.\-_](\d{2})");
            if (m1.Success && int.TryParse(m1.Groups[1].Value, out int y1)
                && int.TryParse(m1.Groups[2].Value, out int mo1)
                && int.TryParse(m1.Groups[3].Value, out int d1))
            { try { return new DateTime(y1, mo1, d1); } catch { } }

            // DD-MM-YYYY
            var m2 = Regex.Match(raw, @"(\d{2})[\.\-_](\d{2})[\.\-_](\d{4})");
            if (m2.Success && int.TryParse(m2.Groups[3].Value, out int y2)
                && int.TryParse(m2.Groups[2].Value, out int mo2)
                && int.TryParse(m2.Groups[1].Value, out int d2))
            { try { return new DateTime(y2, mo2, d2); } catch { } }

            // YYYY ... DD MM
            var m3 = Regex.Match(raw, @"\b(19|20)(\d{2})\b.*?\b(\d{2})\s+(\d{2})\b");
            if (m3.Success && int.TryParse(m3.Groups[1].Value + m3.Groups[2].Value, out int y3)
                && int.TryParse(m3.Groups[3].Value, out int p1)
                && int.TryParse(m3.Groups[4].Value, out int p2))
            { try { return new DateTime(y3, p2, p1); } catch { } }

            return null;
        }

        private static string CleanFilename(string raw)
        {
            string name = raw.Replace('.', ' ');
            name = Regex.Replace(name, @"\bUtd\b", "United", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\b(RS|PS)\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"(\d{2})(\d{3,4}p)", "$1 $2");
            name = Regex.Replace(name, @"\b\d{3,4}p(?:[a-zA-Z0-9]+)?\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\b\d{2,3}fps\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\b\d{1,2}_\d{2}\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\bPart\s?\d+\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\bUFC\s\d{3}\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\bUFC\sFight\sNight\b", "", RegexOptions.IgnoreCase);
            // Strip "Match N" or "Round N" prefix patterns (e.g. "Match 52", "Round 24")
            name = Regex.Replace(name, @"\bMatch\s\d+\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\bRound\s\d+\b", "", RegexOptions.IgnoreCase);
            foreach (var t in NoiseTags)
                name = Regex.Replace(name, @"\b" + Regex.Escape(t) + @"\b", "", RegexOptions.IgnoreCase);
            foreach (var s in SuffixStrips)
                name = Regex.Replace(name, @"\b" + Regex.Escape(s) + @"\b", "", RegexOptions.IgnoreCase);
            foreach (var s in LeagueNameStrips)
                name = Regex.Replace(name, @"\b" + Regex.Escape(s) + @"\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\s+", " ").Trim();
            var mI = Regex.Match(name, @"(\d{4})[\.\-_](\d{2})[\.\-_](\d{2})");
            if (mI.Success) name = name.Replace(mI.Value, "").Trim();
            var mE = Regex.Match(name, @"(\d{2})[\.\-_](\d{2})[\.\-_](\d{4})");
            if (mE.Success) name = name.Replace(mE.Value, "").Trim();
            name = Regex.Replace(name, @"\b(19|20)\d{2}\b", "", RegexOptions.IgnoreCase);
            // Strip any isolated leading number that appears before "vs" (e.g. "268 Moreno vs Kavanagh" → "Moreno vs Kavanagh")
            name = Regex.Replace(name, @"^\d+\s+(?=\S)", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\s+", " ").Trim();
            return name.Trim('-', ' ', '~', '_');
        }

        private static string BuildOverview(string? desc)
        {
            if (string.IsNullOrEmpty(desc)) return "";
            desc = desc.Trim();
            if (desc.Length <= 1000) return desc;
            int i = desc.LastIndexOf('.', 1000);
            return i >= 0 ? desc.Substring(0, i + 1).Trim() + "..." : desc.Substring(0, 1000).Trim() + "...";
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
            => new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            var id = item.GetProviderId("TheSportsDB");
            if (string.IsNullOrEmpty(id)) return list;
            var r = await _client.GetEventAsync(id, cancellationToken).ConfigureAwait(false);
            var ev = r?.events?.FirstOrDefault() ?? r?.@event?.FirstOrDefault();
            if (ev == null) return list;
            if (!string.IsNullOrEmpty(ev.strThumb))
                list.Add(new RemoteImageInfo { Url = ev.strThumb, Type = ImageType.Primary, ProviderName = Name });
            else if (!string.IsNullOrEmpty(ev.strPoster))
                list.Add(new RemoteImageInfo { Url = ev.strPoster, Type = ImageType.Primary, ProviderName = Name });
            if (!string.IsNullOrEmpty(ev.strFanart))
                list.Add(new RemoteImageInfo { Url = ev.strFanart, Type = ImageType.Backdrop, ProviderName = Name });
            return list;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
            => _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }
}