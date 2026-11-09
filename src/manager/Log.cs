using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MW3ModManager;

// One place for the manager-side trail: enough environment and action detail
// to diagnose a report from the log alone, without dumping the settings file.
internal static class Log
{
    private const long MaxBytes = 4L * 1024 * 1024;
    private static readonly object Gate = new();
    private static bool _started;

    public static string Dir => ModConfig.LogDir;
    public static string ManagerPath => ModConfig.ManagerLogPath;

    public static void Init()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;

            try
            {
                Directory.CreateDirectory(Dir);
                MigrateLegacy(Path.Combine(ModConfig.DataDir, "runtime.log"), ModConfig.RuntimeLogPath);
                MigrateLegacy(Path.Combine(ModConfig.DataDir, "runtime.log.old"), ModConfig.RuntimeLogPath + ".old");
                MigrateOtherLogs();
                Rotate();

                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
                Raw($"==== MW3 Mod Manager {version} ====");
                Raw("Started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Raw("OS: " + RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")");
                Raw(".NET: " + RuntimeInformation.FrameworkDescription);
                Raw("Process: " + (Environment.ProcessPath ?? "unknown"));
                Raw("Base: " + AppContext.BaseDirectory);
                Raw("Settings: " + ModConfig.SettingsPath);
                Raw("Runtime log: " + ModConfig.RuntimeLogPath);
                Raw("Manager log: " + ModConfig.ManagerLogPath);
                Raw("");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine("Could not start the log: " + ex);
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        lock (Gate)
        {
            Write("ERROR", message);
            if (ex is not null) Raw(ex + Environment.NewLine);
        }
    }

    private static void Write(string level, string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
        Append(line);
    }

    private static void Raw(string text) =>
        Append(text.EndsWith('\n') ? text : text + Environment.NewLine);

    private static void Append(string text)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                Rotate();
                using var stream = new FileStream(ManagerPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine("Could not write the log: " + ex);
            }
        }
    }

    private static void Rotate()
    {
        try
        {
            var file = new FileInfo(ManagerPath);
            if (!file.Exists || file.Length < MaxBytes) return;
            File.Move(ManagerPath, ManagerPath + ".old", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("Could not rotate the log: " + ex);
        }
    }

    private static void MigrateLegacy(string source, string target)
    {
        try
        {
            if (!File.Exists(source) || File.Exists(target)) return;
            File.Move(source, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("Could not move the old log: " + ex);
        }
    }

    // Older builds and the investigation scripts left logs beside settings.ini.
    // Keep them, but out of the way, so the data folder holds settings and Log.
    private static void MigrateOtherLogs()
    {
        string legacyDir = Path.Combine(Dir, "legacy");
        try
        {
            Directory.CreateDirectory(legacyDir);
            foreach (string source in Directory.GetFiles(ModConfig.DataDir))
            {
                string name = Path.GetFileName(source);
                bool isLog = name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                          || name.EndsWith(".log.old", StringComparison.OrdinalIgnoreCase);
                if (!isLog) continue;
                if (name.Equals("runtime.log", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("runtime.log.old", StringComparison.OrdinalIgnoreCase)) continue;

                string target = Path.Combine(legacyDir, name);
                if (File.Exists(target)) continue;
                try { File.Move(source, target); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine("Could not move " + name + ": " + ex);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("Could not move the old logs: " + ex);
        }
    }
}
