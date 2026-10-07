using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Beholder
{
    public static class CompositeExporter
    {
        public static Rect ImageBounds(Size viewport, double aspect, double zoom, Vector normalizedPan)
        {
            double fit = Math.Min(Math.Max(1, viewport.Width - 12) / aspect, Math.Max(1, viewport.Height - 12));
            double width = fit * aspect, height = fit;
            return new Rect((viewport.Width - width * zoom) / 2 + normalizedPan.X * width,
                (viewport.Height - height * zoom) / 2 + normalizedPan.Y * height, width * zoom, height * zoom);
        }

        public static BitmapSource Render(BeholderWindow window)
        {
            if (window.Tiles.Count == 0) throw new InvalidOperationException("Add images before saving a composite.");
            window.UpdateLayout();
            int n = window.Tiles.Count;
            var aspects = new List<double>(n);
            double lowArea = double.PositiveInfinity, lowW = 1, lowH = 1;
            for (int i = 0; i < n; i++)
            {
                var item = window.Tiles[i].Item;
                aspects.Add(TileLayout.SafeAspect(item.Aspect));
                double area = (double)item.Width * item.Height;
                if (area < lowArea) { lowArea = area; lowW = item.Width; lowH = item.Height; }
            }
            // Base layout at 100x100 gives the scale at which the lowest-resolution
            // image renders at its native pixels; per-tile zoom and pan stay applied.
            var baseRects = TileLayout.Arrange(100, 100, aspects, false);
            double baseFit = 1;
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(window.Tiles[i].Item.Width * (double)window.Tiles[i].Item.Height - lowArea) > 0.01) continue;
                double a = aspects[i];
                baseFit = Math.Max(1, Math.Min(baseRects[i].Width / a, baseRects[i].Height));
                break;
            }
            List<Rect> rectangles;
            double canvasW, canvasH;
            if (window.EqualTiles)
            {
                // Equal tiles: every comparison cell is exactly the lowest-res image size.
                int cols = Math.Max(1, Math.Min(n, (int)Math.Round(Math.Sqrt(n * lowH / Math.Max(1.0, lowW)))));
                int rows = (n + cols - 1) / cols;
                const double gap = 8;
                rectangles = new List<Rect>(n);
                for (int i = 0; i < n; i++)
                    rectangles.Add(new Rect((i % cols) * (lowW + gap), (i / cols) * (lowH + gap), lowW, lowH));
                canvasW = cols * lowW + gap * (cols - 1);
                canvasH = rows * lowH + gap * (rows - 1);
            }
            else
            {
                // Auto tiles: scale the whole layout so the smallest image lands at
                // native resolution and every other cell keeps its aspect ratio.
                double k = lowW / baseFit;
                canvasW = Math.Ceiling(100 * k);
                canvasH = Math.Ceiling(100 * k);
                rectangles = TileLayout.Arrange(canvasW, canvasH, aspects, false);
            }
            int width = (int)Math.Ceiling(canvasW), height = (int)Math.Ceiling(canvasH);
            if (width <= 0 || height <= 0 || (long)width * height > 256000000) throw new InvalidOperationException("Full-resolution composite is too large to export.");
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(window.CanvasBackground, null, new Rect(0, 0, width, height));
                for (int i = 0; i < window.Tiles.Count; i++)
                {
                    var tile = window.Tiles[i]; var cell = rectangles[i];
                    dc.PushClip(new RectangleGeometry(cell));
                    dc.PushTransform(new TranslateTransform(cell.X, cell.Y));
                    Rect image = ImageBounds(cell.Size, tile.Item.Aspect, tile.Zoom, tile.NormalizedPan);
                    dc.DrawImage(tile.DisplayBitmap, image);
                    if (window.ShowGuidesOn(tile))
                    {
                        if (window.IsometricEnabled) Drawing.Isometric(dc, new Rect(image.X - 1, image.Y - 1, image.Width + 2, image.Height + 2), window.IsometricAngleDegrees);
                        if (window.PitchEnabled) Drawing.Pitch(dc, image, window.PitchAngleDegrees);
                        if (window.CubeEnabled) Drawing.Cube(dc, image, window.CubeProjection, window.CubeProjection == "Isometric" ? window.IsometricAngleDegrees : window.PitchAngleDegrees, tile.PerspectivePoints, window.Cubes, window.SelectedCubeIndex, false);
                    }
                    if (window.PerspectiveEnabled) Drawing.Perspective(dc, tile, tile.PerspectivePoints, true);
                    dc.Pop(); dc.Pop();
                }
            }
            var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            result.Render(drawing); result.Freeze(); return result;
        }

        public static void Save(BeholderWindow window, string path)
        {
            string destination = Path.GetFullPath(path);
            if (window.Tiles.Any(t => !string.IsNullOrEmpty(t.Item.Path) && string.Equals(Path.GetFullPath(t.Item.Path), destination, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Choose a different filename; Beholder never overwrites an original image.");
            var bitmap = Render(window);
            string temporary = Path.Combine(Path.GetDirectoryName(destination), ".beholder-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) encoder.Save(stream);
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
