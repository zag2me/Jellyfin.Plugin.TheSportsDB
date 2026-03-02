using System;
using Microsoft.Data.Sqlite;

public class SportsResolverDb
{
    private readonly string _dbPath;
    public SportsResolverDb(string dbPath) { _dbPath = dbPath; }

    public string? GetLeagueSlug(string leagueId)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};");
            conn.Open();
            using var cmd = new SqliteCommand("SELECT look_up FROM leagues_lookup WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", leagueId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? r.GetString(0) : null;
        }
        catch { return null; }
    }

    public string? GetTeamFullName(string lookupName, string leagueId)
    {
        if (string.IsNullOrWhiteSpace(lookupName)) return null;
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};");
            conn.Open();
            // Pass 1: strict league
            using (var cmd = new SqliteCommand(@"SELECT name FROM teams WHERE league_id=@lid AND (name=@l OR short_name=@l OR stripped_name=@l OR alternative_names LIKE @k) LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@lid", leagueId);
                cmd.Parameters.AddWithValue("@l", lookupName);
                cmd.Parameters.AddWithValue("@k", $"%{lookupName}%");
                using var r = cmd.ExecuteReader();
                if (r.Read()) return r.GetString(0);
            }
            // Pass 2: cross-league
            using (var cmd2 = new SqliteCommand(@"SELECT name FROM teams WHERE (name=@l OR short_name=@l OR stripped_name=@l OR alternative_names LIKE @k) LIMIT 1", conn))
            {
                cmd2.Parameters.AddWithValue("@l", lookupName);
                cmd2.Parameters.AddWithValue("@k", $"%{lookupName}%");
                using var r2 = cmd2.ExecuteReader();
                if (r2.Read()) return r2.GetString(0);
            }
            return null;
        }
        catch { return null; }
    }

    public string? GetLeagueIdFromAlias(string alias)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};");
            conn.Open();
            using var cmd = new SqliteCommand("SELECT id FROM leagues_lookup WHERE alias LIKE @a OR name LIKE @a", conn);
            cmd.Parameters.AddWithValue("@a", $"%{alias}%");
            using var r = cmd.ExecuteReader();
            return r.Read() ? r.GetString(0) : null;
        }
        catch { return null; }
    }

    public string? GetLeagueId(string lookup) => GetLeagueIdFromAlias(lookup);
    public string? GetSportName(string lookup) => null;
}