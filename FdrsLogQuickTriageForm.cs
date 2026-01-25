using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FDRS_Log_Quick_Triage
{
    /// <summary>
    /// FDRS Log Quick Triage - Main Form
    ///
    /// Purpose:
    /// - Load an FDRS log file (txt)
    /// - Extract lines that match "triage" keywords (fail/error/nrc/voltage/etc.)
    /// - Classify each included line into a Severity bucket (Info/Warning/Error)
    /// - Provide UI filtering:
    ///     - text search (contains)
    ///     - severity drop-down filter
    ///     - "unique only" toggle to remove duplicates
    /// - Export the currently displayed (filtered) rows to CSV
    ///
    /// Important design choice:
    /// - _rows is the master data set (what was extracted from the file).
    /// - The DataGridView shows a filtered view only.
    /// - ApplyFilter() is the single "render" function that refreshes the grid.
    /// </summary>
    public partial class FdrsLogQuickTriageForm : Form
    {
        // ======== FIX: labels must be fields inside the Form class, and should be nullable for safety ========
        private Label lblSearch;
        private Label lblSeverity;

        /// <summary>
        /// Master list of extracted rows from the loaded log file.
        /// </summary>
        private readonly List<(int Line, string Severity, string Message)> _rows = new();

        /// <summary>
        /// Regex used to decide whether a line should be INCLUDED in triage results.
        /// </summary>
        private static readonly Regex IncludeKeywordsRx = new Regex(
            "(fail|error|nrc|denied|soc|voltage|memory|battery)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        /// <summary>
        /// Form constructor.
        /// </summary>
        public FdrsLogQuickTriageForm()
        {
            InitializeComponent();

            // --- Defensive cleanup: remove a stray designer control if it exists ---
            // NOTE: Only keep this if your Designer actually created "button1".
            // If you removed button1 from the designer, you can delete this block.
            if (button1 != null)
            {
                Controls.Remove(button1);
                button1.Dispose();
            }

            // --- FIX: Create labels ONCE after InitializeComponent, then add them ONCE ---
            lblSearch = new Label { AutoSize = true, Text = "Search" };
            lblSeverity = new Label { AutoSize = true, Text = "Severity" };

            if (!Controls.Contains(lblSearch)) Controls.Add(lblSearch);
            if (!Controls.Contains(lblSeverity)) Controls.Add(lblSeverity);

            lblSearch.BringToFront();
            lblSeverity.BringToFront();

            // --- Normalize UI text so users never see default control names ---
            btnOpenLog.Text = "Open Log";
            label1.Text = "No file loaded";
            checkBox1.Text = "Unique only";
            button2.Text = "Export CSV";

            // --- DataGridView baseline configuration for read-only triage output ---
            dataGridView1.ReadOnly = true;
            dataGridView1.AllowUserToAddRows = false;
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.MultiSelect = false;

            // --- Severity filter dropdown options ---
            comboBox1.Items.Clear();
            comboBox1.Items.AddRange(new object[] { "All", "Info", "Warning", "Error" });
            comboBox1.SelectedIndex = 0;

            // Export should not be clickable until there are results displayed.
            button2.Enabled = false;

            // --- Event wiring hygiene (avoid duplicate handlers) ---
            btnOpenLog.Click -= BtnOpenLog_Click;
            btnOpenLog.Click += BtnOpenLog_Click;

            button2.Click -= BtnExportCsv_Click;
            button2.Click += BtnExportCsv_Click;

            textBox1.TextChanged -= TxtSearch_TextChanged;
            textBox1.TextChanged += TxtSearch_TextChanged;

            comboBox1.SelectedIndexChanged -= CmbSeverity_SelectedIndexChanged;
            comboBox1.SelectedIndexChanged += CmbSeverity_SelectedIndexChanged;

            checkBox1.CheckedChanged -= ChkUnique_CheckedChanged;
            checkBox1.CheckedChanged += ChkUnique_CheckedChanged;

            // Apply initial positioning/sizing of controls.
            ApplyLayout();
        }

        /// <summary>
        /// Form Load event. Keep it if the designer wires it.
        /// </summary>
        private void Form1_Load(object sender, EventArgs e)
        {
            ApplyLayout();
        }

        /// <summary>
        /// Keep layout stable as the form resizes.
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLayout();
        }

        /// <summary>
        /// Manual layout function:
        /// Row 1: [Open Log] [File name label] [Progress bar]
        /// Row 2: Titles + [Search box] [Severity dropdown] [Unique only] [Export CSV]
        /// Row 3: DataGridView fills remaining space
        /// Bottom: StatusStrip docked at bottom
        /// </summary>
        private void ApplyLayout()
        {
            int margin = 12;

            // Enforce a minimum window size so controls aren't crushed.
            MinimumSize = new Size(900, 600);

            // =========================
            // Row 1: Open | FileLabel | Progress
            // =========================

            btnOpenLog.Location = new Point(margin, margin);
            btnOpenLog.Size = new Size(120, 32);

            progressBar1.Size = new Size(240, 18);
            progressBar1.Location = new Point(ClientSize.Width - progressBar1.Width - margin, margin + 7);
            progressBar1.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            label1.Location = new Point(btnOpenLog.Right + 12, margin);
            label1.Size = new Size(progressBar1.Left - label1.Left - 12, 32);
            label1.TextAlign = ContentAlignment.MiddleLeft;
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // =========================
            // Row 2: Search | Severity | Unique | Export
            // =========================

            int titleGap = 2;
            int titleHeight = 16;

            // Put row2 BELOW row1 AND below the title line so titles never overlap row1
            int row2Top = btnOpenLog.Bottom + 12 + titleHeight + titleGap;

            button2.Size = new Size(120, 30);
            button2.Location = new Point(ClientSize.Width - button2.Width - margin, row2Top);
            button2.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(button2.Left - 12 - checkBox1.PreferredSize.Width, row2Top + 6);
            checkBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            comboBox1.Size = new Size(140, 30);
            comboBox1.Location = new Point(checkBox1.Left - 12 - comboBox1.Width, row2Top);
            comboBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // Titles above inputs
            if (lblSearch != null)
            {
                lblSearch.Location = new Point(margin, row2Top - titleHeight - titleGap);
                lblSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }

            if (lblSeverity != null)
            {
                lblSeverity.Location = new Point(comboBox1.Left, row2Top - titleHeight - titleGap);
                lblSeverity.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            }

            // Search box width fills space up to comboBox
            int textBoxWidth = Math.Max(250, comboBox1.Left - margin - 12);
            textBox1.Location = new Point(margin, row2Top);
            textBox1.Size = new Size(textBoxWidth, 30);
            textBox1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // =========================
            // Grid: fills remainder
            // =========================

            int gridTop = textBox1.Bottom + 12;
            int bottomSpace = statusStrip1.Height + margin + 8;

            dataGridView1.Location = new Point(margin, gridTop);
            dataGridView1.Size = new Size(
                ClientSize.Width - (margin * 2),
                ClientSize.Height - gridTop - bottomSpace
            );
            dataGridView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            statusStrip1.Dock = DockStyle.Bottom;
        }

        // =========================
        // Event handlers
        // =========================

        private void BtnOpenLog_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Log files (*.txt)|*.txt|All files (*.*)|*.*"
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                label1.Text = Path.GetFileName(ofd.FileName);

                string[] lines = File.ReadAllLines(ofd.FileName);

                progressBar1.Minimum = 0;
                progressBar1.Maximum = Math.Max(1, lines.Length);
                progressBar1.Value = 0;

                // Build columns fresh each load
                dataGridView1.Columns.Clear();
                dataGridView1.Rows.Clear();
                dataGridView1.Columns.Add("Line", "Line");
                dataGridView1.Columns.Add("Severity", "Severity");
                dataGridView1.Columns.Add("Message", "Message");

                _rows.Clear();

                for (int i = 0; i < lines.Length; i++)
                {
                    progressBar1.Value = Math.Min(progressBar1.Maximum, i + 1);

                    string msg = lines[i];

                    if (!IncludeKeywordsRx.IsMatch(msg))
                        continue;

                    string sev = GetSeverity(msg);
                    _rows.Add((i + 1, sev, msg));
                }

                ApplyFilter();
            }
        }

        private void TxtSearch_TextChanged(object? sender, EventArgs e) => ApplyFilter();
        private void CmbSeverity_SelectedIndexChanged(object? sender, EventArgs e) => ApplyFilter();
        private void ChkUnique_CheckedChanged(object? sender, EventArgs e) => ApplyFilter();

        private static string GetSeverity(string message)
        {
            if (Regex.IsMatch(message, "(error|fail|nrc|denied)", RegexOptions.IgnoreCase))
                return "Error";

            if (Regex.IsMatch(message, "(warning|timeout|voltage)", RegexOptions.IgnoreCase))
                return "Warning";

            return "Info";
        }

        private void ApplyFilter()
        {
            string search = textBox1.Text.Trim();
            string selectedSeverity = comboBox1.SelectedItem?.ToString() ?? "All";
            bool uniqueOnly = checkBox1.Checked;

            dataGridView1.Rows.Clear();

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in _rows)
            {
                if (!string.IsNullOrEmpty(search) &&
                    r.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (selectedSeverity != "All" &&
                    !r.Severity.Equals(selectedSeverity, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (uniqueOnly && !seen.Add(r.Message))
                    continue;

                dataGridView1.Rows.Add(r.Line, r.Severity, r.Message);
            }

            button2.Enabled = dataGridView1.Rows.Count > 0;
        }

        private void BtnExportCsv_Click(object? sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = "export.csv"
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK)
                    return;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Line,Severity,Message");

                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    if (row.Cells[0].Value == null) continue;

                    string line = row.Cells[0].Value?.ToString() ?? "";
                    string sev = row.Cells[1].Value?.ToString() ?? "";
                    string msg = row.Cells[2].Value?.ToString() ?? "";

                    msg = msg.Replace("\"", "\"\"");
                    sb.AppendLine($"{line},{sev},\"{msg}\"");
                }

                File.WriteAllText(sfd.FileName, sb.ToString());
            }
        }
    }
}
