using System;
using System.Drawing;
using System.Windows.Forms;

namespace XplorerCheatEditorWinForms.Services
{
    internal sealed class BusyProgressDialog : Form
    {
        private readonly Label _lbl;
        private readonly ProgressBar _bar;
        private readonly TextBox _txt;
        private readonly Button _btnCancel;

        public event Action? CancelRequested;

        private bool _sawProgressLine;

        private bool ShouldShowLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var t = line.Trim();
            if (_sawProgressLine) return true;

            // Filter NOtPsxSerial banner noise until we see progress/output lines.
            if (t.StartsWith("===") || t.StartsWith("---")) return false;
            if (t.StartsWith("Totally NOtPsxSerial", StringComparison.OrdinalIgnoreCase)) return false;
            if (t.StartsWith("Thanks:", StringComparison.OrdinalIgnoreCase)) return false;
            if (t.StartsWith("Instructions", StringComparison.OrdinalIgnoreCase)) return false;
            if (t.StartsWith("Discord", StringComparison.OrdinalIgnoreCase)) return false;
            if (t.StartsWith("Note:", StringComparison.OrdinalIgnoreCase)) return false;
            if (t.StartsWith("- ") || t.StartsWith("•")) return false;

            if (t.StartsWith("Offset ", StringComparison.OrdinalIgnoreCase) || t.Contains("(%") || t.Contains(")%"))
            {
                _sawProgressLine = true;
                return true;
            }

            // Drop early informational lines.
            return false;
        }

        public BusyProgressDialog(string title)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ControlBox = false; // no X - user wanted it to auto-close
            Width = 520;
            Height = 320;

            _lbl = new Label
            {
                AutoSize = false,
                Text = "Working…",
                Left = 12,
                Top = 12,
                Width = ClientSize.Width - 24,
                Height = 40,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _bar = new ProgressBar
            {
                Left = 12,
                Top = 56,
                Width = ClientSize.Width - 24,
                Height = 18,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Style = ProgressBarStyle.Marquee
            };

            _txt = new TextBox
            {
                Left = 12,
                Top = 84,
                Width = ClientSize.Width - 24,
                Height = ClientSize.Height - 96,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };


_btnCancel = new Button
{
    Text = "Cancel",
    Width = 90,
    Height = 28,
    Left = ClientSize.Width - 12 - 90,
    Top = ClientSize.Height - 12 - 28,
    Anchor = AnchorStyles.Bottom | AnchorStyles.Right
};
_btnCancel.Click += (_, __) => CancelRequested?.Invoke();

// adjust textbox height to leave room for the button row
_txt.Height = ClientSize.Height - _txt.Top - 12 - _btnCancel.Height - 8;
            Controls.Add(_lbl);
            Controls.Add(_bar);
            Controls.Add(_txt);
            Controls.Add(_btnCancel);
        }

        public void ReportLine(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(ReportLine), line);
                return;
            }

            // Filter noisy banner lines; keep UI readable
            if (!ShouldShowLine(line))
                return;

            {
                _txt.AppendText(line);
                if (!line.EndsWith(Environment.NewLine))
                    _txt.AppendText(Environment.NewLine);
                if (_txt.TextLength > 20000)
                    _txt.Text = _txt.Text.Substring(_txt.TextLength - 20000);
                _txt.SelectionStart = _txt.TextLength;
                _txt.ScrollToCaret();

                _lbl.Text = line.Trim();
            }
        }

        public void SetProgress(int percent)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int>(SetProgress), percent);
                return;
            }

            if (percent < 0)
            {
                _bar.Style = ProgressBarStyle.Marquee;
                return;
            }

            if (_bar.Style != ProgressBarStyle.Continuous)
                _bar.Style = ProgressBarStyle.Continuous;

            if (percent > 100) percent = 100;
            if (percent < 0) percent = 0;
            _bar.Value = percent;
        }
    }
}
