using System.Collections;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace BlueChain;

[BepInPlugin(Id, "Blue Chain", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private const string Id = "bluechain.seapower.loader";
    private const string AnchorChainPreloader = "io.github.seapower_modders.anchorchain_preloader";
    private static Plugin? instance;
    private bool attempted;

    private void Awake()
    {
        try
        {
            var target = AccessTools.DeclaredMethod(typeof(FileManager), nameof(FileManager.CheckCompatibility), Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(FileManager).FullName, nameof(FileManager.CheckCompatibility));
            instance = this;
            var postfix = new HarmonyMethod(typeof(Plugin), nameof(AfterCompatibilityCheck)) { priority = Priority.Last };
            new Harmony(Id).Patch(target, postfix: postfix);
            Logger.LogInfo("Blue Chain is waiting for Sea Power's mod compatibility check.");
        }
        catch (Exception error)
        {
            Logger.LogError($"Could not register Blue Chain's startup hook: {error}");
        }
    }

    private static void AfterCompatibilityCheck(FileManager __instance, ref IEnumerator __result) =>
        __result = RunAfter(__result, () => instance!.StartSelectedMods(__instance));

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

    private void StartSelectedMods(FileManager fileManager)
    {
        if (attempted) return;
        attempted = true;

        try
        {
            if (Chainloader.PluginInfos.ContainsKey(AnchorChainPreloader) ||
                AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    assembly.GetName().Name == "AnchorChain.Preloader" ||
                    (assembly.GetName().Name == "AnchorChain" && assembly.GetType("AnchorChain.AnchorChainLoader") is { IsAbstract: false })))
            {
                Logger.LogError("Anchor Chain is installed and active in this process. Blue Chain did not start any mods. Remove the Anchor Chain preloader and restart Sea Power.");
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
                Logger.LogWarning($"Selected mod folder could not be verified as local or installed Workshop content: '{missing}'.");

            var lookupRoots = checkedDirectories.Select(directory => directory.DirectoryInfo.FullName).ToArray();
            var entryPoints = new AnchorChainEntryPoints(lookupRoots, streamingAssets, Logger);
            new ModLoader(Logger).Start(enabled, AnchorChainEntryPoints.EntryAttribute, entryPoints.Read, AnchorChainEntryPoints.ResolveApi);
        }
        catch (Exception error)
        {
            Logger.LogError($"Blue Chain could not start the selected mods: {error}");
        }
    }
}
