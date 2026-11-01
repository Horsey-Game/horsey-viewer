using System.Drawing.Drawing2D;
using System.Xml.Linq;

namespace horsey
{
    // One tileset from the map: the first tile id it covers and the image the tiles come from.
    public sealed class TileSet : IDisposable
    {
        public int FirstGid { get; init; }
        public Bitmap Image { get; init; } = null!;
        public int Columns { get; init; }

        public void Dispose() => Image.Dispose();
    }

    // An object placed on the map (a location or a spawn point). X and Y are the top-left corner
    // in world units, where 32 world units make one tile.
    public sealed class MapObject
    {
        public string Type { get; init; } = "";
        public int Gid { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
    }

    // The map from data/horsey.tmx: the terrain layer and the object groups.
    public sealed class TerrainMap : IDisposable
    {
        private const int GidFlagMask = 0x0FFFFFFF; // the top bits are Tiled's flip flags

        public int Width { get; private init; }
        public int Height { get; private init; }
        public int TileSize { get; private init; }
        public int[] Cells { get; private init; } = Array.Empty<int>();
        public List<TileSet> Sets { get; private init; } = new();
        public List<MapObject> Objects { get; private init; } = new();

        public static TerrainMap Load(string tmxPath)
        {
            string dir = Path.GetDirectoryName(tmxPath) ?? ".";
            XElement map = XDocument.Load(tmxPath).Root ?? throw new InvalidDataException("no map element");
            int width = (int)map.Attribute("width")!;
            int height = (int)map.Attribute("height")!;
            int tile = (int)map.Attribute("tilewidth")!;

            // Each tileset refers to a .tsx file. Its image is the .png with the same name, next to the map.
            var sets = new List<TileSet>();
            foreach (XElement ts in map.Elements("tileset"))
            {
                int first = (int)ts.Attribute("firstgid")!;
                string source = (string?)ts.Attribute("source") ?? "";
                Bitmap bmp = LoadBitmap(Path.Combine(dir, Path.ChangeExtension(source, ".png")));
                sets.Add(new TileSet { FirstGid = first, Image = bmp, Columns = bmp.Width / tile });
            }

            XElement layer = map.Elements("layer").FirstOrDefault(l => (string?)l.Attribute("name") == "Tiles")
                ?? throw new InvalidDataException("no Tiles layer in the map");
            string csv = (string?)layer.Element("data") ?? "";
            int[] cells = csv
                .Split(new[] { ',', '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.Parse(s) & GidFlagMask)
                .ToArray();
            if (cells.Length != width * height)
                throw new InvalidDataException($"Tiles layer has {cells.Length} cells, expected {width * height}");

            var objects = new List<MapObject>();
            foreach (XElement group in map.Elements("objectgroup"))
            {
                foreach (XElement o in group.Elements("object"))
                {
                    int gid = ((int?)o.Attribute("gid") ?? 0) & GidFlagMask;
                    double w = (double?)o.Attribute("width") ?? 0.0;
                    double h = (double?)o.Attribute("height") ?? 0.0;
                    double x = (double?)o.Attribute("x") ?? 0.0;
                    double y = (double?)o.Attribute("y") ?? 0.0;
                    // Tiled puts a tile object's y at its bottom edge; drawing wants the top edge.
                    if (gid != 0) y -= h;
                    objects.Add(new MapObject
                    {
                        Type = (string?)o.Attribute("type") ?? (string?)o.Attribute("name") ?? "",
                        Gid = gid,
                        X = x,
                        Y = y,
                        Width = w,
                        Height = h,
                    });
                }
            }

            return new TerrainMap
            {
                Width = width,
                Height = height,
                TileSize = tile,
                Cells = cells,
                Sets = sets,
                Objects = objects,
            };
        }

        // Draws the terrain and the objects at `pixelsPerTile` pixels per tile.
        public Bitmap Render(float pixelsPerTile)
        {
            int w = Math.Max(1, (int)Math.Ceiling(Width * pixelsPerTile));
            int h = Math.Max(1, (int)Math.Ceiling(Height * pixelsPerTile));
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(40, 40, 40));
            Draw(g, pixelsPerTile, PointF.Empty, new Size(w, h));
            return bmp;
        }

        // Draws only the tiles and objects that fall inside the canvas. `pan` is where the map's top-left
        // corner sits on the canvas. Used directly when the map is too big to render whole.
        public void Draw(Graphics g, float pixelsPerTile, PointF pan, Size canvas)
        {
            // Nearest neighbour keeps tile edges sharp and stops neighbouring tiles bleeding into each other.
            g.InterpolationMode = InterpolationMode.NearestNeighbor;

            int x0 = Math.Max(0, (int)Math.Floor(-pan.X / pixelsPerTile));
            int y0 = Math.Max(0, (int)Math.Floor(-pan.Y / pixelsPerTile));
            int x1 = Math.Min(Width, (int)Math.Ceiling((canvas.Width - pan.X) / pixelsPerTile));
            int y1 = Math.Min(Height, (int)Math.Ceiling((canvas.Height - pan.Y) / pixelsPerTile));

            // Tile edges are snapped to whole pixels, so neighbouring tiles share an exact edge with no gap.
            for (int y = y0; y < y1; y++)
            {
                int top = (int)Math.Round(pan.Y + y * pixelsPerTile);
                int bottom = (int)Math.Round(pan.Y + (y + 1) * pixelsPerTile);
                for (int x = x0; x < x1; x++)
                {
                    int gid = Cells[y * Width + x];
                    if (gid == 0) continue;
                    int left = (int)Math.Round(pan.X + x * pixelsPerTile);
                    int right = (int)Math.Round(pan.X + (x + 1) * pixelsPerTile);
                    DrawTile(g, gid, new RectangleF(left, top, right - left, bottom - top));
                }
            }

            // Objects use world units, so they are scaled from TileSize per tile.
            float unit = pixelsPerTile / TileSize;
            foreach (MapObject o in Objects)
            {
                if (o.Gid == 0) continue;
                DrawTile(g, o.Gid, new RectangleF(
                    pan.X + (float)(o.X * unit), pan.Y + (float)(o.Y * unit),
                    (float)(o.Width * unit), (float)(o.Height * unit)));
            }
        }

        private void DrawTile(Graphics g, int gid, RectangleF dest)
        {
            // The tileset with the largest first id that is not above this gid.
            TileSet? set = null;
            foreach (TileSet s in Sets)
            {
                if (s.FirstGid <= gid && (set is null || s.FirstGid > set.FirstGid)) set = s;
            }
            if (set is null || set.Columns <= 0) return;

            int local = gid - set.FirstGid;
            var src = new RectangleF((local % set.Columns) * TileSize, (local / set.Columns) * TileSize, TileSize, TileSize);
            if (src.Right > set.Image.Width || src.Bottom > set.Image.Height) return;
            g.DrawImage(set.Image, dest, src, GraphicsUnit.Pixel);
        }

        private static Bitmap LoadBitmap(string path)
        {
            // Copy the image so the file is not kept open.
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }

        public void Dispose()
        {
            foreach (TileSet s in Sets) s.Dispose();
        }
    }
}
