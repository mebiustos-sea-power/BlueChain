using System.Reflection;
using AnchorChain;
using BepInEx.Logging;

namespace BlueChain;

internal sealed class AnchorChainEntryPoints
{
    private readonly AnchorChainConfiguration configuration;

    internal AnchorChainEntryPoints(IReadOnlyList<string> lookupRoots, string streamingAssets, ManualLogSource log)
    {
        configuration = new AnchorChainConfiguration(lookupRoots, Path.Combine(streamingAssets, "ACConfigs"), log);
        AnchorChainLoader.ConfigureFileLookup(configuration.FindFile);
    }

    internal static Type EntryAttribute => typeof(ACPlugin);

    internal static Assembly? ResolveApi(AssemblyName request) =>
        request.Name == "AnchorChain" && (request.GetPublicKeyToken()?.Length ?? 0) == 0
            ? typeof(IAnchorChainMod).Assembly : null;

    internal ModEntry Read(Type type)
    {
        if (!type.IsVisible || !typeof(IAnchorChainMod).IsAssignableFrom(type))
            throw new TypeLoadException($"'{type.FullName}' must implement the public Anchor Chain plug-in interface.");

        var plugin = type.GetCustomAttribute<ACPlugin>(inherit: false)
            ?? throw new TypeLoadException($"'{type.FullName}' is missing ACPlugin metadata.");
        var requirements = type.GetCustomAttributes<ACDependency>(inherit: false)
            .Select(dependency => new ModRequirement(dependency.GUID, dependency.MinVersion, dependency.MaxVersion))
            .ToArray();
        var conflicts = type.GetCustomAttributes<ACIncompatibility>(inherit: false)
            .Select(conflict => conflict.GUID).ToArray();
        var config = type.GetCustomAttribute<ACConfig>(inherit: false);

        return new ModEntry(plugin.GUID, plugin.Name, plugin.Version, requirements,
            plugin.Before, plugin.After, conflicts,
            config is null ? null : () => configuration.Prepare(plugin.GUID, config.Required),
            () => ((IAnchorChainMod)Activator.CreateInstance(type)!).TriggerEntryPoint());
    }
}
