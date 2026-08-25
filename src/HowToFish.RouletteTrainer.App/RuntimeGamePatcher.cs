using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HowToFish.RouletteTrainer.App;

internal static class RuntimeGamePatcher
{
    private const string MarkerPrefix = "HowToFish.RouletteTrainer.OriginalSha256.";

    internal static PatchInspection Inspect(string assemblyPath)
    {
        try
        {
            using var game = Read(assemblyPath);
            var missing = RequiredMembers(game).Distinct().ToArray();
            var originalHash = game.MainModule.Resources
                .Select(r => r.Name)
                .FirstOrDefault(n => n.StartsWith(MarkerPrefix, StringComparison.Ordinal))?[MarkerPrefix.Length..];
            var patched = game.MainModule.AssemblyReferences.Any(r =>
                              r.Name.Equals("HowToFish.RouletteTrainer.Bridge", StringComparison.OrdinalIgnoreCase)) ||
                          originalHash is not null;
            return new PatchInspection(missing.Length == 0, patched, originalHash,
                missing.Length == 0 ? "Compatible roulette structure detected" : "Missing: " + string.Join(", ", missing));
        }
        catch (Exception ex)
        {
            return new PatchInspection(false, false, null, "Assembly inspection failed: " + ex.Message);
        }
    }

    internal static void Patch(string source, string bridgePath, string output, string originalHash)
    {
        using var game = Read(source, Path.GetDirectoryName(output)!);
        var missing = RequiredMembers(game).Distinct().ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException("This build has an incompatible roulette structure. " + string.Join(", ", missing));
        if (Inspect(source).IsPatched)
            throw new InvalidOperationException("The selected source assembly is already patched.");

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(source)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(bridgePath)!);
        using var bridge = AssemblyDefinition.ReadAssembly(bridgePath, new ReaderParameters
        {
            InMemory = true, ReadSymbols = false, AssemblyResolver = resolver
        });

        var entryType = bridge.MainModule.Types.Single(t =>
            t.FullName == "HowToFish.RouletteTrainer.Bridge.TrainerEntryPoints");
        MethodReference Import(string name, int parameters) => game.MainModule.ImportReference(
            entryType.Methods.Single(m => m.Name == name && m.Parameters.Count == parameters));

        var startRuntime = Import("StartRuntime", 0);
        var casinoAwake = Import("OnCasinoAwake", 1);
        var casinoDestroyed = Import("OnCasinoDestroyed", 1);
        var spinStarted = Import("OnSpinStarted", 1);
        var fixedUpdate = Import("OnFixedUpdate", 1);
        var spinFinal = Import("OnSpinFinal", 1);
        var payoutCompleted = Import("OnPayoutCompleted", 0);

        var mainMenu = Type(game, "MainMenuManager");
        var localCasino = Type(game, "LocalCasino");
        var casinoManager = Type(game, "CasinoManager");
        InsertAtStart(Method(mainMenu, "Start", 0), il => new[] { il.Create(OpCodes.Call, startRuntime) });
        InsertBeforeReturns(Method(localCasino, "Awake", 0), il => new[]
        {
            il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Call, casinoAwake)
        });
        InsertAtStart(Method(localCasino, "OnDestroy", 0), il => new[]
        {
            il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Call, casinoDestroyed)
        });
        InsertBeforeReturns(Method(localCasino, "ServerStartRoulette", 0), il => new[]
        {
            il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Call, spinStarted)
        });
        InsertBeforeReturns(Method(localCasino, "FixedUpdate", 0), il => new[]
        {
            il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Call, fixedUpdate)
        });
        InsertAtStart(Method(casinoManager, "ServerRouletteResult", 1), il => new[]
        {
            il.Create(OpCodes.Ldarg_1), il.Create(OpCodes.Conv_U1), il.Create(OpCodes.Call, spinFinal)
        });
        InsertBeforeReturns(Method(casinoManager, "ServerRouletteResult", 1), il => new[]
        {
            il.Create(OpCodes.Call, payoutCompleted)
        });

        game.MainModule.Resources.Add(new EmbeddedResource(MarkerPrefix + originalHash,
            ManifestResourceAttributes.Private, Array.Empty<byte>()));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        game.Write(output, new WriterParameters { WriteSymbols = false });
        var result = Inspect(output);
        if (!result.IsCompatible || !result.IsPatched || result.OriginalHash != originalHash)
            throw new InvalidDataException("Patched assembly verification failed.");
    }

    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static AssemblyDefinition Read(string path, params string[] additionalDirectories)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        foreach (var directory in additionalDirectories.Where(Directory.Exists))
            resolver.AddSearchDirectory(directory);
        return AssemblyDefinition.ReadAssembly(Path.GetFullPath(path), new ReaderParameters
        {
            InMemory = true, ReadSymbols = false, AssemblyResolver = resolver
        });
    }

    private static IEnumerable<string> RequiredMembers(AssemblyDefinition game)
    {
        var required = new (string Type, string Method, int Parameters)[]
        {
            ("MainMenuManager", "Start", 0), ("LocalCasino", "Awake", 0),
            ("LocalCasino", "OnDestroy", 0), ("LocalCasino", "ServerStartRoulette", 0),
            ("LocalCasino", "FixedUpdate", 0), ("CasinoManager", "ServerRouletteResult", 1)
        };
        foreach (var item in required)
        {
            var type = game.MainModule.Types.SingleOrDefault(t => t.Name == item.Type);
            if (type is null) { yield return "type " + item.Type; continue; }
            if (!type.Methods.Any(m => m.Name == item.Method && m.Parameters.Count == item.Parameters && m.HasBody))
                yield return item.Type + "." + item.Method;
        }
        var casino = game.MainModule.Types.SingleOrDefault(t => t.Name == "LocalCasino");
        foreach (var field in new[] { "_ball", "_ballSpawnPoint", "_wheel", "_ballAngleObject", "_ballForce", "_slotSize", "_curWheelSpeed", "_timeInSameSlot", "_isPlaying" })
            if (casino is not null && !casino.Fields.Any(f => f.Name == field)) yield return "LocalCasino." + field;
    }

    private static TypeDefinition Type(AssemblyDefinition game, string name) =>
        game.MainModule.Types.Single(t => t.Name == name);
    private static MethodDefinition Method(TypeDefinition type, string name, int parameters) =>
        type.Methods.Single(m => m.Name == name && m.Parameters.Count == parameters && m.HasBody);

    private static void InsertAtStart(MethodDefinition method, Func<ILProcessor, IEnumerable<Instruction>> factory)
    {
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions[0];
        foreach (var instruction in factory(il)) il.InsertBefore(first, instruction);
        method.Body.MaxStackSize += 2;
    }

    private static void InsertBeforeReturns(MethodDefinition method, Func<ILProcessor, IEnumerable<Instruction>> factory)
    {
        var il = method.Body.GetILProcessor();
        foreach (var ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            var additions = factory(il).ToArray();
            foreach (var addition in additions) il.InsertBefore(ret, addition);
            RedirectTargets(method, ret, additions[0]);
        }
        method.Body.MaxStackSize += 2;
    }

    private static void RedirectTargets(MethodDefinition method, Instruction oldTarget, Instruction newTarget)
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (ReferenceEquals(instruction.Operand, oldTarget)) instruction.Operand = newTarget;
            else if (instruction.Operand is Instruction[] targets)
                for (var i = 0; i < targets.Length; i++)
                    if (ReferenceEquals(targets[i], oldTarget)) targets[i] = newTarget;
        }
    }
}

internal sealed record PatchInspection(bool IsCompatible, bool IsPatched, string? OriginalHash, string Details);
