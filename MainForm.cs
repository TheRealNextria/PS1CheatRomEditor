using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using XplorerCheatEditorWinForms.Models;
using XplorerCheatEditorWinForms.Services;

using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;
namespace XplorerCheatEditorWinForms;

public partial class MainForm : Form
{
        private static readonly Regex _rxNopsPct = new Regex(@"\((\d{1,3})\)%", RegexOptions.Compiled);

        private static int TryParseNopsPercent(string line)
        {
            try
            {
                var m = _rxNopsPct.Match(line);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int pct))
                    return pct;
            }
            catch { }
            return -1;
        }

        private async Task<T> RunWithNopsDialogAsync<T>(string title, Func<Action<string>, CancellationToken, Task<T>> action)
        {
            using var dlg = new XplorerCheatEditorWinForms.Services.BusyProgressDialog(title);
            using var cts = new CancellationTokenSource();
            dlg.CancelRequested += () => cts.Cancel();
            dlg.SetProgress(-1);
            dlg.Show(this);
            Enabled = false;
            try
            {
                T result = await action(line =>
                {
dlg.ReportLine(line);
                    int pct = TryParseNopsPercent(line);
                    if (pct >= 0) dlg.SetProgress(pct);
                
                }, cts.Token);
                return result;
            }
            finally
            {
                Enabled = true;
                try { dlg.Close(); } catch { }
            }
        }

    private static bool _encodingRegistered;

    private RomDocument? _doc;
    private GameEntry? _selectedGame;
    private CheatEntry? _selectedCheat;

    // For compressed ROMs, status bar space is based on packed block bytes in the original ROM.
    private int? _statusUsedBytesOverride;
    private int? _statusFreeBytesOverride;

    // FX (compressed) workflow state
    private bool _isFxDecoded;
    private byte[]? _fxOriginalRomBytes;
    private XplorerCheatEditorWinForms.Services.FxRecompressor.FxVariant? _fxVariant;
    private byte[]? _fxDecodedTailBytes; // bytes after last parsed cheat in decoded_lzw_raw.bin
    private int _fxDecodedTailStart; // offset where tail begins in decoded block

    // Prevent programmatic UI updates from overwriting parsed data.
    private bool _suppressGridSync;
    private bool _suppressCheatUiEvents;

    public MainForm()
    {
		    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            this.ShowInTaskbar = true;
        if (!_encodingRegistered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encodingRegistered = true;
        }


        InitializeComponent();
		dgvCodes.CellValidating += dgvCodes_CellValidating;												   

            // Populate ROM size dropdown (NOPS manual dump)
            InitRomSizeDropdown();
            if (cmbRomSize.Items.Count > 0 && cmbRomSize.SelectedIndex < 0)
            {
                // default to 384 KB when available, else first item
                cmbRomSize.SelectedIndex = Math.Min(2, cmbRomSize.Items.Count - 1);
            }

		MinimumSize = Size;
        RefreshComPorts();
        UpdateUiEnabled(false);
        lblFormat.Text = "Format: (none)";
        lblRomPath.Text = "ROM: (none)";
        lblCheatBlock.Text = "Cheat block: (none)";
    }

    private void UpdateUiEnabled(bool hasDoc)
    {
        btnExportJson.Enabled = hasDoc;
        btnExportTxt.Enabled = hasDoc;
        btnSaveAs.Enabled = hasDoc;
        btnAddGame.Enabled = hasDoc;
        btnRemoveGame.Enabled = hasDoc;
        btnAddCheat.Enabled = hasDoc;
        btnRemoveCheat.Enabled = hasDoc;
		btnPasteCodes.Enabled = hasDoc;
        txtGameTitle.Enabled = hasDoc;
        txtCheatName.Enabled = hasDoc;
        chkNoCode.Enabled = hasDoc;
        dgvCodes.Enabled = hasDoc;
    }

    private void btnOpen_Click(object sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "ROM files|*.bin;*.dat;*.rom|All files|*.*",
            Title = "Open ROM dump"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        OpenRomFromPath(ofd.FileName);
    }

    private void OpenRomFromPath(string filePath)
    {
        try
        {
            ClearCurrentSelectionUi();

            var bytes = File.ReadAllBytes(filePath);

            // Reset any previous space overrides.
            _statusUsedBytesOverride = null;
            _statusFreeBytesOverride = null;

            // Reset any FX state.
            _isFxDecoded = false;
            _fxOriginalRomBytes = null;
            _fxVariant = null;

            // Reset any previous manual override.
            Xplorer.ManualCheatBlockStart = null;
            Gameshark.ManualCheatBlockStart = null;

            // Try to match this ROM against roms.json / GSroms.json (if present).
            var jsonMatch = RomJsonMatcher.TryMatch(bytes, filePath);
            if (jsonMatch != null)
            {
                bool isGameShark = RomJsonMatcher.IsLikelyGameShark(jsonMatch);

                // If this ROM is marked as uncompressed, use the cheatOffset
                // as a manual override for the cheat block start.
                if (!jsonMatch.Compressed && RomJsonMatcher.TryGetCheatOffset(jsonMatch, out int cheatOffset))
                {
                    if (isGameShark)
                        Gameshark.ManualCheatBlockStart = cheatOffset;
                    else
                        Xplorer.ManualCheatBlockStart = cheatOffset;
                }
                // If this ROM is marked as compressed, hand it off to the external
                // decompressor and skip the normal parser.
                else if (jsonMatch.Compressed && RomJsonMatcher.TryGetCheatOffset(jsonMatch, out int compressedOffset))
                {
                    HandleCompressedRom(filePath, compressedOffset);
                    return;
                }
            }

            var fmt = RomFormatRegistry.Detect(bytes, out var reason);
            if (fmt == null)
            {
                MessageBox.Show(this, reason, "Unsupported ROM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _doc = fmt.Parse(filePath, bytes);
            lblFormat.Text = $"Format: {_doc.FormatId}";
            lblRomPath.Text = $"ROM: {Path.GetFileName(filePath)}";
            lblCheatBlock.Text = $"Cheat block: 0x{_doc.CheatBlockOffset:X} - 0x{_doc.CheatBlockEndOffset:X}";

            this.txtCheatBlockStart.Text = _doc.CheatBlockOffset.ToString("X");
            this.txtCheatBlockEnd.Text = _doc.CheatBlockEndOffset.ToString("X");

            PopulateTree();
            UpdateUiEnabled(true);
            UpdateCheatSpaceStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearCurrentSelectionUi()
    {
        _selectedGame = null;
        _selectedCheat = null;

        tvGames.SelectedNode = null;
        txtGameTitle.Text = "";
        txtCheatName.Text = "";
        chkNoCode.Checked = false;
        dgvCodes.Rows.Clear();
    }

    private void UpdateCheatSpaceStatus()
    {
        if (_doc == null)
        {
            statusLabel.Text = "Ready.";
            return;
        }

        int used = _statusUsedBytesOverride ?? (_doc.CheatBlockEndOffset - _doc.CheatBlockOffset);
        if (used < 0) used = 0;

        int free = _statusFreeBytesOverride ?? GetImmediatePaddingLength(_doc.RomBytes, _doc.CheatBlockEndOffset);
        int total = used + free;
        double percent = total > 0 ? (used * 100.0) / total : 0.0;

        statusLabel.Text =
            $"Loaded {_doc.Games.Count} game(s). Cheat space: {percent:F1}% used ({free} bytes free)";
    }

    private static int GetImmediatePaddingLength(byte[] rom, int start)
    {
        if (rom == null || start < 0 || start >= rom.Length)
            return 0;

        int i = start;
        while (i < rom.Length && rom[i] == 0xFF)
            i++;

        return i - start;
    }

    private static void GetCompressedBlockSpace(byte[] romBytes, int compressedStart, out int used, out int free)
    {
        used = 0;
        free = 0;

        if (romBytes == null || romBytes.Length == 0)
            return;

        if (compressedStart < 0)
            compressedStart = 0;
        if (compressedStart >= romBytes.Length)
            return;

        int end = romBytes.Length - 1;
        while (end >= compressedStart && romBytes[end] == 0xFF)
            end--;

        int compressedEnd = Math.Max(compressedStart, end + 1);
        used = Math.Max(0, compressedEnd - compressedStart);
        free = Math.Max(0, romBytes.Length - compressedEnd);
    }

    private void PopulateTree()
    {
        tvGames.BeginUpdate();
        tvGames.Nodes.Clear();

        if (_doc == null)
        {
            tvGames.EndUpdate();
            return;
        }

        foreach (var g in _doc.Games)
        {
            var gn = new TreeNode(g.Title) { Tag = g };
            foreach (var c in g.Cheats)
                gn.Nodes.Add(new TreeNode(c.Name) { Tag = c });

            tvGames.Nodes.Add(gn);
        }

        tvGames.EndUpdate();
        tvGames.CollapseAll();
    }

    private void tvGames_AfterSelect(object sender, TreeViewEventArgs e)
    {
        if (_doc == null) return;

        // Persist edits of the previously selected cheat before switching.
        SyncGridToModel();

        if (e.Node?.Tag is GameEntry g)
        {
            _selectedGame = g;
            _selectedCheat = null;
            ShowGame(g);
            return;
        }

        if (e.Node?.Tag is CheatEntry c)
        {
            _selectedCheat = c;
            _selectedGame = e.Node.Parent?.Tag as GameEntry;
            if (_selectedGame != null) txtGameTitle.Text = _selectedGame.Title;
            ShowCheat(c);
        }
    }

    private void ShowGame(GameEntry g)
    {
        _suppressCheatUiEvents = true;
        _suppressGridSync = true;
        txtGameTitle.Text = g.Title;
        txtCheatName.Text = "";
        chkNoCode.Checked = false;
        dgvCodes.Rows.Clear();

        _suppressGridSync = false;
        _suppressCheatUiEvents = false;
    }

    private void ShowCheat(CheatEntry c)
    {
        _suppressCheatUiEvents = true;
        _suppressGridSync = true;
        txtCheatName.Text = c.Name;
        chkNoCode.Checked = c.IsNoCodeNote;
        dgvCodes.Rows.Clear();

        foreach (var line in c.Lines)
            dgvCodes.Rows.Add(line.Address.ToString("X8"), line.Value.ToString("X4"));

        _suppressGridSync = false;
        _suppressCheatUiEvents = false;
    }

    private void txtGameTitle_TextChanged(object sender, EventArgs e)
    {
        if (_suppressCheatUiEvents) return;
        if (_selectedGame == null) return;

        string newTitle = txtGameTitle.Text;
        _selectedGame.Title = newTitle;

        TreeNode? node = tvGames.SelectedNode;
        if (node?.Tag is CheatEntry)
            node = node.Parent;

        if (node?.Tag == _selectedGame)
            node.Text = newTitle;
    }

    private void txtCheatName_TextChanged(object sender, EventArgs e)
    {
        if (_suppressCheatUiEvents) return;
        if (_selectedCheat == null) return;
        var newName = txtCheatName.Text;

        // For header/template cheats, allow changing the PREFIX without converting.
        if (_selectedCheat.IsHeaderCheat && _selectedCheat.HeaderBytes is { Length: > 0 } hdr)
        {
            // 4-byte template: ?? 20 07 00  (02 = Unlimited Money)
            if (hdr.Length == 4 && hdr[1] == 0x20 && hdr[2] == 0x07 && hdr[3] == 0x00 && hdr[0] == 0x02)
            {
                const string suffix = "Unlimited Money";
                if (newName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    var prefix = newName.Substring(0, newName.Length - suffix.Length);
                    _selectedCheat.PrefixRaw = Encoding.ASCII.GetBytes(prefix);
                    _selectedCheat.Name = prefix + suffix;
                    if (tvGames.SelectedNode?.Tag == _selectedCheat)
                        tvGames.SelectedNode.Text = _selectedCheat.Name;
                    return;
                }
            }

            // 3-byte template: 01 ?? 00 (Infinite Lives/Energy/Time)
            if (hdr.Length == 3 && hdr[0] == 0x01 && hdr[2] == 0x00)
            {
                // If user keeps the exact template name, do nothing.
                if (string.Equals(newName, _selectedCheat.Name, StringComparison.Ordinal))
                    return;
            }

            // Otherwise: convert to named record.
            _selectedCheat.IsHeaderCheat = false;
            _selectedCheat.HeaderBytes = null;
            _selectedCheat.PrefixRaw = null;
            _selectedCheat.NameRaw = null;
            _selectedCheat.Name = newName;

            if (tvGames.SelectedNode?.Tag == _selectedCheat)
                tvGames.SelectedNode.Text = _selectedCheat.Name;
            return;
        }

        _selectedCheat.Name = newName;
        _selectedCheat.NameRaw = null; // re-encode on save from display name

        if (tvGames.SelectedNode?.Tag == _selectedCheat)
            tvGames.SelectedNode.Text = _selectedCheat.Name;
    }

    private void chkNoCode_CheckedChanged(object sender, EventArgs e)
    {
        if (_suppressCheatUiEvents) return;
        if (_selectedCheat == null) return;
        _selectedCheat.IsNoCodeNote = chkNoCode.Checked;
        if (_selectedCheat.IsNoCodeNote)
        {
            _selectedCheat.Lines.Clear();
            dgvCodes.Rows.Clear();
        }
    }

    private static bool IsExactHex(string text, int length)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.Length != length) return false;
        return text.All(Uri.IsHexDigit);
    }

    private void dgvCodes_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_suppressGridSync) return;
        if (_selectedCheat == null || _selectedCheat.IsNoCodeNote) return;
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (dgvCodes.Rows[e.RowIndex].IsNewRow) return;

        string text = (e.FormattedValue?.ToString() ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (e.ColumnIndex == 0)
        {
            if (!IsExactHex(text, 8))
            {
                MessageBox.Show(this,
                    "Address must contain exactly 8 hex characters.",
                    "Invalid address",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                e.Cancel = true;
            }
        }
        else if (e.ColumnIndex == 1)
        {
            if (!IsExactHex(text, 4))
            {
                MessageBox.Show(this,
                    "Value must contain exactly 4 hex characters.",
                    "Invalid value",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                e.Cancel = true;
            }
        }
    }														   

    private void dgvCodes_CellEndEdit(object sender, DataGridViewCellEventArgs e) => SyncGridToModel();
    private void dgvCodes_RowsRemoved(object sender, DataGridViewRowsRemovedEventArgs e) => SyncGridToModel();

    private void btnAddCheat_Click(object sender, EventArgs e)
    {
        if (_doc == null) return;

        var g = _selectedGame;
        if (g == null)
        {
            MessageBox.Show(this, "Select a game first.", "Add Cheat", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var cheat = new CheatEntry { Name = "New Cheat" };
        g.Cheats.Add(cheat);

        TreeNode? gameNode = tvGames.SelectedNode;
        if (gameNode?.Tag is CheatEntry)
            gameNode = gameNode.Parent;

        if (gameNode?.Tag is GameEntry)
        {
            var cn = new TreeNode(cheat.Name) { Tag = cheat };
            gameNode.Nodes.Add(cn);
            tvGames.SelectedNode = cn;
            gameNode.Expand();
        }

        UpdateCheatSpaceStatus();
    }


    private void btnAddGame_Click(object sender, EventArgs e)
    {
        if (_doc == null) return;
        SyncGridToModel();

        var title = PromptForText(this, "Add Game", "Game title:", "");
        if (title == null) return;

        title = title.Trim();
        if (title.Length == 0)
        {
            MessageBox.Show(this, "Game title cannot be empty.", "Add Game",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (title.IndexOf('\0') >= 0)
        {
            MessageBox.Show(this, "Game title cannot contain NUL (\0).", "Add Game",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_doc.Games.Any(g => string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "A game with that title already exists.", "Add Game",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

		var newGame = new GameEntry { Title = title };
		_doc.Games.Add(newGame);

		PopulateTree();

        foreach (TreeNode n in tvGames.Nodes)
        {
            if (ReferenceEquals(n.Tag, newGame))
            {
                tvGames.SelectedNode = n;
                n.EnsureVisible();
                break;
            }
        }

        UpdateCheatSpaceStatus();
    }

    private void btnRemoveGame_Click(object sender, EventArgs e)
    {
        if (_doc == null) return;
        SyncGridToModel();

        var g = _selectedGame;
        if (g == null)
        {
            MessageBox.Show(this, "Select a game first.", "Remove Game",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, $"Remove game '{g.Title}' and all its cheats?", "Remove Game",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _doc.Games.Remove(g);
        _selectedGame = null;
        _selectedCheat = null;

        PopulateTree();
        txtGameTitle.Text = "";
        txtCheatName.Text = "";
        chkNoCode.Checked = false;
        dgvCodes.Rows.Clear();

        UpdateCheatSpaceStatus();
    }

    private void btnRemoveCheat_Click(object sender, EventArgs e)
    {
        if (_doc == null || _selectedGame == null || _selectedCheat == null) return;

        if (MessageBox.Show(this, $"Remove cheat '{_selectedCheat.Name}'?", "Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _selectedGame.Cheats.Remove(_selectedCheat);

        if (tvGames.SelectedNode?.Tag == _selectedCheat)
        {
            var toRemove = tvGames.SelectedNode;
            tvGames.SelectedNode = toRemove.Parent;
            toRemove.Remove();
        }

        _selectedCheat = null;
        txtCheatName.Text = "";
        chkNoCode.Checked = false;
        dgvCodes.Rows.Clear();

        UpdateCheatSpaceStatus();
    }

private void btnPasteCodes_Click(object sender, EventArgs e)
{
    if (_doc == null) return;
    if (_selectedCheat == null) return;

    // If this cheat was marked as "no code", unmark it because we're about to add lines.
    if (_selectedCheat.IsNoCodeNote)
    {
        _selectedCheat.IsNoCodeNote = false;
        chkNoCode.Checked = false;
    }

    var text = ShowPasteCodesDialog();
    if (text == null) return; // cancelled

    if (!TryParsePastedCodes(text, out var lines, out var error))
    {
        MessageBox.Show(this, error, "Paste Codes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
    }

    ApplyPastedCodeLines(lines);
    statusLabel.Text = $"Pasted {lines.Count} code line(s).";
}

private string? ShowPasteCodesDialog()
{
    using var dlg = new Form();
    dlg.Text = "Paste Codes";
    dlg.StartPosition = FormStartPosition.CenterParent;
    dlg.MinimizeBox = false;
    dlg.MaximizeBox = false;
    dlg.ShowInTaskbar = false;
    dlg.FormBorderStyle = FormBorderStyle.Sizable;
    dlg.ClientSize = new Size(700, 500);

    // Layout: label (autosize) + textbox (fill) + buttons (bottom-right)
    var layout = new TableLayoutPanel();
    layout.Dock = DockStyle.Fill;
    layout.ColumnCount = 1;
    layout.RowCount = 3;
    layout.Padding = new Padding(10);
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    var lbl = new Label();
    lbl.AutoSize = true;
    lbl.Dock = DockStyle.Fill;
    lbl.Margin = new Padding(0, 0, 0, 8);
    lbl.Text = "Paste one code per line as:  ADDRESS VALUE\r\n" +
               "Examples: 800997F8 3000   or   $800997F8 3000\r\n" +
               "8-hex values are split into two 16-bit lines (addr, addr+2).";

    var tb = new TextBox();
    tb.Multiline = true;
    tb.ScrollBars = ScrollBars.Both;
    tb.AcceptsReturn = true;
    tb.AcceptsTab = true;
    tb.WordWrap = false;
    tb.Dock = DockStyle.Fill;
    tb.Font = new Font(FontFamily.GenericMonospace, 9f);

    var buttons = new FlowLayoutPanel();
    buttons.Dock = DockStyle.Fill;
    buttons.FlowDirection = FlowDirection.RightToLeft;
    buttons.WrapContents = false;
    buttons.AutoSize = true;
    buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
    buttons.Margin = new Padding(0, 8, 0, 0);

    var btnOk = new Button();
    btnOk.Text = "OK";
    btnOk.DialogResult = DialogResult.OK;
    btnOk.AutoSize = true;
    btnOk.MinimumSize = new Size(90, 28);

    var btnCancel = new Button();
    btnCancel.Text = "Cancel";
    btnCancel.DialogResult = DialogResult.Cancel;
    btnCancel.AutoSize = true;
    btnCancel.MinimumSize = new Size(90, 28);

    buttons.Controls.Add(btnCancel);
    buttons.Controls.Add(btnOk);

    layout.Controls.Add(lbl, 0, 0);
    layout.Controls.Add(tb, 0, 1);
    layout.Controls.Add(buttons, 0, 2);

    dlg.Controls.Add(layout);

    dlg.AcceptButton = btnOk;
    dlg.CancelButton = btnCancel;

    dlg.Shown += (_, __) => tb.Focus();

    return dlg.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
}

private bool TryParsePastedCodes(string input, out List<CodeLine> lines, out string error)
{
    lines = new List<CodeLine>();
    error = "";

    var lineNo = 0;
    foreach (var raw in input.Replace("\r", "").Split('\n'))
    {
        lineNo++;
        var s = raw.Trim();
        if (string.IsNullOrWhiteSpace(s)) continue;

        // Remove comments
        var cidx = s.IndexOf("//", StringComparison.Ordinal);
        if (cidx >= 0) s = s.Substring(0, cidx).Trim();
        if (string.IsNullOrWhiteSpace(s)) continue;
        if (s.StartsWith("#")) continue;

        // Normalize separators
        s = s.Replace("\t", " ");
        s = s.Replace(",", " ");
        s = s.Replace("=", " ");
        s = s.Replace(":", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");

        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            error = $"Line {lineNo}: expected 'ADDRESS VALUE'.";
            return false;
        }

        var addrStr = parts[0];
        var valStr = parts[1];

        if (!TryParseHex32(addrStr, out var addr))
        {
            error = $"Line {lineNo}: invalid address '{addrStr}'.";
            return false;
        }

        // Try 16-bit value first
        if (TryParseHex16(valStr, out var v16))
        {
            lines.Add(new CodeLine { Address = addr, Value = v16 });
            continue;
        }

        // Allow 32-bit values as 8 hex chars (split into two 16-bit lines)
        var cleanVal = valStr.Replace("$", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (cleanVal.Length is > 4 and <= 8 && uint.TryParse(cleanVal, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v32))
        {
            var hi = (ushort)((v32 >> 16) & 0xFFFF);
            var lo = (ushort)(v32 & 0xFFFF);
            lines.Add(new CodeLine { Address = addr, Value = hi });
            lines.Add(new CodeLine { Address = addr + 2, Value = lo });
            continue;
        }

        error = $"Line {lineNo}: invalid value '{valStr}'. Use 4 hex digits (16-bit) or 8 hex digits (32-bit).";
        return false;
    }

    if (lines.Count == 0)
    {
        error = "No valid codes found to paste.";
        return false;
    }

    return true;
}

private void ApplyPastedCodeLines(List<CodeLine> lines)
{
    if (_selectedCheat == null) return;

    _selectedCheat.Lines.Clear();
    _selectedCheat.Lines.AddRange(lines);

    _suppressGridSync = true;
    try
    {
        dgvCodes.Rows.Clear();
        foreach (var l in lines)
        {
            dgvCodes.Rows.Add(l.Address.ToString("X8"), l.Value.ToString("X4"));
        }
    }
    finally
    {
        _suppressGridSync = false;
    }

    UpdateCheatSpaceStatus();
}

    private void SyncGridToModel()
    {
        if (_suppressGridSync) return;
        if (_selectedCheat == null || _selectedCheat.IsNoCodeNote) return;

        var lines = new List<CodeLine>();
        foreach (DataGridViewRow row in dgvCodes.Rows)
        {
            if (row.IsNewRow) continue;

            var addrStr = (row.Cells[0].Value?.ToString() ?? "").Trim();
            var valStr = (row.Cells[1].Value?.ToString() ?? "").Trim();

            if (string.IsNullOrWhiteSpace(addrStr) && string.IsNullOrWhiteSpace(valStr))
                continue;

            if (addrStr.Length != 8 || valStr.Length != 4 ||
                !TryParseHex32(addrStr, out var addr) || !TryParseHex16(valStr, out var val))
            {
                statusLabel.Text = "Address must be 8 hex chars and value 4 hex chars.";
                continue;
            }
            lines.Add(new CodeLine { Address = addr, Value = val });
        }

        _selectedCheat.Lines.Clear();
        _selectedCheat.Lines.AddRange(lines);
        UpdateCheatSpaceStatus();
    }

    private static bool TryParseHex32(string s, out uint v)
    {
        v = 0;
        s = s.Replace("$", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        return uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v);
    }

    private static bool TryParseHex16(string s, out ushort v)
    {
        v = 0;
        s = s.Replace("$", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        return ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v);
    }

    private void btnExportJson_Click(object sender, EventArgs e)
    {
        if (_doc == null) return;
        SyncGridToModel();

        using var sfd = new SaveFileDialog { Filter = "JSON|*.json", FileName = "cheats.json" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var payload = new
        {
            format = _doc.FormatId,
            source = Path.GetFileName(_doc.SourcePath),
            cheatBlockOffset = _doc.CheatBlockOffset,
            cheatBlockEndOffset = _doc.CheatBlockEndOffset,
            games = _doc.Games.Select(g => new
            {
                title = g.Title,
                cheats = g.Cheats.Select(c => new
                {
                    name = c.Name,
                    noCode = c.IsNoCodeNote,
                    codes = c.Lines.Select(l => new { address = l.Address.ToString("X8"), value = l.Value.ToString("X4") }).ToList()
                }).ToList()
            }).ToList()
        };

        File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        statusLabel.Text = "Exported JSON.";
    }

    private void btnExportTxt_Click(object sender, EventArgs e)
    {
        if (_doc == null) return;
        SyncGridToModel();

        using var sfd = new SaveFileDialog { Filter = "Text|*.txt", FileName = "cheats.txt" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("@Parse FCD");
        sb.AppendLine("// Xplorer, Cheat Codes, Exported by PS1 Cheat ROM Editor");
        sb.AppendLine();

        bool firstGame = true;
        foreach (var g in _doc.Games)
        {
            if (firstGame)
            {
                sb.AppendLine("//-----");
                sb.AppendLine();
                firstGame = false;
            }
            else
            {
                sb.AppendLine("//========");
                sb.AppendLine();
            }

            sb.AppendLine($"\"{g.Title}\"");
            foreach (var c in g.Cheats)
            {
                sb.AppendLine(c.Name);
                if (c.IsNoCodeNote || c.Lines.Count == 0)
                {
                    sb.AppendLine("$NoCode");
                }
                else
                {
                    foreach (var l in c.Lines)
                        sb.AppendLine(l.ToString());
                }
            }

            sb.AppendLine();
        }
        // Final end marker
        sb.AppendLine("//========");


        File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
        statusLabel.Text = "Exported TXT.";
    }

    private void btnSaveAs_Click(object sender, EventArgs e)
    {
        if (_doc == null) return;
        SyncGridToModel();

        using var sfd = new SaveFileDialog
        {
            Filter = "ROM files|*.bin;*.rom;*.dat|All files|*.*",
            FileName = Path.GetFileNameWithoutExtension(_doc.SourcePath) + "_patched.bin"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            // Special case: FX decoded (compressed) ROMs.
            if (_isFxDecoded)
            {
                if (_fxOriginalRomBytes == null)
                    throw new InvalidOperationException("FX original ROM bytes are missing. Re-open the ROM.");
                if (_fxVariant == null)
                    throw new InvalidOperationException("FX variant info is missing (decoded_variant.txt). Re-open the ROM.");

                // Rebuild the decoded (decompressed) cheat block.
                var fmtDecoded = new Xplorer();
                var rebuiltDecoded = fmtDecoded.Build(_doc);

                // Trim trailing 0xFF padding from the decoded stream (the encoder expects the logical payload).
                int decodedLen = rebuiltDecoded.Length;
                while (decodedLen > 0 && rebuiltDecoded[decodedLen - 1] == 0xFF)
                    decodedLen--;
                byte[] decodedPayload = rebuiltDecoded.Take(decodedLen).ToArray();

                // Append preserved tail bytes from the original decoded block (if any).
                if (_fxDecodedTailBytes is { Length: > 0 })
                {
                    var combined = new byte[decodedPayload.Length + _fxDecodedTailBytes.Length];
                    Buffer.BlockCopy(decodedPayload, 0, combined, 0, decodedPayload.Length);
                    Buffer.BlockCopy(_fxDecodedTailBytes, 0, combined, decodedPayload.Length, _fxDecodedTailBytes.Length);
                    decodedPayload = combined;
                }

                var packed = XplorerCheatEditorWinForms.Services.FxRecompressor.Recompress(decodedPayload, _fxVariant.Value);

                int start = _fxVariant.Value.Offset;
                if (start < 0 || start > _fxOriginalRomBytes.Length)
                    throw new InvalidOperationException("Invalid FX offset from variant info.");

                int availablePackedSpace = _fxOriginalRomBytes.Length - start;
                if (packed.Length > availablePackedSpace)
                    throw new InvalidOperationException(
                        $"Compressed stream too large ({packed.Length} bytes) for available space ({availablePackedSpace} bytes)." +
                        "\r\n\r\nThe repacked stream would extend past EOF.");

                // Optional exact roundtrip: if the repacked stream is byte-identical to the original
                // compressed bytes, write the untouched ROM back out.
                int originalPackedLen = Math.Max(0, Math.Min(_fxOriginalRomBytes.Length, _fxVariant.Value.CompressedEnd) - start);
                bool packedMatchesOriginal = packed.Length == originalPackedLen;
                if (packedMatchesOriginal)
                {
                    for (int i = 0; i < packed.Length; i++)
                    {
                        if (_fxOriginalRomBytes[start + i] != packed[i])
                        {
                            packedMatchesOriginal = false;
                            break;
                        }
                    }
                }

                if (packedMatchesOriginal)
                {
                    File.WriteAllBytes(sfd.FileName, _fxOriginalRomBytes);

                    MessageBox.Show(this,
                        "Saved patched ROM (FX repacked, byte-identical to original).",
                        "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    statusLabel.Text = "Saved patched ROM (FX repacked, byte-identical to original).";
                    return;
                }

                // Write packed stream back into the original ROM and fill the rest to EOF with 0xFF.
                var outRom = (byte[])_fxOriginalRomBytes.Clone();
                for (int i = start; i < outRom.Length; i++)
                    outRom[i] = 0xFF;
                Buffer.BlockCopy(packed, 0, outRom, start, packed.Length);

                File.WriteAllBytes(sfd.FileName, outRom);

                MessageBox.Show(this,
                    "Saved patched ROM (FX repacked).",
                    "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);

                statusLabel.Text = "Saved patched ROM (FX repacked).";
                return;
            }

            var fmt = RomFormatRegistry.Detect(_doc.RomBytes, out _);
            if (fmt == null) throw new InvalidOperationException("Format not detected.");

            var rebuilt = fmt.Build(_doc);
            File.WriteAllBytes(sfd.FileName, rebuilt);

            MessageBox.Show(this,
                "Saved patched ROM.\n\nNote: ROM rebuild is experimental until we fully confirm all pointer/checksum behavior.",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);

            statusLabel.Text = "Saved patched ROM.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void txtCheatBlockStart_TextChanged(object sender, EventArgs e)
    {

    }


    private void txtCheatBlockStart_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ApplyManualCheatBlockOffset();
        }
    }

    private void ApplyManualCheatBlockOffset()
    {
        if (_doc == null)
            return;

        var text = this.txtCheatBlockStart.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            Xplorer.ManualCheatBlockStart = null;
        }
        else
        {
            if (!int.TryParse(text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int manualOffset))
            {
                MessageBox.Show(this,
                    "Invalid offset. Please enter a hexadecimal value (e.g. 2AFFF).",
                    "Invalid offset",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Xplorer.ManualCheatBlockStart = manualOffset;
        }

        var path = _doc.SourcePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            MessageBox.Show(this,
                "Cannot reload ROM: original source path is not available.",
                "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            _statusUsedBytesOverride = null;
            _statusFreeBytesOverride = null;
            var fmt = RomFormatRegistry.Detect(bytes, out var reason);
            if (fmt == null)
            {
                MessageBox.Show(this, reason, "Unsupported ROM",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _doc = fmt.Parse(path, bytes);
            lblFormat.Text = $"Format: {_doc.FormatId}";
            lblRomPath.Text = $"ROM: {Path.GetFileName(path)}";
            lblCheatBlock.Text =
                $"Cheat block: 0x{_doc.CheatBlockOffset:X} - 0x{_doc.CheatBlockEndOffset:X}";

            this.txtCheatBlockStart.Text = _doc.CheatBlockOffset.ToString("X");
            this.txtCheatBlockEnd.Text = _doc.CheatBlockEndOffset.ToString("X");

            PopulateTree();
            UpdateUiEnabled(true);
            UpdateCheatSpaceStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Failed to reload ROM with manual cheat block offset:\n" + ex.Message,
                "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void HandleCompressedRom(string romPath, int cheatOffset)
{
    string offsetHex = "0x" + cheatOffset.ToString("X");

    string fxExe = Path.Combine(AppContext.BaseDirectory, "Tools", "FxTokenDecoder.exe");
    if (!File.Exists(fxExe))
    {
        MessageBox.Show(this,
            "FxTokenDecoder.exe niet gevonden in dezelfde map als PS1 Cheat ROM Editor.\n" +
            "Zet de decompressor daar neer.",
            "FxTokenDecoder ontbreekt",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        return;
    }

    string tmpDir = Path.Combine(AppContext.BaseDirectory, "tmp");
    Directory.CreateDirectory(tmpDir);

    string baseName = Path.GetFileNameWithoutExtension(romPath);
    string outTextPath = Path.Combine(tmpDir, baseName + "_decoded.txt");

    var psi = new ProcessStartInfo
    {
        FileName = fxExe,
        Arguments = $"\"{romPath}\" {offsetHex} \"{outTextPath}\" 200000",
        UseShellExecute = false,
        CreateNoWindow = true,
    };

        try
        {
            using var proc = Process.Start(psi);
            proc?.WaitForExit();

            string rawPath = Path.Combine(tmpDir, "decoded_lzw_raw.bin");
            if (proc?.ExitCode == 0 && File.Exists(rawPath))
            {
                byte[] originalRom = File.ReadAllBytes(romPath);
                byte[] block = File.ReadAllBytes(rawPath);

                // Load variant info written by FxTokenDecoder (needed for recompress / repack).
                string variantPath = Path.Combine(tmpDir, "decoded_variant.txt");
                if (!XplorerCheatEditorWinForms.Services.FxRecompressor.TryLoadVariantFile(variantPath, out var variant, out var varErr))
                {
                    MessageBox.Show(this,
                        "decoded_variant.txt ontbreekt of kon niet worden gelezen.\n\n" + varErr,
                        "FX variant ontbreekt",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                _isFxDecoded = true;
                _fxOriginalRomBytes = originalRom;
                _fxVariant = variant;

                GetCompressedBlockSpace(originalRom, cheatOffset, out int packedUsed, out int packedFree);
                _statusUsedBytesOverride = packedUsed;
                _statusFreeBytesOverride = packedFree;

                // Use decompressed block as ROM for parser: cheat block at offset 0
                Xplorer.ManualCheatBlockStart = 0;
                var fmt = new Xplorer();
                var doc = fmt.Parse(romPath, block);

                _doc = doc;

                // Preserve any trailing bytes after the last parsed cheat. Some FX ROMs contain
                // non-cheat tail bytes that must be preserved for byte-identical repacks.
                _fxDecodedTailStart = _doc.CheatBlockEndOffset;
                _fxDecodedTailBytes = (_fxDecodedTailStart >= 0 && _fxDecodedTailStart < block.Length)
                    ? block.Skip(_fxDecodedTailStart).ToArray()
                    : Array.Empty<byte>();
                lblFormat.Text = $"Format: {_doc.FormatId} (FX decoded)";
                lblRomPath.Text = $"ROM: {Path.GetFileName(romPath)}";
                lblCheatBlock.Text =
                    $"Cheat block: 0x{_doc.CheatBlockOffset:X} - 0x{_doc.CheatBlockEndOffset:X}";

                this.txtCheatBlockStart.Text = _doc.CheatBlockOffset.ToString("X");
                this.txtCheatBlockEnd.Text = _doc.CheatBlockEndOffset.ToString("X");

                PopulateTree();
                UpdateUiEnabled(true);
                UpdateCheatSpaceStatus();
            }
            else
            {
                MessageBox.Show(this,
                    "FxTokenDecoder kon de ROM niet succesvol decoderen (decoded_lzw_raw.bin ontbreekt).",
                    "Decompressie mislukt",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Fout bij starten van FxTokenDecoder:\n" + ex.Message,
                "Fout",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }


    private static string? PromptForText(IWin32Window owner, string title, string label, string defaultValue)
    {
        using var dlg = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };

        var lbl = new Label
        {
            AutoSize = true,
            Text = label,
            Left = 12,
            Top = 12
        };

        var tb = new TextBox
        {
            Left = 12,
            Top = lbl.Bottom + 8,
            Width = 420,
            Text = defaultValue ?? string.Empty,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        var btnOk = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Width = 90,
            Left = tb.Left + tb.Width - 90,
            Top = tb.Bottom + 12,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 90,
            Left = btnOk.Left - 98,
            Top = btnOk.Top,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        dlg.Controls.Add(lbl);
        dlg.Controls.Add(tb);
        dlg.Controls.Add(btnCancel);
        dlg.Controls.Add(btnOk);

        dlg.AcceptButton = btnOk;
        dlg.CancelButton = btnCancel;

        dlg.ClientSize = new Size(tb.Right + 12, btnOk.Bottom + 12);

        tb.SelectAll();
        tb.Focus();

        return dlg.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
    }


    private void txtSearch_TextChanged(object sender, EventArgs e)
    {
        if (_doc == null) return;

        string q = txtSearch.Text.Trim().ToLowerInvariant();

        tvGames.BeginUpdate();
        tvGames.Nodes.Clear();

        foreach (var g in _doc.Games)
        {
            bool gameMatch = g.Title.ToLowerInvariant().Contains(q);
            var gameNode = new TreeNode(g.Title) { Tag = g };

            foreach (var c in g.Cheats)
            {
                bool cheatMatch =
                    c.Name.ToLowerInvariant().Contains(q) ||
                    c.Lines.Any(l =>
                        l.Address.ToString("X8").ToLowerInvariant().Contains(q) ||
                        l.Value.ToString("X4").ToLowerInvariant().Contains(q));

                if (gameMatch || cheatMatch || q == "")
                    gameNode.Nodes.Add(new TreeNode(c.Name) { Tag = c });
            }

            if (gameNode.Nodes.Count > 0 || gameMatch || q == "")
                tvGames.Nodes.Add(gameNode);
        }

        tvGames.EndUpdate();
    }

    private void MainForm_Load(object sender, EventArgs e)
    {

    }

    private void lblSearch_Click(object sender, EventArgs e)
    {

    }
    // --- NOPS helpers (Dump/Flash integration) ---
    private static string[] GetComPortNames()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key == null) return Array.Empty<string>();
            return key.GetValueNames()
                .Select(n => key.GetValue(n)?.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
				.Select(s => s!)				
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    
    private void InitRomSizeDropdown()
    {
        try
        {
            cmbRomSize.Items.Clear();
            cmbRomSize.Items.Add(new ComboBoxItem<int>("128 KB", 128 * 1024));
            cmbRomSize.Items.Add(new ComboBoxItem<int>("256 KB", 256 * 1024));
            cmbRomSize.Items.Add(new ComboBoxItem<int>("384 KB", 384 * 1024));
            cmbRomSize.Items.Add(new ComboBoxItem<int>("512 KB", 512 * 1024));
            cmbRomSize.Items.Add(new ComboBoxItem<int>("256 KB GS V2", 256 * 1024));
            cmbRomSize.Items.Add(new ComboBoxItem<int>("512 KB GS V3", 512 * 1024));
            cmbRomSize.SelectedIndex = 2; // default 384 KB
        }
        catch { }
    }

private void RefreshComPorts()
    {
        try
        {
            var ports = GetComPortNames()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var current = cmbComPort.SelectedItem as string ?? cmbComPort.Text;

            cmbComPort.BeginUpdate();
            cmbComPort.Items.Clear();
            cmbComPort.Items.AddRange(ports);
            cmbComPort.EndUpdate();

            if (!string.IsNullOrWhiteSpace(current) && ports.Contains(current, StringComparer.OrdinalIgnoreCase))
                cmbComPort.SelectedItem = ports.First(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
            else if (ports.Length > 0)
                cmbComPort.SelectedIndex = 0;
        }
        catch
        {
            // ignore; COM enumeration can fail on some systems
        }
    }

        private int? _nopsDetectedSizeBytes;
    private int GetSelectedDumpSizeBytes()
    {
        if (cmbRomSize?.SelectedItem is ComboBoxItem<int> item)
            return item.Value;

        // fallback: try parse text like "384 KB"
        var t = cmbRomSize?.Text ?? "";
        var digits = new string(t.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var kb) && kb > 0)
            return kb * 1024;

        // default
        return 384 * 1024;
    }

    private async void btnManualDump_Click(object sender, EventArgs e)
    {
        var com = GetSelectedComPort();
        if (com == null)
        {
            MessageBox.Show(this, "Select a COM port first.", "Manual Dump", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string selText = (cmbRomSize?.SelectedItem as object)?.ToString() ?? (cmbRomSize?.Text ?? "");
bool isGsV2 = selText.StartsWith("256 KB GS V2", StringComparison.OrdinalIgnoreCase);
bool isGsV3 = selText.StartsWith("512 KB GS V3", StringComparison.OrdinalIgnoreCase);

int sizeBytes = isGsV2 ? (256 * 1024) : isGsV3 ? (512 * 1024) : GetSelectedDumpSizeBytes();

string defaultName = isGsV2 ? "PAR2_256KB.rom" : isGsV3 ? "PAR3_512KB.rom" : $"cartridge_{sizeBytes / 1024}KB.bin";

using var sfd = new SaveFileDialog
{
    Title = "Save cartridge dump as",
    Filter = "ROM/BIN files (*.rom;*.bin)|*.rom;*.bin|All files (*.*)|*.*",
    FileName = defaultName,
    AddExtension = true,
    OverwritePrompt = true
};

        if (sfd.ShowDialog(this) != DialogResult.OK)
            return;

        string baseDir = AppContext.BaseDirectory;
        string nopsExe = Path.Combine(baseDir, "Tools", "NOPS.exe");

        bool success;
string? errMsg = null;

if (isGsV3)
{
    var r = await RunWithNopsDialogAsync("Dumping GS V3…", (cb, ct) => Par3Dumper.DumpPar3_512KBAsync(nopsExe, com, sfd.FileName, this, cb, ct));
    success = r.Success;
    errMsg = r.Error;
}
else if (isGsV2)
{
    var r = await RunWithNopsDialogAsync("Dumping GS V2…", (cb, ct) => NopsCartridgeService.DumpGsV2_256KBAsync(nopsExe, com, sfd.FileName, this, cb, ct));
    success = r.Success;
    errMsg = r.Error;
}
else
{
    var r = await RunWithNopsDialogAsync("Dumping…", (cb, ct) => NopsCartridgeService.DumpAsync(nopsExe, com, sizeBytes, sfd.FileName, this, cb, ct));
    success = r.Success;
    errMsg = r.Error;
}

if (!success)
{
    MessageBox.Show(this, errMsg ?? "Dump failed.", "Manual Dump", MessageBoxButtons.OK, MessageBoxIcon.Error);
    return;
}

        var resp = MessageBox.Show(this, $"Dump completed:\n{sfd.FileName}\n\nLoad this ROM now?", "Manual Dump", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (resp == DialogResult.Yes)
            OpenRomFromPath(sfd.FileName);
    }

    private async void btnFlashCartridge_Click(object sender, EventArgs e)
    {
        var com = GetSelectedComPort();
        if (com == null)
        {
            MessageBox.Show(this, "Select a COM port first.", "Flash Cartridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Title = "Select ROM file to flash",
            Filter = "ROM/BIN files (*.rom;*.bin)|*.rom;*.bin|All files (*.*)|*.*",
            Multiselect = false
        };

        if (ofd.ShowDialog(this) != DialogResult.OK)
            return;

        var confirm = MessageBox.Show(this,
            $"This will flash the cartridge using NOPS.\n\nFile:\n{ofd.FileName}\n\nContinue?",
            "Flash Cartridge",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
            return;

        string baseDir = AppContext.BaseDirectory;
        string nopsExe = Path.Combine(baseDir, "Tools", "NOPS.exe");

        var res = await RunWithNopsDialogAsync("Flashing…", (cb, ct) => NopsCartridgeService.FlashRomAsync(nopsExe, com, ofd.FileName, this, cb, ct));

if (!res.Success)
        {
            MessageBox.Show(this, res.Error ?? "Flash failed.", "Flash Cartridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(this, "Flash completed.", "Flash Cartridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }



    private async Task<bool> RunNopsDumpAsync(string nopsExe, string comPort, int sizeBytes, string outFile)
    {
        string lenHex = "0x" + sizeBytes.ToString("X");
        var psi = new ProcessStartInfo
        {
            FileName = nopsExe,
            Arguments = $"/dump 0x1F000000 {lenHex} \"{outFile}\" {comPort}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return false;

            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                MessageBox.Show(this,
                    $"NOPS dump failed (exit {proc.ExitCode}).\n\n{stdout}\n{stderr}",
                    "Dump Cartridge",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            return File.Exists(outFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Dump Cartridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

private string? GetSelectedComPort()
    {
        var s = (cmbComPort.SelectedItem as string) ?? cmbComPort.Text;
        s = s?.Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private void btnComRefresh_Click(object sender, EventArgs e)
        => RefreshComPorts();

    private async void btnDetectSize_Click(object sender, EventArgs e)
    {
        var com = GetSelectedComPort();
        if (com == null)
        {
            MessageBox.Show(this, "Select a COM port first.", "Detect Size", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string baseDir = AppContext.BaseDirectory;
        string nopsExe = Path.Combine(baseDir, "Tools", "NOPS.exe");
        string romsJson = Path.Combine(baseDir, "Tools", "roms.json");

        var result = await RunWithNopsDialogAsync("Detecting…", (cb, ct) => NopsDetectService.DetectXplorerAsync(nopsExe, romsJson, com, this, cb, ct));

        if (!result.Success)
        {
            MessageBox.Show(this, result.Error ?? "Detection failed.", "Detect Size", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _nopsDetectedSizeBytes = result.RomSizeKB * 1024;

        // Sync manual size dropdown
        try
        {
            for (int i = 0; i < cmbRomSize.Items.Count; i++)
            {
                if (cmbRomSize.Items[i] is ComboBoxItem<int> it && it.Value == _nopsDetectedSizeBytes)
                {
                    cmbRomSize.SelectedIndex = i;
                    break;
                }
            }
        }
        catch { }


        var msg = $"Detected: {result.RomName}\nSize: {result.RomSizeKB} KB\n\nDump now with this size?";
        var choice = MessageBox.Show(this, msg, "Detect Size", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        if (choice != DialogResult.Yes)
            return;

        using var sfd = new SaveFileDialog
        {
            Title = "Save cartridge dump as",
            Filter = "ROM dumps (*.bin;*.rom)|*.bin;*.rom|All files (*.*)|*.*",
            FileName = "xplorer_dump.bin",
            OverwritePrompt = true
        };

        if (sfd.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(sfd.FileName))
            return;

        if (!File.Exists(nopsExe))
        {
            MessageBox.Show(this, $"NOPS.exe not found:\n{nopsExe}", "Dump Cartridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var dumpResult = await RunWithNopsDialogAsync("Dumping…",
            (cb, ct) => NopsCartridgeService.DumpAsync(nopsExe, com, _nopsDetectedSizeBytes.Value, sfd.FileName, this, cb, ct));

        if (!dumpResult.Success)
        {
            MessageBox.Show(this, dumpResult.Error ?? "Dump failed.", "Dump Cartridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var resp = MessageBox.Show(this, "Dump completed.\r\n\r\nLoad this ROM now?", "Dump Cartridge", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (resp == DialogResult.Yes)
            OpenRomFromPath(sfd.FileName);
    }


    private sealed class ComboBoxItem<T>
    {
        public ComboBoxItem(string text, T value)
        {
            Text = text;
            Value = value;
        }

        public string Text { get; }
        public T Value { get; }

        public override string ToString() => Text;
    }

}