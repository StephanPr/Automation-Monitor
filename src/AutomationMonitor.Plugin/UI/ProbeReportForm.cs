using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutomationMonitor.Plugin.UI
{
    /// <summary>
    /// The probe result as text the operator can read and copy inside XrmToolBox (AM-36).
    ///
    /// <para>
    /// This window exists because the host log was never a hand-back path: XrmToolBox writes plugin entries to
    /// <c>Logs\AutomationMonitor.Plugin.log</c> only at Warning or above and its shipped default is Warning, so
    /// an Information-level diagnostic never reaches disk at all. The decisive numbers of a diagnostic run have
    /// to be readable where the run was started, whatever the host's log setting says.
    /// </para>
    ///
    /// <para>
    /// The text arrives already built and sanitized by Core. This form displays it and nothing else: it parses
    /// nothing, evaluates nothing, and never composes a path, a url or a command from it - the report is data,
    /// exactly like every other provider string this plugin handles.
    /// </para>
    /// </summary>
    public sealed class ProbeReportForm : Form
    {
        private readonly TextBox _report;
        private readonly Label _hint;
        private readonly Font _mono;

        public ProbeReportForm(string report)
        {
            _mono = new Font(FontFamily.GenericMonospace, 9.25f);

            _report = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                HideSelection = false,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = _mono,
                TabIndex = 0,
                Text = report ?? string.Empty
            };

            _hint = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 8, 0),
                Text = "Press Copy all, then paste the whole block back to the maintainer."
            };

            Button close = new Button { Text = "Close", Width = 96, Height = 26, Dock = DockStyle.Right, TabIndex = 2 };
            Button copy = new Button { Text = "Copy all", Width = 104, Height = 26, Dock = DockStyle.Right, TabIndex = 1 };
            close.Click += (sender, args) => Close();
            copy.Click += (sender, args) => CopyAll();

            Panel actions = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };

            // Added in this order on purpose: docked controls are laid out back to front, so Close ends up
            // rightmost and Copy all sits to its left at every font size and every DPI.
            actions.Controls.Add(close);
            actions.Controls.Add(copy);

            TableLayoutPanel footer = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(8, 4, 8, 6)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224f));
            footer.Controls.Add(_hint, 0, 0);
            footer.Controls.Add(actions, 1, 0);

            TableLayoutPanel body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                Padding = new Padding(8, 8, 8, 0)
            };
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.Controls.Add(_report, 0, 0);

            // Same rule as the buttons: the Fill body is added before the Bottom footer.
            Controls.Add(body);
            Controls.Add(footer);

            Text = "Run-history identity probe - result";
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(920, 560);
            MinimumSize = new Size(560, 320);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;
            AcceptButton = copy;
            CancelButton = close;

            // Selected on open so Ctrl+C works even before anyone reaches for the button.
            Shown += (sender, args) =>
            {
                _report.SelectAll();
                _report.Focus();
            };
        }

        /// <summary>
        /// Copies the block, or says plainly that it could not and leaves the text selected so Ctrl+C still
        /// works. The copy must never fail into silence: the hint line is the answer either way.
        /// </summary>
        private void CopyAll()
        {
            if (_report.TextLength == 0)
            {
                _hint.Text = "There is nothing to copy - the probe produced no report.";
                return;
            }

            try
            {
                Clipboard.SetText(_report.Text);
                _hint.Text = "Copied. Paste the whole block back to the maintainer.";
            }
            catch (Exception)
            {
                // The clipboard throws when another process is holding it. Nothing is lost: the text stays
                // selected and the operator is told what to do instead.
                _report.SelectAll();
                _report.Focus();
                _hint.Text = "The clipboard is busy. The text is selected - press Ctrl+C.";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _mono.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
