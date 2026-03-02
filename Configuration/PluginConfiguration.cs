using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace Jellyfin.Plugin.TheSportsDB.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string ApiKey { get; set; } = "123";

        // Remove custom getter/setter. Use auto-property only!
        public List<LeagueMapping> LeagueMappings { get; set; } = new();

        /// <summary>Auto-download DB updates from GitHub Releases on startup.</summary>
        public bool EnableDbAutoUpdate { get; set; } = true;

        public PluginConfiguration() { }
    }

    public class LeagueMapping
    {
        public string Name { get; set; } = string.Empty;
        public string LeagueId { get; set; } = string.Empty;
    }
}