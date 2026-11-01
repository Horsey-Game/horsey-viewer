namespace horsey
{
    // Two genomes side by side. A gene is highlighted when the two horses' alleles differ.
    public sealed class CompareForm : Form
    {
        private const int AlleleBOffset = 240;

        private readonly byte[] a;
        private readonly byte[] b;
        private readonly List<GeneDefDto> defs;
        private readonly string nameA;
        private readonly string nameB;
        private readonly CheckBox foalPreview = new() { Text = "Foal preview (A x B)", Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 6, 0, 0) };
        private readonly DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        private readonly CheckBox onlyDifferent = new() { Text = "Only genes that differ", Checked = true, Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 6, 0, 0) };

        public CompareForm(string nameA, byte[] a, string nameB, byte[] b, IEnumerable<GeneDefDto> defs)
        {
            this.a = a;
            this.b = b;
            this.defs = defs.OrderBy(d => d.Index).ToList();
            this.nameA = nameA;
            this.nameB = nameB;
            Text = $"Compare: {nameA} vs {nameB}";
            ClientSize = new Size(640, 620);

            grid.Columns.Add("Gene", "Gene");
            grid.Columns.Add("A", nameA);
            grid.Columns.Add("B", nameB);
            grid.Columns.Add("C", "Foal max");
            grid.Columns[0].FillWeight = 40;
            grid.Columns[1].FillWeight = 20;
            grid.Columns[2].FillWeight = 20;
            grid.Columns[3].FillWeight = 20;
            grid.Columns[3].Visible = false;

            onlyDifferent.CheckedChanged += (_, _) => Fill();
            foalPreview.CheckedChanged += (_, _) => Fill();
            Controls.Add(grid);
            Controls.Add(onlyDifferent); // added after the grid, so it docks at the top
            Controls.Add(foalPreview);
            Fill();
        }

        private void Fill()
        {
            grid.Rows.Clear();
            bool foal = foalPreview.Checked;
            onlyDifferent.Enabled = !foal;
            grid.Columns[1].HeaderText = foal ? "Foal min" : nameA;
            grid.Columns[2].HeaderText = foal ? "Foal avg" : nameB;
            grid.Columns[3].Visible = foal;
            if (foal)
            {
                foreach (GeneOdds o in BreedingPreview.Foal(a, b, defs))
                {
                    grid.Rows.Add(o.Def.Name, o.Min.ToString("0.###"), o.Mean.ToString("0.###"), o.Max.ToString("0.###"));
                }
                return;
            }
            foreach (GeneDefDto d in defs)
            {
                int a1 = a[d.Index] & 3;
                int a2 = a[d.Index + AlleleBOffset] & 3;
                int b1 = b[d.Index] & 3;
                int b2 = b[d.Index + AlleleBOffset] & 3;
                bool differs = a1 != b1 || a2 != b2;
                if (onlyDifferent.Checked && !differs) continue;

                int row = grid.Rows.Add(d.Name, Describe(d, a1, a2), Describe(d, b1, b2));
                if (differs) grid.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(255, 236, 160);
            }
        }

        // "GA  (12.5)": the two alleles' letters and the expressed value.
        private static string Describe(GeneDefDto d, int x, int y)
        {
            string letters = Letter(d, x) + Letter(d, y);
            return $"{letters}  ({GenomeEditor.Expressed(d, x, y):0.###})";
        }

        private static string Letter(GeneDefDto d, int allele) => allele < d.Letters.Length ? d.Letters[allele].ToString() : "?";
    }
}
