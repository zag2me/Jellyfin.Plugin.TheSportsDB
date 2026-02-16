using Microsoft.Data.Sqlite;

public class SportsResolverDb
{
    private readonly string _dbPath;

    public SportsResolverDb(string dbPath) { _dbPath = dbPath; }

    // Sport category lookup
    public string? GetSportName(string leagueAbbr)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};");
        conn.Open();
        using var cmd = new SqliteCommand("SELECT sport_name FROM sport_category_lookup WHERE lookup = @lookup", conn);
        cmd.Parameters.AddWithValue("@lookup", leagueAbbr.ToLower());
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? reader.GetString(0) : null;
    }

    // Team name lookup by short_name and league_id
    public string? GetTeamFullName(string shortName, string leagueId)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};");
        conn.Open();
        using var cmd = new SqliteCommand("SELECT name FROM teams WHERE short_name = @short_name AND league_id = @league_id", conn);
        cmd.Parameters.AddWithValue("@short_name", shortName.ToUpper());
        cmd.Parameters.AddWithValue("@league_id", leagueId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? reader.GetString(0) : null;
    }
	
    // League ID lookup
    public string? GetLeagueId(string leagueAbbr)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};");
        conn.Open();
        using var cmd = new SqliteCommand("SELECT league_id FROM sport_category_lookup WHERE lookup = @lookup", conn);
        cmd.Parameters.AddWithValue("@lookup", leagueAbbr.ToUpper());
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? reader.GetString(0) : null;
    }
}