using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TheSportsDB.Providers
{
    public class TheSportsDBEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IRemoteImageProvider
    {
        private readonly TheSportsDbClient _client;
        private readonly ILogger<TheSportsDBEpisodeProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SportsResolverDb _sportsResolverDb; // Added

        // Remove KnownLeagueIds for DB-powered mapping

        private static readonly string[] LeagueNameStrips = new[]
        {
            "English Premier League", "NHL", "EPL", "NFL", "NBA", "MLB", "UFC", "La Liga", "Spanish La Liga"
        };
        private static readonly string[] SuffixStrips = new[]
        {
            "Early Prelims", "Early Card", "Main Card", "Main Event", "Prelims", "Fight-BB",
            "Kickoff", "Pre Show", "Post Show", "Weigh-in", "Face Off"
        };

        public string Name => "TheSportsDB";
        public bool Supports(BaseItem item) => item is Episode;

        // --- Constructor now requires SportsResolverDb ---
        public TheSportsDBEpisodeProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<TheSportsDBEpisodeProvider> logger,
            ILogger<TheSportsDbClient> clientLogger,
            IApplicationPaths applicationPaths
        )
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _client = new TheSportsDbClient(httpClientFactory, clientLogger);
            
            // Locate the DB file relative to the plugin assembly
            // Use typeof(...) to avoid reliance on Plugin.Instance which might be null during early init
            string pluginLocation = typeof(TheSportsDBEpisodeProvider).Assembly.Location;
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(pluginLocation) ?? "", "sports_resolver.db");
            _sportsResolverDb = new SportsResolverDb(dbPath);
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            _logger.LogInformation("TheSportsDB: Searching events for {Name}", searchInfo.Name);
            var result = await _client.SearchEventsAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
            var list = new List<RemoteSearchResult>();

            var eventList = result?.events ?? result?.@event;
            if (eventList != null)
            {
                foreach (var ev in eventList)
                {
                    list.Add(new RemoteSearchResult
                    {
                        Name = ev.strEvent,
                        ProviderIds = { { "TheSportsDB", ev.idEvent } },
                        ProductionYear = DateTime.TryParse(ev.dateEvent, out var date) ? date.Year : null,
                        ImageUrl = ev.strThumb,
                        PremiereDate = DateTime.TryParse(ev.dateEvent, out var dt) ? dt : null
                    });
                }
            }
            return list;
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();
            
            var rawName = info.Name;
            var path = info.Path;
            // EpisodeInfo might not have SeriesName locally, rely on path parsing
            string? seriesName = null;
            
            if (!string.IsNullOrEmpty(path))
            {
                seriesName = GetSeriesNameFromPath(path);
                _logger.LogInformation("TheSportsDB: Series Name resolved from path: \"{SeriesName}\"", seriesName);
            }
            
            _logger.LogInformation("TheSportsDB: GetMetadata called for Item: \"{ItemName}\", Series: \"{SeriesName}\", Folder: \"{Path}\"", rawName, seriesName, info.Path);

            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                 _logger.LogWarning("TheSportsDB: Configuration is null.");
            }

            // Check mappings
            string? leagueId = null;
            if (config != null && !string.IsNullOrEmpty(seriesName))
            {
                var mapping = config.LeagueMappings.FirstOrDefault(m => string.Equals(m.Name, seriesName, StringComparison.OrdinalIgnoreCase));
                if (mapping != null)
                {
                    leagueId = mapping.LeagueId;
                     _logger.LogInformation("TheSportsDB: Series \"{Series}\" mapped found in Config -> LeagueId: \"{Id}\"", seriesName, leagueId);
                }
            }

            // Fallback: If no mapping in config, try DB alias lookup (e.g. Folder "NHL" -> 4380)
            if (string.IsNullOrEmpty(leagueId) && !string.IsNullOrEmpty(seriesName))
            {
                leagueId = _sportsResolverDb.GetLeagueIdFromAlias(seriesName);
                if (!string.IsNullOrEmpty(leagueId))
                {
                    _logger.LogInformation("TheSportsDB: Series \"{Series}\" resolved via DB Alias to LeagueId: \"{Id}\"", seriesName, leagueId);
                }
            }

            // --- GET League Slug (API Name) from the DB ---
            string? leagueSlug = null;
            if (!string.IsNullOrEmpty(leagueId))
            {
                leagueSlug = _sportsResolverDb.GetLeagueSlug(leagueId);
            }
            
            string? sportName = null; 
            _logger.LogInformation("TheSportsDB: Resolved League Slug: \"{Slug}\"", leagueSlug);

            // Use filename if info.Name seems to be just the series name (common Jellyfin issue with sports)
            if (!string.IsNullOrEmpty(info.Path))
            {
                string filename = System.IO.Path.GetFileNameWithoutExtension(info.Path);
                // Heuristic: If name is short or equals series/league, prefer filename
                // Using rawName check against strips
                if (rawName.Length <= 4 || 
                    string.Equals(rawName, seriesName, StringComparison.OrdinalIgnoreCase) ||
                    LeagueNameStrips.Any(s => string.Equals(rawName, s, StringComparison.OrdinalIgnoreCase)))
                {
                     _logger.LogInformation("TheSportsDB: info.Name \"{Name}\" seems invalid. Using filename \"{Filename}\" instead.", rawName, filename);
                     rawName = filename;
                }
            }

            string cleanName = CleanEpisodeName(rawName, out string? cardType, out DateTime? date);

            // --- Try to resolve team names (normalize for matching) ---
            string resolvedCleanName = cleanName;
            var vsIdx = cleanName.IndexOf(" vs ", StringComparison.OrdinalIgnoreCase);
            if (vsIdx > 0 && leagueId != null)
            {
                var teamAAbbr = cleanName.Substring(0, vsIdx).Trim();
                var teamBAbbr = cleanName.Substring(vsIdx + 4).Trim();
                // Updated DB method:
                var teamAname = _sportsResolverDb.GetTeamFullName(teamAAbbr, leagueId);
                var teamBname = _sportsResolverDb.GetTeamFullName(teamBAbbr, leagueId);
                if (!string.IsNullOrEmpty(teamAname) && !string.IsNullOrEmpty(teamBname))
                {
                    resolvedCleanName = $"{teamAname} vs {teamBname}";
                }
            }
            else if (cleanName.Contains("-") && leagueId != null) 
            {
               // Try split by hyphen for cases like "VGK-LAK" or "2026-01-26-EDM-ANA" (if date wasn't fully cleaned)
               var parts = cleanName.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
               if (parts.Length >= 2)
               {
                   // Assume the last two parts are the teams if there are multiple segments (e.g. date residue)
                   var teamAAbbr = parts[parts.Length - 2].Trim();
                   var teamBAbbr = parts[parts.Length - 1].Trim();
                   
                   // Ensure these look like abbreviations or short names
                   if (teamAAbbr.Length <= 5 && teamBAbbr.Length <= 5)
                   {
                        var teamAname = _sportsResolverDb.GetTeamFullName(teamAAbbr, leagueId);
                        var teamBname = _sportsResolverDb.GetTeamFullName(teamBAbbr, leagueId);
                        
                        if (!string.IsNullOrEmpty(teamAname) && !string.IsNullOrEmpty(teamBname))
                        {
                            resolvedCleanName = $"{teamAname} vs {teamBname}";
                            // Also update rawName alias for matching if we successfully resolved teams from codes
                            // This helps FindMatchAsync trust this resolved name over the obscure "EDM-ANA" string
                        }
                   }
               }
            }

            // --- Pass leagueSlug as leagueName parameter ---
            var eventMatch = await FindMatchWithSwapAndCleanAsync(resolvedCleanName, rawName, leagueId, sportName, leagueSlug, date, cancellationToken);

            // result already instantiated at top
            if (eventMatch != null)
            {
                result.HasMetadata = true;
                
                // Construct the display title
                // Start with the API Event Name (e.g. "Volkanovski vs Lopes")
                var displayTitle = eventMatch.strEvent;

                // Check for specific suffixes in the ORIGINAL filename to append to the title
                // This preserves "Prelims", "Early Prelims", "Weigh-in" etc. in the UI
                var suffixes = new[] { "Early Prelims", "Prelims", "Main Card", "Weigh-in", "Post Show", "Pre Show", "Kickoff", "Press Conference" };
                foreach (var s in suffixes)
                {
                    if (rawName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Avoid duplication if the API name already has it
                        if (displayTitle.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            displayTitle += $" ({s})";
                        }
                        break; // Only append the first/most significant matching suffix
                    }
                }

                result.Item = new Episode
                {
                    Name = displayTitle,
                    Overview = BuildOverview(eventMatch.strDescriptionEN, eventMatch.strEvent),
                    PremiereDate = DateTime.TryParse(eventMatch.dateEvent, out var d) ? d : null,
                    ProductionYear = DateTime.TryParse(eventMatch.dateEvent, out var d2) ? d2.Year : null,
                };
                result.Item.ProviderIds["TheSportsDB"] = eventMatch.idEvent;

                // --- Map Images ---
                if (!string.IsNullOrEmpty(eventMatch.strThumb))
                {
                     result.Item.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = eventMatch.strThumb }, 0);
                }
                else if (!string.IsNullOrEmpty(eventMatch.strPoster)) // Fallback to poster if thumb is missing (common for PPVs)
                {
                     result.Item.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = eventMatch.strPoster }, 0);
                }

                if (!string.IsNullOrEmpty(eventMatch.strFanart))
                {
                     result.Item.SetImage(new ItemImageInfo { Type = ImageType.Backdrop, Path = eventMatch.strFanart }, 0);
                }
            }
            return result;
        }

        private static string? GetSeriesNameFromPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (dir == null) return null;
            var folderName = System.IO.Path.GetFileName(dir);
            if (Regex.IsMatch(folderName ?? "", @"\d{4}|Season", RegexOptions.IgnoreCase))
                dir = System.IO.Path.GetDirectoryName(dir);
            return dir != null ? System.IO.Path.GetFileName(dir) : null;
        }

        private static string CleanEpisodeName(string raw, out string? cardType, out DateTime? fileDate)
        {
            string name = raw;
            name = name.Replace('.', ' '); // This replaces dots with spaces GLOBALLY first.
            // Wait, if we replace dots with spaces first, then '07.02.2026' becomes '07 02 2026'.
            // The regex '(\d{4})[ _\-](\d{2})...' matches space ' ', passed.
            // But let's verify if the user meant preserving dots for matching or just general cleanup.
            // The existing code line 188: name = name.Replace('.', ' '); destroys dots.
            // So '07.02.2026' -> '07 02 2026'.
            // Regex 1: `(\d{4})[ _\-](\d{2})[ _\-](\d{2})`. matches `2026 02 07`.
            // Regex 2: `(\d{2})[ _\-]?(\d{2})[ _\-]?(\d{2,4})`. matches `07 02 2026`.
            // So effectively, dot support is already there via the Replace call. 
            // However, let's optimize to be safer and not rely on global replace if possible, 
            // but the global replace is for "Ep.1" etc.
            
            // Let's stick to the Replace approach but ensure the regex covers the resulting space.
            // Current regex `[ _\-]` covers space.
            
            // Re-applying the method with slight robustness improvements for years.
            
            name = Regex.Replace(name, @"\bUtd\b", "United", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\b(RS|PS)\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"(\d{2})(\d{3,4}p)", "$1 $2");
            // Enhanced noise removal:
            // Matches 720p, 1080p, 720pEN, 720pEN60fps etc.
            name = Regex.Replace(name, @"\b\d{3,4}p(?:[a-zA-Z0-9]+)?\b", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\b\d{2,3}fps\b", "", RegexOptions.IgnoreCase);
            
            // Strip part numbers like "2_02", "Part 1", "CD1"
            name = Regex.Replace(name, @"\b\d{1,2}_\d{2}\b", "", RegexOptions.IgnoreCase); // 2_02
            name = Regex.Replace(name, @"\bPart\s?\d+\b", "", RegexOptions.IgnoreCase);

            // Strip Event Prefixes that might confuse the strict matcher (e.g. "UFC 325 Volkanovski" -> "Volkanovski")
            // TSDB often lists events as just "Fighter A vs Fighter B" without the "UFC 123" prefix
            name = Regex.Replace(name, @"\bUFC\s\d{3}\b", "", RegexOptions.IgnoreCase); 
            name = Regex.Replace(name, @"\bUFC\sFight\sNight\b", "", RegexOptions.IgnoreCase);

            var noiseTags = new[] { "Fubo", "Peacock", "Sky", "TNT", "Amazon", "BBC", "ITV", "HDTV", "WEB-DL", "WEBRip", "X264", "H264", "X265", "H265", "HEVC", "AAC", "MKV", "MP4" };
            foreach (var tag in noiseTags)
            {
                name = Regex.Replace(name, @"\b" + Regex.Escape(tag) + @"\b", "", RegexOptions.IgnoreCase);
            }
            
            cardType = null;
            foreach (var s in SuffixStrips)
            {
                if (name.Contains(s, StringComparison.OrdinalIgnoreCase))
                    cardType = s;
                name = Regex.Replace(name, @"\b" + Regex.Escape(s) + @"\b", "", RegexOptions.IgnoreCase);
            }
            foreach (var s in LeagueNameStrips)
            {
                name = Regex.Replace(name, @"\b" + Regex.Escape(s) + @"\b", "", RegexOptions.IgnoreCase);
            }
            name = Regex.Replace(name, @"\s+", " ").Trim();

            fileDate = null;
            // Try matching YYYY-MM-DD (or space/dot separated due to replace)
            // Try matching YYYY-MM-DD (or space/dot separated due to replace)
            var mIso = Regex.Match(name, @"(\d{4})[ \.\-_](\d{2})[ \.\-_](\d{2})");
            if (mIso.Success)
            {
                if (int.TryParse(mIso.Groups[1].Value, out int year) &&
                    int.TryParse(mIso.Groups[2].Value, out int month) &&
                    int.TryParse(mIso.Groups[3].Value, out int day))
                {
                     try { 
                        fileDate = new DateTime(year, month, day); 
                        // _logger would be nice here but it's static method or needs instance.
                        // We will rely on the caller logging the result.
                     } catch { }
                     name = name.Replace(mIso.Value, "").Trim();
                }
            }
            else
            {
                // Fallback to DD-MM-YYYY or MM-DD-YYYY
                // Expanded to allow dot explicitly if the global replace didn't happen yet (it did, but regex is safer)
                var m = Regex.Match(name, @"(\d{2})[ \.\-_]?(\d{2})[ \.\-_]?(\d{4})");
                if (m.Success)
                {
                    if (int.TryParse(m.Groups[3].Value, out int year) &&
                        int.TryParse(m.Groups[2].Value, out int month) &&
                        int.TryParse(m.Groups[1].Value, out int day))
                    {
                         try { fileDate = new DateTime(year, month, day); } catch { }
                         name = name.Replace(m.Value, "").Trim();
                    }
                }
                else
                {
                    // Fallback for split date: "EPL 2026 Arsenal vs Sunderland 07 02"
                    // Looks for Year (19xx or 20xx) ... DD MM
                    var mSplit = Regex.Match(name, @"\b(19|20)(\d{2})\b.*?\b(\d{2})\s+(\d{2})\b");
                    if (mSplit.Success)
                    {
                        // Groups: 1=Century, 2=YearLast2, 3=PartA, 4=PartB
                        string yearStr = mSplit.Groups[1].Value + mSplit.Groups[2].Value;
                        if (int.TryParse(yearStr, out int year) &&
                            int.TryParse(mSplit.Groups[3].Value, out int p1) &&
                            int.TryParse(mSplit.Groups[4].Value, out int p2))
                        {
                            // Assume DD MM first, so p1=Day, p2=Month
                            try { 
                                fileDate = new DateTime(year, p2, p1); 
                                // Remove the year and the date parts
                                name = name.Replace(yearStr, "").Replace($"{mSplit.Groups[3].Value} {mSplit.Groups[4].Value}", "").Trim();
                            } 
                            catch 
                            { 
                                // valid datetime failed? Try swapping?
                            }
                        }
                    }
                }
            }
            
            // Clean up any double spaces or leading/trailing hyphens/tildes left by date removal or prefix stripping
            name = Regex.Replace(name, @"\s+", " ").Trim();
            name = name.Trim('-', ' ', '~', '_');

            return name;
        }

        private async Task<Event?> FindMatchWithSwapAndCleanAsync(string cleanName, string rawName, string? leagueId, string? sportName, string? leagueName, DateTime? fileDate, CancellationToken cancellationToken)
        {
            Event? match = null;
            match = await FindMatchAsync(cleanName, rawName, leagueId, sportName, leagueName, fileDate, cancellationToken);
            if (match != null) return match;

            if (fileDate.HasValue)
            {
                var vsIdx = cleanName.IndexOf(" vs ", StringComparison.OrdinalIgnoreCase);
                if (vsIdx > 0)
                {
                    var teamA = cleanName.Substring(0, vsIdx).Trim();
                    var teamB = cleanName.Substring(vsIdx + 4).Trim();
                    string swapped = $"{teamB} vs {teamA}";
                    match = await FindMatchAsync(swapped, rawName, leagueId, sportName, leagueName, fileDate, cancellationToken);
                }
            }
            return match;
        }

        private async Task<Event?> FindMatchAsync(string name, string rawName, string? leagueId, string? sportName, string? leagueName, DateTime? fileDate, CancellationToken cancellationToken)
        {
            // 1. Try Date-based lookup if possible
            if (leagueId != null && fileDate.HasValue)
            {
                // Expanded date offsets to handle significant timezone differences (UTC vs Local)
                // e.g. File says 26th, API says 27th (UTC) or 25th (Local).
                int[] dateOffsets = new[] { 0, 1, -1, 2, -2 };
                foreach (int offset in dateOffsets)
                {
                    DateTime dateParam = fileDate.Value.AddDays(offset);
                    _logger.LogInformation("TheSportsDB: Searching events by day for LeagueId: {LeagueId}, Sport: {Sport}, LeagueName: {LeagueName}, Date: {Date}", leagueId, sportName, leagueName, dateParam.ToString("yyyy-MM-dd"));
                    
                    var eventsResult = await _client.GetEventsByDayAsync(dateParam, sportName, leagueId, leagueName, cancellationToken).ConfigureAwait(false);
                    var evList = eventsResult?.events ?? eventsResult?.@event;
                    
                    if (evList != null && evList.Count > 0)
                    {
                        _logger.LogInformation("TheSportsDB: Found {Count} events for query.", evList.Count);
                        foreach (var ev in evList)
                        {
                            // CHECK 1: MATCH STRFILENAME (Exact match check)
                            // Priority: High. If the API explicitly names the file consistently with our file, trust it above all else.
                            // This bypasses LeagueId mismatches (e.g. if local DB has wrong ID but event is valid).
                            if (!string.IsNullOrEmpty(ev.strFilename) && !string.IsNullOrEmpty(rawName))
                            {
                                if (string.Equals(ev.strFilename.Trim(), rawName.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogInformation("TheSportsDB: Exact match found via strFilename: \"{StrFilename}\"", ev.strFilename);
                                    return ev;
                                }
                            }

                            // Filter by LeagueId if provided (since we removed &l= from API call)
                            if (leagueId != null && !string.Equals(ev.idLeague, leagueId, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            // CHECK 2: Loose Team Match (Order Agnostic) if we have resolved Team Names
                            // If cleanName looks like "TeamA vs TeamB", check if event contains BOTH.
                            // Useful for "EDM vs ANA" mapping to "Anaheim Ducks vs Edmonton Oilers" on the same day.
                            if (name.Contains(" vs ", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = name.Split(new[] { " vs " }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length == 2)
                                {
                                    var t1 = parts[0].Trim();
                                    var t2 = parts[1].Trim();
                                    bool hasT1 = ev.strEvent.Contains(t1, StringComparison.OrdinalIgnoreCase);
                                    bool hasT2 = ev.strEvent.Contains(t2, StringComparison.OrdinalIgnoreCase);

                                    _logger.LogInformation("TheSportsDB: Checking Loose Match: \"{T1}\" & \"{T2}\" in Event \"{Event}\" -> {HasT1}/{HasT2}", t1, t2, ev.strEvent, hasT1, hasT2);

                                    if (hasT1 && hasT2)
                                    {
                                        _logger.LogInformation("TheSportsDB: Loose Team Match (Both teams found, order ignored): \"{Event}\" matches \"{Name}\"", ev.strEvent, name);
                                        return ev;
                                    }
                                }
                            }

                            // CHECK 3: Standard IsEventMatch
                            bool isMatch = IsEventMatch(ev.strEvent, name);
                            _logger.LogInformation("TheSportsDB: Comparing event \"{EventName}\" (League: {EvLeague}) with \"{FileName}\" -> Match: {Match}", ev.strEvent, ev.strLeague, name, isMatch);
                            if (isMatch)
                            {
                                return ev;
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation("TheSportsDB: No events found for query.");
                    }
                }
            }

            // 2. Fallback: Search by name (if date lookup failed or didn't exist)
            if (!string.IsNullOrWhiteSpace(name))
            {
                _logger.LogInformation("TheSportsDB: Searching events by name (fallback): {Name}", name);
                var searchResult = await _client.SearchEventsAsync(name, cancellationToken).ConfigureAwait(false);
                var evList = searchResult?.events ?? searchResult?.@event;
                
                if (evList != null)
                {
                    _logger.LogInformation("TheSportsDB: Found {Count} events via fallback search.", evList.Count);
                    foreach (var ev in evList)
                    {
                        if (IsEventMatch(ev.strEvent, name))
                        {
                            // Optional: Validate date if we have one to avoid false positives from other years
                            if (fileDate.HasValue && DateTime.TryParse(ev.dateEvent, out var evDate))
                            {
                                // Allow +/- 2 days variance
                                if (Math.Abs((evDate - fileDate.Value).TotalDays) <= 2)
                                {
                                     _logger.LogInformation("TheSportsDB: Match found (by name + date check): {Event}", ev.strEvent);
                                     return ev;
                                }
                                else
                                {
                                    _logger.LogInformation("TheSportsDB: Name matched but date mismatch. File: {FileDate}, Event: {EventDate}", fileDate.Value.ToString("yyyy-MM-dd"), ev.dateEvent);
                                }
                            }
                            else
                            {
                                // No strict date to check, accept match
                                _logger.LogInformation("TheSportsDB: Match found (by name only): {Event}", ev.strEvent);
                                return ev;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static bool IsEventMatch(string? a, string? b)
        {
            static string Canon(string? s) => Regex.Replace(s ?? "", @"[^A-Za-z0-9]", "").ToLowerInvariant();
            return Canon(a) == Canon(b);
        }

        private static string BuildOverview(string? desc, string? title)
        {
            // Removed blocking logic for Prelims/Early cards so description is always returned.
            // If cardType is specific, we could prepend it, but user just wants the data.
            /*
            if (!string.IsNullOrWhiteSpace(cardType) &&
                (cardType.Contains("Prelims", StringComparison.OrdinalIgnoreCase) || cardType.Contains("Early", StringComparison.OrdinalIgnoreCase)))
            {
                return "";
            }
            */
            if (string.IsNullOrEmpty(desc)) return "";
            desc = desc.Trim();
            if (desc.Length <= 1000) return desc;
            
            // Try to find a sentence break near the limit
            int idx = desc.LastIndexOf('.', 1000);
            if (idx >= 0) return desc.Substring(0, idx + 1).Trim() + "...";
            return desc.Substring(0, 1000).Trim() + "...";
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner };
        
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            var id = item.GetProviderId("TheSportsDB");

            if (string.IsNullOrEmpty(id)) return list;

            var result = await _client.GetEventAsync(id, cancellationToken).ConfigureAwait(false);
            var ev = result?.events?.FirstOrDefault() ?? result?.@event?.FirstOrDefault();

            if (ev != null)
            {
                if (!string.IsNullOrEmpty(ev.strThumb))
                    list.Add(new RemoteImageInfo { Url = ev.strThumb, Type = ImageType.Primary, ProviderName = Name });
                else if (!string.IsNullOrEmpty(ev.strPoster))
                    list.Add(new RemoteImageInfo { Url = ev.strPoster, Type = ImageType.Primary, ProviderName = Name });

                if (!string.IsNullOrEmpty(ev.strFanart))
                    list.Add(new RemoteImageInfo { Url = ev.strFanart, Type = ImageType.Backdrop, ProviderName = Name });
            }

            return list;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            return client.GetAsync(url, cancellationToken);
        }
    }
}