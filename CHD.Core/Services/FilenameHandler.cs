using System.Text.RegularExpressions;

namespace CHD.Core.Services;

public class FilenameHandler
{
    public static bool HasKorean(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return false;

        return Regex.IsMatch(filename, @"[\uAC00-\uD7A3]");
    }

    public static bool HasKoreanInDirectory(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        string directory = Path.GetDirectoryName(filePath);
        return !string.IsNullOrEmpty(directory) && HasKorean(directory);
    }

    public static bool HasKoreanInFilename(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        string filename = Path.GetFileName(filePath);
        return HasKorean(filename);
    }

    public class PathValidationResult
    {
        public bool IsValid { get; set; }
        public bool HasKoreanInDirectory { get; set; }
        public bool HasKoreanInFilename { get; set; }
        public string Message { get; set; }
    }

    public static PathValidationResult ValidatePath(string filePath)
    {
        return new PathValidationResult
        {
            IsValid = true,
            HasKoreanInDirectory = HasKoreanInDirectory(filePath),
            HasKoreanInFilename = HasKoreanInFilename(filePath)
        };
    }

    public static string GenerateTempFilename(string originalPath)
    {
        var directory = Path.GetDirectoryName(originalPath);
        var extension = Path.GetExtension(originalPath);
        var tempName = $"temp_{Guid.NewGuid():N}{extension}";
        return Path.Combine(directory, tempName);
    }

    public static string RenameToTemp(string originalPath)
    {
        if (!File.Exists(originalPath))
            throw new FileNotFoundException($"파일을 찾을 수 없습니다: {originalPath}");
        var tempPath = GenerateTempFilename(originalPath);
        File.Move(originalPath, tempPath);
        return tempPath;
    }

    public static (string tempCue, string tempBin) RenameCueWithBin(string originalCuePath)
    {
        if (!File.Exists(originalCuePath))
            throw new FileNotFoundException($"CUE 파일을 찾을 수 없습니다: {originalCuePath}");
        var directory = Path.GetDirectoryName(originalCuePath);
        var binPath = Path.ChangeExtension(originalCuePath, ".bin");
        if (!File.Exists(binPath))
            throw new FileNotFoundException($"BIN 파일을 찾을 수 없습니다: {binPath}");
        var tempBaseName = $"temp_{Guid.NewGuid():N}";
        var tempCuePath = Path.Combine(directory, tempBaseName + ".cue");
        var tempBinPath = Path.Combine(directory, tempBaseName + ".bin");
        File.Move(binPath, tempBinPath);
        var cueContent = File.ReadAllText(originalCuePath);
        var originalBinName = Path.GetFileName(binPath);
        var tempBinName = Path.GetFileName(tempBinPath);
        cueContent = cueContent.Replace($"\"{originalBinName}\"", $"\"{tempBinName}\"");
        cueContent = cueContent.Replace(originalBinName, tempBinName);
        File.WriteAllText(tempCuePath, cueContent);
        File.Delete(originalCuePath);
        return (tempCuePath, tempBinPath);
    }

    public static void RestoreOriginalName(string tempPath, string originalPath)
    {
        if (!File.Exists(tempPath))
            return;
        if (File.Exists(originalPath))
            File.Delete(originalPath);
        File.Move(tempPath, originalPath);
    }

    public static void RestoreCueWithBin(string tempCuePath, string tempBinPath,
        string originalCuePath, string originalBinPath)
    {
        if (File.Exists(tempBinPath))
        {
            if (File.Exists(originalBinPath))
                File.Delete(originalBinPath);
            File.Move(tempBinPath, originalBinPath);
        }
        if (File.Exists(tempCuePath))
        {
            var cueContent = File.ReadAllText(tempCuePath);
            var tempBinName = Path.GetFileName(tempBinPath);
            var originalBinName = Path.GetFileName(originalBinPath);
            cueContent = cueContent.Replace($"\"{tempBinName}\"", $"\"{originalBinName}\"");
            cueContent = cueContent.Replace(tempBinName, originalBinName);
            File.WriteAllText(originalCuePath, cueContent);
            File.Delete(tempCuePath);
        }
    }

    public static void CleanupTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
        }
    }
}