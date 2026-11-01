using System.Text.Json;

namespace horsey
{
    // One horse as written by the mapview mod. Field names come from snake_case JSON keys.
    public sealed class HorseDot
    {
        public int Pos { get; set; }
        // The game's actor object address. Unlike Pos, it stays the same for the horse's whole life.
        public long Actor { get; set; }
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool Wild { get; set; }
        public bool Recorded { get; set; }
        public bool Sprite { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public int TileX { get; set; }
        public int TileY { get; set; }
        public int State { get; set; }
        public int Age { get; set; }
        public int MaxAge { get; set; }
        public bool Hungry { get; set; }
        public bool Tired { get; set; }
        public bool Old { get; set; }
        // Expressed value of every gene, by gene name (from shim.genes).
        public Dictionary<string, double> Genes { get; set; } = new();
    }

    // The whole reply in horses.json: the map size in tiles, where the player is, and the horses.
    public sealed class MapSnapshot
    {
        // A sprite PNG is drawn this many world units per pixel. The mod captures 128 x 128 pixels,
        // so 0.5 makes a sprite 64 world units across, about two tiles. Change this to size them.
        public const float SpriteWorldUnitsPerPixel = 0.5f;

        public int Width { get; set; }
        public int Height { get; set; }
        public string Location { get; set; } = "";
        public List<HorseDot> Horses { get; set; } = new();

        public static MapSnapshot Load(string path)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            };
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<MapSnapshot>(stream, options)
                ?? throw new JsonException("horses.json is empty");
        }

        // Draws the horses over a map drawn at `pixelsPerTile`, with the map's top-left corner at `origin`
        // in panel pixels. Horse positions are world units, where 32 world units make one tile.
        // Horses with a picture are drawn as that picture, keyed by list position; the rest are dots.
        public void Draw(Graphics g, float pixelsPerTile, PointF origin, Size canvas, Font font,
            IReadOnlyDictionary<int, Bitmap> sprites, Func<HorseDot, bool>? visible = null)
        {
            float unit = pixelsPerTile / 32f;
            IEnumerable<HorseDot> shown = visible is null ? Horses : Horses.Where(visible);

            using var ambient = new SolidBrush(Color.FromArgb(120, 120, 120));
            using var wild = new SolidBrush(Color.FromArgb(230, 120, 30));
            using var owned = new SolidBrush(Color.FromArgb(40, 160, 60));

            // Pictures first. A dot only for horses with no picture, so they still show up on the map.
            foreach (var h in shown)
            {
                if (sprites.TryGetValue(h.Pos, out Bitmap? sprite))
                    DrawSprite(g, sprite, h, unit, origin);
            }
            foreach (var h in shown)
            {
                if (sprites.ContainsKey(h.Pos)) continue;
                Brush brush = !h.Recorded ? ambient : h.Wild ? wild : owned;
                DrawDot(g, brush, h, unit, origin, h.Recorded ? 5f : 2.5f);
            }

            string legend = $"{Location}   grey: unrecorded   wheel: zoom   drag: pan";
            g.DrawString(legend, font, Brushes.White, 6, canvas.Height - 22);
        }

        private static PointF ToPanel(HorseDot h, float unit, PointF origin)
            => new(origin.X + (float)(h.X * unit), origin.Y + (float)(h.Y * unit));

        private static void DrawDot(Graphics g, Brush brush, HorseDot h, float unit, PointF origin, float radius)
        {
            PointF p = ToPanel(h, unit, origin);
            g.FillEllipse(brush, p.X - radius, p.Y - radius, radius * 2, radius * 2);
        }

        // The sprite is centred on the horse's position.
        private static void DrawSprite(Graphics g, Bitmap sprite, HorseDot h, float unit, PointF origin)
        {
            PointF p = ToPanel(h, unit, origin);
            float w = sprite.Width * SpriteWorldUnitsPerPixel * unit;
            float height = sprite.Height * SpriteWorldUnitsPerPixel * unit;
            g.DrawImage(sprite, p.X - w / 2f, p.Y - height / 2f, w, height);
        }
    }

    // The map area. Double buffered so redraws don't flicker.
    internal sealed class MapCanvas : Panel
    {
        public MapCanvas()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(40, 40, 40);
        }
    }
}
