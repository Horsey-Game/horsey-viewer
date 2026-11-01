using System.Drawing.Drawing2D;
using System.Xml;

namespace horsey
{
    public partial class Form1 : Form
    {
        // The game's mods/mapview folder. The mapview mod reads refresh.req and writes horses.json here.
        private const string ModDir = @"D:\SteamLibrary\steamapps\common\Horsey Game\mods\mapview";
        private static readonly string RequestPath = Path.Combine(ModDir, "refresh.req");
        private static readonly string JsonPath = Path.Combine(ModDir, "horses.json");

        // The game's data folder. The map is read from here and never written.
        private const string DataDir = @"D:\SteamLibrary\steamapps\common\Horsey Game\data";
        private static readonly string TmxPath = Path.Combine(DataDir, "horsey.tmx");

        private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(8);

        // Zoom: 1 fits the whole map in the panel. Each zoom step multiplies by ZoomStep.
        private const float MinZoom = 1f;
        private const float MaxZoom = 500f;
        private const float ZoomStep = 1.25f;
        // Each wheel notch (120 units of delta) zooms by this much, so trackpads zoom in small steps.
        private const float WheelZoomStep = 1.15f;
        // The terrain is drawn into one bitmap. Its longest side is capped so zooming stays within memory.
        private const float MaxRenderSide = 4000f;
        // While zooming, the bitmap is only stretched. It is redrawn sharp once the zoom has paused this long.
        private const int RerenderDelayMs = 150;

        private TerrainMap? terrain;
        private Bitmap? mapBitmap;     // the terrain drawn at mapPixelsPerTile, reused between paints
        private float mapPixelsPerTile;
        private MapSnapshot? snapshot;
        private DateTime requestedAtUtc;

        private float zoom = 1f;
        private PointF pan;            // where the map's top-left corner sits, in panel pixels
        private bool dragging;
        private Point lastMouse;
        private readonly Dictionary<int, Bitmap> sprites = new();
        private readonly System.Windows.Forms.Timer rerenderTimer = new() { Interval = RerenderDelayMs };

        // The genome editor lives in its own window, shown when a horse is double-clicked.
        private readonly GenomeEditor genomeEditor = new(ModDir);
        private readonly Form genomeWindow = new() { Text = "Genome", ClientSize = new Size(400, 700) };
        private HorseDot? selected;
        private bool pickingFamily;    // true after Family tree is clicked, until a horse on the map is picked
        private Point downAt;          // where the mouse went down on the map, to tell a click from a drag

        // Family tree: every horse record in the save, read from records.json.
        private static readonly string RecordsReqPath = Path.Combine(ModDir, "records.req");
        private static readonly string RecordsJsonPath = Path.Combine(ModDir, "records.json");
        private static readonly System.Text.Json.JsonSerializerOptions RecordJson = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        };
        private Dictionary<int, RecordDto> records = new();
        private DateTime recordsRequestedUtc;
        private readonly System.Windows.Forms.Timer recordsTimer = new() { Interval = 200 };
        private readonly Button readSaveButton = new() { Text = "Read save", Location = new Point(116, 6), Size = new Size(100, 28) };
        private readonly Button familyTreeButton = new() { Text = "Family tree", Location = new Point(222, 6), Size = new Size(100, 28) };

        private static readonly string RenameReqPath = Path.Combine(ModDir, "rename.req");
        private static readonly string RenameResPath = Path.Combine(ModDir, "rename.res");
        private DateTime renameRequestedUtc;
        private readonly System.Windows.Forms.Timer renameTimer = new() { Interval = 200 };
        private readonly Panel filterPanel = new() { Dock = DockStyle.Top, Height = 36 };
        private readonly TextBox searchBox = new() { Location = new Point(8, 6), Width = 150 };
        private readonly ComboBox filterBox = new() { Location = new Point(166, 5), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown minAgeBox = new() { Location = new Point(284, 5), Width = 60, Maximum = 100000 };
        private readonly CheckBox autoRefreshBox = new() { Text = "Auto-refresh every", Location = new Point(360, 7), AutoSize = true };
        private readonly NumericUpDown intervalBox = new() { Location = new Point(490, 5), Width = 55, Minimum = 2, Maximum = 600, Value = 5 };
        private readonly System.Windows.Forms.Timer autoTimer = new() { Interval = 5000 };
        private readonly Button renameButton = new() { Text = "Rename", Location = new Point(560, 4), Size = new Size(90, 28) };

        public Form1()
        {
            InitializeComponent();
            genomeWindow.Controls.Add(genomeEditor);
            genomeWindow.FormClosing += (_, e) =>
            {
                // Closing the genome window only hides it, so the next double-click can reuse it.
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                genomeWindow.Hide();
            };
            Controls.SetChildIndex(toolPanel, 0); // the toolbar docks first, so the map fills the rest
            ClientSize = new Size(1000, 700);
            genomeEditor.Saved += (_, _) => RefreshButton_Click(null, EventArgs.Empty);
            rerenderTimer.Tick += (_, _) =>
            {
                rerenderTimer.Stop();
                float fit = FitPpt(mapPanel.ClientSize);
                if (fit > 0 && UseBitmap(fit * zoom)) RenderTerrain(fit * zoom);
                mapPanel.Invalidate();
            };
            refreshButton.Click += RefreshButton_Click;
            toolPanel.Controls.Add(readSaveButton);
            toolPanel.Controls.Add(familyTreeButton);
            statusLabel.Location = new Point(334, 12); // moved right to make room for the buttons
            readSaveButton.Click += ReadSaveButton_Click;
            familyTreeButton.Click += FamilyTreeButton_Click;
            renameButton.Click += RenameButton_Click;
            renameTimer.Tick += RenameTimer_Tick;
            filterBox.Items.AddRange(new object[] { "All", "Wild", "Tame", "Hungry", "Tired", "Old", "Unrecorded" });
            filterBox.SelectedIndex = 0;
            filterBox.SelectedIndexChanged += (_, _) => mapPanel.Invalidate();
            minAgeBox.ValueChanged += (_, _) => mapPanel.Invalidate();
            searchBox.KeyDown += SearchBox_KeyDown;
            autoRefreshBox.CheckedChanged += (_, _) => autoTimer.Enabled = autoRefreshBox.Checked;
            intervalBox.ValueChanged += (_, _) => autoTimer.Interval = (int)intervalBox.Value * 1000;
            autoTimer.Tick += AutoTimer_Tick;
            filterPanel.Controls.AddRange(new Control[] { searchBox, filterBox, minAgeBox, autoRefreshBox, intervalBox, renameButton });
            Controls.Add(filterPanel);
            Controls.SetChildIndex(filterPanel, 1);
            recordsTimer.Tick += RecordsTimer_Tick;
            pollTimer.Interval = 200;
            pollTimer.Tick += PollTimer_Tick;
            mapPanel.Paint += MapPanel_Paint;
            mapPanel.MouseEnter += (_, _) => mapPanel.Focus(); // so the wheel reaches the panel
            mapPanel.MouseWheel += MapPanel_MouseWheel;
            mapPanel.MouseDown += MapPanel_MouseDown;
            mapPanel.MouseMove += MapPanel_MouseMove;
            mapPanel.MouseUp += MapPanel_MouseUp;
            mapPanel.MouseDoubleClick += MapPanel_MouseDoubleClick;
            FormClosed += (_, _) =>
            {
                rerenderTimer.Dispose();
                recordsTimer.Dispose();
                renameTimer.Dispose();
                autoTimer.Dispose();
                mapBitmap?.Dispose();
                terrain?.Dispose();
                DisposeSprites();
            };
            LoadTerrain();
        }

        private void LoadTerrain()
        {
            try
            {
                terrain = TerrainMap.Load(TmxPath);
                statusLabel.Text = "Press Refresh to show the horses.";
            }
            catch (Exception ex) when (ex is IOException || ex is XmlException || ex is FormatException || ex is InvalidDataException)
            {
                statusLabel.Text = "Could not load the map: " + ex.Message;
            }
        }

        private void RefreshButton_Click(object? sender, EventArgs e)
        {
            if (!Directory.Exists(ModDir))
            {
                statusLabel.Text = "Mod folder not found: " + ModDir;
                return;
            }

            try
            {
                File.WriteAllText(RequestPath, string.Empty);
            }
            catch (IOException ex)
            {
                statusLabel.Text = "Could not ask the game: " + ex.Message;
                return;
            }

            requestedAtUtc = DateTime.UtcNow;
            refreshButton.Enabled = false;
            statusLabel.Text = "Waiting for the game...";
            pollTimer.Start();
        }

        // Asks the mod to read every horse record in the save. Load the save in the game first.
        private void ReadSaveButton_Click(object? sender, EventArgs e)
        {
            if (!Directory.Exists(ModDir))
            {
                statusLabel.Text = "Mod folder not found: " + ModDir;
                return;
            }

            try
            {
                File.WriteAllText(RecordsReqPath, string.Empty);
            }
            catch (IOException ex)
            {
                statusLabel.Text = "Could not ask the game: " + ex.Message;
                return;
            }

            recordsRequestedUtc = DateTime.UtcNow;
            readSaveButton.Enabled = false;
            statusLabel.Text = "Reading the save...";
            recordsTimer.Start();
        }

        private void RecordsTimer_Tick(object? sender, EventArgs e)
        {
            bool replied = File.Exists(RecordsJsonPath) && File.GetLastWriteTimeUtc(RecordsJsonPath) >= recordsRequestedUtc;
            if (!replied)
            {
                if (DateTime.UtcNow - recordsRequestedUtc > ReplyTimeout)
                {
                    recordsTimer.Stop();
                    readSaveButton.Enabled = true;
                    TryDelete(RecordsReqPath);
                    statusLabel.Text = "No answer from the game. Is it running with the mapview mod?";
                }
                return;
            }

            recordsTimer.Stop();
            readSaveButton.Enabled = true;
            try
            {
                string text = File.ReadAllText(RecordsJsonPath);
                RecordFile file = System.Text.Json.JsonSerializer.Deserialize<RecordFile>(text, RecordJson) ?? new RecordFile();
                records = file.Records.ToDictionary(r => r.Id);
                statusLabel.Text = $"Read {records.Count} horse records from the save.";
            }
            catch (Exception ex) when (ex is IOException || ex is System.Text.Json.JsonException || ex is ArgumentException)
            {
                statusLabel.Text = "Could not read records.json: " + ex.Message;
            }
        }

        // Puts the map in pick mode: the next horse clicked on the map gets its family tree. Needs a read of the save first.
        private void FamilyTreeButton_Click(object? sender, EventArgs e)
        {
            if (records.Count == 0)
            {
                statusLabel.Text = "Click Read save first.";
                return;
            }
            pickingFamily = true;
            mapPanel.Cursor = Cursors.Cross;
            statusLabel.Text = "Click a horse on the map for its family tree.";
        }

        private void ShowFamilyTree(HorseDot horse)
        {
            if (horse.Id == -1)
            {
                statusLabel.Text = $"{horse.Name} has no record, so it has no family tree.";
                return;
            }
            new FamilyTreeForm(records, horse.Id).Show(this);
        }

        private void PollTimer_Tick(object? sender, EventArgs e)
        {
            // The reply is only current if horses.json was written after this request.
            bool replied = File.Exists(JsonPath) && File.GetLastWriteTimeUtc(JsonPath) >= requestedAtUtc;
            if (!replied)
            {
                if (DateTime.UtcNow - requestedAtUtc > ReplyTimeout)
                {
                    pollTimer.Stop();
                    refreshButton.Enabled = true;
                    TryDelete(RequestPath); // so a later game start does not answer an old request
                    statusLabel.Text = "No answer from the game. Is it running with the mapview mod?";
                }
                return;
            }

            pollTimer.Stop();
            refreshButton.Enabled = true;
            try
            {
                snapshot = MapSnapshot.Load(JsonPath);
                LoadSprites(snapshot);
                RelinkSelection();
                statusLabel.Text = $"{snapshot.Horses.Count} horses on {snapshot.Location}, updated {DateTime.Now:HH:mm:ss}";
                mapPanel.Invalidate();
            }
            catch (Exception ex) when (ex is IOException || ex is System.Text.Json.JsonException)
            {
                statusLabel.Text = "Could not read horses.json: " + ex.Message;
            }
        }

        // Copies each horse picture the mod saved, keyed by list position. The copies are held in memory,
        // so the game can overwrite the PNG files on the next refresh.
        private void LoadSprites(MapSnapshot snap)
        {
            DisposeSprites();
            foreach (HorseDot h in snap.Horses)
            {
                if (!h.Sprite) continue;
                string name = h.Recorded ? $"sprite_{h.Id}.png" : $"sprite_pos_{h.Pos}.png";
                string path = Path.Combine(ModDir, name);
                try
                {
                    using var stream = File.OpenRead(path);
                    using var image = Image.FromStream(stream);
                    Bitmap copy = new Bitmap(image);
                    if (HasVisiblePixels(copy)) sprites[h.Pos] = copy;
                    else copy.Dispose();
                }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
                {
                    // A missing or unreadable picture just leaves that horse as a dot.
                }
            }
        }

        private static bool HasVisiblePixels(Bitmap bmp)
        {
            for (int x = 0; x < bmp.Width; x += 4)
            {
                for (int y = 0; y < bmp.Height; y += 4)
                {
                    if (bmp.GetPixel(x, y).A > 0) return true;
                }
            }
            return false;
        }

        private void DisposeSprites()
        {
            foreach (Bitmap b in sprites.Values) b.Dispose();
            sprites.Clear();
        }

        // Fit-to-panel scale in panel pixels per tile, before zoom. Zero if there is nothing to draw.
        private float FitPpt(Size canvas)
        {
            if (terrain is null || canvas.Width <= 0 || canvas.Height <= 0) return 0;
            return Math.Min((float)canvas.Width / terrain.Width, (float)canvas.Height / terrain.Height);
        }

        // True while the whole map fits in one bitmap no bigger than MaxRenderSide. Past that, only the
        // visible tiles are drawn each time.
        private bool UseBitmap(float ppt)
            => terrain is not null && Math.Max(terrain.Width, terrain.Height) * ppt <= MaxRenderSide;

        // Keeps the map on screen. A map smaller than the panel is centred; a larger one stays
        // covering the panel.
        private void ClampPan(Size canvas, float mapWidth, float mapHeight)
        {
            pan.X = mapWidth <= canvas.Width
                ? (canvas.Width - mapWidth) / 2f
                : Math.Clamp(pan.X, canvas.Width - mapWidth, 0f);
            pan.Y = mapHeight <= canvas.Height
                ? (canvas.Height - mapHeight) / 2f
                : Math.Clamp(pan.Y, canvas.Height - mapHeight, 0f);
        }

        // Changes the zoom by `factor`, keeping the map point under `anchor` where it was.
        private void ZoomAt(PointF anchor, float factor)
        {
            if (terrain is null) return;
            float fit = FitPpt(mapPanel.ClientSize);
            if (fit <= 0) return;

            float newZoom = Math.Clamp(zoom * factor, MinZoom, MaxZoom);
            if (newZoom == zoom) return;

            float oldPpt = fit * zoom;
            float tileX = (anchor.X - pan.X) / oldPpt;
            float tileY = (anchor.Y - pan.Y) / oldPpt;

            zoom = newZoom;
            float newPpt = fit * zoom;
            pan = new PointF(anchor.X - tileX * newPpt, anchor.Y - tileY * newPpt);
            rerenderTimer.Stop();
            rerenderTimer.Start();
            mapPanel.Invalidate();
        }

        // Renders the terrain at `ppt` pixels per tile. This is the slow step, so it is kept off the zoom path.
        private void RenderTerrain(float ppt)
        {
            if (terrain is null) return;
            mapBitmap?.Dispose();
            mapBitmap = terrain.Render(ppt);
            mapPixelsPerTile = ppt;
        }

        private void MapPanel_MouseWheel(object? sender, MouseEventArgs e)
        {
            ZoomAt(e.Location, (float)Math.Pow(WheelZoomStep, e.Delta / 120.0));
        }

        private void MapPanel_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            downAt = e.Location;
            dragging = true;
            lastMouse = e.Location;
            mapPanel.Capture = true;
            mapPanel.Cursor = Cursors.Hand;
        }

        private void MapPanel_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!dragging) return;
            pan = new PointF(pan.X + (e.X - lastMouse.X), pan.Y + (e.Y - lastMouse.Y));
            lastMouse = e.Location;
            mapPanel.Invalidate();
        }

        private void MapPanel_MouseUp(object? sender, MouseEventArgs e)
        {
            bool click = dragging && e.Button == MouseButtons.Left
                && Math.Abs(e.X - downAt.X) + Math.Abs(e.Y - downAt.Y) < 4;
            dragging = false;
            mapPanel.Capture = false;
            mapPanel.Cursor = pickingFamily ? Cursors.Cross : Cursors.Default;
            if (!click || !pickingFamily) return;

            HorseDot? horse = HorseAt(e.Location);
            if (horse is null)
            {
                statusLabel.Text = "No horse there. Click a horse on the map for its family tree.";
                return;
            }
            pickingFamily = false;
            mapPanel.Cursor = Cursors.Default;
            ShowFamilyTree(horse);
        }

        // Double-clicking a horse opens its genome editor in a window.
        private void MapPanel_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            SelectHorse(HorseAt(e.Location));
        }

        // The horse drawn nearest the point, within 16 pixels. Null if none is that close.
        private HorseDot? HorseAt(Point p)
        {
            if (snapshot is null) return null;
            float fit = FitPpt(mapPanel.ClientSize);
            if (fit <= 0) return null;

            float unit = fit * zoom / 32f; // same scale MapSnapshot.Draw uses
            HorseDot? best = null;
            float bestDistance = 16f;
            foreach (HorseDot h in snapshot.Horses)
            {
                if (!Matches(h)) continue;
                float x = pan.X + (float)h.X * unit;
                float y = pan.Y + (float)h.Y * unit;
                float distance = MathF.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
                if (distance <= bestDistance)
                {
                    best = h;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // Loads the horse's genome into the editor window and shows the window. Null does nothing.
        private void SelectHorse(HorseDot? horse)
        {
            if (horse is null) return;
            selected = horse;
            genomeEditor.Edit(horse.Actor, horse.Id, horse.Name);
            if (!genomeWindow.Visible) genomeWindow.Show(this);
            genomeWindow.BringToFront();
        }

        // After a refresh, the selected horse's data is taken from the new snapshot. Its genome is kept,
        // so unsaved changes survive. If the horse is gone from the map, the genome panel is cleared.
        private void RelinkSelection()
        {
            if (selected is null || snapshot is null) return;
            HorseDot? now = selected.Actor == 0
                ? null
                : snapshot.Horses.FirstOrDefault(h => h.Actor == selected.Actor);
            selected = now;
            if (now is null) genomeEditor.Clear();
        }

        private void MapPanel_Paint(object? sender, PaintEventArgs e)
        {
            if (terrain is null)
            {
                e.Graphics.DrawString("No map loaded.", Font, Brushes.Gainsboro, 10, 10);
                return;
            }

            Size canvas = mapPanel.ClientSize;
            float fit = FitPpt(canvas);
            if (fit <= 0) return;

            float ppt = fit * zoom;
            ClampPan(canvas, terrain.Width * ppt, terrain.Height * ppt);

            if (UseBitmap(ppt))
            {
                // Rendering the terrain is slow, so the first draw renders it and zooming afterwards only
                // stretches the existing bitmap. The timer re-renders it sharp once the zoom stops.
                if (mapBitmap is null) RenderTerrain(ppt);
                else if (Math.Abs(ppt - mapPixelsPerTile) > 0.0001f) rerenderTimer.Start(); // stale, e.g. after a resize

                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.DrawImage(mapBitmap, pan.X, pan.Y, terrain.Width * ppt, terrain.Height * ppt);
            }
            else
            {
                // Zoomed in too far for one bitmap, so only the tiles on screen are drawn.
                terrain.Draw(e.Graphics, ppt, pan, canvas);
            }
            snapshot?.Draw(e.Graphics, ppt, pan, canvas, Font, sprites, Matches);
        }

        private bool Matches(HorseDot h)
        {
            if (h.Age < minAgeBox.Value) return false;
            return filterBox.SelectedIndex switch
            {
                1 => h.Wild,
                2 => !h.Wild,
                3 => h.Hungry,
                4 => h.Tired,
                5 => h.Old,
                6 => !h.Recorded,
                _ => true,
            };
        }

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || snapshot is null) return;
            e.SuppressKeyPress = true;
            string query = searchBox.Text.Trim();
            if (query.Length == 0) return;

            HorseDot? hit = snapshot.Horses.FirstOrDefault(h => h.Id.ToString() == query)
                ?? snapshot.Horses.FirstOrDefault(h => h.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
            {
                statusLabel.Text = $"No horse matches \"{query}\".";
                return;
            }
            CentreOn(hit);
            SelectHorse(hit);
        }

        private void CentreOn(HorseDot h)
        {
            float fit = FitPpt(mapPanel.ClientSize);
            if (fit <= 0) return;
            float unit = fit * zoom / 32f;
            Size c = mapPanel.ClientSize;
            pan = new PointF(c.Width / 2f - (float)h.X * unit, c.Height / 2f - (float)h.Y * unit);
            mapPanel.Invalidate();
        }

        private void RenameButton_Click(object? sender, EventArgs e)
        {
            if (selected is null || selected.Id == -1)
            {
                statusLabel.Text = "Double-click a recorded horse on the map, then Rename.";
                return;
            }

            string entered = Microsoft.VisualBasic.Interaction.InputBox(
                $"New name for {selected.Name} (1 to 15 characters):", "Rename horse", selected.Name);
            string name = entered.Trim();
            if (name.Length == 0 || name == selected.Name) return;
            if (name.Length > 15)
            {
                statusLabel.Text = "Names are 1 to 15 characters.";
                return;
            }

            try
            {
                TryDelete(RenameResPath);
                File.WriteAllText(RenameReqPath, $"{selected.Id}\n{name}");
            }
            catch (IOException ex)
            {
                statusLabel.Text = "Could not ask the game: " + ex.Message;
                return;
            }

            if (records.TryGetValue(selected.Id, out RecordDto? record)) record.Name = name;
            renameRequestedUtc = DateTime.UtcNow;
            renameButton.Enabled = false;
            statusLabel.Text = "Renaming...";
            renameTimer.Start();
        }

        private void RenameTimer_Tick(object? sender, EventArgs e)
        {
            bool replied = File.Exists(RenameResPath) && File.GetLastWriteTimeUtc(RenameResPath) >= renameRequestedUtc;
            if (!replied)
            {
                if (DateTime.UtcNow - renameRequestedUtc > ReplyTimeout)
                {
                    renameTimer.Stop();
                    renameButton.Enabled = true;
                    TryDelete(RenameReqPath);
                    statusLabel.Text = "No answer from the game. Is it running with the mapview mod?";
                }
                return;
            }

            renameTimer.Stop();
            renameButton.Enabled = true;
            string result = File.ReadAllText(RenameResPath).Trim();
            TryDelete(RenameResPath);
            if (result == "ok")
            {
                statusLabel.Text = "Renamed.";
                RefreshButton_Click(null, EventArgs.Empty);
            }
            else
            {
                statusLabel.Text = result;
            }
        }

        private void AutoTimer_Tick(object? sender, EventArgs e)
        {
            if (refreshButton.Enabled) RefreshButton_Click(null, EventArgs.Empty);
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

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}
