namespace AnchorChain;

public interface IAnchorChainMod
{
    void TriggerEntryPoint();
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ACPlugin : Attribute, IEquatable<ACPlugin>, IEquatable<string>
{
    public ACPlugin(string guid, string? name, string? version, string[]? before = null, string[]? after = null)
    {
        GUID = guid;
        Name = name ?? string.Empty;
        Version = new Version(version ?? "1.0");
        Before = new HashSet<string>(before ?? Array.Empty<string>(), StringComparer.Ordinal);
        After = new HashSet<string>(after ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    public string GUID { get; }
    public string Name { get; }
    public Version Version { get; }
    public HashSet<string> Before { get; }
    public HashSet<string> After { get; }
    public HashSet<ACDependency> Dependencies { get; internal set; } = new();
    public HashSet<ACIncompatibility> Incompatibilities { get; internal set; } = new();

    public bool Equals(ACPlugin? other) => other is not null && Equals(other.GUID);
    public bool Equals(string? other) => string.Equals(GUID, other, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(GUID);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ACDependency : Attribute
{
    public ACDependency(string guid, string? min, string? max)
    {
        GUID = guid;
        MinVersion = min is null ? null : new Version(min);
        MaxVersion = max is null ? null : new Version(max);
    }

    public string GUID { get; }
    public Version? MinVersion { get; }
    public Version? MaxVersion { get; }

    public bool Contains(Version version) =>
        (MinVersion is null || MinVersion <= version) && (MaxVersion is null || version <= MaxVersion);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ACIncompatibility : Attribute
{
    public ACIncompatibility(string guid) => GUID = guid;
    public string GUID { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ACConfig : Attribute
{
    public ACConfig(bool required = true) => Required = required;
    public bool Required { get; }
}

public static class AnchorChainLoader
{
    private static Func<string, string?>? findFile;

    internal static void ConfigureFileLookup(Func<string, string?> lookup) => findFile = lookup;

    public static string? FindFile(string fileName) => findFile?.Invoke(fileName);
}
