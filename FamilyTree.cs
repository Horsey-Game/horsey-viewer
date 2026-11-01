namespace horsey
{
    // One horse record from records.json. Parents are -1 when there are none.
    public sealed class RecordDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Special { get; set; }
        public int ColorA { get; set; }
        public int ColorB { get; set; }
        public int ParentA { get; set; } = -1;
        public int ParentB { get; set; } = -1;
        public List<int> Children { get; set; } = new();
    }

    // What the mod writes to records.json: every horse record in the save.
    public sealed class RecordFile
    {
        public int Count { get; set; }
        public List<RecordDto> Records { get; set; } = new();
    }

    // Draws one horse's ancestors as a pedigree chart: the horse at the bottom, its parents above it joined by a
    // couple line, then grandparents and great-grandparents in the rows above. Click an ancestor to make it the centre.
    public sealed class FamilyTreeForm : Form
    {
        private const int Gens = 3;       // generations above the centre horse
        private const int BoxW = 130;
        private const int BoxH = 46;
        private const int RowGap = 56;    // space between rows, where the couple lines are drawn
        private const int SlotW = 146;    // width of one slot in the top row
        private const int Pad = 16;

        private readonly Dictionary<int, RecordDto> records;
        private readonly Canvas canvas = new();
        // (generation, slot) -> the horse drawn there and its box. Generation 0 is the centre horse.
        // Slots are numbered left to right, and the parents of slot s are slots 2s and 2s+1 one generation up.
        private readonly Dictionary<(int Gen, int Slot), (int Id, Rectangle Box)> nodes = new();
        private int rootId;
        private readonly List<(int Id, Rectangle Box)> kids = new();
        private readonly Label info = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 6, 0, 0) };

        public FamilyTreeForm(Dictionary<int, RecordDto> records, int rootId)
        {
            this.records = records;
            this.rootId = rootId;
            ClientSize = new Size(1000, 600);
            canvas.Paint += Canvas_Paint;
            canvas.MouseClick += Canvas_MouseClick;
            Controls.Add(canvas);
            Controls.Add(info);
            Rebuild();
        }

        // Works out where every ancestor goes, then sizes the canvas to fit.
        private void Rebuild()
        {
            Text = "Family tree: " + NameOf(rootId);
            nodes.Clear();
            kids.Clear();
            Place(rootId, 0, 0);

            int rootTop = Pad + Gens * (BoxH + RowGap);
            int rootCentre = Pad + (1 << Gens) * SlotW / 2;
            if (records.TryGetValue(rootId, out RecordDto? root) && root.Children.Count > 0)
            {
                int childTop = rootTop + BoxH + RowGap;
                int n = root.Children.Count;
                int startX = rootCentre - (n - 1) * SlotW / 2;
                for (int i = 0; i < n; i++)
                {
                    int cx = startX + i * SlotW;
                    kids.Add((root.Children[i], new Rectangle(cx - BoxW / 2, childTop, BoxW, BoxH)));
                }
            }

            int width = Math.Max((1 << Gens) * SlotW + 2 * Pad, 2 * Pad + kids.Count * SlotW);
            int height = Pad + Gens * (BoxH + RowGap) + BoxH + (kids.Count > 0 ? RowGap + BoxH : 0) + Pad;
            canvas.AutoScrollMinSize = new Size(width, height);
            info.Text = Summary();
            canvas.Invalidate();
        }

        private string Summary()
        {
            var lines = new List<string> { "Click a horse to make it the centre." };
            if (records.TryGetValue(rootId, out RecordDto? r) && r.ParentA >= 0 && r.ParentB >= 0)
            {
                List<int> shared = SharedAncestors(r.ParentA, r.ParentB);
                string names = shared.Count == 0 ? "none" : string.Join(", ", shared.Select(NameOf));
                lines.Add($"Parents share {shared.Count} ancestor(s): {names}");
            }
            return string.Join("\n", lines);
        }

        private List<int> SharedAncestors(int x, int y)
        {
            HashSet<int> left = AncestorsOf(x);
            return AncestorsOf(y).Where(left.Contains).OrderBy(i => i).ToList();
        }

        private HashSet<int> AncestorsOf(int id)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(id);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                if (cur < 0 || !records.TryGetValue(cur, out RecordDto? r)) continue;
                if (!seen.Add(cur)) continue;
                stack.Push(r.ParentA);
                stack.Push(r.ParentB);
            }
            seen.Remove(id);
            return seen;
        }

        // Places horse `id` in the given slot and, up to the top row, its parents in the slots above.
        private void Place(int id, int gen, int slot)
        {
            int columns = 1 << gen;
            double columnW = (double)((1 << Gens) * SlotW) / columns;
            int centreX = Pad + (int)((slot + 0.5) * columnW);
            int y = Pad + (Gens - gen) * (BoxH + RowGap);
            nodes[(gen, slot)] = (id, new Rectangle(centreX - BoxW / 2, y, BoxW, BoxH));

            if (gen == Gens || !records.TryGetValue(id, out RecordDto? r)) return;
            if (r.ParentA >= 0) Place(r.ParentA, gen + 1, slot * 2);
            if (r.ParentB >= 0) Place(r.ParentB, gen + 1, slot * 2 + 1);
        }

        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TranslateTransform(canvas.AutoScrollPosition.X, canvas.AutoScrollPosition.Y);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Each child gets a couple line joining its two parents, with a drop from the middle of that line to the child.
            using var line = new Pen(Color.FromArgb(150, 150, 150), 2);
            foreach (var kv in nodes)
            {
                var (gen, slot) = kv.Key;
                if (gen == Gens) continue;
                Rectangle child = kv.Value.Box;
                int childTop = child.Top;
                int childCentreX = child.Left + child.Width / 2;
                int barY = childTop - RowGap / 2;

                bool hasA = nodes.TryGetValue((gen + 1, slot * 2), out var a);
                bool hasB = nodes.TryGetValue((gen + 1, slot * 2 + 1), out var b);
                if (hasA && hasB)
                {
                    int ax = a.Box.Left + a.Box.Width / 2;
                    int bx = b.Box.Left + b.Box.Width / 2;
                    int bottom = a.Box.Bottom;
                    g.DrawLine(line, ax, bottom, ax, barY);
                    g.DrawLine(line, bx, bottom, bx, barY);
                    g.DrawLine(line, ax, barY, bx, barY);
                    int dropX = (ax + bx) / 2;
                    g.DrawLine(line, dropX, barY, dropX, childTop);
                }
                else if (hasA)
                {
                    g.DrawLine(line, a.Box.Left + a.Box.Width / 2, a.Box.Bottom, childCentreX, childTop);
                }
                else if (hasB)
                {
                    g.DrawLine(line, b.Box.Left + b.Box.Width / 2, b.Box.Bottom, childCentreX, childTop);
                }
            }

            using var normal = new SolidBrush(Color.FromArgb(58, 58, 58));
            using var centre = new SolidBrush(Color.FromArgb(60, 100, 150));
            using var border = new Pen(Color.FromArgb(150, 150, 150));
            using var ink = new SolidBrush(Color.Gainsboro);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
            };
            foreach (var node in nodes.Values)
            {
                g.FillRectangle(node.Id == rootId ? centre : normal, node.Box);
                g.DrawRectangle(border, node.Box);
                g.DrawString($"{NameOf(node.Id)}\nid {node.Id}", Font, ink, node.Box, format);
            }

            if (nodes.TryGetValue((0, 0), out var centreNode))
            {
                int cx = centreNode.Box.Left + centreNode.Box.Width / 2;
                foreach (var k in kids)
                {
                    g.DrawLine(line, cx, centreNode.Box.Bottom, k.Box.Left + k.Box.Width / 2, k.Box.Top);
                }
            }
            foreach (var k in kids)
            {
                g.FillRectangle(normal, k.Box);
                g.DrawRectangle(border, k.Box);
                g.DrawString($"{NameOf(k.Id)}\nid {k.Id}", Font, ink, k.Box, format);
            }
        }

        private void Canvas_MouseClick(object? sender, MouseEventArgs e)
        {
            // The click is on the panel, the boxes are in the scrolled canvas.
            var p = new Point(e.X - canvas.AutoScrollPosition.X, e.Y - canvas.AutoScrollPosition.Y);
            int? picked = null;
            foreach (var node in nodes.Values.Concat(kids))
            {
                if (node.Box.Contains(p) && node.Id != rootId && records.ContainsKey(node.Id))
                {
                    picked = node.Id;
                    break;
                }
            }
            if (picked is int id)
            {
                rootId = id;
                Rebuild();
            }
        }

        private string NameOf(int id)
        {
            if (!records.TryGetValue(id, out RecordDto? r)) return "unknown";
            return string.IsNullOrWhiteSpace(r.Name) ? "unnamed" : r.Name;
        }

        // A panel that redraws without flicker and scrolls when the chart is bigger than the window.
        private sealed class Canvas : Panel
        {
            public Canvas()
            {
                Dock = DockStyle.Fill;
                AutoScroll = true;
                BackColor = Color.FromArgb(30, 30, 30);
                DoubleBuffered = true;
            }
        }
    }
}
