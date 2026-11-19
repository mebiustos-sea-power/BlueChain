using Steamworks;

namespace BlueChain;

internal static class ModDiscovery
{
    // Sea Power runs as a Windows process on both Windows and Proton.
    internal static readonly StringComparer PathComparer =
        Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static IReadOnlyList<string> Discover(string streamingAssets)
    {
        var paths = Directory.GetDirectories(streamingAssets).ToList();
        if (!SteamManager.Initialized) return paths;

        var items = new PublishedFileId_t[SteamUGC.GetNumSubscribedItems(false)];
        if (items.Length == 0) return paths;
        uint count = SteamUGC.GetSubscribedItems(items, (uint)items.Length, false);
        for (int i = 0; i < count; i++)
        {
            var state = (EItemState)SteamUGC.GetItemState(items[i]);
            if ((state & EItemState.k_EItemStateInstalled) == 0 ||
                (state & (EItemState.k_EItemStateDownloading | EItemState.k_EItemStateDownloadPending)) != 0)
                continue;
            if (SteamUGC.GetItemInstallInfo(items[i], out _, out string path, 4096, out _) && Directory.Exists(path))
                paths.Add(path);
        }
        return paths;
    }

    internal static IReadOnlyList<string> InGameOrder(IEnumerable<string> discovered, IEnumerable<string> selected)
    {
        var candidates = new HashSet<string>(discovered.Select(NormalizePath), PathComparer);
        return selected.Select(NormalizePath).Where(candidates.Remove).ToArray();
    }

    internal static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
