namespace TileSplitter.Core;

public static class ExportNames
{
    public static string? Error(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name must not be blank.";
        }

        if (name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.'))
        {
            return "Names cannot end with a period or space or contain traversal components.";
        }

        if (name.Any(character => character < 32 || "<>:\"/\\|?*".Contains(character)))
        {
            return "Name contains a Windows filename character or directory separator.";
        }

        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();

        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4
                && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                && "123456789¹²³".Contains(stem[3])))
        {
            return "Name is a reserved Windows device name.";
        }

        if (name.Length > 250)
        {
            return "Name is too long for the resulting filename.";
        }

        return null;
    }
}
