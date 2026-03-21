namespace XplorerCheatEditorWinForms.Services;

public static class RomFormatRegistry
{
    private static readonly List<IRomFormat> _formats = new()
    {
        new Gameshark(),
        new Xplorer(),
    };

    public static IRomFormat? Detect(byte[] rom, out string reason)
    {
        foreach (var f in _formats)
        {
            if (f.TryProbe(rom, out reason))
                return f;
        }
        reason = "Unknown / unsupported ROM format (for now).";
        return null;
    }
}
