using System.Reflection;
using BepInEx.Logging;

namespace BlueChain;

internal sealed record ModEntry(
    string Id, string Name, Version Version, IReadOnlyList<ModRequirement> Requires,
    IReadOnlyCollection<string> Before, IReadOnlyCollection<string> After, IReadOnlyList<string> IncompatibleWith,
    Action? Prepare, Action Initialize);

internal sealed record ModRequirement(string Id, Version? Minimum, Version? Maximum)
{
    internal bool Allows(Version version) => (Minimum is null || Minimum <= version) && (Maximum is null || version <= Maximum);
}

internal sealed class ModLoader
{
    private readonly ManualLogSource log;

    internal ModLoader(ManualLogSource log) => this.log = log;

    internal void Start(IReadOnlyList<string> modRoots, Type entryAttribute, Func<Type, ModEntry> readEntry,
        Func<AssemblyName, Assembly?> resolveApi)
    {
        var catalog = new AssemblyCatalog(modRoots, entryAttribute, log);
        catalog.RegisterResolver(resolveApi);

        var entries = new List<ModEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in catalog.EntryLibraries)
        {
            Assembly assembly;
            try { assembly = catalog.Load(library); }
            catch (Exception error)
            {
                log.LogError($"Could not load or bind '{library.Path}': {error}");
                continue;
            }

            foreach (string typeName in library.EntryTypes)
            {
                try
                {
                    var entry = readEntry(assembly.GetType(typeName, throwOnError: true)!);
                    if (string.IsNullOrWhiteSpace(entry.Id) ||
                        entry.Before.Concat(entry.After).Concat(entry.IncompatibleWith).Concat(entry.Requires.Select(x => x.Id))
                            .Any(string.IsNullOrWhiteSpace))
                        throw new FormatException($"'{typeName}' declares an empty plug-in ID.");

                    if (ids.Add(entry.Id)) entries.Add(entry);
                    else log.LogWarning($"Skipping duplicate plug-in '{typeName}' ({entry.Id}) in '{library.Path}'.");
                }
                catch (Exception error)
                {
                    log.LogError($"Could not register '{typeName}' in '{library.Path}': {error}");
                }
            }
        }

        var prepared = new List<ModEntry>();
        foreach (var entry in entries)
        {
            try
            {
                entry.Prepare?.Invoke();
                prepared.Add(entry);
            }
            catch (Exception error)
            {
                log.LogError($"Could not prepare {entry.Id}: {error}");
            }
        }

        int started = Initialize(prepared);
        log.LogInfo($"Blue Chain initialized {started} of {entries.Count} discovered plug-ins from {modRoots.Count} enabled mod folders.");
    }

    private int Initialize(List<ModEntry> pending)
    {
        var active = pending.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var selected = new HashSet<string>(active.Keys, StringComparer.Ordinal);
        while (true)
        {
            var rejected = pending.Select(entry => (Entry: entry, Reason: Rejection(entry, selected, active)))
                .Where(entry => entry.Reason is not null).ToArray();
            if (rejected.Length == 0) break;

            foreach (var item in rejected)
            {
                log.LogWarning($"Skipping {item.Entry.Id}: {item.Reason}");
                active.Remove(item.Entry.Id);
                pending.Remove(item.Entry);
            }
        }

        var prerequisites = pending.ToDictionary(entry => entry.Id,
            entry => new HashSet<string>(entry.After, StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var entry in pending)
        {
            foreach (string later in entry.Before)
                prerequisites[later].Add(entry.Id);
        }

        var completed = new Dictionary<string, bool>(StringComparer.Ordinal);
        while (pending.Count != 0)
        {
            var ready = pending.Where(entry => prerequisites[entry.Id].All(completed.ContainsKey)).ToArray();
            if (ready.Length == 0)
            {
                log.LogError("Circular load order blocks: " + string.Join(", ", pending.Select(entry => entry.Id)));
                break;
            }

            foreach (var entry in ready)
            {
                pending.Remove(entry);
                string? failed = prerequisites[entry.Id].FirstOrDefault(required => !completed[required]);
                if (failed is not null)
                {
                    log.LogWarning($"Skipping {entry.Id}: load-order prerequisite '{failed}' did not start.");
                    completed.Add(entry.Id, false);
                    continue;
                }

                try
                {
                    entry.Initialize();
                }
                catch (Exception error)
                {
                    completed.Add(entry.Id, false);
                    log.LogError($"Could not initialize {entry.Id}: {error}");
                    continue;
                }
                completed.Add(entry.Id, true);
                log.LogInfo($"Loaded {entry.Name} ({entry.Id}).");
            }
        }
        return completed.Values.Count(success => success);
    }

    private static string? Rejection(ModEntry entry, HashSet<string> selected, IReadOnlyDictionary<string, ModEntry> active)
    {
        foreach (string incompatible in entry.IncompatibleWith)
            if (selected.Contains(incompatible)) return $"incompatible plug-in '{incompatible}' is present.";

        foreach (var dependency in entry.Requires)
        {
            if (!active.TryGetValue(dependency.Id, out ModEntry required)) return $"missing dependency '{dependency.Id}'.";
            if (!dependency.Allows(required.Version))
                return $"dependency '{dependency.Id}' has unsupported version {required.Version}.";
        }

        foreach (string id in entry.Before.Concat(entry.After))
            if (!active.ContainsKey(id)) return $"missing load-order dependency '{id}'.";

        return null;
    }
}
