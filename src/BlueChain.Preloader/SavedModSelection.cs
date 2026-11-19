using System.Globalization;

namespace BlueChain.Preloader;

internal readonly struct ModFolder(string directory, ulong itemId = 0)
{
    internal string Directory { get; } = directory;
    internal ulong ItemId { get; } = itemId;
    internal string Name => Path.GetFileName(Directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}

internal sealed class SavedModSelection
{
    private readonly List<(string Name, string Enabled, ulong ItemId)> rows = new();

    internal static SavedModSelection Read(string path)
    {
        var selection = new SavedModSelection();
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (FileNotFoundException) { return selection; }
        catch (DirectoryNotFoundException) { return selection; }

        bool inLoadOrder = false;
        int count = 0;
        var numberedRows = new SortedDictionary<int, string>();
        foreach (string line in lines)
        {
            string text = line.Trim();
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                inLoadOrder = text.Equals("[LoadOrder]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            int separator = text.IndexOf('=');
            if (!inLoadOrder || separator < 0) continue;
            string key = text.Substring(0, separator).Trim();
            string value = text.Substring(separator + 1).Trim();
            if (key.Equals("NumberOfModFiles", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count))
                    throw new FormatException("Invalid NumberOfModFiles in Sea Power's [LoadOrder].");
            }
            else if (key.Length > 12 && key.StartsWith("Mod", StringComparison.OrdinalIgnoreCase)
                && key.EndsWith("Directory", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(key.AsSpan(3, key.Length - 12), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index > 0)
            {
                numberedRows[index] = value;
            }
        }

        foreach (var row in numberedRows)
        {
            if (row.Key > count) break;
            string[] fields = row.Value.Split(',');
            string name = fields[0].Trim();
            string enabled = fields.Length >= 2 ? fields[1].Trim() : string.Empty;
            ulong itemId = 0;
            if (fields.Length >= 3)
                ulong.TryParse(fields[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out itemId);
            if (name.Length != 0 || itemId != 0)
                selection.rows.Add((name, enabled, itemId));
        }
        return selection;
    }

    internal bool? GetLocalEnabled(string name)
    {
        foreach (var row in rows)
        {
            if (row.Name != name) continue;
            // A prior Workshop row can fall through to the local folder if its item is no longer installed.
            return row.ItemId == 0 ? ParseEnabled(row.Enabled, name) : null;
        }
        return false;
    }

    internal bool ResolveEnabled(ModFolder selected, List<ModFolder> remaining)
    {
        foreach (var row in rows)
        {
            int index = row.ItemId == 0 ? -1 : remaining.FindIndex(folder => folder.ItemId == row.ItemId);
            if (index == -1 && row.Name.Length != 0)
                index = remaining.FindIndex(folder => folder.Name == row.Name);
            if (index == -1) continue;

            var match = remaining[index];
            if (match.ItemId == selected.ItemId && match.Directory == selected.Directory)
                return ParseEnabled(row.Enabled, selected.Name);

            // The game's next saved row only sees folders that have not already been matched.
            remaining.RemoveAt(index);
        }
        return false;
    }

    private static bool ParseEnabled(string value, string name) =>
        bool.TryParse(value, out bool enabled) ? enabled :
            throw new FormatException($"Invalid enabled state for '{name}' in Sea Power's [LoadOrder].");
}
