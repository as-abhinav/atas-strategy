using System;
using System.IO;

namespace AtasStrategies.Strategies;

/// <summary>
/// File logging shared by strategies. Writes to %APPDATA%\ATAS\Logs, falling back to the temp folder.
/// Never throws: logging must not break a strategy.
/// </summary>
internal static class StrategyLog
{
    public static string LogFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS", "Logs");

    public static void Trace(string fileName, string message) =>
        Append(fileName, null, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");

    public static void AppendCsv(string fileName, string header, string line) =>
        Append(fileName, header, line);

    private static void Append(string fileName, string? header, string line)
    {
        foreach (string folder in new[] { LogFolder, Path.GetTempPath() })
        {
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, fileName);
                if (header is not null && !File.Exists(path))
                    File.WriteAllText(path, header + Environment.NewLine);
                File.AppendAllText(path, line + Environment.NewLine);
                return;
            }
            catch (Exception)
            {
                // Try the next folder.
            }
        }
    }
}
