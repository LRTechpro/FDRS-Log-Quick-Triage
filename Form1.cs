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
    public partial class Form1 : Form
    {
        /// <summary>
        /// Master list of extracted rows from the loaded log file.
        ///
        /// Tuple fields:
        /// - Line: 1-based line number from the source log file
        /// - Severity: computed category "Info" | "Warning" | "Error"
        /// - Message: the raw line text from the log
        ///
        /// This list is never modified by UI filtering; filtering is applied only
        /// when repopulating the DataGridView.
        /// </summary>
        private readonly List<(int Line, string Severity, string Message)> _rows = new();

        /// <summary>
        /// Regex used to decide whether a line should be INCLUDED in triage results.
        ///
        /// - If a line does not match this regex, it is ignored entirely.
        /// - Keep this list relatively small and high-signal to avoid noise.
        ///
        /// Notes:
        /// - RegexOptions.Compiled improves performance for repeated matching.
        /// - IgnoreCase makes keyword matching case-insensitive.
        /// </summary>
        private static readonly Regex IncludeKeywordsRx = new Regex(
            "(fail|error|nrc|denied|soc|voltage|memory|battery)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        /// <summary>
        /// Form constructor:
        /// - InitializeComponent() builds the UI controls created by the Designer.
        /// - Then we "hard-fix" / standardize control text, grid behavior, and event wiring
        ///   to avoid common WinForms designer issues:
        ///     - duplicate event handlers
        ///     - wrong handlers wired to wrong controls
        ///     - leftover controls (button1)
        ///     - placeholder labels like "label1", "checkBox1"
        /// </summary>
        public Form1()
        {
            InitializeComponent();

            // --- Defensive cleanup: remove a stray designer control if it exists ---
            // (This prevents an extra unused button from appearing and also avoids leaks.)
            if (button1 != null)
            {
                Controls.Remove(button1);
                button1.Dispose();
            }

            // --- Normalize UI text so users never see default control names ---
            btnOpenLog.Text = "Open Log";
            label1.Text = "No file loaded";
            checkBox1.Text = "Unique only";
            button2.Text = "Export CSV";

            // --- DataGridView baseline configuration for read-only triage output ---
            dataGridView1.ReadOnly = true; // user cannot edit log content
            dataGridView1.AllowUserToAddRows = false; // prevents extra empty row
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; // columns fill available width
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect; // row selection feels like "record selection"
            dataGridView1.MultiSelect = false; // keep selection simple for triage UI

            // --- Severity filter dropdown options ---
            comboBox1.Items.Clear();
            comboBox1.Items.AddRange(new object[] { "All", "Info", "Warning", "Error" });
            comboBox1.SelectedIndex = 0; // default to "All"

            // Export should not be clickable until there are results displayed.
            button2.Enabled = false;

            // --- Event wiring hygiene ---
            // Remove any handlers that might have been wired in the Designer (possibly twice),
            // then wire exactly once to the correct handler.
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
        /// Form Load event.
        /// The Designer often wires Form.Load; we keep it and simply call ApplyLayout().
        /// </summary>
        private void Form1_Load(object sender, EventArgs e)
        {
            ApplyLayout();
        }

        /// <summary>
        /// Keep layout stable as the form resizes.
        /// This avoids relying on Designer anchors alone (since we place controls manually).
        ///
        /// Note:
        /// - ApplyLayout() uses ClientSize, so calling it on resize repositions components.
        /// - base.OnResize(e) must be called to allow normal WinForms processing.
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLayout();
        }

        /// <summary>
        /// Manual layout function:
        /// Positions and sizes controls in a consistent arrangement.
        ///
        /// Layout (top to bottom):
        /// Row 1: [Open Log] [File name label] [Progress bar]
        /// Row 2: [Search box] ... [Severity dropdown] [Unique only] [Export CSV]
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

            // "Open Log" button: fixed size at top-left
            btnOpenLog.Location = new Point(margin, margin);
            btnOpenLog.Size = new Size(120, 32);

            // Progress bar: top-right
            progressBar1.Size = new Size(240, 18);
            progressBar1.Location = new Point(ClientSize.Width - progressBar1.Width - margin, margin + 7);
            progressBar1.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // File label: expands between Open button and progress bar
            label1.Location = new Point(btnOpenLog.Right + 12, margin);
            label1.Size = new Size(progressBar1.Left - label1.Left - 12, 32);
            label1.TextAlign = ContentAlignment.MiddleLeft;
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // =========================
            // Row 2: Search | Severity | Unique | Export
            // =========================

            int row2Top = btnOpenLog.Bottom + 12;

            // Export button: top-right under progress bar area
            button2.Size = new Size(120, 30);
            button2.Location = new Point(ClientSize.Width - button2.Width - margin, row2Top);
            button2.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // Unique checkbox: immediately left of Export
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(button2.Left - 12 - checkBox1.PreferredSize.Width, row2Top + 6);
            checkBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // Severity combo: left of Unique checkbox
            comboBox1.Size = new Size(140, 30);
            comboBox1.Location = new Point(checkBox1.Left - 12 - comboBox1.Width, row2Top);
            comboBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // Search box: fills remaining horizontal space on the left
            textBox1.Location = new Point(margin, row2Top);
            textBox1.Size = new Size(Math.Max(250, comboBox1.Left - margin - 12), 30);
            textBox1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // =========================
            // Grid: fills remainder
            // =========================

            int gridTop = textBox1.Bottom + 12;

            // Leave room for status strip + margin
            int bottomSpace = statusStrip1.Height + margin + 8;

            dataGridView1.Location = new Point(margin, gridTop);
            dataGridView1.Size = new Size(
                ClientSize.Width - (margin * 2),
                ClientSize.Height - gridTop - bottomSpace
            );
            dataGridView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // Status strip always at bottom
            statusStrip1.Dock = DockStyle.Bottom;
        }

        // =========================
        // Event handlers
        // =========================

        /// <summary>
        /// Open Log click:
        /// - Prompt user to choose a file
        /// - Read all lines
        /// - Reset progress bar and grid
        /// - Extract "interesting" lines by IncludeKeywordsRx
        /// - Compute severity per included line
        /// - Store extracted results into _rows
        /// - ApplyFilter() to display initial view
        /// </summary>
        private void BtnOpenLog_Click(object sender, EventArgs e)
        {
            // OpenFileDialog is disposed automatically by using(...)
            using (OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Log files (*.txt)|*.txt|All files (*.*)|*.*"
            })
            {
                // If user cancels, stop.
                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                // Show file name at the top so user knows what's loaded.
                label1.Text = Path.GetFileName(ofd.FileName);

                // Read entire file into memory as an array of lines.
                // (For huge logs, a streaming approach could be used later.)
                string[] lines = File.ReadAllLines(ofd.FileName);

                // Configure progress bar range
                progressBar1.Minimum = 0;
                progressBar1.Maximum = Math.Max(1, lines.Length);
                progressBar1.Value = 0;

                // Build DataGridView columns fresh for each load
                // (avoids duplicate columns across multiple file loads)
                dataGridView1.Columns.Clear();
                dataGridView1.Rows.Clear();
                dataGridView1.Columns.Add("Line", "Line");
                dataGridView1.Columns.Add("Severity", "Severity");
                dataGridView1.Columns.Add("Message", "Message");

                // Clear previous extraction results
                _rows.Clear();

                // Scan each line and keep only those that match IncludeKeywordsRx
                for (int i = 0; i < lines.Length; i++)
                {
                    // Update progress bar (safe clamp in case of edge conditions)
                    progressBar1.Value = Math.Min(progressBar1.Maximum, i + 1);

                    string msg = lines[i];

                    // Skip anything that doesn't contain triage keywords
                    if (!IncludeKeywordsRx.IsMatch(msg))
                        continue;

                    // Classify severity based on additional patterns
                    string sev = GetSeverity(msg);

                    // Store 1-based line number for user friendliness
                    _rows.Add((i + 1, sev, msg));
                }

                // Render results (with current search / severity / unique filters)
                ApplyFilter();
            }
        }

        /// <summary>
        /// Search text changed => re-apply filter to refresh grid.
        /// (All UI filters route through ApplyFilter for consistency.)
        /// </summary>
        private void TxtSearch_TextChanged(object sender, EventArgs e) => ApplyFilter();

        /// <summary>
        /// Severity selection changed => re-apply filter.
        /// </summary>
        private void CmbSeverity_SelectedIndexChanged(object sender, EventArgs e) => ApplyFilter();

        /// <summary>
        /// Unique checkbox toggled => re-apply filter.
        /// </summary>
        private void ChkUnique_CheckedChanged(object sender, EventArgs e) => ApplyFilter();

        /// <summary>
        /// Compute severity classification for a log line.
        ///
        /// Rules:
        /// - If it contains error markers, classify as Error
        /// - Else if it contains warning markers, classify as Warning
        /// - Else default Info
        ///
        /// Notes:
        /// - This is intentionally simple and explainable.
        /// - You can tune these patterns based on real FDRS log content later.
        /// </summary>
        private static string GetSeverity(string message)
        {
            // Highest severity first
            if (Regex.IsMatch(message, "(error|fail|nrc|denied)", RegexOptions.IgnoreCase))
                return "Error";

            // Warning heuristics
            if (Regex.IsMatch(message, "(warning|timeout|voltage)", RegexOptions.IgnoreCase))
                return "Warning";

            return "Info";
        }

        /// <summary>
        /// Rebuilds the DataGridView display from _rows based on current UI filters:
        /// - search text: substring match (case-insensitive)
        /// - selected severity: All or exact match
        /// - uniqueOnly: remove duplicate messages (case-insensitive)
        ///
        /// Export button state is also updated here (enabled only when rows exist).
        /// </summary>
        private void ApplyFilter()
        {
            // Capture filter values from UI
            string search = textBox1.Text.Trim();
            string selectedSeverity = comboBox1.SelectedItem?.ToString() ?? "All";
            bool uniqueOnly = checkBox1.Checked;

            // Clear current grid content
            dataGridView1.Rows.Clear();

            // Track seen messages for uniqueness filtering
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Iterate over master rows and apply filters
            foreach (var r in _rows)
            {
                // Filter #1: Search substring
                if (!string.IsNullOrEmpty(search) &&
                    r.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Filter #2: Severity dropdown
                if (selectedSeverity != "All" &&
                    !r.Severity.Equals(selectedSeverity, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filter #3: Unique-only option
                // If the message has already been added, skip it
                if (uniqueOnly && !seen.Add(r.Message))
                    continue;

                // Passed all filters => add to grid
                dataGridView1.Rows.Add(r.Line, r.Severity, r.Message);
            }

            // Export is only valid when there is something to export
            button2.Enabled = dataGridView1.Rows.Count > 0;
        }

        /// <summary>
        /// Export CSV click:
        /// - Prompt user for output .csv file path
        /// - Export only the rows currently displayed in the DataGridView
        ///   (so export respects filters + unique-only setting)
        ///
        /// CSV output format:
        /// Line,Severity,Message
        /// </summary>
        private void BtnExportCsv_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = "export.csv"
            })
            {
                // If user cancels, stop.
                if (sfd.ShowDialog() != DialogResult.OK)
                    return;

                StringBuilder sb = new StringBuilder();

                // CSV header row
                sb.AppendLine("Line,Severity,Message");

                // Iterate displayed rows
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    // Skip any row without a line number (defensive; should not happen with AllowUserToAddRows=false)
                    if (row.Cells[0].Value == null) continue;

                    // Extract cell values safely
                    string line = row.Cells[0].Value?.ToString() ?? "";
                    string sev = row.Cells[1].Value?.ToString() ?? "";
                    string msg = row.Cells[2].Value?.ToString() ?? "";

                    // CSV-safe quoting:
                    // - Escape any embedded quotes by doubling them.
                    // - Wrap Message in quotes to preserve commas, etc.
                    msg = msg.Replace("\"", "\"\"");

                    sb.AppendLine($"{line},{sev},\"{msg}\"");
                }

                // Write to disk
                File.WriteAllText(sfd.FileName, sb.ToString());
            }
        }
    }
}
