namespace XplorerCheatEditorWinForms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    private Button btnOpen;
    private Button btnExportJson;
    private Button btnExportTxt;
    private Button btnSaveAs;


    private GroupBox grpNops;
    private ComboBox cmbComPort;
    private Button btnComRefresh;
    private Button btnDetectSize;
    private ComboBox cmbRomSize;
    private Button btnManualDump;
    private Button btnFlashCartridge;
    private Button btnAddGame;
    private Button btnRemoveGame;

    private SplitContainer splitMain;
    private TreeView tvGames;
    private TextBox txtSearch;
    private Label lblSearch;

    private TextBox txtGameTitle;
    private TextBox txtCheatName;
    private CheckBox chkNoCode;
    private DataGridView dgvCodes;

    private Button btnAddCheat;
    private Button btnRemoveCheat;


    private Button btnPasteCodes;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel statusLabel;

    private Label lblFormat;
    private Label lblRomPath;
    private Label lblCheatBlock;

    private TextBox txtCheatBlockStart;
    private TextBox txtCheatBlockEnd;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        btnOpen = new Button();
        btnExportJson = new Button();
        btnExportTxt = new Button();
        btnSaveAs = new Button();
        grpNops = new GroupBox();
        cmbComPort = new ComboBox();
        btnComRefresh = new Button();
        btnDetectSize = new Button();
        cmbRomSize = new ComboBox();
        btnManualDump = new Button();
        btnFlashCartridge = new Button();
        btnAddGame = new Button();
        btnRemoveGame = new Button();
        splitMain = new SplitContainer();
        tvGames = new TreeView();
        btnPasteCodes = new Button();
        lblGame = new Label();
        txtGameTitle = new TextBox();
        lblCheat = new Label();
        txtCheatName = new TextBox();
        chkNoCode = new CheckBox();
        btnAddCheat = new Button();
        btnRemoveCheat = new Button();
        dgvCodes = new DataGridView();
        dataGridViewTextBoxColumn1 = new DataGridViewTextBoxColumn();
        dataGridViewTextBoxColumn2 = new DataGridViewTextBoxColumn();
        txtSearch = new TextBox();
        lblSearch = new Label();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();
        lblFormat = new Label();
        lblRomPath = new Label();
        lblCheatBlock = new Label();
        txtCheatBlockStart = new TextBox();
        txtCheatBlockEnd = new TextBox();
        grpNops.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitMain).BeginInit();
        splitMain.Panel1.SuspendLayout();
        splitMain.Panel2.SuspendLayout();
        splitMain.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvCodes).BeginInit();
        statusStrip.SuspendLayout();
        SuspendLayout();
        // 
        // btnOpen
        // 
        btnOpen.Location = new Point(12, 12);
        btnOpen.Name = "btnOpen";
        btnOpen.Size = new Size(120, 23);
        btnOpen.TabIndex = 0;
        btnOpen.Text = "Open ROM...";
        btnOpen.Click += btnOpen_Click;
        // 
        // btnExportJson
        // 
        btnExportJson.Location = new Point(140, 12);
        btnExportJson.Name = "btnExportJson";
        btnExportJson.Size = new Size(120, 23);
        btnExportJson.TabIndex = 1;
        btnExportJson.Text = "Export JSON...";
        btnExportJson.Click += btnExportJson_Click;
        // 
        // btnExportTxt
        // 
        btnExportTxt.Location = new Point(268, 12);
        btnExportTxt.Name = "btnExportTxt";
        btnExportTxt.Size = new Size(120, 23);
        btnExportTxt.TabIndex = 2;
        btnExportTxt.Text = "Export TXT...";
        btnExportTxt.Click += btnExportTxt_Click;
        // 
        // btnSaveAs
        // 
        btnSaveAs.Location = new Point(396, 12);
        btnSaveAs.Name = "btnSaveAs";
        btnSaveAs.Size = new Size(140, 23);
        btnSaveAs.TabIndex = 3;
        btnSaveAs.Text = "Save ROM As...";
        btnSaveAs.Click += btnSaveAs_Click;
        // 
        // grpNops
        // 
        grpNops.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        grpNops.Controls.Add(cmbComPort);
        grpNops.Controls.Add(btnComRefresh);
        grpNops.Controls.Add(btnDetectSize);
        grpNops.Controls.Add(cmbRomSize);
        grpNops.Controls.Add(btnManualDump);
        grpNops.Controls.Add(btnFlashCartridge);
        grpNops.Location = new Point(634, 38);
        grpNops.Name = "grpNops";
        grpNops.Size = new Size(423, 86);
        grpNops.TabIndex = 200;
        grpNops.TabStop = false;
        grpNops.Text = "NOPS";
        // 
        // cmbComPort
        // 
        cmbComPort.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbComPort.Location = new Point(12, 22);
        cmbComPort.Name = "cmbComPort";
        cmbComPort.Size = new Size(90, 23);
        cmbComPort.TabIndex = 0;
        // 
        // btnComRefresh
        // 
        btnComRefresh.Location = new Point(108, 21);
        btnComRefresh.Name = "btnComRefresh";
        btnComRefresh.Size = new Size(80, 23);
        btnComRefresh.TabIndex = 1;
        btnComRefresh.Text = "Refresh";
        btnComRefresh.UseVisualStyleBackColor = true;
        btnComRefresh.Click += btnComRefresh_Click;
        // 
        // btnDetectSize
        // 
        btnDetectSize.Location = new Point(194, 22);
        btnDetectSize.Name = "btnDetectSize";
        btnDetectSize.Size = new Size(155, 23);
        btnDetectSize.TabIndex = 2;
        btnDetectSize.Text = "Detect Size (Xplorer)";
        btnDetectSize.UseVisualStyleBackColor = true;
        btnDetectSize.Click += btnDetectSize_Click;
        // 
        // cmbRomSize
        // 
        cmbRomSize.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRomSize.Location = new Point(12, 54);
        cmbRomSize.Name = "cmbRomSize";
        cmbRomSize.Size = new Size(89, 23);
        cmbRomSize.TabIndex = 203;
        // 
        // btnManualDump
        // 
        btnManualDump.Location = new Point(108, 54);
        btnManualDump.Name = "btnManualDump";
        btnManualDump.Size = new Size(143, 23);
        btnManualDump.TabIndex = 204;
        btnManualDump.Text = "Manual Dump";
        btnManualDump.UseVisualStyleBackColor = true;
        btnManualDump.Click += btnManualDump_Click;
        // 
        // btnFlashCartridge
        // 
        btnFlashCartridge.Location = new Point(257, 54);
        btnFlashCartridge.Name = "btnFlashCartridge";
        btnFlashCartridge.Size = new Size(150, 23);
        btnFlashCartridge.TabIndex = 205;
        btnFlashCartridge.Text = "Flash Cartridge";
        btnFlashCartridge.UseVisualStyleBackColor = true;
        btnFlashCartridge.Click += btnFlashCartridge_Click;
        // 
        // btnAddGame
        // 
        btnAddGame.Location = new Point(12, 69);
        btnAddGame.Name = "btnAddGame";
        btnAddGame.Size = new Size(110, 23);
        btnAddGame.TabIndex = 4;
        btnAddGame.Text = "Add Game";
        btnAddGame.Click += btnAddGame_Click;
        // 
        // btnRemoveGame
        // 
        btnRemoveGame.Location = new Point(128, 69);
        btnRemoveGame.Name = "btnRemoveGame";
        btnRemoveGame.Size = new Size(122, 23);
        btnRemoveGame.TabIndex = 5;
        btnRemoveGame.Text = "Remove Game";
        btnRemoveGame.Click += btnRemoveGame_Click;
        // 
        // splitMain
        // 
        splitMain.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        splitMain.Location = new Point(12, 110);
        splitMain.Name = "splitMain";
        // 
        // splitMain.Panel1
        // 
        splitMain.Panel1.Controls.Add(tvGames);
        // 
        // splitMain.Panel2
        // 
        splitMain.Panel2.Controls.Add(btnPasteCodes);
        splitMain.Panel2.Controls.Add(lblGame);
        splitMain.Panel2.Controls.Add(txtGameTitle);
        splitMain.Panel2.Controls.Add(lblCheat);
        splitMain.Panel2.Controls.Add(txtCheatName);
        splitMain.Panel2.Controls.Add(btnRemoveGame);
        splitMain.Panel2.Controls.Add(btnAddGame);
        splitMain.Panel2.Controls.Add(chkNoCode);
        splitMain.Panel2.Controls.Add(btnAddCheat);
        splitMain.Panel2.Controls.Add(btnRemoveCheat);
        splitMain.Panel2.Controls.Add(dgvCodes);
        splitMain.Size = new Size(1045, 546);
        splitMain.SplitterDistance = 353;
        splitMain.TabIndex = 9;
        // 
        // tvGames
        // 
        tvGames.Dock = DockStyle.Fill;
        tvGames.HideSelection = false;
        tvGames.Location = new Point(0, 0);
        tvGames.Name = "tvGames";
        tvGames.Size = new Size(353, 546);
        tvGames.TabIndex = 0;
        tvGames.AfterSelect += tvGames_AfterSelect;
        // 
        // btnPasteCodes
        // 
        btnPasteCodes.Location = new Point(128, 150);
        btnPasteCodes.Name = "btnPasteCodes";
        btnPasteCodes.Size = new Size(110, 23);
        btnPasteCodes.TabIndex = 7;
        btnPasteCodes.Text = "Paste Codes...";
        btnPasteCodes.UseVisualStyleBackColor = true;
        btnPasteCodes.Click += btnPasteCodes_Click;
        // 
        // lblGame
        // 
        lblGame.Location = new Point(12, 8);
        lblGame.Name = "lblGame";
        lblGame.Size = new Size(51, 18);
        lblGame.TabIndex = 0;
        lblGame.Text = "Game:";
        // 
        // txtGameTitle
        // 
        txtGameTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtGameTitle.Location = new Point(12, 33);
        txtGameTitle.Name = "txtGameTitle";
        txtGameTitle.Size = new Size(663, 23);
        txtGameTitle.TabIndex = 1;
        txtGameTitle.TextChanged += txtGameTitle_TextChanged;
        // 
        // lblCheat
        // 
        lblCheat.Location = new Point(12, 95);
        lblCheat.Name = "lblCheat";
        lblCheat.Size = new Size(80, 23);
        lblCheat.TabIndex = 2;
        lblCheat.Text = "Cheat:";
        // 
        // txtCheatName
        // 
        txtCheatName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtCheatName.Location = new Point(12, 121);
        txtCheatName.Name = "txtCheatName";
        txtCheatName.Size = new Size(663, 23);
        txtCheatName.TabIndex = 3;
        txtCheatName.TextChanged += txtCheatName_TextChanged;
        // 
        // chkNoCode
        // 
        chkNoCode.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chkNoCode.Location = new Point(525, 150);
        chkNoCode.Name = "chkNoCode";
        chkNoCode.Size = new Size(150, 24);
        chkNoCode.TabIndex = 4;
        chkNoCode.Text = "NoCode / Note";
        chkNoCode.CheckedChanged += chkNoCode_CheckedChanged;
        // 
        // btnAddCheat
        // 
        btnAddCheat.Location = new Point(12, 151);
        btnAddCheat.Name = "btnAddCheat";
        btnAddCheat.Size = new Size(110, 23);
        btnAddCheat.TabIndex = 5;
        btnAddCheat.Text = "Add Cheat";
        btnAddCheat.Click += btnAddCheat_Click;
        // 
        // btnRemoveCheat
        // 
        btnRemoveCheat.Location = new Point(244, 150);
        btnRemoveCheat.Name = "btnRemoveCheat";
        btnRemoveCheat.Size = new Size(122, 23);
        btnRemoveCheat.TabIndex = 6;
        btnRemoveCheat.Text = "Remove Cheat";
        btnRemoveCheat.Click += btnRemoveCheat_Click;
        // 
        // dgvCodes
        // 
        dgvCodes.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dgvCodes.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgvCodes.Columns.AddRange(new DataGridViewColumn[] { dataGridViewTextBoxColumn1, dataGridViewTextBoxColumn2 });
        dgvCodes.Location = new Point(12, 180);
        dgvCodes.Name = "dgvCodes";
        dgvCodes.RowHeadersVisible = false;
        dgvCodes.Size = new Size(673, 363);
        dgvCodes.TabIndex = 7;
        dgvCodes.CellEndEdit += dgvCodes_CellEndEdit;
        dgvCodes.RowsRemoved += dgvCodes_RowsRemoved;
        // 
        // dataGridViewTextBoxColumn1
        // 
        dataGridViewTextBoxColumn1.HeaderText = "Address (hex)";
        dataGridViewTextBoxColumn1.MaxInputLength = 8;
        dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
        // 
        // dataGridViewTextBoxColumn2
        // 
        dataGridViewTextBoxColumn2.HeaderText = "Value (hex)";
        dataGridViewTextBoxColumn2.MaxInputLength = 4;
        dataGridViewTextBoxColumn2.Name = "dataGridViewTextBoxColumn2";
        // 
        // txtSearch
        // 
        txtSearch.Location = new Point(68, 81);
        txtSearch.Name = "txtSearch";
        txtSearch.Size = new Size(297, 23);
        txtSearch.TabIndex = 1;
        txtSearch.TextChanged += txtSearch_TextChanged;
        // 
        // lblSearch
        // 
        lblSearch.Location = new Point(12, 84);
        lblSearch.Name = "lblSearch";
        lblSearch.Size = new Size(50, 15);
        lblSearch.TabIndex = 2;
        lblSearch.Text = "Search:";
        lblSearch.Click += lblSearch_Click;
        // 
        // statusStrip
        // 
        statusStrip.Items.AddRange(new ToolStripItem[] { statusLabel });
        statusStrip.Location = new Point(0, 659);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(1084, 22);
        statusStrip.TabIndex = 10;
        // 
        // statusLabel
        // 
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(42, 17);
        statusLabel.Text = "Ready.";
        // 
        // lblFormat
        // 
        lblFormat.Location = new Point(12, 44);
        lblFormat.Name = "lblFormat";
        lblFormat.Size = new Size(601, 17);
        lblFormat.TabIndex = 4;
        lblFormat.Text = "Format:";
        lblFormat.Visible = false;
        // 
        // lblRomPath
        // 
        lblRomPath.Location = new Point(12, 61);
        lblRomPath.Name = "lblRomPath";
        lblRomPath.Size = new Size(601, 17);
        lblRomPath.TabIndex = 5;
        lblRomPath.Text = "ROM:";
        // 
        // lblCheatBlock
        // 
        lblCheatBlock.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCheatBlock.Location = new Point(542, 12);
        lblCheatBlock.Name = "lblCheatBlock";
        lblCheatBlock.Size = new Size(78, 23);
        lblCheatBlock.TabIndex = 6;
        lblCheatBlock.Text = "Cheat block:";
        // 
        // txtCheatBlockStart
        // 
        txtCheatBlockStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        txtCheatBlockStart.Location = new Point(626, 9);
        txtCheatBlockStart.Name = "txtCheatBlockStart";
        txtCheatBlockStart.Size = new Size(80, 23);
        txtCheatBlockStart.TabIndex = 7;
        txtCheatBlockStart.TextChanged += txtCheatBlockStart_TextChanged;
        txtCheatBlockStart.KeyDown += txtCheatBlockStart_KeyDown;
        // 
        // txtCheatBlockEnd
        // 
        txtCheatBlockEnd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        txtCheatBlockEnd.Location = new Point(712, 9);
        txtCheatBlockEnd.Name = "txtCheatBlockEnd";
        txtCheatBlockEnd.ReadOnly = true;
        txtCheatBlockEnd.Size = new Size(80, 23);
        txtCheatBlockEnd.TabIndex = 8;
        txtCheatBlockEnd.TabStop = false;
        // 
        // MainForm
        // 
        ClientSize = new Size(1084, 681);
        Controls.Add(btnOpen);
        Controls.Add(grpNops);
        Controls.Add(lblSearch);
        Controls.Add(txtSearch);
        Controls.Add(btnExportJson);
        Controls.Add(btnExportTxt);
        Controls.Add(btnSaveAs);
        Controls.Add(lblFormat);
        Controls.Add(lblRomPath);
        Controls.Add(lblCheatBlock);
        Controls.Add(txtCheatBlockStart);
        Controls.Add(txtCheatBlockEnd);
        Controls.Add(splitMain);
        Controls.Add(statusStrip);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PS1 Cheat ROM Editor (Experimental)";
        Load += MainForm_Load;
        grpNops.ResumeLayout(false);
        splitMain.Panel1.ResumeLayout(false);
        splitMain.Panel2.ResumeLayout(false);
        splitMain.Panel2.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)splitMain).EndInit();
        splitMain.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dgvCodes).EndInit();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
    private Label lblGame;
    private Label lblCheat;
    private DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;
    private DataGridViewTextBoxColumn dataGridViewTextBoxColumn2;
}