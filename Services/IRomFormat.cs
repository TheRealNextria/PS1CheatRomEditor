using XplorerCheatEditorWinForms.Models;

namespace XplorerCheatEditorWinForms.Services;

public interface IRomFormat
{
    string FormatId { get; }
    bool TryProbe(byte[] rom, out string reason);
    RomDocument Parse(string sourcePath, byte[] rom);
    byte[] Build(RomDocument doc);
}
