using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HowToFish.RouletteTrainer.Bridge;

internal static class TrainerLog
{
    internal static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly object Sync = new();
    private static string _path;

    internal static string Path
    {
        get
        {
            EnsureInitialized();
            return _path;
        }
    }

    internal static void Emit(string eventName, string fields = null)
    {
        try
        {
            EnsureInitialized();
            var line = "{\"utc\":" + Quote(DateTime.UtcNow.ToString("O", Invariant)) +
                       ",\"event\":" + Quote(eventName) +
                       (string.IsNullOrEmpty(fields) ? string.Empty : "," + fields) + "}" + Environment.NewLine;
            lock (Sync)
            {
                File.AppendAllText(_path, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never alter the game's control flow.
        }
    }

    internal static string Quote(string value)
    {
        if (value == null) return "null";
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 32) builder.Append("\\u").Append(((int)c).ToString("x4", Invariant));
                    else builder.Append(c);
                    break;
            }
        }
        return builder.Append('"').ToString();
    }

    internal static string Float(float value) => value.ToString("R", Invariant);

    private static void EnsureInitialized()
    {
        if (_path != null) return;
        var configured = Environment.GetEnvironmentVariable("HTF_TRAINER_LOG_PATH");
        _path = string.IsNullOrWhiteSpace(configured)
            ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "roulette-trainer.jsonl")
            : System.IO.Path.GetFullPath(configured);
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }
}
