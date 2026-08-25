using System.Diagnostics;

namespace HowToFish.RouletteTrainer.App;

internal sealed class GameInstallation
{
    private const string BridgeFile = "HowToFish.RouletteTrainer.Bridge.dll";
    private readonly string _exePath;
    private readonly string? _managedDirectory;

    internal GameInstallation(string exePath)
    {
        _exePath = Path.GetFullPath(exePath);
        _managedDirectory = ResolveManagedDirectory(_exePath);
    }

    internal string Root => Path.GetDirectoryName(_exePath)!;
    internal string ExePath => _exePath;
    internal string ModePath => Path.Combine(Root, "trainer-mode.txt");
    internal string AssemblyPath => Path.Combine(_managedDirectory ?? Root, "Assembly-CSharp.dll");
    internal string BridgePath => Path.Combine(_managedDirectory ?? Root, BridgeFile);
    internal string LegacyBackupPath => AssemblyPath + ".roulette-original";
    internal string BackupDirectory => Path.Combine(Root, "roulette-trainer-backups");

    internal bool IsValid => File.Exists(_exePath) && _exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                             _managedDirectory is not null && File.Exists(AssemblyPath);
    internal PatchInspection Inspection => IsValid ? RuntimeGamePatcher.Inspect(AssemblyPath) :
        new PatchInspection(false, false, null, "Select the game's executable next to its Unity data folder.");
    internal bool IsInstalled
    {
        get { try { var value = Inspection; return value.IsCompatible && value.IsPatched && File.Exists(BridgePath); } catch { return false; } }
    }

    internal int? RunningPid()
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_exePath)))
        {
            try { if (Path.GetFullPath(process.MainModule!.FileName).Equals(_exePath, StringComparison.OrdinalIgnoreCase)) return process.Id; }
            catch { }
        }
        return null;
    }

    internal string ReadMode()
    {
        try { var mode = File.Exists(ModePath) ? File.ReadAllText(ModePath).Trim().ToUpperInvariant() : "NONE"; return IsMode(mode) ? mode : "NONE"; }
        catch { return "NONE"; }
    }

    internal void SetMode(string mode)
    {
        mode = mode.Trim().ToUpperInvariant();
        if (!IsMode(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!IsInstalled) throw new InvalidOperationException("Install or repair the trainer first.");
        File.WriteAllText(ModePath, mode + Environment.NewLine);
        File.SetLastWriteTimeUtc(ModePath, DateTime.UtcNow);
    }

    internal void Install(string payloadDirectory)
    {
        if (!IsValid) throw new InvalidOperationException("Select the game executable next to its Unity data folder.");
        if (RunningPid() is not null) throw new InvalidOperationException("Close the game before installing or repairing.");
        var bridgePayload = Path.Combine(payloadDirectory, BridgeFile);
        if (!File.Exists(bridgePayload)) throw new InvalidOperationException("The trainer bridge payload is missing.");

        var activeInspection = Inspection;
        string original;
        if (activeInspection.IsPatched) original = LocateOriginal(activeInspection.OriginalHash);
        else
        {
            if (!activeInspection.IsCompatible) throw new InvalidOperationException(activeInspection.Details);
            original = AssemblyPath;
        }

        var originalInspection = RuntimeGamePatcher.Inspect(original);
        if (!originalInspection.IsCompatible || originalInspection.IsPatched)
            throw new InvalidOperationException("A compatible unmodified game assembly could not be located.");
        var originalHash = RuntimeGamePatcher.Hash(original);
        Directory.CreateDirectory(BackupDirectory);
        var versionedBackup = BackupPath(originalHash);
        if (File.Exists(versionedBackup))
        {
            if (RuntimeGamePatcher.Hash(versionedBackup) != originalHash)
                throw new InvalidOperationException("The versioned backup failed its integrity check.");
        }
        else File.Copy(original, versionedBackup, overwrite: false);

        var temporary = AssemblyPath + ".roulette-trainer.tmp";
        try
        {
            RuntimeGamePatcher.Patch(versionedBackup, bridgePayload, temporary, originalHash);
            File.Copy(temporary, AssemblyPath, overwrite: true);
            File.Copy(bridgePayload, BridgePath, overwrite: true);
            SetMode("NONE");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal void Restore()
    {
        if (!IsValid) throw new InvalidOperationException("Select the correct game executable.");
        if (RunningPid() is not null) throw new InvalidOperationException("Close the game before restoring.");
        var inspection = Inspection;
        if (!inspection.IsPatched) { CleanupRuntimeFiles(); return; }
        var original = LocateOriginal(inspection.OriginalHash);
        var originalInspection = RuntimeGamePatcher.Inspect(original);
        if (!originalInspection.IsCompatible || originalInspection.IsPatched)
            throw new InvalidOperationException("The original backup is missing or invalid.");
        File.Copy(original, AssemblyPath, overwrite: true);
        CleanupRuntimeFiles();
    }

    private string LocateOriginal(string? markedHash)
    {
        if (!string.IsNullOrWhiteSpace(markedHash))
        {
            var versioned = BackupPath(markedHash);
            if (File.Exists(versioned) && RuntimeGamePatcher.Hash(versioned).Equals(markedHash, StringComparison.OrdinalIgnoreCase)) return versioned;
        }
        if (File.Exists(LegacyBackupPath))
        {
            var legacy = RuntimeGamePatcher.Inspect(LegacyBackupPath);
            if (legacy.IsCompatible && !legacy.IsPatched) return LegacyBackupPath;
        }
        throw new InvalidOperationException("No matching original backup was found. Verify the game files, then install again.");
    }

    private string BackupPath(string hash) => Path.Combine(BackupDirectory, "Assembly-CSharp." + hash + ".original.dll");
    private void CleanupRuntimeFiles()
    {
        if (File.Exists(BridgePath)) File.Delete(BridgePath);
        if (File.Exists(ModePath)) File.Delete(ModePath);
    }

    internal static string? GuessGame()
    {
        var configured = Settings.Load();
        if (configured is not null && File.Exists(configured) && ResolveManagedDirectory(configured) is not null) return configured;
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steamapps\common\How to Fish\How to Fish\How to Fish.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Steam\steamapps\common\How to Fish\How to Fish\How to Fish.exe")
        };
        return candidates.FirstOrDefault(path => File.Exists(path) && ResolveManagedDirectory(path) is not null);
    }

    internal static int SelfTest() => IsMode("NONE") && IsMode("BLACK") && IsMode("RED") && IsMode("GREEN") && !IsMode("BLUE") ? 0 : 1;
    private static bool IsMode(string mode) => mode is "NONE" or "BLACK" or "RED" or "GREEN";

    private static string? ResolveManagedDirectory(string exePath)
    {
        if (!File.Exists(exePath)) return null;
        var root = Path.GetDirectoryName(Path.GetFullPath(exePath))!;
        var named = Path.Combine(root, Path.GetFileNameWithoutExtension(exePath) + "_Data", "Managed");
        if (File.Exists(Path.Combine(named, "Assembly-CSharp.dll"))) return named;
        var canonical = Path.Combine(root, "How to Fish_Data", "Managed");
        if (File.Exists(Path.Combine(canonical, "Assembly-CSharp.dll"))) return canonical;
        try
        {
            return Directory.EnumerateDirectories(root, "*_Data", SearchOption.TopDirectoryOnly)
                .Select(path => Path.Combine(path, "Managed"))
                .SingleOrDefault(path => File.Exists(Path.Combine(path, "Assembly-CSharp.dll")));
        }
        catch { return null; }
    }
}

internal static class Settings
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HowToFishRouletteTrainer");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "game-path.txt");
    internal static string? Load() { try { return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : null; } catch { return null; } }
    internal static void Save(string path) { Directory.CreateDirectory(DirectoryPath); File.WriteAllText(FilePath, Path.GetFullPath(path)); }
}
