using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

const string SupportedSha256 = "871F76587F0A61338C2F3F8E68D3AA1E2EDC01AB6F907399EF2F01E8CD352BCA";

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: TrainerPatcher <original Assembly-CSharp.dll> <trainer bridge.dll> <output.dll>");
    return 2;
}

var source = Path.GetFullPath(args[0]);
var bridgePath = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var sourceHash = Hash(source);
if (!string.Equals(sourceHash, SupportedSha256, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Unsupported Assembly-CSharp.dll SHA-256: " + sourceHash);
    return 3;
}

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(source)!);
resolver.AddSearchDirectory(Path.GetDirectoryName(bridgePath)!);

using var game = AssemblyDefinition.ReadAssembly(source, new ReaderParameters
{
    AssemblyResolver = resolver,
    InMemory = true,
    ReadSymbols = false
});
using var bridge = AssemblyDefinition.ReadAssembly(bridgePath, new ReaderParameters
{
    AssemblyResolver = resolver,
    InMemory = true,
    ReadSymbols = false
});

var entryType = bridge.MainModule.Types.Single(t => t.FullName == "HowToFish.RouletteTrainer.Bridge.TrainerEntryPoints");
MethodReference Import(string name, int parameters) => game.MainModule.ImportReference(
    entryType.Methods.Single(m => m.Name == name && m.Parameters.Count == parameters));

var startRuntime = Import("StartRuntime", 0);
var casinoAwake = Import("OnCasinoAwake", 1);
var casinoDestroyed = Import("OnCasinoDestroyed", 1);
var spinStarted = Import("OnSpinStarted", 1);
var fixedUpdate = Import("OnFixedUpdate", 1);
var spinFinal = Import("OnSpinFinal", 1);
var payoutCompleted = Import("OnPayoutCompleted", 0);

var mainMenu = Type("MainMenuManager");
var localCasino = Type("LocalCasino");
var casinoManager = Type("CasinoManager");

InsertAtStart(Method(mainMenu, "Start", 0), il => new[] { il.Create(OpCodes.Call, startRuntime) });
InsertBeforeReturns(Method(localCasino, "Awake", 0), il => new[]
{
    il.Create(OpCodes.Ldarg_0),
    il.Create(OpCodes.Call, casinoAwake)
});
InsertAtStart(Method(localCasino, "OnDestroy", 0), il => new[]
{
    il.Create(OpCodes.Ldarg_0),
    il.Create(OpCodes.Call, casinoDestroyed)
});
InsertBeforeReturns(Method(localCasino, "ServerStartRoulette", 0), il => new[]
{
    il.Create(OpCodes.Ldarg_0),
    il.Create(OpCodes.Call, spinStarted)
});
InsertBeforeReturns(Method(localCasino, "FixedUpdate", 0), il => new[]
{
    il.Create(OpCodes.Ldarg_0),
    il.Create(OpCodes.Call, fixedUpdate)
});
InsertAtStart(Method(casinoManager, "ServerRouletteResult", 1), il => new[]
{
    il.Create(OpCodes.Ldarg_1),
    il.Create(OpCodes.Conv_U1),
    il.Create(OpCodes.Call, spinFinal)
});
InsertBeforeReturns(Method(casinoManager, "ServerRouletteResult", 1), il => new[]
{
    il.Create(OpCodes.Call, payoutCompleted)
});

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
game.Write(output, new WriterParameters { WriteSymbols = false });

Console.WriteLine("PATCHED=" + output);
Console.WriteLine("SOURCE_SHA256=" + sourceHash);
Console.WriteLine("OUTPUT_SHA256=" + Hash(output));
Console.WriteLine("MODE_SCOPE=None,Black,Red,Green");
return 0;

TypeDefinition Type(string name) => game.MainModule.Types.SingleOrDefault(t => t.Name == name)
    ?? throw new InvalidDataException("Required type was not found: " + name);

static MethodDefinition Method(TypeDefinition type, string name, int parameters) =>
    type.Methods.SingleOrDefault(m => m.Name == name && m.Parameters.Count == parameters && m.HasBody)
    ?? throw new InvalidDataException($"Required method was not found: {type.Name}.{name}/{parameters}");

static void InsertAtStart(MethodDefinition method, Func<ILProcessor, IEnumerable<Instruction>> factory)
{
    var il = method.Body.GetILProcessor();
    var first = method.Body.Instructions[0];
    foreach (var instruction in factory(il)) il.InsertBefore(first, instruction);
    method.Body.MaxStackSize += 2;
}

static void InsertBeforeReturns(MethodDefinition method, Func<ILProcessor, IEnumerable<Instruction>> factory)
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

static void RedirectTargets(MethodDefinition method, Instruction oldTarget, Instruction newTarget)
{
    foreach (var instruction in method.Body.Instructions)
    {
        if (ReferenceEquals(instruction.Operand, oldTarget)) instruction.Operand = newTarget;
        else if (instruction.Operand is Instruction[] targets)
        {
            for (var i = 0; i < targets.Length; i++)
                if (ReferenceEquals(targets[i], oldTarget)) targets[i] = newTarget;
        }
    }
}

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
