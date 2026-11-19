using System.Reflection;
using BepInEx.Logging;
using Steamworks;

namespace BlueChain.Preloader;

internal sealed class RuntimeStartup
{
    private const EItemState Ready = EItemState.k_EItemStateSubscribed | EItemState.k_EItemStateInstalled;
    private const EItemState Unavailable = EItemState.k_EItemStateDownloading |
        EItemState.k_EItemStateDownloadPending | EItemState.k_EItemStateDisabledLocally;
    private bool attempted;

    internal bool TryStartLocal(string name, IReadOnlyList<ModFolder> local, SavedModSelection selection,
        Action<string> start, Action<string> report)
    {
        foreach (var folder in local)
        {
            if (!string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (selection.GetLocalEnabled(folder.Name) is not { } enabled) return false;

            StartIfEnabled(folder, enabled, start, report);
            return true;
        }
        return false;
    }

    internal void StartFromInstalled(string name, IEnumerable<ModFolder> installed, SavedModSelection selection,
        Action<string> start, Action<string> report)
    {
        if (attempted) return;
        var remaining = installed.ToList();
        int index = remaining.FindIndex(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index == -1)
        {
            attempted = true;
            report($"Blue Chain runtime folder '{name}' was not found locally or in the installed Workshop items.");
            return;
        }

        var folder = remaining[index];
        StartIfEnabled(folder, selection.ResolveEnabled(folder, remaining), start, report);
    }

    private void StartIfEnabled(ModFolder folder, bool enabled, Action<string> start, Action<string> report)
    {
        if (attempted) return;
        attempted = true;

        if (!enabled)
        {
            report($"Blue Chain runtime at '{folder.Directory}' is disabled in Sea Power's saved mod selection.");
            return;
        }

        start(Path.GetFullPath(Path.Combine(folder.Directory, "BlueChain.dll")));
    }

    internal static bool IsWorkshopReady(EItemState state) => (state & Ready) == Ready && (state & Unavailable) == 0;

    internal static MethodInfo GetEntryPoint(string path)
    {
        var runtime = Assembly.LoadFrom(path);
        if (runtime.GetName().Name != "BlueChain" ||
            !string.Equals(runtime.Location, path, StringComparison.OrdinalIgnoreCase))
            throw new FileLoadException("A different copy of the Blue Chain runtime is already loaded.", path);

        return runtime.GetType("BlueChain.RuntimeEntryPoint", throwOnError: true)!
            .GetMethod("StartV1", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ManualLogSource) }, null)
            is { ReturnType: var returnType } entry && returnType == typeof(void)
                ? entry
                : throw new MissingMethodException("Blue Chain runtime does not have a compatible StartV1 entry point.");
    }
}
