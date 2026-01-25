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
    /// High-level problem this app solves:
    /// - FDRS log files can be large, noisy, and time-consuming to review manually.
    /// - This tool "triages" the log by extracting only lines likely relevant to failures,
    ///   communication problems, voltage/battery events, access denial, etc.
    ///
    /// What the form does end-to-end:
    /// 1) User clicks "Open Log" and selects a .txt file.
    /// 2) The file is read line-by-line.
    /// 3) Lines matching key triage keywords are included and classified.
    /// 4) User can filter the displayed results (search + severity + unique toggle).
    /// 5) User can export the currently displayed rows to CSV for sharing.
    ///
    /// Important design choice (separation of concerns):
    /// - _rows = master data set (what we extracted from the file).
    /// - DataGridView = view only (shows filtered rows, never “owns” the data).
    /// - ApplyFilter() = single render pipeline (always re-renders the grid from _rows).
    ///
    /// Why that design matters:
    /// - Filtering does not permanently mutate the data.
    /// - Any combination of filters can be changed instantly without re-reading the file.
    /// - Export always exports what the user sees, not the raw underlying file.
    /// </summary>
    public partial class FdrsLogQuickTriageForm : Form
    {
        // =====================================================================================
        // UI LABEL FIELDS (RUNTIME-CREATED)
        // =====================================================================================
        //
        // These labels are created dynamically (not placed by the WinForms designer).
        // Because ApplyLayout() can run during resize/layout events, these must exist
        // as fields so they can be positioned reliably.
        //
        // NOTE:
        // - These are non-nullable on purpose. They are always created in the constructor.
        // - That prevents the classic WinForms lifecycle crash: layout runs before control exists.
        //
        // Purpose of these labels:
        // - Provide "title" text above the Search textbox and the Severity combobox.
        //
        private Label lblSearch;
        private Label lblSeverity;

        // =====================================================================================
        // MASTER DATA SET (MODEL)
        // =====================================================================================
        //
        // This list contains the extracted rows from the log file and is treated as the
        // single source of truth for what the tool has ingested.
        //
        // Tuple meanings:
        // - Line:     Original line number in the source log file (1-based index).
        // - Severity: Derived classification label ("Info", "Warning", "Error").
        // - Message:  The raw log line text.
        //
        // NOTE:
        // - Filtering NEVER modifies this list.
        // - ApplyFilter() reads from this list and re-populates the DataGridView.
        //
        private readonly List<(int Line, string Severity, string Message)> _rows = new();

        // =====================================================================================
        // TRIAGE INCLUSION LOGIC (REGEX FILTER)
        // =====================================================================================
        //
        // This regex decides whether a log line is "interesting enough" to include in results.
        //
        // Keywords chosen to catch:
        // - fail/error/nrc/denied: common failure/fault indicators
        // - soc: can indicate security access / diagnostic sessions / module state
        // - voltage/battery: programming failures frequently correlate with voltage instability
        // - memory: potential resource/flash/ECU storage issues
        //
        // RegexOptions:
        // - IgnoreCase: matches regardless of capitalization
        // - Compiled:   improves performance for repeated matching on large files
        //
        private static readonly Regex IncludeKeywordsRx = new Regex(
            "(fail|error|nrc|denied|soc|voltage|memory|battery)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        /// <summary>
        /// Form constructor.
        ///
        /// This is where:
        /// - Designer controls are initialized (InitializeComponent()).
        /// - Dynamic controls (lblSearch/lblSeverity) are created and added to Controls.
        /// - UI defaults are set (button text, combo items, grid config).
        /// - Event handlers are attached (and detached first to avoid duplicates).
        /// - A first layout pass is applied.
        /// </summary>
        public FdrsLogQuickTriageForm()
        {
            // This creates all designer-built UI controls (buttons, grid, textbox, etc.).
            InitializeComponent();

            // ---------------------------------------------------------------------------------
            // Defensive cleanup:
            //
            // If the designer left behind a control named "button1" that you no longer use,
            // this removes it so it doesn’t accidentally appear or interfere with layout.
            //
            // NOTE:
            // - If button1 does not exist in designer code, this block is unnecessary.
            // - But keeping it is harmless if button1 is indeed present.
            // ---------------------------------------------------------------------------------
            if (button1 != null)
            {
                Controls.Remove(button1);
                button1.Dispose();
            }

            // ---------------------------------------------------------------------------------
            // Dynamic labels:
            //
            // These labels are NOT in the designer. We create them at runtime so we can
            // position them precisely in ApplyLayout() above their respective controls.
            //
            // Why not put these in the designer?
            // - You can, but this approach allows everything to be driven by code layout.
            //
            // AutoSize = true:
            // - Label sizes itself based on text.
            // ---------------------------------------------------------------------------------
            lblSearch = new Label { AutoSize = true, Text = "Search" };
            lblSeverity = new Label { AutoSize = true, Text = "Severity" };

            // Add the labels to the form’s Controls collection (only if not already present).
            // This prevents accidental duplication if constructor logic runs more than once.
            if (!Controls.Contains(lblSearch)) Controls.Add(lblSearch);
            if (!Controls.Contains(lblSeverity)) Controls.Add(lblSeverity);

            // Ensure they are visible above other controls.
            lblSearch.BringToFront();
            lblSeverity.BringToFront();

            // ---------------------------------------------------------------------------------
            // UI text normalization:
            //
            // Ensures the user sees clear labels, not default names like “button2”.
            // ---------------------------------------------------------------------------------
            btnOpenLog.Text = "Open Log";
            label1.Text = "No file loaded";
            checkBox1.Text = "Unique only";
            button2.Text = "Export CSV";

            // ---------------------------------------------------------------------------------
            // DataGridView baseline configuration:
            //
            // - ReadOnly: prevents accidental edits to triage output
            // - AllowUserToAddRows: removes the extra empty row at the bottom
            // - AutoSizeColumnsMode.Fill: uses all horizontal space for readability
            // - SelectionMode.FullRowSelect: clicking selects the whole row (better UX)
            // - MultiSelect false: keep exports/behavior simple, one selection at a time
            // ---------------------------------------------------------------------------------
            dataGridView1.ReadOnly = true;
            dataGridView1.AllowUserToAddRows = false;
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.MultiSelect = false;

            // ---------------------------------------------------------------------------------
            // Severity filter dropdown setup:
            //
            // “All” means no severity filtering.
            // “Info/Warning/Error” correspond to GetSeverity() outputs.
            // ---------------------------------------------------------------------------------
            comboBox1.Items.Clear();
            comboBox1.Items.AddRange(new object[] { "All", "Info", "Warning", "Error" });
            comboBox1.SelectedIndex = 0;

            // Export should remain disabled until there is something to export.
            button2.Enabled = false;

            // ---------------------------------------------------------------------------------
            // Event wiring hygiene:
            //
            // The pattern "remove then add" prevents multiple subscriptions.
            // Duplicate subscriptions cause handlers to fire multiple times per click/change.
            // ---------------------------------------------------------------------------------
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

            // Initial control positioning / sizing.
            // This ensures the UI looks correct immediately after launch.
            ApplyLayout();
        }

        /// <summary>
        /// Form Load event (runs when the form is shown).
        ///
        /// If the designer wires this event, it’s safe to re-apply layout here.
        /// This helps if any control sizes weren’t finalized until after construction.
        /// </summary>
        private void Form1_Load(object sender, EventArgs e)
        {
            ApplyLayout();
        }

        /// <summary>
        /// Resize handler.
        ///
        /// Whenever the user resizes the window, we re-run ApplyLayout()
        /// so controls stay aligned and the grid continues to fill the space.
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLayout();
        }

        /// <summary>
        /// Manual layout function (pixel-based layout).
        ///
        /// Layout plan:
        /// - Row 1: [Open Log] [File name label] [Progress bar]
        /// - Row 2: Titles + [Search box] [Severity dropdown] [Unique only] [Export CSV]
        /// - Row 3: DataGridView fills the remaining space
        /// - Bottom: StatusStrip docked to bottom
        ///
        /// Why manual layout?
        /// - Complete control over placement
        /// - No dependency on WinForms designer layouts
        ///
        /// Caution:
        /// - Manual layout requires careful math and anchors.
        /// - Always keep MinimumSize reasonable to prevent overlap.
        /// </summary>
        private void ApplyLayout()
        {
            // Standard margin used throughout the UI so everything aligns neatly.
            int margin = 12;

            // Prevents the form from being resized to a size where controls overlap.
            MinimumSize = new Size(900, 600);

            // =============================================================================
            // Row 1: Open | FileLabel | Progress
            // =============================================================================

            // Open button: top-left corner
            btnOpenLog.Location = new Point(margin, margin);
            btnOpenLog.Size = new Size(120, 32);

            // Progress bar: top-right corner
            // Location uses ClientSize.Width so it remains pinned to the right edge.
            progressBar1.Size = new Size(240, 18);
            progressBar1.Location = new Point(ClientSize.Width - progressBar1.Width - margin, margin + 7);
            progressBar1.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // File label: sits between Open button and Progress bar, and expands/shrinks
            // horizontally as the form width changes.
            label1.Location = new Point(btnOpenLog.Right + 12, margin);
            label1.Size = new Size(progressBar1.Left - label1.Left - 12, 32);
            label1.TextAlign = ContentAlignment.MiddleLeft;
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // =============================================================================
            // Row 2: Search | Severity | Unique | Export
            // =============================================================================

            // Small vertical gap between title labels and the input controls beneath them.
            int titleGap = 2;

            // Expected label height (used to compute "titles above inputs" positioning).
            int titleHeight = 16;

            // Row 2 starts below Row 1, plus additional space for title labels.
            // This ensures titles never overlap row 1.
            int row2Top = btnOpenLog.Bottom + 12 + titleHeight + titleGap;

            // Export button: right-most control on row2
            button2.Size = new Size(120, 30);
            button2.Location = new Point(ClientSize.Width - button2.Width - margin, row2Top);
            button2.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // Unique checkbox: positioned to the left of Export button
            // PreferredSize is used to compute width for proper alignment.
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(button2.Left - 12 - checkBox1.PreferredSize.Width, row2Top + 6);
            checkBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // Severity combo box: to the left of checkbox
            comboBox1.Size = new Size(140, 30);
            comboBox1.Location = new Point(checkBox1.Left - 12 - comboBox1.Width, row2Top);
            comboBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // Title label above the search textbox.
            // Even though lblSearch is always created, this guard prevents issues if
            // someone later refactors and makes labels optional.
            if (lblSearch != null)
            {
                // Position label above the search box baseline.
                lblSearch.Location = new Point(margin, row2Top - titleHeight - titleGap);
                lblSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }

            // Title label above the severity combo box.
            if (lblSeverity != null)
            {
                lblSeverity.Location = new Point(comboBox1.Left, row2Top - titleHeight - titleGap);
                lblSeverity.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            }

            // Search textbox expands to fill available space between left margin and comboBox.
            // Math.Max ensures it never shrinks below a usable width (250px minimum).
            int textBoxWidth = Math.Max(250, comboBox1.Left - margin - 12);
            textBox1.Location = new Point(margin, row2Top);
            textBox1.Size = new Size(textBoxWidth, 30);
            textBox1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // =============================================================================
            // Grid: fills remainder
            // =============================================================================

            // Grid starts below row 2 with some spacing.
            int gridTop = textBox1.Bottom + 12;

            // Reserve space at bottom so grid does not overlap status strip.
            int bottomSpace = statusStrip1.Height + margin + 8;

            // Grid fills the remaining client area (minus margins and bottom reserved space).
            dataGridView1.Location = new Point(margin, gridTop);
            dataGridView1.Size = new Size(
                ClientSize.Width - (margin * 2),
                ClientSize.Height - gridTop - bottomSpace
            );

            // Anchor to all sides so it grows/shrinks with the window.
            dataGridView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // Status strip is docked so it stays pinned to the bottom automatically.
            statusStrip1.Dock = DockStyle.Bottom;
        }

        // =============================================================================
        // Event handlers
        // =============================================================================

        /// <summary>
        /// Open Log button handler.
        ///
        /// Responsibilities:
        /// - Prompt user for a file.
        /// - Load file contents.
        /// - Build/refresh grid columns.
        /// - Populate _rows with extracted triage lines.
        /// - Trigger ApplyFilter() to display current view.
        /// </summary>
        private void BtnOpenLog_Click(object? sender, EventArgs e)
        {
            // OpenFileDialog is wrapped in using so unmanaged resources are cleaned up.
            using (OpenFileDialog ofd = new OpenFileDialog
            {
                // Filter restricts choices to .txt but still allows "All files".
                Filter = "Log files (*.txt)|*.txt|All files (*.*)|*.*"
            })
            {
                // If user cancels, exit immediately.
                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                // Display just the file name (not full path) for readability.
                label1.Text = Path.GetFileName(ofd.FileName);

                // Load entire file into memory as lines.
                // For very large logs, this could be optimized to stream line-by-line,
                // but this approach keeps logic simple.
                string[] lines = File.ReadAllLines(ofd.FileName);

                // Initialize progress bar bounds for a visual indicator of processing.
                progressBar1.Minimum = 0;
                progressBar1.Maximum = Math.Max(1, lines.Length);
                progressBar1.Value = 0;

                // Rebuild columns fresh each time:
                // - Ensures consistent ordering
                // - Avoids duplicate columns across multiple loads
                dataGridView1.Columns.Clear();
                dataGridView1.Rows.Clear();
                dataGridView1.Columns.Add("Line", "Line");
                dataGridView1.Columns.Add("Severity", "Severity");
                dataGridView1.Columns.Add("Message", "Message");

                // Reset the master data set before loading new file data.
                _rows.Clear();

                // Iterate through every log line, classify and store if relevant.
                for (int i = 0; i < lines.Length; i++)
                {
                    // Update progress safely without exceeding maximum.
                    progressBar1.Value = Math.Min(progressBar1.Maximum, i + 1);

                    // Current line text
                    string msg = lines[i];

                    // If the line doesn't match our triage keywords, ignore it.
                    if (!IncludeKeywordsRx.IsMatch(msg))
                        continue;

                    // Determine severity category based on message content.
                    string sev = GetSeverity(msg);

                    // Store line number as 1-based (human readable).
                    _rows.Add((i + 1, sev, msg));
                }

                // Render the current filtered view into the DataGridView.
                ApplyFilter();
            }
        }

        /// <summary>
        /// Search textbox changed.
        /// Always re-apply filter so grid updates live as user types.
        /// </summary>
        private void TxtSearch_TextChanged(object? sender, EventArgs e) => ApplyFilter();

        /// <summary>
        /// Severity dropdown changed.
        /// Re-filter to show only the chosen severity bucket.
        /// </summary>
        private void CmbSeverity_SelectedIndexChanged(object? sender, EventArgs e) => ApplyFilter();

        /// <summary>
        /// Unique checkbox changed.
        /// Re-filter to optionally remove duplicate message lines from the view.
        /// </summary>
        private void ChkUnique_CheckedChanged(object? sender, EventArgs e) => ApplyFilter();

        /// <summary>
        /// Determines severity classification for a single log line.
        ///
        /// Rules:
        /// - If line contains strong failure indicators -> Error
        /// - Else if line contains weaker issues -> Warning
        /// - Else -> Info
        ///
        /// NOTE:
        /// - These rules are heuristic.
        /// - You can refine these patterns as you learn FDRS log semantics.
        /// </summary>
        private static string GetSeverity(string message)
        {
            // "Error" bucket: direct failure indicators
            if (Regex.IsMatch(message, "(error|fail|nrc|denied)", RegexOptions.IgnoreCase))
                return "Error";

            // "Warning" bucket: timeouts/voltage issues can cause flashing failures
            if (Regex.IsMatch(message, "(warning|timeout|voltage)", RegexOptions.IgnoreCase))
                return "Warning";

            // Everything else defaults to informational.
            return "Info";
        }

        /// <summary>
        /// Applies user-selected filters to the master list and repopulates the grid.
        ///
        /// Filters supported:
        /// 1) Text search: keep rows whose Message contains the typed string (case-insensitive)
        /// 2) Severity filter: keep rows matching selected severity (unless "All")
        /// 3) Unique only: remove duplicate message lines from view
        ///
        /// NOTE:
        /// - This method is the single source of truth for grid rendering.
        /// - It always clears and repopulates the DataGridView from _rows.
        /// </summary>
        private void ApplyFilter()
        {
            // Current search text (trim whitespace so accidental spaces don’t break matching).
            string search = textBox1.Text.Trim();

            // Current severity selection; if null, default to "All".
            string selectedSeverity = comboBox1.SelectedItem?.ToString() ?? "All";

            // Whether we are deduplicating the view.
            bool uniqueOnly = checkBox1.Checked;

            // Clear current grid view before rebuilding it.
            dataGridView1.Rows.Clear();

            // Tracks messages already added when uniqueOnly is enabled.
            // Case-insensitive comparison so duplicates with different casing are treated same.
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Walk the master data set, applying filters in a predictable order.
            foreach (var r in _rows)
            {
                // 1) Text search filter (contains, case-insensitive).
                if (!string.IsNullOrEmpty(search) &&
                    r.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // 2) Severity filter (skip if not matching selection).
                if (selectedSeverity != "All" &&
                    !r.Severity.Equals(selectedSeverity, StringComparison.OrdinalIgnoreCase))
                    continue;

                // 3) Unique filter (skip if message already added).
                if (uniqueOnly && !seen.Add(r.Message))
                    continue;

                // If it passed all filters, add it to the grid.
                dataGridView1.Rows.Add(r.Line, r.Severity, r.Message);
            }

            // Enable export only when there are rows visible to export.
            button2.Enabled = dataGridView1.Rows.Count > 0;
        }

        /// <summary>
        /// Export CSV button handler.
        ///
        /// Exports exactly what the user currently sees in the DataGridView (post-filter).
        ///
        /// CSV behavior:
        /// - Header row is included: Line,Severity,Message
        /// - Message is quoted
        /// - Quotes inside the message are escaped ("" per CSV rules)
        /// </summary>
        private void BtnExportCsv_Click(object? sender, EventArgs e)
        {
            // SaveFileDialog controls where the export will be written.
            using (SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = "export.csv"
            })
            {
                // If user cancels, exit.
                if (sfd.ShowDialog() != DialogResult.OK)
                    return;

                // Build CSV in memory using StringBuilder for speed and simplicity.
                StringBuilder sb = new StringBuilder();

                // CSV header row
                sb.AppendLine("Line,Severity,Message");

                // Iterate visible rows (not _rows) so export matches UI exactly.
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    // Skip empty/uninitialized rows defensively.
                    if (row.Cells[0].Value == null) continue;

                    // Extract cell values safely (null -> empty string).
                    string line = row.Cells[0].Value?.ToString() ?? "";
                    string sev = row.Cells[1].Value?.ToString() ?? "";
                    string msg = row.Cells[2].Value?.ToString() ?? "";

                    // Escape quotes inside message for valid CSV formatting.
                    msg = msg.Replace("\"", "\"\"");

                    // Write CSV row: message is quoted to preserve commas/newlines safely.
                    sb.AppendLine($"{line},{sev},\"{msg}\"");
                }

                // Write the completed CSV to disk.
                File.WriteAllText(sfd.FileName, sb.ToString());
            }
        }
    }
}
