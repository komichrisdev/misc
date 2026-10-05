using System;
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
            int width = (int)Math.Ceiling(window.Workspace.ActualWidth), height = (int)Math.Ceiling(window.Workspace.ActualHeight);
            if (width <= 0 || height <= 0 || (long)width * height > 64000000) throw new InvalidOperationException("Canvas is too small or too large to export.");
            var rectangles = TileLayout.Arrange(width, height, window.Tiles.Select(t => t.Item.Aspect).ToList(), window.EqualTiles);
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
                    if (window.IsometricEnabled) Drawing.Isometric(dc, new Rect(image.X - 1, image.Y - 1, image.Width + 2, image.Height + 2), window.IsometricAngleDegrees);
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
