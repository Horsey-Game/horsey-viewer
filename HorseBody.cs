namespace horsey
{
    // Port of the game's horse body builder. Gene indexes follow gene_index_map.csv (the game's numbering).
    public static class HorseBody
    {
        private static readonly Dictionary<string, int> indexByName = LoadIndexMap(
            Path.Combine(GameFolder.Root, "gene_index_map.csv"));

        private static Dictionary<string, int> LoadIndexMap(string path)
        {
            var map = new Dictionary<string, int>();
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                string[] f = line.Split(',');
                if (f.Length >= 3 && int.TryParse(f[0], out int index))
                    map[f[2].Trim()] = index;
            }
            return map;
        }

        // Expressed value of gene `name` for a horse's Genes dictionary, or 0 if missing.
        public static double Gene(Dictionary<string, double> genes, string name)
            => genes.TryGetValue(name, out double v) ? v : 0.0;

        // Index of a gene in the game's numbering, or -1 if the name is unknown.
        public static int GeneIndex(string name)
            => indexByName.TryGetValue(name, out int i) ? i : -1;

        // TODO: port genes_to_body_spec (Ghidra 0x14009f720), then horse_build_body (0x1400ab470).
    }
}
