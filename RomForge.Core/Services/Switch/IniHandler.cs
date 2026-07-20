using System.IO;

namespace RomForge.Core.Services.Switch
{
    public class IniHandler(string filePath)
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data = new(StringComparer.OrdinalIgnoreCase);

        public async Task LoadAsync()
        {
            if (!File.Exists(filePath))
                return;

            string currentSection = "Default";
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                string trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(';'))
                    continue;

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    currentSection = trimmed[1..^1];
                    if (!_data.ContainsKey(currentSection))
                        _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    var parts = trimmed.Split(['='], 2);

                    if (parts.Length == 2)
                    {
                        if (!_data.ContainsKey(currentSection))
                            _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        _data[currentSection][parts[0].Trim()] = parts[1].Trim('\"');
                    }
                }
            }
        }

        public string GetValue(string section, string key, string defaultValue = "")
        {
            if (_data.TryGetValue(section, out var sectionData) && sectionData.TryGetValue(key, out string? value))
                return value;

            return defaultValue;
        }

        public void SetValue(string section, string key, string value)
        {
            if (!_data.ContainsKey(section))
                _data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            _data[section][key] = value;
        }

        public async Task SaveAsync()
        {
            var lines = new List<string>();

            foreach (var section in _data)
            {
                lines.Add($"[{section.Key}]");

                foreach (var kvp in section.Value)
                    lines.Add($"{kvp.Key}=\"{kvp.Value}\"");

                lines.Add(string.Empty);
            }

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);

            foreach (var line in lines)
                await writer.WriteLineAsync(line);
        }
    }
}