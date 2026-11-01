namespace horsey
{
    public sealed record GeneOdds(GeneDefDto Def, double Min, double Max, double Mean);

    public static class BreedingPreview
    {
        private const int AlleleBOffset = 240;
        private const int HeadChimera = 0x6b;
        private static readonly HashSet<int> ColourGenes = new() { 0xb7, 0xb8, 0xbd, 0xbe, 0xc2, 0xc5 };

        public static List<GeneOdds> Foal(byte[] a, byte[] b, IEnumerable<GeneDefDto> defs)
        {
            int carrier = CarrierParent(a, b);
            var result = new List<GeneOdds>();
            foreach (GeneDefDto d in defs.OrderBy(x => x.Index))
            {
                int g = d.Index;
                int p0 = 0;
                int p1 = 1;
                if (carrier >= 0)
                {
                    p0 = p1 = InHeadBlock(g) ? carrier : 1 - carrier;
                }

                var values = new List<double>(4);
                foreach (int x in Alleles(p0 == 0 ? a : b, g))
                {
                    foreach (int y in Alleles(p1 == 0 ? a : b, g))
                    {
                        values.Add(GenomeEditor.Expressed(d, x, y));
                    }
                }
                result.Add(new GeneOdds(d, values.Min(), values.Max(), values.Average()));
            }
            return result;
        }

        private static IEnumerable<int> Alleles(byte[] genome, int g)
        {
            yield return genome[g] & 3;
            yield return genome[g + AlleleBOffset] & 3;
        }

        private static int CarrierParent(byte[] a, byte[] b)
        {
            bool ca = IsCarrier(a);
            bool cb = IsCarrier(b);
            if (ca == cb) return -1;
            return ca ? 0 : 1;
        }

        private static bool IsCarrier(byte[] genome)
            => (genome[HeadChimera] & 3) == 1 && (genome[HeadChimera + AlleleBOffset] & 3) == 1;

        private static bool InHeadBlock(int g)
        {
            if (ColourGenes.Contains(g)) return true;
            bool head = (g >= 0x48 && g <= 0x56) || (g >= 0x61 && g <= 0xae);
            return head && g != 0x53 && g != HeadChimera;
        }
    }
}
