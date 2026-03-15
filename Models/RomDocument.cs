namespace XplorerCheatEditorWinForms.Models;

public sealed class RomDocument
{
    public string FormatId { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public byte[] RomBytes { get; set; } = Array.Empty<byte>();

    public int CheatBlockOffset { get; set; }
    public int CheatBlockEndOffset { get; set; } // exclusive
    public List<GameEntry> Games { get; } = new();
}

public sealed class GameEntry
{
    public string Title { get; set; } = "";
    public List<CheatEntry> Cheats { get; } = new();
}

public sealed class CheatEntry
{
    public string Name { get; set; } = "";
    // Raw name bytes (excluding the trailing 0x00). Used to preserve special tokens like 0x01 0x20 = "Infinite ".
    public byte[]? NameRaw { get; set; }

    // Optional prefix bytes for header/template cheats (ASCII, NOT null-terminated).
    // Example: "Allies. " before the header 02 20 07 00.
    public byte[]? PrefixRaw { get; set; }

    // Some ROMs contain "header" cheats without a name, starting with 4 bytes like: ?? 20 07 00.
    public bool IsHeaderCheat { get; set; }
    // Header bytes for template cheats.
    // Known shapes:
    // - 4 bytes: ?? 20 07 00   (e.g. 02 20 07 00 = Unlimited Money)
    // - 3 bytes: 01 ?? 00      (e.g. 01 03 00 = Infinite Lives)
    public byte[]? HeaderBytes { get; set; }
    public bool IsNoCodeNote { get; set; }
    public List<CodeLine> Lines { get; } = new();
}

public sealed class CodeLine
{
    public uint Address { get; set; }
    public ushort Value { get; set; }

    public override string ToString() => $"${Address:X8} {Value:X4}";
}
