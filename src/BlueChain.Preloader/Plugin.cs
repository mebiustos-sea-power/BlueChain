using BepInEx;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace BlueChain.Preloader;

[BepInPlugin(Id, "Blue Chain Preloader", "0.2.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private const string Id = "bluechain.preloader";
    private const uint SeaPowerAppId = 1286220;
    private static Plugin? instance;
    private readonly RuntimeStartup startup = new();
    private string modFolder = string.Empty;
    private ModFolder[] localFolders = Array.Empty<ModFolder>();
    private SavedModSelection? selection;

    private void Awake()
    {
        try
        {
            modFolder = Config.Bind("Runtime", "ModFolder", "BlueChain",
                "Installed mod folder name. Blue Chain checks StreamingAssets first, then subscribed Steam Workshop folders.").Value.Trim();
            if (modFolder.Length == 0 || modFolder is "." or ".." || modFolder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new FormatException("Runtime.ModFolder must be a single folder name.");

            selection = SavedModSelection.Read(Path.Combine(Application.persistentDataPath, "usersettings.ini"));
            localFolders = Directory.GetDirectories(Application.streamingAssetsPath).Select(path => new ModFolder(path)).ToArray();
            if (startup.TryStartLocal(modFolder, localFolders, selection, StartRuntime, Logger.LogInfo))
                return;

            if (SteamAPI.GetHSteamPipe().m_HSteamPipe != 0)
            {
                StartForSteam();
                return;
            }

            // Sea Power calls SteamAPI.Init() in SteamManager.Awake. Hook InitEx so the runtime can
            // register its startup handler before the game creates its compatibility check.
            instance = this;
            var target = AccessTools.DeclaredMethod(typeof(SteamAPI), nameof(SteamAPI.InitEx), new[] { typeof(string).MakeByRefType() })
                ?? throw new MissingMethodException(typeof(SteamAPI).FullName, nameof(SteamAPI.InitEx));
            new Harmony(Id).Patch(target, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterSteamInit)));
        }
        catch (Exception error)
        {
            Logger.LogError($"Could not start Blue Chain Preloader: {error}");
        }
    }

    private static void AfterSteamInit(ESteamAPIInitResult __result)
    {
        if (instance is not { } plugin) return;
        if (__result == ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            plugin.StartForSteam();
        else
            plugin.Logger.LogWarning($"Blue Chain is waiting for Steam initialization ({__result}).");
    }

    private void StartForSteam()
    {
        try
        {
            if (SteamUtils.GetAppID().m_AppId != SeaPowerAppId)
                throw new InvalidOperationException("Blue Chain requires the Sea Power Steam app.");

            startup.StartFromInstalled(modFolder, localFolders.Concat(InstalledWorkshopFolders()), selection!, StartRuntime, Logger.LogInfo);
        }
        catch (Exception error)
        {
            Logger.LogError($"Could not start Blue Chain from folder '{modFolder}': {error}");
        }
    }

    private void StartRuntime(string path) => RuntimeStartup.GetEntryPoint(path).Invoke(null, new object[] { Logger });

    private static IEnumerable<ModFolder> InstalledWorkshopFolders()
    {
        var items = new PublishedFileId_t[SteamUGC.GetNumSubscribedItems(false)];
        if (items.Length == 0) yield break;

        uint count = SteamUGC.GetSubscribedItems(items, (uint)items.Length, false);
        for (int i = 0; i < count; i++)
        {
            if (!RuntimeStartup.IsWorkshopReady((EItemState)SteamUGC.GetItemState(items[i]))) continue;
            if (SteamUGC.GetItemInstallInfo(items[i], out _, out string directory, 4096, out _) && Directory.Exists(directory))
                yield return new ModFolder(directory, items[i].m_PublishedFileId);
        }
    }
}
