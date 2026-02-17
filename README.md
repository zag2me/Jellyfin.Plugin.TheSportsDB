# Jellyfin.Plugin.TheSportsDB

A comprehensive Jellyfin metadata provider plugin for [TheSportsDB](https://www.thesportsdb.com/). This plugin fetches rich metadata for sports content across **ANY sport** available on TheSportsDB, including Soccer ⚽, Ice Hockey 🏒, American Football 🏈, Basketball 🏀, Baseball ⚾, MMA/UFC 🥊, and many more!

## 🎯 Plugin Overview

This plugin integrates TheSportsDB's extensive sports database with Jellyfin, providing:

- **League Metadata**: Name, Overview, Year, Logo Images, and Fanart
- **Event/Match Metadata**: Event Name, Date, Teams, Thumbnails, and Fanart
- **Smart Mapping**: Leagues map to "Series" and Events map to "Episodes" in Jellyfin
- **Flexible Configuration**: Custom API Keys and League Mappings support
- **Multi-Sport Support**: Works with ANY sport available on TheSportsDB

## 📦 Installation

### Option 1: Install via Jellyfin Plugin Repository (Recommended)

1. **Open Jellyfin Web Interface**: Log in to your Jellyfin server.
2. **Go to Plugins**: Navigate to the manage repositories section.
3. **Click New Repository
4. **Enter The Name TheSportsDB
5. **Enter https://raw.githubusercontent.com/retrorat1/Jellyfin.Plugin.TheSportsDB/main/manifest.json
6. **Browse or Search**: Locate **TheSportsDB** plugin in the repository.
7. **Install**: Click to install directly from the Jellyfin plugin repository.
8. **Restart Jellyfin**: Restart Jellyfin if prompted to load the plugin.

### Option 2: Manual Installation

1. **Download the Plugin**: Get the latest release from the [GitHub Releases page](https://github.com/retrorat1/Jellyfin.Plugin.TheSportsDB/releases)
2. **Extract the ZIP**: Unzip the archive to your Jellyfin plugins directory:
   - **Linux**: `/var/lib/jellyfin/plugins/TheSportsDB/`
   - **Windows**: `%ProgramData%\Jellyfin\Server\plugins\TheSportsDB\`
   - **Docker**: Map to `/config/plugins/TheSportsDB/`
3. **Ensure Database File**: The `sports_resolver.db` file must be in the plugin directory (it's included in the release)
4. **Restart Jellyfin**: Restart Jellyfin to load the plugin

## ⚙️ Configuration

### Setting up your API Key

1. Get a free API key from [TheSportsDB.com](https://www.thesportsdb.com/)
2. Go to **Dashboard > Plugins > TheSportsDB**
3. Enter your **API Key**
4. Click **Save**
5. **Note**: Jellyfin will restart automatically after saving configuration changes (this is normal behavior)

### League Mappings

The plugin includes built-in support for popular leagues (see [Known Leagues](#-known-leagues-built-in) below). For custom or abbreviated folder names that aren't automatically detected, you can create manual mappings:

1. Go to **Dashboard > Plugins > TheSportsDB**
2. Scroll to **League Mappings**
3. Click **Add Mapping**
4. Enter your **Folder Name** — This is just the folder name (e.g., `EPL`, `Süper Lig`), **NOT** the full path
5. Enter the **TheSportsDB League ID** (e.g., `4339` for Süper Lig)
   - You can find League IDs in the URL of the league page on TheSportsDB.com
6. Click **Save**
7. **Note**: Saving configuration will restart Jellyfin (this is normal)
8. Rescan your library

## 📝 Recommended File Naming Guide

**This is the most important section for getting great results!** The plugin parses filenames to match against TheSportsDB events, so naming conventions have a huge impact on match accuracy.

### Best Formats (by Match Rate)

| Format | Example | Match Rate |
|--------|---------|------------|
| `YYYY-MM-DD Team1 vs Team2` | `2026-02-08 Liverpool vs Manchester City.mkv` | ⭐⭐⭐⭐⭐ |
| `YYYY-MM-DD-ABBR-ABBR` | `2026-02-05-NJD-NYI.mkv` | ⭐⭐|
| `Team1 vs Team2` | `Liverpool vs Manchester City.mkv` | ⭐⭐⭐⭐ |
| `Full Event Name` | `UFC 315 Jones vs Aspinall.mkv` | ⭐⭐⭐⭐ |
| `YYYY Team1 vs Team2 DD MM` | `2026 Liverpool vs Manchester City 08 02.mkv` | ⭐⭐⭐ |
| `League YYYY Team1 vs Team2 DD MM codec` | `EPL 2026 Liverpool vs Manchester City 08 02 720p.mkv` | ⭐⭐ |

### Key Naming Rules

- **✅ Always use ISO dates (YYYY-MM-DD)** when possible — This avoids DD/MM vs MM/DD ambiguity entirely
- **✅ Use full team names** where possible (e.g., "Liverpool" not "LIV", "Manchester City" not "MCI")
- **✅ Use `vs` as the separator** between team names — This matches TheSportsDB's event naming convention
- **✅ Keep the folder name as the league abbreviation** (e.g., `EPL`, `NHL`, `NBA`, `UFC`)
- **✅ Avoid scene tags in filenames** if possible — Remove `720p`, `1080p`, `Fubo`, `60fps`, `x265`, `HEVC`, etc. The plugin tries to strip these, but cleaner names = better results
- **✅ Season folders should contain the year(s)** — Use `Season 2025-26` or `Season 2025-2026`
- **✅ Team abbreviations work best for NHL** and other leagues with standard 3-letter codes (e.g., `NJD`, `NYI`, `EDM`, `PIT`)
- **✅ For soccer/football, use full team names** — Abbreviations like `LIV` or `MCI` are less standardized internationally

### Recommended Folder Structure

```
/Media/Sports/
├── EPL/
│   └── Season 2025-26/
│       ├── 2026-02-08 Liverpool vs Manchester City.mkv
│       └── 2026-01-15 Arsenal vs Chelsea.mkv
├── NHL/
│   └── Season 2025-2026/
│       ├── 2026-02-05-NJD-NYI.mkv
│       └── 2026-01-22-EDM-PIT.mkv
├── NBA/
│   └── Season 2025-2026/
│       ├── 2026-02-10 Los Angeles Lakers vs Boston Celtics.mkv
│       └── 2026-01-28 Golden State Warriors vs Miami Heat.mkv
├── NFL/
│   └── Season 2025-2026/
│       ├── 2026-02-01 Kansas City Chiefs vs Philadelphia Eagles.mkv
│       └── 2026-01-20 Buffalo Bills vs Baltimore Ravens.mkv
└── UFC/
    └── Season 2026/
        └── 2026-03-15 UFC 315 Jones vs Aspinall.mkv
```

## 🔧 How the Plugin Works

The plugin uses a sophisticated matching pipeline to find the correct metadata for your sports files:

1. **Derive Series/League Name**: Extracts the league name from your folder structure
2. **Resolve League ID**: Searches for the league ID in this order:
   - User-configured League Mappings
   - Built-in internal league map (NHL, EPL, NFL, NBA, MLB, UFC)
   - Local sports_resolver.db database
   - TheSportsDB API search (as last resort)
3. **Clean the Filename**: Removes dates, league prefixes, and scene tags from the filename
4. **Expand Team Abbreviations**: Converts common abbreviations to full team names (e.g., MTL → Montreal Canadiens)
5. **Search TheSportsDB API**: Searches for matching events using the cleaned team names
6. **Date-Based Fallback**: If no match is found, falls back to date-based lookup with ±1 day tolerance (to handle timezone differences)

## 🏆 Known Leagues (Built-in)

The following leagues have built-in support and don't require manual mapping:

| League | League ID | Sport |
|--------|-----------|-------|
| NHL | 4380 | Ice Hockey 🏒 |
| EPL | 4328 | Soccer ⚽ |
| NFL | 4391 | American Football 🏈 |
| NBA | 4387 | Basketball 🏀 |
| MLB | 4424 | Baseball ⚾ |
| UFC | 4463 | Mixed Martial Arts 🥊 |

**Note**: ANY league on TheSportsDB can be used via the League Mappings configuration. These are just the ones that work automatically without configuration.

## 🛠️ Troubleshooting

### Common Issues and Solutions

**"No metadata found" or Empty Results**
- Check your filename format — Try using ISO date format (YYYY-MM-DD)
- Verify the event exists on TheSportsDB.com
- Check your API key is correctly configured
- Try using full team names instead of abbreviations

**"Season Unknown"**
- Ensure your Season folder contains the year (e.g., `Season 2025-26` or `Season 2025-2026`)
- Check that the parent folder matches a known league or has a mapping configured

**"Logs stop after saving configuration"**
- This is normal! Jellyfin automatically restarts after configuration changes
- Wait 30-60 seconds for Jellyfin to restart, then check your library

**"Wrong match found" or Incorrect Metadata**
- Use more specific filenames with ISO dates (YYYY-MM-DD)
- Include full team names rather than abbreviations
- Verify the teams/event names match what's on TheSportsDB.com

**Plugin doesn't recognize my league folder (e.g., "Bundesliga", "La Liga")**
- Add a League Mapping in the plugin configuration
- Find the League ID by visiting the league page on TheSportsDB.com and checking the URL
- The League ID is the number in the URL: `https://www.thesportsdb.com/league/4331` → League ID = `4331`

## ❤️ Supporting TheSportsDB

This plugin relies entirely on the amazing work done by the team at [TheSportsDB](https://www.thesportsdb.com/). They provide a comprehensive sports database API that makes plugins like this possible.

### Free API Key

You can get started with a **free API key** for testing and small-scale usage. Register at [TheSportsDB.com](https://www.thesportsdb.com/).

### Premium API (Recommended)

If you use this plugin regularly, we **strongly encourage** you to support TheSportsDB by subscribing to their Patreon. A premium API key provides:

- ✅ Better API rate limits and reliability
- ✅ Access to additional endpoints and data
- ✅ Live scores and more detailed event information
- ✅ Supporting the continued development and maintenance of the database

### How to Support TheSportsDB

 — Starting from $10.50/month for individual developers
- 🌐 **Website**: [thesportsdb.com](https://www.thesportsdb.com/)

> **Note**: This plugin is not affiliated with TheSportsDB. We simply use their public API and want to ensure the amazing service they provide continues to be available for everyone. If you find value in this plugin, please consider supporting the people who make the data available!

## 🤝 Contributing

We welcome contributions! Here's how you can help:

### Building the Plugin

1. Clone the repository:
   ```bash
   git clone https://github.com/retrorat1/Jellyfin.Plugin.TheSportsDB.git
   cd Jellyfin.Plugin.TheSportsDB
   ```

2. Build the plugin:
   ```bash
   dotnet build
   ```

3. Package the plugin:
   ```powershell
   # Windows
   .\build-and-package.ps1
   ```

### Reporting Issues

Found a bug or have a feature request? Please [open an issue](https://github.com/retrorat1/Jellyfin.Plugin.TheSportsDB/issues) on GitHub.

When reporting issues, please include:
- Jellyfin version
- Plugin version
- Example filename that isn't working
- Relevant log entries from Jellyfin

### Contributing Code

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Test thoroughly
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to your fork (`git push origin feature/amazing-feature`)
7. Open a Pull Request

## 📄 License

This project is licensed under the [MIT License](./LICENSE).

## 🙏 Acknowledgments

- **TheSportsDB Team** — For providing the incredible sports database API
- **Jellyfin Team** — For the amazing media server platform
- **Contributors** — Thank you to everyone who has contributed to this plugin!

