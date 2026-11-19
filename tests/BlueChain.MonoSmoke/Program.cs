using System.Collections;
using System.Reflection;
using AnchorChain;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace BlueChain;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            VerifyCoroutine();
            string scratch = Path.GetFullPath(args[0]);
            VerifyModLoading(scratch);
            VerifyFileLookup(scratch);
            Console.WriteLine("PASS Startup waits for compatibility checks, selects active mods, binds the plug-in entry points, initializes in dependency order, and resolves configuration files.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void VerifyCoroutine()
    {
        bool done = false;
        IEnumerator Check()
        {
            yield return "waiting";
            done = true;
        }

        int calls = 0;
        var wrapped = Plugin.RunAfter(Check(), () =>
        {
            Require(done, "Initialization ran before the game check finished.");
            calls++;
        });
        Require(wrapped.MoveNext() && Equals(wrapped.Current, "waiting") && calls == 0, "Game yield was lost.");
        Require(!wrapped.MoveNext() && calls == 1, "Initialization did not run after the game check.");
        var canceled = Plugin.RunAfter(Check(), () => calls++);
        Require(canceled.MoveNext(), "The game check did not start.");
        (canceled as IDisposable)?.Dispose();
        Require(calls == 1, "Initialization ran after the game check was canceled.");
    }

    private static void VerifyModLoading(string scratch)
    {
        string first = Directory.CreateDirectory(Path.Combine(scratch, "Workshop")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(scratch, "Local")).FullName;
        string disabled = Directory.CreateDirectory(Path.Combine(scratch, "Disabled")).FullName;
        string events = Path.Combine(scratch, "initialized.txt");

        WriteSupportLibrary(second, events);
        WriteMod(first, "Follower", 2, "follower", after: "base", requires: "base");
        WriteMod(second, "Base", 1, "base", before: "tail", requires: "tail");
        WriteMod(second, "Duplicate", 1, "base");
        WriteMod(second, "Missing", 1, "missing", requires: "disabled");
        WriteMod(second, "Tail", 1, "tail", incompatible: "disabled");
        WriteMod(second, "Conflict", 1, "conflict", incompatible: "base");
        WriteMod(second, "CycleLeft", 1, "cycle.left", after: "cycle.right");
        WriteMod(second, "CycleRight", 1, "cycle.right", after: "cycle.left");
        WriteMod(second, "Failure", 1, "failure", fail: true);
        WriteMod(second, "AfterFailure", 1, "after.failure", after: "failure");
        WriteMod(disabled, "Disabled", 1, "disabled");

        var selected = ModDiscovery.InGameOrder(new[] { disabled, second, first }, new[] { first, second, first });
        Require(selected.SequenceEqual(new[] { first, second }), "The game's selected folder order was not preserved.");
        var log = new ManualLogSource("Blue Chain smoke test");
        log.LogEvent += (_, entry) => Console.WriteLine(entry.Data);
        var entryPoints = new AnchorChainEntryPoints(selected, scratch, log);
        new ModLoader(log).Start(selected, AnchorChainEntryPoints.EntryAttribute, entryPoints.Read, AnchorChainEntryPoints.ResolveApi);

        string[] expected =
        {
            "Base constructed", "Base", "Failure constructed", "Follower constructed", "Follower", "Tail constructed", "Tail"
        };
        Require(File.Exists(events), "No initialized plug-ins were recorded.");
        string[] actual = File.ReadAllLines(events);
        Require(actual.SequenceEqual(expected), "Unexpected initializations: " + string.Join(", ", actual));
        Require(AppDomain.CurrentDomain.GetAssemblies().All(assembly => assembly.GetName().Name != "Disabled"),
            "The disabled assembly was loaded.");

        string fixture = Path.Combine(second, "Base.dll");
        Assembly existing = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.Location == fixture);
        var catalog = new AssemblyCatalog(new[] { second }, typeof(ACPlugin), log);
        var library = catalog.EntryLibraries.Single(candidate => candidate.Path == fixture);
        Require(ReferenceEquals(existing, catalog.Load(library)), "A preloaded assembly was loaded a second time.");
    }

    private static void VerifyFileLookup(string scratch)
    {
        string local = Path.Combine(scratch, "Local");
        string configs = Path.Combine(scratch, "ACConfigs");
        Directory.CreateDirectory(configs);
        string defaults = Path.Combine(local, "example.ini");
        string user = Path.Combine(configs, "example_user.ini");
        File.WriteAllText(defaults, "[Values]\nValue=default\n");
        File.WriteAllText(user, "[Values]\nValue=mine\n");
        File.WriteAllText(Path.Combine(configs, "example.ini"), "[Values]\nValue=fallback\n");
        Require(AnchorChainLoader.FindFile("example.ini") == defaults, "File lookup did not prefer the selected mod directory.");
        Require(AnchorChainLoader.FindFile("example_user.ini") == user, "File lookup did not fall back to ACConfigs.");
        Require(AnchorChainLoader.FindFile("unknown.ini") is null, "File lookup reported a file that does not exist.");
    }

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }

    private static void WriteSupportLibrary(string directory, string events)
    {
        using var library = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("FixtureSupport", new Version(1, 0, 0, 0)),
            "FixtureSupport", ModuleKind.Dll);
        var module = library.MainModule;
        var type = new TypeDefinition("Fixture", "Support", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        var record = new MethodDefinition("Record", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        record.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        type.Methods.Add(record);
        var il = record.Body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, events);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, Environment.NewLine);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })));
        il.Emit(OpCodes.Call, module.ImportReference(typeof(File).GetMethod(nameof(File.AppendAllText), new[] { typeof(string), typeof(string) })));
        il.Emit(OpCodes.Ret);
        library.Write(Path.Combine(directory, "FixtureSupport.dll"));
    }

    private static void WriteMod(string directory, string assemblyName, int apiMinor, string guid,
        string? before = null, string? after = null, string? requires = null, string? incompatible = null, bool fail = false)
    {
        using var library = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)),
            assemblyName, ModuleKind.Dll);
        var module = library.MainModule;
        var support = new AssemblyNameReference("FixtureSupport", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(support);
        var supportType = new TypeReference("Fixture", "Support", module, support);
        var record = new MethodReference("Record", module.TypeSystem.Void, supportType);
        record.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var type = new TypeDefinition("Fixture", "Entry", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        type.Interfaces.Add(new InterfaceImplementation(module.ImportReference(typeof(IAnchorChainMod))));
        var attribute = new CustomAttribute(module.ImportReference(typeof(ACPlugin).GetConstructors().Single()));
        foreach (string argument in new[] { guid, assemblyName, "1.0" })
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, argument));
        var array = new ArrayType(module.TypeSystem.String);
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(array, before is null ? null :
            new[] { new CustomAttributeArgument(module.TypeSystem.String, before) }));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(array, after is null ? null :
            new[] { new CustomAttributeArgument(module.TypeSystem.String, after) }));
        type.CustomAttributes.Add(attribute);

        if (requires is not null)
        {
            var dependency = new CustomAttribute(module.ImportReference(typeof(ACDependency).GetConstructors().Single()));
            dependency.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, requires));
            dependency.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, null));
            dependency.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, null));
            type.CustomAttributes.Add(dependency);
        }

        if (incompatible is not null)
        {
            var conflict = new CustomAttribute(module.ImportReference(typeof(ACIncompatibility).GetConstructors().Single()));
            conflict.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, incompatible));
            type.CustomAttributes.Add(conflict);
        }

        var constructor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        type.Methods.Add(constructor);
        var il = constructor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)));
        il.Emit(OpCodes.Ldstr, assemblyName + " constructed");
        il.Emit(OpCodes.Call, record);
        il.Emit(OpCodes.Ret);

        var entry = new MethodDefinition("TriggerEntryPoint",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot, module.TypeSystem.Void);
        type.Methods.Add(entry);
        il = entry.Body.GetILProcessor();
        if (fail)
        {
            il.Emit(OpCodes.Newobj, module.ImportReference(typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)));
            il.Emit(OpCodes.Throw);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, assemblyName);
            il.Emit(OpCodes.Call, record);
            il.Emit(OpCodes.Ret);
        }

        module.AssemblyReferences.Single(reference => reference.Name == "AnchorChain").Version = new Version(1, apiMinor, 0, 0);
        library.Write(Path.Combine(directory, assemblyName + ".dll"));
    }
}
