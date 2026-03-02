using System.IO;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.TheSportsDB.Providers
{
    public static class DbPathResolver
    {
        public static string Resolve(IApplicationPaths appPaths)
        {
            string downloaded = Path.Combine(appPaths.DataPath, "sports_resolver.db");
            if (File.Exists(downloaded))
                return downloaded;
            string pluginDir = Path.GetDirectoryName(
                typeof(DbPathResolver).Assembly.Location) ?? "";
            return Path.Combine(pluginDir, "sports_resolver.db");
        }
    }
}
