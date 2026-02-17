
using System;
using System.Text.RegularExpressions;

public class Program
{
    public static void Main()
    {
        string filename = "EPL 2026 Arsenal vs Sunderland 07 02 720pEN60fps Fubo";
        CleanEpisodeName(filename);
        
        filename = "EPL 2026 Liverpool vs Manchester City 08 02 720p";
        CleanEpisodeName(filename);
    }

    private static void CleanEpisodeName(string raw)
    {
        string name = raw;
        name = name.Replace('.', ' ');
        name = Regex.Replace(name, @"\bUtd\b", "United", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\b(RS|PS)\b", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"(\d{2})(\d{3,4}p)", "$1 $2");
        name = Regex.Replace(name, @"\d{2,4}fp[s]?\b", "", RegexOptions.IgnoreCase);

        // Simulated strips
        string[] SuffixStrips = new[] { "720p", "1080p", "4k", "Fubo" };
        string[] LeagueNameStrips = new[] { "EPL", "NBA" };

        foreach (var s in SuffixStrips)
        {
            name = Regex.Replace(name, @"\b" + Regex.Escape(s) + @"\b", "", RegexOptions.IgnoreCase);
        }
        foreach (var s in LeagueNameStrips)
        {
            name = Regex.Replace(name, @"\b" + Regex.Escape(s) + @"\b", "", RegexOptions.IgnoreCase);
        }
        name = Regex.Replace(name, @"\s+", " ").Trim();

        Console.WriteLine($"Name after basic clean: '{name}'");

        DateTime? fileDate = null;
        var mIso = Regex.Match(name, @"(\d{4})[ \.\-_](\d{2})[ \.\-_](\d{2})");
        if (mIso.Success)
        {
            Console.WriteLine("Matched ISO");
        }
        else
        {
            var m = Regex.Match(name, @"(\d{2})[ \.\-_]?(\d{2})[ \.\-_]?(\d{4})");
            if (m.Success)
            {
                Console.WriteLine("Matched DMY");
            }
            else
            {
                // Fallback regex being tested
                var mSplit = Regex.Match(name, @"\b(19|20)(\d{2})\b.*?\b(\d{2})\s+(\d{2})\b");
                if (mSplit.Success)
                {
                    Console.WriteLine("Matched Split!");
                    string yearStr = mSplit.Groups[1].Value + mSplit.Groups[2].Value;
                    Console.WriteLine($"Year: {yearStr}, P1: {mSplit.Groups[3].Value}, P2: {mSplit.Groups[4].Value}");
                }
                else
                {
                     Console.WriteLine("NO MATCH");
                }
            }
        }
    }
}
