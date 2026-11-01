using System.Text.Json;

namespace horsey
{
    // One gene's definition, from the mod's genome.json (the game's genes.xml).
    public sealed class GeneDefDto
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public int M { get; set; }
        public int S { get; set; } = 1;
        public string Letters { get; set; } = "";
        public double[] Values { get; set; } = new double[4];
    }

    // What the mod writes to genome.json for one horse.
    public sealed class GenomeReply
    {
        public long Actor { get; set; }
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Alleles { get; set; }
        public string? Error { get; set; }
        public List<GeneDefDto> Genes { get; set; } = new();
    }

    // Edits one horse's genome. Every gene has two alleles, each one of the gene's four bases (0..3).
    // The preview column is the game's own blend: the lower allele gets m%, the higher gets the rest.
    public sealed class GenomeEditor : UserControl
    {
        public event EventHandler? Saved;

        // Raised when a button needs a horse clicked on the map. The owner then calls HorsePicked.
        public event Action? PickRequested;

        private enum PickPurpose { None, Swap, Compare }

        private const int GenomeBytes = 480;
        private const int AlleleBOffset = 240;
        private const string AllGroups = "All genes";
        private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(8);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        // Clipboard file and undo copies live in Documents\Horsey.
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Horsey");
        private static string ClipboardPath => Path.Combine(DataDir, "genome_clipboard.txt");

        private readonly string modDir;
        private long actor;
        private int id;
        private string horseName = "";
        private bool haveGenome;
        private byte[]? loadedBytes; // the genome the game holds for this horse
        private PickPurpose pickFor = PickPurpose.None;

        private readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        private readonly Label titleLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Padding = new Padding(0, 6, 0, 0) };
        private readonly TableLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2 };
        private readonly Button saveButton = new() { Text = "Save to game", Dock = DockStyle.Fill };
        private readonly Button copyButton = new() { Text = "Copy", Dock = DockStyle.Fill };
        private readonly Button pasteButton = new() { Text = "Paste", Dock = DockStyle.Fill };
        private readonly Button undoButton = new() { Text = "Undo save", Dock = DockStyle.Fill };
        private readonly Button swapButton = new() { Text = "Swap from...", Dock = DockStyle.Fill };
        private readonly Button compareButton = new() { Text = "Compare...", Dock = DockStyle.Fill };
        private Button[] actionButtons = Array.Empty<Button>();
        private readonly TableLayoutPanel groupRow = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        private readonly Label groupLabel = new() { Text = "Gene group:", AutoSize = true, Padding = new Padding(0, 6, 6, 0) };
        private readonly ComboBox groupBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly Label statusLabel = new() { AutoSize = true, Padding = new Padding(6, 6, 0, 6) };
        private readonly DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
        };
        private readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 200 };

        private readonly Dictionary<int, GeneDefDto> defByIndex = new();
        private DateTime waitStartUtc;
        private Func<bool>? waiting;

        public GenomeEditor(string modDir)
        {
            this.modDir = modDir;
            Dock = DockStyle.Fill; // fill the cell of the table it is placed in

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Gene", ReadOnly = true, Width = 130, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Allele A", Width = 95, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Allele B", Width = 95, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", ReadOnly = true, Width = 65, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.DataError += (_, e) =>
            {
                // Shown in the status line instead of a dialog. The edit is ignored.
                statusLabel.Text = "Grid: " + e.Exception?.Message;
                e.ThrowException = false;
            };
            grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            // Buttons: Save, Copy, Paste / Undo, Swap, Compare.
            actionButtons = new[] { saveButton, copyButton, pasteButton, undoButton, swapButton, compareButton };
            for (int c = 0; c < 3; c++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            actions.Controls.Add(saveButton, 0, 0);
            actions.Controls.Add(copyButton, 1, 0);
            actions.Controls.Add(pasteButton, 2, 0);
            actions.Controls.Add(undoButton, 0, 1);
            actions.Controls.Add(swapButton, 1, 1);
            actions.Controls.Add(compareButton, 2, 1);

            groupRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            groupRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            groupRow.Controls.Add(groupLabel, 0, 0);
            groupRow.Controls.Add(groupBox, 1, 0);

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(titleLabel, 0, 0);
            layout.Controls.Add(actions, 0, 1);
            layout.Controls.Add(groupRow, 0, 2);
            layout.Controls.Add(grid, 0, 3);
            layout.Controls.Add(statusLabel, 0, 4);
            Controls.Add(layout);

            saveButton.Click += SaveButton_Click;
            copyButton.Click += CopyButton_Click;
            pasteButton.Click += PasteButton_Click;
            undoButton.Click += UndoButton_Click;
            swapButton.Click += (_, _) => StartPick(PickPurpose.Swap);
            compareButton.Click += (_, _) => StartPick(PickPurpose.Compare);
            groupBox.SelectedIndexChanged += (_, _) => ApplyGroupFilter();
            pollTimer.Tick += PollTimer_Tick;
            Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) pollTimer.Dispose();
            base.Dispose(disposing);
        }

        // Shows the genome of the horse with this actor and record id. Asks the game for it.
        public void Edit(long actor, int id, string horseName)
        {
            this.actor = actor;
            this.id = id;
            this.horseName = horseName;
            haveGenome = false;
            loadedBytes = null;
            pickFor = PickPurpose.None;
            grid.Rows.Clear();
            defByIndex.Clear();
            groupBox.Items.Clear();
            titleLabel.Text = horseName;
            if (!WriteRequest(actor, id))
            {
                EnableActions();
                return;
            }
            Wait(TryLoadGenome, "Waiting for the game...");
        }

        // Empties the editor, for when no horse is selected.
        public void Clear()
        {
            waiting = null;
            pollTimer.Stop();
            haveGenome = false;
            loadedBytes = null;
            pickFor = PickPurpose.None;
            grid.Rows.Clear();
            defByIndex.Clear();
            groupBox.Items.Clear();
            titleLabel.Text = "No horse selected";
            statusLabel.Text = "Click a horse on the map.";
            EnableActions();
        }

        // Called by the owner with the horse the user clicked during a pick.
        public void HorsePicked(long pickedActor, int pickedId, string name)
        {
            PickPurpose purpose = pickFor;
            pickFor = PickPurpose.None;
            if (purpose == PickPurpose.None || !haveGenome) return;
            if (pickedActor == actor)
            {
                statusLabel.Text = "That is the same horse.";
                return;
            }
            if (!WriteRequest(pickedActor, pickedId)) return;

            Wait(() =>
            {
                GenomeReply? reply = ReadReply(pickedActor);
                if (reply is null) return false;
                if (reply.Error is not null)
                {
                    statusLabel.Text = reply.Error;
                    return true;
                }
                if (purpose == PickPurpose.Swap) SwapInGroup(reply, name);
                else OpenCompare(reply, name);
                return true;
            }, $"Reading {name}...");
        }

        // Asks the owner to let the user click a horse on the map.
        private void StartPick(PickPurpose purpose)
        {
            if (!haveGenome) return;
            pickFor = purpose;
            PickRequested?.Invoke();
        }

        private bool WriteRequest(long target, int targetId)
        {
            try
            {
                File.WriteAllText(Path.Combine(modDir, "genome.req"), $"{target}\n{targetId}");
                return true;
            }
            catch (IOException ex)
            {
                statusLabel.Text = "Could not ask the game: " + ex.Message;
                return false;
            }
        }

        private void Wait(Func<bool> check, string text)
        {
            waitStartUtc = DateTime.UtcNow;
            waiting = check;
            statusLabel.Text = text;
            pollTimer.Start();
            EnableActions();
        }

        // Buttons work only with a genome on screen and no request in flight.
        private void EnableActions()
        {
            bool on = haveGenome && waiting is null;
            foreach (Button b in actionButtons) b.Enabled = on;
            groupBox.Enabled = on;
        }

        private void PollTimer_Tick(object? sender, EventArgs e)
        {
            Func<bool>? check = waiting;
            if (check is null)
            {
                pollTimer.Stop();
                return;
            }
            if (check())
            {
                // A callback may have started a new wait; only finish the one that is still current.
                if (ReferenceEquals(waiting, check))
                {
                    waiting = null;
                    pollTimer.Stop();
                    EnableActions();
                }
                return;
            }
            if (DateTime.UtcNow - waitStartUtc > ReplyTimeout && ReferenceEquals(waiting, check))
            {
                waiting = null;
                pollTimer.Stop();
                EnableActions();
                statusLabel.Text = "No answer from the game. Is it running with the mapview mod?";
            }
        }

        // The mod's answer to the last genome.req for this actor, once it is newer than the request.
        private GenomeReply? ReadReply(long wantActor)
        {
            string path = Path.Combine(modDir, "genome.json");
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < waitStartUtc) return null;
            try
            {
                using var stream = File.OpenRead(path);
                GenomeReply? reply = JsonSerializer.Deserialize<GenomeReply>(stream, JsonOptions);
                return reply is not null && reply.Actor == wantActor ? reply : null;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                return null;
            }
        }

        private bool TryLoadGenome()
        {
            GenomeReply? reply = ReadReply(actor);
            if (reply is null) return false;
            if (reply.Error is not null)
            {
                statusLabel.Text = reply.Error;
                return true;
            }
            ShowGenome(reply);
            return true;
        }

        private void ShowGenome(GenomeReply reply)
        {
            byte[]? bytes = ParseGenome(reply.Alleles);
            if (bytes is null)
            {
                statusLabel.Text = "The game sent an unreadable genome.";
                return;
            }
            loadedBytes = bytes;

            defByIndex.Clear();
            grid.Rows.Clear();
            foreach (GeneDefDto d in reply.Genes.OrderBy(g => g.Index))
            {
                defByIndex[d.Index] = d;
                int row = grid.Rows.Add();
                DataGridViewRow r = grid.Rows[row];
                r.Tag = d.Index;
                r.Cells[0].Value = d.Name;
                int a = bytes[d.Index] & 3;
                int b = bytes[d.Index + AlleleBOffset] & 3;
                FillCombo((DataGridViewComboBoxCell)r.Cells[1], d, a);
                FillCombo((DataGridViewComboBoxCell)r.Cells[2], d, b);
                r.Cells[3].Value = Preview(d, a, b);
            }

            FillGroups();
            haveGenome = true;
            titleLabel.Text = reply.Id == -1 ? "Unrecorded horse" : $"{reply.Name} (id {reply.Id})";
            statusLabel.Text = $"{grid.Rows.Count} genes";
            EnableActions();
        }

        // Gene groups are the part of the name before the first underscore: LEG_IN and LEG_TAG are both LEG.
        private static string GroupOf(string name)
        {
            int i = name.IndexOf('_');
            return i > 0 ? name[..i] : name;
        }

        private void FillGroups()
        {
            groupBox.BeginUpdate();
            groupBox.Items.Clear();
            groupBox.Items.Add(AllGroups);
            foreach (string key in defByIndex.Values.OrderBy(d => d.Index).Select(d => GroupOf(d.Name)).Distinct())
                groupBox.Items.Add(key);
            groupBox.EndUpdate();
            groupBox.SelectedIndex = 0;
        }

        private bool InGroup(GeneDefDto d)
        {
            string group = groupBox.SelectedItem as string ?? AllGroups;
            return group == AllGroups || GroupOf(d.Name) == group;
        }

        // Hides the rows outside the chosen group. Hidden rows are still saved.
        private void ApplyGroupFilter()
        {
            foreach (DataGridViewRow row in grid.Rows)
                row.Visible = InGroup(defByIndex[(int)row.Tag!]);
        }

        // Each allele choice is a plain string item, "G  (0)" for allele 0 of a gene with base G.
        // Item k is allele k, so the item's position is the allele.
        private static void FillCombo(DataGridViewComboBoxCell cell, GeneDefDto d, int current)
        {
            cell.Items.Clear();
            for (int k = 0; k < 4; k++)
            {
                string letter = k < d.Letters.Length ? d.Letters[k].ToString() : "?";
                cell.Items.Add($"{letter}  ({d.Values[k]:0.##})");
            }
            cell.Value = cell.Items[current];
        }

        // The allele picked in a combo cell. 0 while the cell is still empty.
        private static int Choice(DataGridViewRow row, int column)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[column];
            int index = cell.Value is string s ? cell.Items.IndexOf(s) : -1;
            return Math.Max(index, 0);
        }

        // The game's expressed value: the higher allele index and the lower one, blended by m%.
        internal static double Expressed(GeneDefDto d, int a, int b)
        {
            int hi = Math.Max(a, b);
            int lo = Math.Min(a, b);
            double w = d.M / 100.0;
            return (1 - w) * d.Values[hi] + w * d.Values[lo];
        }

        private static string Preview(GeneDefDto d, int a, int b)
            => Expressed(d, a, b).ToString("0.###");

        private void SetAllele(DataGridViewRow row, int column, int allele)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[column];
            cell.Value = cell.Items[allele];
            GeneDefDto d = defByIndex[(int)row.Tag!];
            row.Cells[3].Value = Preview(d, Choice(row, 1), Choice(row, 2));
        }

        // Puts a whole genome (or the genes that pass the filter) into the grid. Nothing is saved yet.
        private void ApplyBytes(byte[] bytes, Func<GeneDefDto, bool> filter)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                GeneDefDto d = defByIndex[(int)row.Tag!];
                if (!filter(d)) continue;
                SetAllele(row, 1, bytes[d.Index] & 3);
                SetAllele(row, 2, bytes[d.Index + AlleleBOffset] & 3);
            }
        }

        // The genome as the grid shows it now, with unsaved edits.
        private byte[] CurrentBytes()
        {
            grid.EndEdit(); // commit an open dropdown before reading the alleles
            byte[] bytes = new byte[GenomeBytes * 2];
            foreach (DataGridViewRow row in grid.Rows)
            {
                int g = (int)row.Tag!;
                bytes[g] = (byte)Choice(row, 1);
                bytes[g + AlleleBOffset] = (byte)Choice(row, 2);
            }
            return bytes;
        }

        // 480 bytes as 960 hex digits. Returns null when the text is not a whole genome.
        private static byte[]? ParseGenome(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Trim().Length != GenomeBytes * 2) return null;
            try
            {
                return Convert.FromHexString(hex.Trim());
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || (e.ColumnIndex != 1 && e.ColumnIndex != 2)) return;
            DataGridViewRow row = grid.Rows[e.RowIndex];
            GeneDefDto d = defByIndex[(int)row.Tag!];
            row.Cells[3].Value = Preview(d, Choice(row, 1), Choice(row, 2));
        }

        // Copies the alleles of the genes in the chosen group from another horse's genome into the grid.
        private void SwapInGroup(GenomeReply source, string sourceName)
        {
            byte[]? bytes = ParseGenome(source.Alleles);
            if (bytes is null)
            {
                statusLabel.Text = "The game sent an unreadable genome.";
                return;
            }
            string group = groupBox.SelectedItem as string ?? AllGroups;
            ApplyBytes(bytes, InGroup);
            string what = group == AllGroups ? "all genes" : $"the {group} genes";
            statusLabel.Text = $"Took {what} from {sourceName}. Click Save to game to keep them.";
        }

        // Shows this horse next to another one, in its own window.
        private void OpenCompare(GenomeReply other, string otherName)
        {
            byte[]? bytes = ParseGenome(other.Alleles);
            if (bytes is null)
            {
                statusLabel.Text = "The game sent an unreadable genome.";
                return;
            }
            var form = new CompareForm(horseName, CurrentBytes(), otherName, bytes, defByIndex.Values);
            Form? owner = FindForm();
            if (owner is not null) form.Show(owner);
            else form.Show();
            statusLabel.Text = $"Comparing {horseName} with {otherName}.";
        }

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            if (!haveGenome) return;
            byte[] bytes = CurrentBytes();
            byte[]? before = loadedBytes;
            SendGenome(bytes, () =>
            {
                // The genome the game had until now becomes the undo point.
                string note = "";
                if (before is not null && !WriteBackup(before)) note = " (The undo copy could not be written.)";
                loadedBytes = bytes;
                return "Saved to the game." + note;
            });
        }

        private void CopyButton_Click(object? sender, EventArgs e)
        {
            if (!haveGenome) return;
            byte[] bytes = CurrentBytes();
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(ClipboardPath, $"horsey-genome 1 {horseName}\n{Convert.ToHexString(bytes).ToLowerInvariant()}\n");
                statusLabel.Text = $"Copied {(string.IsNullOrEmpty(horseName) ? "this horse" : horseName)}'s genome.";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                statusLabel.Text = "Could not write the clipboard file: " + ex.Message;
            }
        }

        // Puts the copied genome into the grid. Click Save to game to apply it to this horse.
        private void PasteButton_Click(object? sender, EventArgs e)
        {
            if (!haveGenome) return;
            if (!File.Exists(ClipboardPath))
            {
                statusLabel.Text = "Nothing copied yet.";
                return;
            }

            string? last;
            try
            {
                last = File.ReadAllLines(ClipboardPath).LastOrDefault(l => l.Trim().Length > 0);
            }
            catch (IOException ex)
            {
                statusLabel.Text = "Could not read the clipboard file: " + ex.Message;
                return;
            }
            byte[]? bytes = ParseGenome(last);
            if (bytes is null)
            {
                statusLabel.Text = "The clipboard file does not hold a genome.";
                return;
            }
            ApplyBytes(bytes, _ => true);
            statusLabel.Text = "Pasted. Click Save to game to put it on this horse.";
        }

        // Restores the most recent undo copy of this horse, and removes it from the list.
        private void UndoButton_Click(object? sender, EventArgs e)
        {
            if (!haveGenome) return;
            string dir = BackupDir();
            string? latest = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.hex").OrderBy(f => f, StringComparer.Ordinal).LastOrDefault()
                : null;
            if (latest is null)
            {
                statusLabel.Text = "No earlier genome saved for this horse.";
                return;
            }

            string file = latest;
            byte[]? bytes = null;
            try
            {
                bytes = ParseGenome(File.ReadAllLines(file).LastOrDefault(l => l.Trim().Length > 0));
            }
            catch (IOException)
            {
            }
            if (bytes is null)
            {
                statusLabel.Text = "That undo copy is unreadable.";
                return;
            }

            byte[] restore = bytes;
            SendGenome(restore, () =>
            {
                ApplyBytes(restore, _ => true);
                loadedBytes = restore;
                TryDelete(file);
                return "Reverted to the earlier genome.";
            });
        }

        // Undo copies: one folder per horse, one file per save, named by time so they sort oldest first.
        private string BackupDir()
            => Path.Combine(DataDir, "genome_backups", id >= 0 ? $"record_{id}" : $"actor_{actor:X}");

        private bool WriteBackup(byte[] bytes)
        {
            try
            {
                string dir = BackupDir();
                Directory.CreateDirectory(dir);
                string name = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".hex";
                File.WriteAllText(Path.Combine(dir, name),
                    $"{horseName}\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{Convert.ToHexString(bytes).ToLowerInvariant()}\n");
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // Sends a genome to the game. onOk runs when the game confirms, and returns the status text.
        private void SendGenome(byte[] bytes, Func<string> onOk)
        {
            string resPath = Path.Combine(modDir, "setgenome.res");
            try
            {
                TryDelete(resPath);
                // Line 1 the actor address, line 2 the record id (-1 if none), line 3 the genome.
                File.WriteAllText(Path.Combine(modDir, "setgenome.req"),
                    $"{actor}\n{id}\n{Convert.ToHexString(bytes).ToLowerInvariant()}");
            }
            catch (IOException ex)
            {
                statusLabel.Text = "Could not send the genome: " + ex.Message;
                return;
            }
            Wait(() => TryReadSaveResult(resPath, onOk), "Waiting for the game to apply the genome...");
        }

        private bool TryReadSaveResult(string path, Func<string> onOk)
        {
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < waitStartUtc) return false;

            string text = File.ReadAllText(path).Trim();
            TryDelete(path);
            if (text != "ok")
            {
                statusLabel.Text = text;
                return true;
            }
            statusLabel.Text = onOk();
            Saved?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
