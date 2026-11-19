using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using BepInEx.Bootstrap;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace BlueChain;

public static class RuntimeEntryPoint
{
    private const string Id = "bluechain.seapower.runtime";
    private const string AnchorChainPreloader = "io.github.seapower_modders.anchorchain_preloader";
    private static ManualLogSource? log;
    private static bool attempted;

    public static void StartV1(ManualLogSource logger)
    {
        if (log is not null) return;
        if (AnchorChainIsActive())
            throw new InvalidOperationException("Anchor Chain is active. Disable its preloader before using Blue Chain.");

        LoadCompatibilityAssembly();
        var target = AccessTools.DeclaredMethod(typeof(FileManager), nameof(FileManager.CheckCompatibility), Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(FileManager).FullName, nameof(FileManager.CheckCompatibility));
        var postfix = new HarmonyMethod(typeof(RuntimeEntryPoint), nameof(AfterCompatibilityCheck)) { priority = Priority.Last };
        new Harmony(Id).Patch(target, postfix: postfix);
        log = logger;
        logger.LogInfo("Blue Chain is waiting for Sea Power's mod compatibility check.");
    }

    private static bool AnchorChainIsActive() =>
        Chainloader.PluginInfos.ContainsKey(AnchorChainPreloader) ||
        AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            assembly.GetName().Name == "AnchorChain.Preloader" ||
            (assembly.GetName().Name == "AnchorChain" && assembly.GetType("AnchorChain.AnchorChainLoader") is { IsAbstract: false }));

    private static void LoadCompatibilityAssembly()
    {
        string package = Path.GetDirectoryName(typeof(RuntimeEntryPoint).Assembly.Location)!;
        string path = Path.Combine(package, "AnchorChain.dll");
        var compatibility = Assembly.LoadFrom(path);
        if (compatibility.GetName().Name != "AnchorChain" ||
            !string.Equals(compatibility.Location, path, StringComparison.OrdinalIgnoreCase))
            throw new FileLoadException("A different copy of the Anchor Chain compatibility assembly is already loaded.", path);
    }

    private static void AfterCompatibilityCheck(FileManager __instance, ref IEnumerator __result) =>
        __result = RunAfter(__result, () => StartSelectedMods(__instance, log!));

    internal static IEnumerator RunAfter(IEnumerator gameCheck, Action start)
    {
        try
        {
            while (gameCheck.MoveNext()) yield return gameCheck.Current;
        }
        finally
        {
            (gameCheck as IDisposable)?.Dispose();
        }
        start();
    }

    private static void StartSelectedMods(FileManager fileManager, ManualLogSource logger)
    {
        if (attempted) return;
        attempted = true;

        try
        {
            if (AnchorChainIsActive())
            {
                logger.LogError("Anchor Chain is installed and active in this process. Blue Chain did not start any mods. Remove the Anchor Chain preloader and restart Sea Power.");
                return;
            }

            string streamingAssets = Application.streamingAssetsPath;
            var available = ModDiscovery.Discover(streamingAssets);
            var checkedDirectories = fileManager.Directories
                .Where(directory => directory.IsChecked && !directory.IsRuntimeMount && directory.DirectoryInfo.Exists)
                .ToArray();
            var selected = checkedDirectories.Where(directory => directory.IsSettingsMod)
                .Select(directory => ModDiscovery.NormalizePath(directory.DirectoryInfo.FullName))
                .Distinct(ModDiscovery.PathComparer).ToArray();
            var enabled = ModDiscovery.InGameOrder(available, selected);
            foreach (string missing in selected.Except(enabled, ModDiscovery.PathComparer))
                logger.LogWarning($"Selected mod folder could not be verified as local or installed Workshop content: '{missing}'.");

            var lookupRoots = checkedDirectories.Select(directory => directory.DirectoryInfo.FullName).ToArray();
            var entryPoints = new AnchorChainEntryPoints(lookupRoots, streamingAssets, logger);
            new ModLoader(logger).Start(enabled, AnchorChainEntryPoints.EntryAttribute, entryPoints.Read, AnchorChainEntryPoints.ResolveApi);
        }
        catch (Exception error)
        {
            logger.LogError($"Blue Chain could not start the selected mods: {error}");
        }
    }
}
