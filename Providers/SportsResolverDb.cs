using System;
using Microsoft.Data.Sqlite;

public class SportsResolverDb
{
    private readonly string _dbPath;

    public SportsResolverDb(string dbPath) 
    { 
        _dbPath = dbPath; 
    }

    // New Schema: leagues_lookup (id, name, look_up, sport_id, alias)
    // New Schema: teams (id, name, sport_id, stripped_name, country, short_name, alternative_names, league_id)

    /// <summary>
    /// Gets the API slug (e.g. 'english_premier_league') using the League ID (e.g. '4328').
    /// </summary>
    public string? GetLeagueSlug(string leagueId)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};");
            conn.Open();
            // 'look_up' is the API slug column in the new schema
            using var cmd = new SqliteCommand("SELECT look_up FROM leagues_lookup WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", leagueId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? reader.GetString(0) : null;
        }
        catch 
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the full team name given a short name/abbreviation and league ID.
    /// Checks multiple columns: short_name, stripped_name, alternative_names.
    /// </summary>
    public string? GetTeamFullName(string lookupName, string leagueId)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};");
            conn.Open();
            
            // Search aliases or short names
            var query = @"
                SELECT name 
                FROM teams 
                WHERE league_id = @lid 
                  AND (
                    short_name = @lookup 
                    OR stripped_name = @lookup
                    OR alternative_names LIKE @likeLookup
                  )
                LIMIT 1";

            using var cmd = new SqliteCommand(query, conn);
            cmd.Parameters.AddWithValue("@lid", leagueId);
            cmd.Parameters.AddWithValue("@lookup", lookupName); 
            cmd.Parameters.AddWithValue("@likeLookup", $"%{lookupName}%");
            
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? reader.GetString(0) : null;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Resolve League ID from an Alias (e.g. "EPL" or "Premier League")
    /// </summary>
    public string? GetLeagueIdFromAlias(string alias)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};");
            conn.Open();
            // alias column is "Premier League|EPL"
            using var cmd = new SqliteCommand("SELECT id FROM leagues_lookup WHERE alias LIKE @alias OR name LIKE @alias", conn);
            cmd.Parameters.AddWithValue("@alias", $"%{alias}%");
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? reader.GetString(0) : null;
        }
        catch { return null; }
    }

    // --- Legacy methods for compatibility if needed, but updated to use new schema where possible or return null ---

    public string? GetSportName(string lookup)
    {
        // New schema doesn't have sport_name text directly in leagues_lookup, 
        // but we might not need it if we rely on GetLeagueSlug.
        // Return null to force usage of League Slug flow.
        return null; 
    }
    
    public string? GetLeagueId(string lookup)
    {
        // Wrapper for alias lookup
        return GetLeagueIdFromAlias(lookup);
    }
}