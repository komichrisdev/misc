using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace Beholder
{
    public static class Tests
    {
        private static BeholderWindow window;
        private static string evidence;
        private static string wide, portrait, square, rotated;
        private static readonly List<XElement> outcomes = new List<XElement>();
        private static int failures;
        [STAThread]
        public static int Main(string[] args)
        {
            evidence = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "beholder-tests");
            Directory.CreateDirectory(evidence);
            Directory.CreateDirectory(Path.Combine(evidence, "fixtures"));
            CreateFixtures();
            var application = new Application();
            window = new BeholderWindow();
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = 60; window.Top = 60; window.Width = 1200; window.Height = 760;
            window.Loaded += async delegate
            {
                try { await Run(); }
                catch (Exception ex) { RecordFailure("harness", ex); }
                finally
                {
                    var suite = new XElement("testsuite", new XAttribute("name", "Beholder"), new XAttribute("tests", outcomes.Count), new XAttribute("failures", failures), new XAttribute("errors", 0), new XAttribute("skipped", 0), outcomes);
                    new XDocument(new XElement("testsuites", suite)).Save(Path.Combine(evidence, "tests.xml"));
                    Console.WriteLine("RESULT: " + outcomes.Count + " tests, " + failures + " failures");
                    window.Close(); application.Shutdown();
                }
            };
            application.Run(window);
            return failures == 0 ? 0 : 1;
        }

        private static async Task Case(string name, Func<Task> body)
        {
            var watch = Stopwatch.StartNew();
            try { await body(); outcomes.Add(new XElement("testcase", new XAttribute("name", name), new XAttribute("time", watch.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)))); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { RecordFailure(name, ex); }
        }
        private static Task Sync(Action body) { body(); return Task.FromResult(0); }
        private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        private static void RecordFailure(string name, Exception ex)
        {
            failures++; Console.WriteLine("FAIL " + name + ": " + ex);
            outcomes.Add(new XElement("testcase", new XAttribute("name", name), new XElement("failure", new XAttribute("message", ex.Message), ex.ToString())));
        }
        private static byte[] Hash(string path) { using (var h = SHA256.Create()) using (var s = File.OpenRead(path)) return h.ComputeHash(s); }
        private static void Pump() { window.UpdateLayout(); window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { })); }

        private static async Task Run()
        {
            await Case("empty canvas has no tiles and accepts drops", () => Sync(delegate { Check(window.Tiles.Count == 0 && window.Workspace.Children.Count == 0, "Canvas is not empty"); Check(window.AllowDrop, "File drops disabled"); }));
            await Case("dark-only canvas reclaims the global footer space", () => Sync(delegate
            {
                Pump(); var shell = (System.Windows.Controls.Grid)window.Content;
                Check(shell.RowDefinitions.Count == 2, "Extra status-bar row still reserves image space");
                Check(window.Workspace.ActualHeight > shell.ActualHeight - shell.RowDefinitions[0].ActualHeight - 36, "Canvas did not reclaim the bottom space");
                var color = ((SolidColorBrush)window.CanvasBackground).Color;
                Check(color.R < 50 && color.G < 50 && color.B < 50, "Canvas is not dark by default");
                var buttons = Visuals<System.Windows.Controls.Button>(window).Select(b => Convert.ToString(b.Content));
                Check(!buttons.Any(s => s == "Dark" || s == "Grey" || s == "Light"), "Theme selector is still exposed");
            }));
            await Case("layout zero and invalid viewport", () => Sync(delegate { Check(TileLayout.Arrange(0, 100, new[] { 1.0 }, false).Count == 0, "Zero viewport produced cells"); Check(TileLayout.Arrange(100, 100, new double[0], false).Count == 0, "Empty input produced cells"); }));
            await Case("two comparisons are side by side and proportionate", () => Sync(delegate { var a = TileLayout.Arrange(1000, 600, new[] { 2.0, 0.5 }, false); Check(a.Count == 2 && a[0].Y == a[1].Y, "Not side by side"); Check(a[0].Width > a[1].Width && Math.Abs(a[0].Width / a[1].Width - 4) < 0.001, "Aspect-aware widths wrong"); }));
            await Case("random auto and equal layouts stay bounded and nonoverlapping", () => Sync(delegate
            {
                var random = new Random(1729);
                for (int run = 0; run < 180; run++)
                {
                    int count = 1 + random.Next(64); double width = run % 9 == 0 ? 1 : 300 + random.Next(1800); double height = run % 9 == 0 ? 1 : 150 + random.Next(1000);
                    var aspects = Enumerable.Range(0, count).Select(i => Math.Exp(random.NextDouble() * 7 - 3.5)).ToList();
                    foreach (bool equal in new[] { false, true }) ValidateRects(TileLayout.Arrange(width, height, aspects, equal), count, width, height);
                }
            }));
            await Case("layout sanitizes nonfinite image proportions", () => Sync(delegate { ValidateRects(TileLayout.Arrange(900, 600, new[] { 0.0, -3.0, double.NaN, double.PositiveInfinity, 1.2 }, false), 5, 900, 600); }));
            await Case("PNG and portrait decode to frozen original dimensions", () => Sync(delegate { var a = ImageLoader.Load(wide); var b = ImageLoader.Load(portrait); Check(a.Width == 1600 && a.Height == 1000 && a.Bitmap.IsFrozen, "Wide decode wrong"); Check(b.Width == 700 && b.Height == 1100 && b.Bitmap.IsFrozen, "Portrait decode wrong"); }));
            await Case("file handles released and original bytes preserved", () => Sync(delegate { byte[] before = Hash(wide); var a = ImageLoader.Load(wide); using (var s = File.Open(wide, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check(s.Length > 0, "Empty fixture"); byte[] pixels = new byte[a.Bitmap.PixelWidth * 4]; a.Bitmap.CopyPixels(new Int32Rect(0, 0, a.Bitmap.PixelWidth, 1), pixels, pixels.Length, 0); Check(before.SequenceEqual(Hash(wide)), "Original bytes changed"); }));
            await Case("JPEG EXIF orientation applied before tiling", () => Sync(delegate { var a = ImageLoader.Load(rotated); Check(a.Width == 300 && a.Height == 500 && a.Bitmap.PixelWidth == 300 && a.Bitmap.PixelHeight == 500, "Orientation 6 not applied"); }));
            await Case("large image preview bounded but original dimensions retained", () => Sync(delegate { var a = ImageLoader.Load(Path.Combine(evidence, "fixtures", "large.png")); Check(a.Width == 8192 && a.Height == 256, "Original dimensions lost"); Check(a.Bitmap.PixelWidth == 4096 && a.Bitmap.PixelHeight == 128, "Preview not bounded"); }));
            await Case("BMP GIF and TIFF native decoders", () => Sync(delegate { foreach (string ext in new[] { "bmp", "gif", "tiff" }) { var a = ImageLoader.Load(Path.Combine(evidence, "fixtures", "format." + ext)); Check(a.Width == 100 && a.Height == 60 && a.Bitmap.IsFrozen, "Bad " + ext + " decode"); } }));
            await Case("transparent PNG retains alpha", () => Sync(delegate { var a = ImageLoader.Load(Path.Combine(evidence, "fixtures", "alpha.png")); var b = new FormatConvertedBitmap(a.Bitmap, PixelFormats.Bgra32, null, 0); var pixels = new byte[4]; b.CopyPixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0); Check(pixels[3] == 0, "Transparent pixel became opaque"); }));
            await Case("drop routed FileDrop data imports multiple images", async delegate
            {
                window.ClearImages();
                var data = new DataObject(DataFormats.FileDrop, new[] { wide, portrait, square });
                var ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single(c => c.GetParameters().Length == 5);
                var drop = (DragEventArgs)ctor.Invoke(new object[] { data, DragDropKeyStates.None, DragDropEffects.Copy, window.Workspace, new Point(40, 40) });
                drop.RoutedEvent = DragDrop.PreviewDropEvent; window.Workspace.RaiseEvent(drop);
                var wait = Stopwatch.StartNew();
                while (window.Tiles.Count < 3 && wait.Elapsed.TotalSeconds < 12) await Task.Delay(40);
                Pump(); Check(window.Tiles.Count == 3 && drop.Handled, "Actual routed drop did not import files");
                ValidateLive();
            });
            await Case("compact image captions preserve readable labels and removal controls", () => Sync(delegate
            {
                Pump();
                foreach (var tile in window.Tiles)
                {
                    var grid = (System.Windows.Controls.Grid)tile.Child;
                    double caption = grid.RowDefinitions[1].ActualHeight;
                    Check(caption <= 24 && caption == TileLayout.CaptionHeight, "Filename strip is not compact or differs from layout geometry");
                    Check(tile.Viewport.ActualHeight >= tile.Height - caption - 2.01, "Image viewport has unnecessary caption padding");
                    Check(Visuals<System.Windows.Controls.TextBlock>(tile).Any(t => t.Text == tile.Item.Name && t.IsVisible && t.FontSize >= 11), "Filename became unreadable or hidden");
                    var remove = Visuals<System.Windows.Controls.Button>(tile).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Remove " + tile.Item.Name);
                    Check(remove.IsEnabled && remove.ActualHeight <= caption && remove.ActualHeight > 0, "Remove control is clipped");
                }
            }));
            await Case("duplicate file drop does not duplicate tiles", async delegate { int count = window.Tiles.Count; await window.AddFilesAsync(new[] { wide, wide }); Check(window.Tiles.Count == count, "Duplicate tile admitted"); });
            await Case("corrupt and missing files are skipped with visible errors", async delegate { string corrupt = Path.Combine(evidence, "fixtures", "corrupt.png"); File.WriteAllText(corrupt, "not an image"); int count = window.Tiles.Count; await window.AddFilesAsync(new[] { corrupt, corrupt + ".missing" }); Check(window.Tiles.Count == count && window.LastMessage.Contains("skipped 2"), "Error not surfaced or corrupt tile added"); Pump(); Check(Visuals<System.Windows.Controls.TextBlock>(window).Any(t => t.Text == window.LastMessage && t.IsVisible), "Import error not visible after removing status bar"); });
            await Case("wheel routed event zooms without clipping outside tile", () => Sync(delegate { var t = window.Tiles[0]; var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120); args.RoutedEvent = UIElement.MouseWheelEvent; t.Viewport.RaiseEvent(args); Check(t.Zoom > 1 && args.Handled && t.Viewport.ClipToBounds, "Wheel route failed"); }));
            await Case("zoom clamp pan clamp and fit reset", () => Sync(delegate { var t = window.Tiles[0]; t.SetView(100, 100000, -100000, false); Check(t.Zoom == 32 && Math.Abs(t.PanX) < 100000 && Math.Abs(t.PanY) < 100000, "Unbounded pan/zoom"); window.ResetAll(); Check(window.Tiles.All(x => x.Zoom == 1 && x.PanX == 0 && x.PanY == 0), "Fit reset failed"); }));
            await Case("linked views synchronize zoom and disable independently", () => Sync(delegate { var t = window.Tiles[0]; window.SetLink(true); t.SetView(3, 0, 0, true); Check(window.Tiles.All(x => x.Zoom == 3), "Linked zoom not propagated"); window.SetLink(false); t.SetView(4, 0, 0, true); Check(window.Tiles[1].Zoom == 3, "Unlinked zoom leaked"); window.ResetAll(); }));
            await Case("focus restores grid and dimensions", () => Sync(delegate { var t = window.Tiles[1]; window.ToggleFocus(t); Pump(); Check(window.Tiles[0].Visibility == Visibility.Collapsed && t.Width == window.Workspace.ActualWidth, "Focus did not fill workspace"); window.ToggleFocus(t); Pump(); Check(window.Tiles.All(x => x.Visibility == Visibility.Visible), "Grid not restored"); ValidateLive(); }));
            await Case("drag-order action preserves every image", () => Sync(delegate { var source = window.Tiles[0]; var target = window.Tiles[2]; window.MoveTile(source, target); Pump(); Check(window.Tiles[2] == source && window.Tiles.Count == 3, "Reorder lost tiles"); ValidateLive(); }));
            await Case("resizing and equal layout keep all tiles within workspace", () => Sync(delegate { window.Width = 780; window.Height = 480; window.SetEqual(true); Pump(); ValidateLive(); window.Width = 1200; window.Height = 760; window.SetEqual(false); Pump(); ValidateLive(); }));
            await Case("remove and clear never delete original files", () => Sync(delegate { window.Remove(window.Tiles[0]); Pump(); Check(window.Tiles.Count == 2, "Removal failed"); window.ClearImages(); Check(window.Tiles.Count == 0 && window.Workspace.Children.Count == 0 && File.Exists(wide) && File.Exists(portrait), "Clear deleted files or retained tiles"); }));
            await Case("clear invalidates in-flight image import", async delegate { var loading = window.AddFilesAsync(new[] { wide, portrait }); window.ClearImages(); await loading; Check(window.Tiles.Count == 0, "Pending import repopulated cleared canvas"); });
            await Case("reimport after clear has generation-safe duplicate tracking", async delegate { var old = window.AddFilesAsync(new[] { wide }); window.ClearImages(); var current = window.AddFilesAsync(new[] { wide, wide }); await Task.WhenAll(old, current); Check(window.Tiles.Count == 1, "Generation change admitted duplicate or stale tile"); window.ClearImages(); });
            await Case("Unicode and spaces in filenames decode", async delegate { string p = Path.Combine(evidence, "fixtures", "study \u65e5\u672c\u8a9e 01.png"); File.Copy(wide, p, true); await window.AddFilesAsync(new[] { p }); Check(window.Tiles.Count == 1 && window.Tiles[0].Item.Name.Contains("01"), "Unicode path failed"); window.ClearImages(); });
            await Case("bundled WebP lossless decode works without a Windows codec", () => Sync(delegate { var a = ImageLoader.Load(WebPFixture("lossless.webp")); Check(a.Width == 100 && a.Height == 60 && a.Bitmap.IsFrozen, "Lossless WebP failed"); }));
            await Case("bundled WebP lossy decode", () => Sync(delegate { var a = ImageLoader.Load(WebPFixture("lossy.webp")); Check(a.Width == 1600 && a.Height == 1000, "Lossy WebP dimensions wrong"); }));
            await Case("WebP transparency preserved", () => Sync(delegate { var a = ImageLoader.Load(WebPFixture("alpha.webp")); var pixels = Pixel(a.Bitmap, 0, 0); Check(pixels[3] == 0, "WebP alpha lost"); }));
            await Case("animated WebP first frame is composited at canvas dimensions", () => Sync(delegate { var a = ImageLoader.Load(WebPFixture("animated.webp")); Check(a.Width == 1600 && a.Height == 1000, "Animated canvas wrong"); var pixels = Pixel(a.Bitmap, 0, 0); Check(pixels[0] == 214 && pixels[1] == 228 && pixels[2] == 236 && pixels[3] == 255, "First animation frame pixels wrong"); }));
            await Case("large WebP preview bounded and original dimensions retained", () => Sync(delegate { var a = ImageLoader.Load(WebPFixture("large.webp")); Check(a.Width == 8192 && a.Bitmap.PixelWidth == 4096 && a.Bitmap.PixelHeight == 128, "WebP preview bound failed"); }));
            await Case("WebP input remains byte-identical and unlocked", () => Sync(delegate { string path = WebPFixture("lossless.webp"); var before = Hash(path); ImageLoader.Load(path); using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check(stream.Length > 0, "No source data"); Check(before.SequenceEqual(Hash(path)), "WebP source modified"); }));
            await Case("corrupt WebP chunks rejected visibly", async delegate { window.ClearImages(); string path = Path.Combine(evidence, "fixtures", "broken.webp"); var bytes = File.ReadAllBytes(WebPFixture("lossless.webp")); File.WriteAllBytes(path, bytes.Take(22).ToArray()); await window.AddFilesAsync(new[] { path }); Check(window.Tiles.Count == 0 && window.LastMessage.Contains("skipped"), "Truncated WebP accepted or error hidden"); });
            await Case("global black and white filter affects every tile", async delegate { window.ClearImages(); await window.AddFilesAsync(new[] { wide, portrait, WebPFixture("lossy.webp") }); await window.SetMonochromeAsync(true); Check(window.Monochrome && window.Tiles.All(t => IsGrey(t.DisplayBitmap)), "Not all images filtered"); });
            await Case("black and white preserves alpha and immutable color source", () => Sync(delegate { var source = Pixels(4, 4, true); var before = Pixel(source, 0, 0); var grey = ImageLoader.Greyscale(source); var after = Pixel(grey, 0, 0); Check(after[0] == after[1] && after[1] == after[2] && after[3] == before[3] && grey.IsFrozen, "Grayscale alpha or color wrong"); Check(Pixel(source, 0, 0).SequenceEqual(before), "Filter mutated color pixels"); }));
            await Case("black and white switch off restores original colors exactly", async delegate { await window.SetMonochromeAsync(false); Check(!window.Monochrome && window.Tiles.All(t => object.ReferenceEquals(t.DisplayBitmap, t.Item.Bitmap)), "Color source not restored"); });
            await Case("newly dropped WebP inherits active global filter", async delegate { await window.SetMonochromeAsync(true); await window.AddFilesAsync(new[] { WebPFixture("lossless.webp") }); var wait = Stopwatch.StartNew(); while (!IsGrey(window.Tiles.Last().DisplayBitmap) && wait.Elapsed.TotalSeconds < 5) await Task.Delay(20); Check(IsGrey(window.Tiles.Last().DisplayBitmap), "New WebP ignored filter"); await window.SetMonochromeAsync(false); window.ClearImages(); });
            await Case("rapid filter toggles never leave stale monochrome views", async delegate { await window.AddFilesAsync(new[] { wide, square }); var pending = window.SetMonochromeAsync(true); await window.SetMonochromeAsync(false); await pending; Check(window.Tiles.All(t => object.ReferenceEquals(t.DisplayBitmap, t.Item.Bitmap)), "Stale filter completion replaced original colors"); window.ClearImages(); });
            await Case("actual rendered sample canvas", async delegate
            {
                await window.AddFilesAsync(new[] { wide, Path.Combine(evidence, "fixtures", "study-b.png"), portrait, square });
                Pump(); await Task.Delay(100); Pump(); ValidateLive();
                var visual = (System.Windows.Media.Visual)window.Content;
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(evidence, "sample-canvas.png"))) encoder.Save(stream);
                Check(new FileInfo(Path.Combine(evidence, "sample-canvas.png")).Length > 2000, "No rendered screenshot");
            });
        }

        private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) yield return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var node in Visuals<T>(VisualTreeHelper.GetChild(root, i))) yield return node;
        }

        private static string WebPFixture(string name)
        {
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tests", "fixtures", name));
        }
        private static byte[] Pixel(BitmapSource source, int x, int y)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var bytes = new byte[4]; converted.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0); return bytes;
        }
        private static bool IsGrey(BitmapSource source)
        {
            var pixels = Pixel(source, 0, 0); return pixels[0] == pixels[1] && pixels[1] == pixels[2];
        }

        private static void ValidateLive()
        {
            var rects = window.Tiles.Select(t => new Rect(System.Windows.Controls.Canvas.GetLeft(t), System.Windows.Controls.Canvas.GetTop(t), t.Width, t.Height)).ToList();
            ValidateRects(rects, window.Tiles.Count, window.Workspace.ActualWidth, window.Workspace.ActualHeight);
        }
        private static void ValidateRects(IList<Rect> rects, int count, double width, double height)
        {
            Check(rects.Count == count, "Layout lost images");
            for (int i = 0; i < rects.Count; i++)
            {
                Rect rect = rects[i];
                Check(rect.Width > 0 && rect.Height > 0 && !double.IsInfinity(rect.Width) && !double.IsNaN(rect.Height), "Invalid tile");
                Check(rect.X >= -0.001 && rect.Y >= -0.001 && rect.Right <= width + 0.001 && rect.Bottom <= height + 0.001, "Tile outside canvas: " + rect + " vs " + width + "x" + height);
                for (int j = 0; j < i; j++) { Rect overlap = Rect.Intersect(rect, rects[j]); Check(overlap.IsEmpty || overlap.Width < 0.0001 || overlap.Height < 0.0001, "Overlapping tiles"); }
            }
        }

        private static void Save(BitmapSource bitmap, string path, BitmapEncoder encoder)
        {
            encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
        private static BitmapSource Pixels(int width, int height, bool transparent = false)
        {
            var bytes = new byte[width * height * 4];
            for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = 90; bytes[i + 1] = 160; bytes[i + 2] = 210; bytes[i + 3] = transparent ? (byte)0 : (byte)255; }
            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4);
        }
        private static void CreateFixtures()
        {
            string dir = Path.Combine(evidence, "fixtures");
            wide = Path.Combine(dir, "study-a.png"); portrait = Path.Combine(dir, "portrait.png"); square = Path.Combine(dir, "square.png"); rotated = Path.Combine(dir, "oriented.jpg");
            Save(Artwork(1600, 1000, 0), wide, new PngBitmapEncoder());
            Save(Artwork(1600, 1000, 1), Path.Combine(dir, "study-b.png"), new PngBitmapEncoder());
            Save(Artwork(700, 1100, 2), portrait, new PngBitmapEncoder());
            Save(Artwork(900, 900, 3), square, new PngBitmapEncoder());
            Save(Pixels(8192, 256), Path.Combine(dir, "large.png"), new PngBitmapEncoder());
            Save(Pixels(100, 60), Path.Combine(dir, "format.bmp"), new BmpBitmapEncoder());
            Save(Pixels(100, 60), Path.Combine(dir, "format.gif"), new GifBitmapEncoder());
            Save(Pixels(100, 60), Path.Combine(dir, "format.tiff"), new TiffBitmapEncoder());
            Save(Pixels(16, 16, true), Path.Combine(dir, "alpha.png"), new PngBitmapEncoder());
            var metadata = new BitmapMetadata("jpg"); metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
            var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Pixels(500, 300), null, metadata, null)); using (var stream = File.Create(rotated)) encoder.Save(stream);
        }
        private static BitmapSource Artwork(int width, int height, int variant)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var palettes = new[] { new[] { "#ECE4D6", "#EF764E", "#243E4C" }, new[] { "#E7E8E1", "#D7895E", "#365656" }, new[] { "#D6E0D8", "#809D89", "#253D3A" }, new[] { "#E2DCF0", "#A194C8", "#35314E" } };
                var colors = palettes[variant];
                dc.DrawRectangle(BeholderWindow.Paint(colors[0]), null, new Rect(0, 0, width, height));
                double unit = Math.Min(width, height);
                dc.DrawEllipse(BeholderWindow.Paint(colors[1]), null, new Point(width * 0.57, height * 0.48), unit * 0.27, unit * 0.27);
                dc.DrawRoundedRectangle(BeholderWindow.Paint(colors[2]), null, new Rect(width * 0.19, height * 0.47, width * 0.45, height * 0.32), unit * 0.02, unit * 0.02);
                dc.DrawEllipse(BeholderWindow.Paint(colors[0]), null, new Point(width * 0.63, height * 0.50), unit * 0.13, unit * 0.13);
                var label = new FormattedText("COLOR STUDY / " + (char)('A' + variant), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), unit * 0.043, BeholderWindow.Paint(colors[2]), 1.0);
                dc.DrawText(label, new Point(unit * 0.09, unit * 0.08));
                var note = new FormattedText("GENERATED TEST IMAGE", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), unit * 0.017, BeholderWindow.Paint(colors[2]), 1.0);
                dc.DrawText(note, new Point(unit * 0.09, height - unit * 0.10));
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
        }
    }
}
