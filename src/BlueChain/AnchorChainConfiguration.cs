using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using SeaPower;

namespace BlueChain;

internal sealed class AnchorChainConfiguration
{
    private const string StateSection = "AnchorChain.State";
    private const string ResetSection = "AnchorChain.ResetValues";

    private readonly IReadOnlyList<string> lookupRoots;
    private readonly string configDirectory;
    private readonly ManualLogSource log;

    internal AnchorChainConfiguration(IReadOnlyList<string> lookupRoots, string configDirectory, ManualLogSource log)
    {
        this.lookupRoots = lookupRoots.Append(configDirectory).ToArray();
        this.configDirectory = configDirectory;
        this.log = log;
    }

    internal string? FindFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || Path.IsPathRooted(fileName)) return null;
        foreach (string root in lookupRoots)
        {
            string candidate = Path.GetFullPath(Path.Combine(root, fileName));
            string relative = Path.GetRelativePath(root, candidate);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(relative)) return null;
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    internal void Prepare(string guid, bool required)
    {
        if (guid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || guid.Contains('/') || guid.Contains('\\'))
            throw new FormatException($"'{guid}' is not valid as a configuration file name.");

        string defaults = FindFile(guid + ".ini")
            ?? throw new FileNotFoundException($"Missing reference configuration '{guid}.ini'.");

        string? userPath = FindFile(guid + "_user.ini");
        if (userPath is null)
        {
            userPath = Path.Combine(configDirectory, guid + "_user.ini");
            try
            {
                Directory.CreateDirectory(configDirectory);
                File.Copy(defaults, userPath, overwrite: false);
            }
            catch (Exception error) when (!required && (error is IOException or UnauthorizedAccessException))
            {
                log.LogWarning($"Could not create optional configuration '{userPath}': {error.Message}");
                return;
            }
        }

        var reference = Read(defaults);
        var user = Read(userPath);
        bool changed = FillMissingKeys(user, reference);
        var reset = GetPendingReset(reference, defaults, userPath);
        if (reset is not null)
        {
            foreach (var section in reference.GetSectionKeyValues(ResetSection))
            {
                foreach (string key in section.Value.Split(','))
                    user.writeValue(section.Key, key, reference.readValue(section.Key, key, ""));
            }
            changed = true;
        }
        if (changed) user.saveFile(true);

        if (reset is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reset.Value.Marker)!);
            File.WriteAllText(reset.Value.Marker, reset.Value.Hash);
        }
    }

    private static bool FillMissingKeys(IniHandler user, IniHandler reference)
    {
        bool changed = false;
        foreach (var section in reference.Data)
        {
            if (section.Key == StateSection || section.Key == ResetSection) continue;
            if (!user.doesSectionExist(section.Key))
            {
                user.AddSection(section.Key, new Dictionary<string, string>());
                changed = true;
            }
            foreach (var entry in section.Value)
            {
                if (user.doesKeyExist(section.Key, entry.Key)) continue;
                user.writeValue(section.Key, entry.Key, entry.Value);
                changed = true;
            }
        }
        return changed;
    }

    private (string Marker, string Hash)? GetPendingReset(IniHandler reference, string defaults, string userPath)
    {
        if (!reference.doesSectionExist(ResetSection) || reference.readValue(StateSection, "hasReset", false))
            return null;

        // Record the reset locally. The reference INI may be supplied by Steam Workshop.
        using var sha256 = SHA256.Create();
        string id = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(userPath)));
        string marker = Path.Combine(configDirectory, "BlueChain", id + ".reset");
        using var stream = File.OpenRead(defaults);
        string hash = BitConverter.ToString(sha256.ComputeHash(stream));
        if (File.Exists(marker) && File.ReadAllText(marker) == hash) return null;
        return (marker, hash);
    }

    private static IniHandler Read(string path)
    {
        var ini = new IniHandler();
        ini.open(path, useCaching: false, forceDisk: true, applyModifiers: false);
        return ini;
    }
}
