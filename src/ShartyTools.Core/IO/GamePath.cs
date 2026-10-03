using System.Text.RegularExpressions;

namespace ShartyTools.Core.IO;

public static partial class GamePath
{
    // The common, portable subset accepted by Quake II virtual filesystems.
    [GeneratedRegex(@"^[A-Za-z0-9_./-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Allowed();

    public static string Validate(string value, int maxBytes = 255)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxBytes || !Allowed().IsMatch(value)
            || value.StartsWith('/') || value.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException($"Invalid game path '{value}'. Use relative paths with forward slashes, letters, digits, underscores, dots or hyphens (max {maxBytes} bytes).");
        return value;
    }

    public static string MapName(string value)
    {
        Validate(value, 59); // maps/ + name + .bsp fits MAX_QPATH including terminator.
        if (value.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) || value.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Map IDs omit the maps/ prefix and .bsp extension.");
        if (value.Length > 54)
            throw new InvalidDataException("Map ID exceeds the Quake II MAX_QPATH limit.");
        return value;
    }

    public static string MapLaunch(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
            throw new InvalidDataException("Map launch expression is empty or too long.");
        foreach (var stage in value.Split('+'))
        {
            var parts = (stage.StartsWith('*') ? stage[1..] : stage).Split('$');
            if (parts.Length > 2) throw new InvalidDataException("A launch stage can have only one spawn-point suffix.");
            MapName(parts[0]);
            if (parts.Length == 2) Validate(parts[1], 63);
        }
        return value;
    }

    public static string MapFileFromLaunch(string value)
    {
        MapLaunch(value);
        return value.Split('+')[^1].TrimStart('*').Split('$')[0];
    }

    public static void RejectLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Symbolic links and junctions are not supported: {current}");
            current = Path.GetDirectoryName(current);
        }
    }

    public static bool IsInside(string path, string directory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}
