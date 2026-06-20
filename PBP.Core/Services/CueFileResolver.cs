namespace PBP.Core.Services;

public static class CueFileResolver
{
    public static string GetBinPath(string cuePath)
    {
        var line = File.ReadLines(cuePath)
            .FirstOrDefault(l => l.TrimStart().StartsWith("FILE", StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"{Path.GetFileName(cuePath)}에서 FILE 항목을 찾을 수 없어요.");

        var match = System.Text.RegularExpressions.Regex.Match(line, "\"(.+?)\"");
        var binFileName = match.Success ? match.Groups[1].Value : line.Split(' ', 2)[1].Trim('"');

        return Path.Combine(Path.GetDirectoryName(cuePath)!, binFileName);
    }
}