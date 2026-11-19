using System.Reflection;
using BepInEx.Logging;
using Mono.Cecil;

namespace BlueChain;

internal sealed class AssemblyCatalog
{
    internal sealed record Library(string Path, string Name, string Identity, string[] EntryTypes);

    private readonly Dictionary<string, Library> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Assembly> loaded = new(ModDiscovery.PathComparer);
    private readonly object loadLock = new();
    private readonly ManualLogSource log;

    internal AssemblyCatalog(IEnumerable<string> directories, Type entryAttribute, ManualLogSource log)
    {
        this.log = log;
        string apiName = entryAttribute.Assembly.GetName().Name!;
        var libraries = new List<Library>();
        foreach (string directory in directories)
        {
            string[] files;
            try { files = FindLibraries(directory); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log.LogError($"Could not enumerate DLLs in '{directory}': {error.Message}");
                continue;
            }

            foreach (string file in files)
            {
                try
                {
                    using var assembly = AssemblyDefinition.ReadAssembly(file);
                    var name = assembly.Name;
                    if (name.Name.Equals(apiName, StringComparison.OrdinalIgnoreCase)) continue;

                    var entryTypes = assembly.MainModule.GetTypes()
                        .Where(type => type.IsClass && !type.IsAbstract && !type.HasGenericParameters &&
                            (type.IsPublic || type.IsNestedPublic) && type.CustomAttributes.Any(attribute =>
                                attribute.AttributeType.FullName == entryAttribute.FullName &&
                                attribute.AttributeType.Scope.Name == apiName))
                        .Select(type => type.FullName.Replace('/', '+')).ToArray();
                    var library = new Library(file, name.Name, name.FullName, entryTypes);
                    if (byName.TryAdd(library.Name, library)) libraries.Add(library);
                    else if (entryTypes.Length != 0 || byName[library.Name].Identity != library.Identity)
                        log.LogWarning($"Skipping duplicate assembly '{file}'. Selected '{byName[library.Name].Path}' by mod order.");
                }
                catch (BadImageFormatException)
                {
                    log.LogDebug($"Ignoring non-managed or invalid DLL '{file}'.");
                }
                catch (Exception error)
                {
                    log.LogWarning($"Could not inspect DLL '{file}': {error.Message}");
                }
            }
        }
        EntryLibraries = libraries.Where(library => library.EntryTypes.Length != 0).ToArray();
    }

    internal IReadOnlyList<Library> EntryLibraries { get; }

    private static string[] FindLibraries(string modDirectory)
    {
        return Directory.EnumerateFiles(modDirectory, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        }).Where(path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
          .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
          .ThenBy(path => path, StringComparer.Ordinal)
          .ToArray();
    }

    internal Assembly Load(Library library)
    {
        lock (loadLock)
        {
            if (loaded.TryGetValue(library.Path, out Assembly existing)) return existing;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || !string.Equals(assembly.GetName().Name, library.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!ModDiscovery.PathComparer.Equals(assembly.Location, library.Path))
                    throw new FileLoadException(
                        $"Assembly '{library.Name}' is already loaded from '{assembly.Location}'.", library.Path);
                if (!string.Equals(assembly.FullName, library.Identity, StringComparison.OrdinalIgnoreCase))
                    throw new FileLoadException($"Assembly '{library.Path}' has changed since it was discovered.", library.Path);
                loaded.Add(library.Path, assembly);
                return assembly;
            }

            Assembly result = Assembly.LoadFile(library.Path);
            if (!string.Equals(result.FullName, library.Identity, StringComparison.OrdinalIgnoreCase))
                throw new FileLoadException($"Assembly '{library.Path}' has changed since it was discovered.", library.Path);
            loaded.Add(library.Path, result);
            return result;
        }
    }

    internal void RegisterResolver(Func<AssemblyName, Assembly?> resolveApi) =>
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) => Resolve(args, resolveApi);

    private Assembly? Resolve(ResolveEventArgs args, Func<AssemblyName, Assembly?> resolveApi)
    {
        var request = new AssemblyName(args.Name);
        var origin = args.RequestingAssembly;
        if (origin is null || origin.IsDynamic) return null;

        var originName = origin.GetName().Name;
        if (originName is null || !byName.TryGetValue(originName, out Library owner) ||
            !ModDiscovery.PathComparer.Equals(origin.Location, owner.Path))
            return null;

        var api = resolveApi(request);
        if (api is not null) return api;
        if (request.Name is null || !byName.TryGetValue(request.Name, out Library library)) return null;
        if (!string.Equals(request.FullName, library.Identity, StringComparison.OrdinalIgnoreCase)) return null;

        try { return Load(library); }
        catch (Exception error)
        {
            log.LogError($"Could not bind {request.FullName} for '{originName}': {error.Message}");
            return null;
        }
    }
}
