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
            await Case("Beholder v3 release identity is visible and versioned", () => Sync(delegate
            {
                Check(window.Title == "Beholder v3", "Window title is not Beholder v3");
                Check(Visuals<System.Windows.Controls.TextBlock>(window).Any(t => t.Text == "Beholder v3"), "In-app brand is not Beholder v3");
                Check(typeof(BeholderWindow).Assembly.GetName().Version.Major == 3, "Executable assembly major version is not 3");
            }));
            await Case("empty canvas has no tiles and accepts drops", () => Sync(delegate { Check(window.Tiles.Count == 0 && window.Workspace.Children.Count == 0, "Canvas is not empty"); Check(window.AllowDrop, "File drops disabled"); }));
            await Case("dark-only canvas reclaims the global footer space", () => Sync(delegate
            {
                Pump(); var shell = (System.Windows.Controls.Grid)window.Content;
                Check(shell.RowDefinitions.Count == 3 && shell.RowDefinitions[1].ActualHeight == 0,
                    "Perspective or status row reserves image space while closed");
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
            await Case("Flameshot clipboard PNG beats transparent DIB bitmap", () => Sync(delegate
            {
                var data = new System.Windows.DataObject();
                var png = new MemoryStream();
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(Pixels(12, 8))); enc.Save(png); png.Position = 0;
                data.SetData(System.Windows.DataFormats.Bitmap, Pixels(12, 8, true));
                data.SetData("image/png", png);
                var method = typeof(ImageLoader).GetMethod("FromClipboardData");
                Check(method != null, "Flameshot-compatible clipboard format selection is missing");
                var item = (LoadedImage)method.Invoke(null, new object[] { data, 7 });
                Check(item != null && item.Bitmap != null && item.Width == 12 && item.Height == 8, "Clipboard PNG did not produce an image");
                var px = Pixel(item.Bitmap, 6, 4);
                Check(px[3] == 255 && px[2] > 150 && px[1] > 100, "Flameshot PNG rendered blank despite opaque source pixels");
                window.ClearImages(); window.AddImage(item); Pump();
                Check(window.Tiles.Count == 1 && Pixel(window.Tiles[0].DisplayBitmap, 6, 4)[3] == 255, "Pasted tile became transparent");
                window.ClearImages();
            }));
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
            await Case("composite JPEG exports every image and excludes focus UI", async delegate
            {
                window.ClearImages(); await window.AddFilesAsync(new[] { wide, portrait, square }); Pump();
                var method = typeof(BeholderWindow).GetMethod("SaveComposite");
                Check(method != null, "Composite JPEG save feature is missing");
                string path = Path.Combine(evidence, "composite-clean.jpg");
                method.Invoke(window, new object[] { path });
                var saved = ImageLoader.Load(path);
                Check(saved.Width > 0 && (saved.Width != (int)Math.Ceiling(window.Workspace.ActualWidth) || saved.Height != (int)Math.Ceiling(window.Workspace.ActualHeight)),
                    "Composite still exports at window size instead of full resolution");
                var before = Hash(path); window.ToggleFocus(window.Tiles[1]); Pump();
                method.Invoke(window, new object[] { path });
                Check(before.SequenceEqual(Hash(path)), "Focus omitted images or changed composite");
                Check(window.FocusedTile == window.Tiles[1], "Export disturbed focus");
                window.ToggleFocus(window.Tiles[1]); Pump(); window.ClearImages();
            });
            await Case("composite exports at full resolution anchored to the lowest-res image", async delegate
            {
                window.ClearImages(); window.SetLink(false);
                await window.AddFilesAsync(new[] { wide, portrait }); Pump();
                window.SetEqual(true);
                window.SaveComposite(Path.Combine(evidence, "full-equal.jpg"));
                var equal = ImageLoader.Load(Path.Combine(evidence, "full-equal.jpg"));
                Check(equal.Width == 700 * 2 + 8 && equal.Height == 1100,
                    "Equal tiles did not size cells to the lowest-res image (got " + equal.Width + "x" + equal.Height + ")");
                window.SetEqual(false);
                window.SaveComposite(Path.Combine(evidence, "full-auto.jpg"));
                var auto = ImageLoader.Load(Path.Combine(evidence, "full-auto.jpg"));
                double a1 = 1600 / (double)1000, a2 = 700 / (double)1100;
                double w0 = (100 - 8) * a1 / (a1 + a2), w1 = (100 - 8) * a2 / (a1 + a2);
                double baseFit = Math.Min(w1 / a2, 100);
                double k = 700 / baseFit;
                Check(Math.Abs(auto.Width - Math.Ceiling(100 * k)) <= 3 && Math.Abs(auto.Height - Math.Ceiling(100 * k)) <= 3,
                    "Auto tiles did not anchor the lowest-res image near native size (got " + auto.Width + "x" + auto.Height + ", expected ~" + Math.Ceiling(100 * k) + ")");
                Check(auto.Width > window.Workspace.ActualWidth, "Full-resolution auto export is smaller than the window");
                window.SetEqual(false); window.ClearImages();
            });
            await Case("perspective toggle off keeps points and re-enable resets them", async delegate
            {
                window.ClearImages(); await window.AddFilesAsync(new[] { wide, portrait }); Pump();
                window.SetPerspective(true);
                var tile = window.Tiles[0]; tile.AddPerspectivePoint(new Point(0.3, 0.4)); tile.AddPerspectivePoint(new Point(0.7, 0.3));
                Check(tile.PerspectivePoints.Count == 2, "Placed points missing");
                window.SetPerspective(false);
                Check(tile.PerspectivePoints.Count == 2, "Toggle off erased points that must stay until re-enabled");
                window.SetPerspective(true);
                Check(window.Tiles.All(t => t.PerspectivePoints.Count == 0), "Re-enabling did not reset placed vanishing points");
                window.SetPerspective(false); window.ClearImages();
            });
            await Case("perspective editing moves and deletes points without deleting tiles", async delegate
            {
                window.ClearImages(); await window.AddFilesAsync(new[] { wide, portrait }); Pump(); window.SetPerspective(true);
                var tile = window.Tiles[0]; tile.AddPerspectivePoint(new Point(0.25, 0.4));
                var move = typeof(ImageTile).GetMethod("MovePerspectivePoint");
                Check(move != null, "Vanishing point dragging is missing");
                move.Invoke(tile, new object[] { 0, new Point(0.6, 0.3) });
                Check(tile.PerspectivePoints[0] == new Point(0.6, 0.3), "Drag did not move point");
                window.Select(tile);
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Delete);
                key.RoutedEvent = Keyboard.PreviewKeyDownEvent; window.RaiseEvent(key);
                Check(key.Handled && tile.PerspectivePoints.Count == 0 && window.Tiles.Count == 2, "Delete removed image instead of point");
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Delete) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Check(window.Tiles.Count == 2, "Delete without selected VP removed image in perspective mode");
                
            });
            await Case("delete removes the last-clicked vanishing point even while the perspective row is open", async delegate
            {
                window.ClearImages(); await window.AddFilesAsync(new[] { wide, portrait }); Pump();
                window.SetPerspective(true);
                var tile = window.Tiles[0];
                tile.AddPerspectivePoint(new Point(0.3, 0.4)); tile.AddPerspectivePoint(new Point(0.7, 0.6));
                var opener = Visuals<System.Windows.Controls.Button>(window)
                    .Single(b => Convert.ToString(b.Content).StartsWith("Perspective"));
                opener.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump();
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Delete);
                key.RoutedEvent = Keyboard.PreviewKeyDownEvent; window.RaiseEvent(key);
                Check(key.Handled && tile.PerspectivePoints.Count == 1 && tile.PerspectivePoints[0] == new Point(0.3, 0.4),
                    "Delete key was swallowed while the row is open or removed the wrong point");
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Delete) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Check(tile.PerspectivePoints.Count == 0, "Delete did not fall back to the last point when nothing is selected");
                opener.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump();
                window.SetPerspective(false); window.ClearImages();
            });
            await Case("linked perspective copies edits to every image and unlinks independently", async delegate
            {
                window.ClearImages(); await window.AddFilesAsync(new[] { wide, portrait }); Pump(); window.SetPerspective(true);
                var first = window.Tiles[0]; var second = window.Tiles[1];
                first.AddPerspectivePoint(new Point(0.2, 0.4)); first.AddPerspectivePoint(new Point(0.8, 0.4));
                window.Select(first); window.SetLink(true);
                Check(second.PerspectivePoints.SequenceEqual(first.PerspectivePoints), "Link did not copy existing vanishing points");
                second.MovePerspectivePoint(0, new Point(0.1, 0.3));
                Check(first.PerspectivePoints[0] == new Point(0.1, 0.3), "Move from linked target did not propagate");
                second.AddPerspectivePoint(new Point(0.5, 0.05));
                Check(first.PerspectivePoints.Count == 3, "Linked add not propagated");
                second.DeleteSelectedPerspectivePoint(); Check(first.PerspectivePoints.Count == 2, "Linked delete not propagated");
                await window.AddFilesAsync(new[] { square }); Pump();
                Check(window.Tiles[2].PerspectivePoints.SequenceEqual(first.PerspectivePoints), "Linked import did not inherit points");
                window.SetLink(false); first.MovePerspectivePoint(0, new Point(0.3, 0.2));
                Check(second.PerspectivePoints[0] == new Point(0.1, 0.3), "Unlinked lists are aliased");
                first.DeleteSelectedPerspectivePoint();
                Check(second.PerspectivePoints.Count == 2, "Unlinked deletion leaked");
                window.Select(window.Tiles[2]); window.Tiles[2].DeleteSelectedPerspectivePoint();
                window.ClearImages(); await window.AddFilesAsync(new[] { wide, portrait });
                window.Tiles[1].AddPerspectivePoint(new Point(0.5, 0.5)); window.Select(window.Tiles[0]); window.SetLink(true);
                Check(window.Tiles.All(t => t.PerspectivePoints.Count == 0), "Link with empty source retained stale points");
                window.SetLink(false); window.SetPerspective(false); window.ClearImages();
            });
            await Case("perspective export renders horizon and one two three point axes only when enabled", async delegate
            {
                window.ClearImages(); window.SetLink(false); await window.AddFilesAsync(new[] { wide, portrait }); Pump();
                var first = window.Tiles[0]; window.SetPerspective(false);
                string clean = Path.Combine(evidence, "perspective-off.jpg"), overlay = Path.Combine(evidence, "perspective-on.jpg");
                window.SaveComposite(clean); var cleanHash = Hash(clean);
                window.SetPerspective(true); first.AddPerspectivePoint(new Point(0.2, 0.35)); window.SaveComposite(overlay);
                Check(!Hash(overlay).SequenceEqual(cleanHash), "Perspective toggle on did not draw exported overlays");
                var imageBounds = first.ImageBounds;
                Check(imageBounds.Width > 10 && imageBounds.Height > 10, "Tile image bounds collapsed: " + imageBounds);
                var geometryType = typeof(BeholderWindow).Assembly.GetType("Beholder.PerspectiveGeometry");
                Check(geometryType != null, "Perspective grid geometry missing");
                var geometryBuild = geometryType.GetMethod("Build");
                var raw = ((System.Collections.IEnumerable)geometryBuild.Invoke(null, new object[] { first.PerspectivePoints, new Rect(0, 0, 800, 500) })).Cast<object>().ToArray();
                Check(raw.Length > 0, "Geometry produced no lines");
                var build = geometryType.GetMethod("Build");
                for (int count = 1; count <= 4; count++)
                {
                    if (count == 2) first.AddPerspectivePoint(new Point(0.85, 0.5));
                    if (count == 3) first.AddPerspectivePoint(new Point(0.5, 0.05));
                    if (count == 4) first.AddPerspectivePoint(new Point(0.6, 0.7));
                    var lines = ((System.Collections.IEnumerable)build.Invoke(null, new object[] { first.PerspectivePoints, new Rect(0, 0, 800, 500) })).Cast<object>().ToArray();
                    var axes = lines.Select(l => (string)l.GetType().GetField("Axis").GetValue(l)).Distinct().ToArray();
                    if (count >= 3) Check(new[] { "Horizon", "X", "Y", "Z" }.All(axes.Contains), "Missing horizon or XYZ at " + count + " points");
                    else Check(new[] { "Horizon", "X", "Z" }.All(axes.Contains), "Missing horizon or ZX at " + count + " points");
                    foreach (var line in lines)
                    {
                        Point a = (Point)line.GetType().GetField("A").GetValue(line), b = (Point)line.GetType().GetField("B").GetValue(line);
                        if (!new Rect(-0.001, -0.001, 800.002, 500.002).Contains(a) || !new Rect(-0.001, -0.001, 800.002, 500.002).Contains(b))
                            throw new InvalidOperationException("Grid escaped image bounds (" + count + " pts): " + a + " to " + b);
                    }
                    if (count >= 2)
                    {
                        var horizon = lines.Single(l => (string)l.GetType().GetField("Axis").GetValue(l) == "Horizon");
                        Point a = (Point)horizon.GetType().GetField("A").GetValue(horizon), b = (Point)horizon.GetType().GetField("B").GetValue(horizon);
                        Check(Math.Abs(a.Y - b.Y) > 1, "Two-point sloped horizon was forced horizontal");
                    }
                    window.SaveComposite(overlay);
                }
                window.SetPerspective(false); window.SaveComposite(clean);
                Check(Hash(clean).SequenceEqual(cleanHash) && first.PerspectivePoints.Count == 4, "Toggle off altered clean export or erased points");
                window.SetPerspective(true);
                Check(first.PerspectivePoints.Count == 0, "Re-enabling did not reset points before overlay checks");
                first.AddPerspectivePoint(new Point(0.2, 0.35)); first.AddPerspectivePoint(new Point(0.85, 0.5)); first.AddPerspectivePoint(new Point(0.5, 0.05)); first.AddPerspectivePoint(new Point(0.6, 0.7));
                window.SaveComposite(overlay); var before = Hash(overlay);
                window.Select(window.Tiles[1]); window.SaveComposite(overlay);
                Check(before.SequenceEqual(Hash(overlay)), "VP selection highlight leaked into saved image");
                var onBits = ImageLoader.Load(overlay).Bitmap;
                var offBits = ImageLoader.Load(clean).Bitmap;
                Check(onBits.PixelWidth == offBits.PixelWidth && onBits.PixelHeight == offBits.PixelHeight, "Evidence dimensions differ");
                var onPx = new FormatConvertedBitmap(onBits, PixelFormats.Bgra32, null, 0);
                var offPx = new FormatConvertedBitmap(offBits, PixelFormats.Bgra32, null, 0);
                int colored = 0, total = 0;
                var row = new byte[onPx.PixelWidth * 4];
                var offRow = new byte[onPx.PixelWidth * 4];
                for (int y = 0; y < onPx.PixelHeight; y++)
                {
                    onPx.CopyPixels(new Int32Rect(0, y, onPx.PixelWidth, 1), row, row.Length, 0);
                    offPx.CopyPixels(new Int32Rect(0, y, onPx.PixelWidth, 1), offRow, offRow.Length, 0);
                    for (int x = 0; x < onPx.PixelWidth; x++)
                    {
                        int b = row[x * 4] & 255, g = row[x * 4 + 1] & 255, r = row[x * 4 + 2] & 255;
                        int cb = offRow[x * 4] & 255, cg = offRow[x * 4 + 1] & 255, cr = offRow[x * 4 + 2] & 255;
                        if (Math.Abs(r - cr) + Math.Abs(g - cg) + Math.Abs(b - cb) > 60) colored++;
                        total++;
                    }
                }
                Check(colored > total / 500, "Perspective overlay produced no colored axis pixels");
                var axisBrush = Drawing.AxisColor("Y", 1) as SolidColorBrush;
                Check(axisBrush != null && !(axisBrush.Color.G > axisBrush.Color.R + 20 && axisBrush.Color.G > axisBrush.Color.B + 20),
                    "Y axis color still matches the green cube");
                window.SetPerspective(false); window.ClearImages();
            });
            await Case("isometric grid uses black strokes on a light background", () => Sync(delegate
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 320, 220));
                    Drawing.Isometric(dc, new Rect(0, 0, 320, 220), 26.565);
                }
                var bitmap = new RenderTargetBitmap(320, 220, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                var row = new byte[converted.PixelWidth * 4];
                int dark = 0, green = 0;
                for (int y = 0; y < converted.PixelHeight; y++)
                {
                    converted.CopyPixels(new Int32Rect(0, y, converted.PixelWidth, 1), row, row.Length, 0);
                    for (int x = 0; x < converted.PixelWidth; x++)
                    {
                        int b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                        if (r < 180 && g < 180 && b < 180) dark++;
                        if (g > r + 25 && g > b + 25) green++;
                    }
                }
                Check(dark > 200 && green == 0, "Isometric lines should be black, not green (dark=" + dark + ", green=" + green + ")");
            }));
            await Case("isometric grid overlay toggles independently and exports with the image", async delegate
            {
                window.ClearImages(); window.SetLink(false); window.SetPerspective(false); await window.AddFilesAsync(new[] { wide, portrait }); Pump();
                string clean = Path.Combine(evidence, "isometric-off.jpg"), grid = Path.Combine(evidence, "isometric-on.jpg");
                window.SaveComposite(clean); var cleanHash = Hash(clean);
                window.SetIsometric(true);
                window.SaveComposite(grid);
                var isoHash = Hash(grid);
                Check(!isoHash.SequenceEqual(cleanHash), "Isometric toggle on did not export grid");
                window.SetIsometric(false); window.SaveComposite(grid);
                Check(Hash(grid).SequenceEqual(cleanHash), "Isometric toggle off did not restore clean export");
                window.Tiles[0].AddPerspectivePoint(new Point(0.4, 0.3));
                window.Select(window.Tiles[1]);
                window.SetIsometric(true); window.SaveComposite(grid);
                Check(Hash(grid).SequenceEqual(isoHash), "Isometric export changed from perspective points");
                window.SetIsometric(false); window.SaveComposite(grid);
                Check(Hash(grid).SequenceEqual(cleanHash), "Perspective points leaked into isometric-off export");
                window.SetIsometric(true); window.SaveComposite(grid);
                Check(Math.Abs(window.IsometricAngleDegrees - 26.565) < 0.001, "Default isometric angle is not 2:1 (26.565)");
                window.SetIsometric(true); window.SaveComposite(grid); var angleRoot = Hash(grid);
                window.SetIsometricAngle(30); window.SaveComposite(grid); var angleChanged = Hash(grid);
                Check(!angleChanged.SequenceEqual(angleRoot), "Angle change did not redraw the grid");
                window.SetIsometricAngle(26.565); window.SaveComposite(grid);
                Check(Hash(grid).SequenceEqual(angleRoot), "Angle restore did not reproduce the 2:1 grid");
                window.SetIsometricAngle(26.565); window.SetIsometric(false);
                window.CycleIsometric();
                Check(window.IsometricEnabled && Math.Abs(window.IsometricAngleDegrees - 26.565) < 0.001, "First click should enable at 2:1");
                window.CycleIsometric();
                Check(Math.Abs(window.IsometricAngleDegrees - 30) < 0.001, "Second click should advance to 30 degrees");
                window.CycleIsometric();
                Check(!window.IsometricEnabled, "Third click should turn the isometric grid off");
                window.CycleIsometric();
                Check(window.IsometricEnabled && Math.Abs(window.IsometricAngleDegrees - 26.565) < 0.001, "Fourth click should return to 2:1");
                window.SetIsometric(false); window.ClearImages();
            });
            await Case("pitch grid adjusts angle, draws black, and exports over images", async delegate
            {
                var set = typeof(BeholderWindow).GetMethod("SetPitch");
                var angle = typeof(BeholderWindow).GetMethod("SetPitchAngle");
                var mode = typeof(BeholderWindow).GetProperty("PitchEnabled");
                var draw = typeof(Drawing).GetMethod("Pitch");
                Check(set != null && angle != null && mode != null && draw != null, "Pitch grid API is missing");
                window.ClearImages(); await window.AddFilesAsync(new[] { wide }); Pump();
                string file = Path.Combine(evidence, "pitch-on.jpg");
                try
                {
                    window.SaveComposite(file); var clean = Hash(file);
                    set.Invoke(window, new object[] { true });
                    Check((bool)mode.GetValue(window, null), "Pitch mode did not enable");
                    window.SaveComposite(file); var first = Hash(file);
                    Check(!first.SequenceEqual(clean), "Pitch grid did not export");
                    angle.Invoke(window, new object[] { 60.0 }); window.SaveComposite(file);
                    Check(!Hash(file).SequenceEqual(first), "Pitch adjustment did not change the grid");
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 320, 220));
                        draw.Invoke(null, new object[] { dc, new Rect(0, 0, 320, 220), 30.0 });
                    }
                    var bitmap = new RenderTargetBitmap(320, 220, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual); var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                    var row = new byte[320 * 4]; int dark = 0, green = 0;
                    for (int y = 0; y < 220; y++)
                    {
                        converted.CopyPixels(new Int32Rect(0, y, 320, 1), row, row.Length, 0);
                        for (int x = 0; x < 320; x++)
                        {
                            int b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                            if (r < 180 && g < 180 && b < 180) dark++;
                            if (g > r + 25 && g > b + 25) green++;
                        }
                    }
                    Check(dark > 200 && green == 0, "Pitch grid must be black, not green (dark=" + dark + ", green=" + green + ")");
                }
                finally { set.Invoke(window, new object[] { false }); window.ClearImages(); }
            });
            await Case("pitch grid rotates a flat plane: parallel columns, foreshortened rows", () => Sync(delegate
            {
                var rect = new Rect(0, 0, 800, 500);
                Func<double, double[]> columnXs = angle => PitchGeometry.Build(rect, angle)
                    .Where(l => Math.Abs(l.A.X - l.B.X) < 0.001 && Math.Abs(l.A.Y - rect.Top) < 0.01)
                    .OrderBy(l => l.A.X).Select(l => l.A.X).Distinct().ToArray();
                Func<double, double[]> rowYs = angle => PitchGeometry.Build(rect, angle)
                    .Where(l => Math.Abs(l.A.Y - l.B.Y) < 0.001 && Math.Abs(l.A.X - rect.X) < 0.01)
                    .OrderBy(l => l.A.Y).Select(l => l.A.Y).Distinct().ToArray();
                var cols20 = columnXs(20); var cols85 = columnXs(85);
                Check(cols20.Length >= 5 && cols85.Length >= 5, "Pitch grid lost its column family");
                for (int i = 1; i < cols20.Length && i < cols85.Length; i++)
                    Check(Math.Abs((cols20[i] - cols20[i - 1]) - (cols85[i] - cols85[i - 1])) < 0.01,
                        "Column spacing changed with rotation (i=" + i + ")");
                var rows20 = rowYs(20); var rows85 = rowYs(85);
                Check(rows20.Length >= 3 && rows85.Length >= 3, "Pitch grid lost its row family");
                // The lowest/highest bands are cut by the image edge; measure the middle bands.
                double gap20 = Math.Abs(rows20[rows20.Length / 2 + 1] - rows20[rows20.Length / 2]);
                double gap85 = Math.Abs(rows85[rows85.Length / 2 + 1] - rows85[rows85.Length / 2]);
                Check(gap20 < gap85 * 0.45,
                    "Rows do not foreshorten when the plane rotates edge-on (20° gap=" + gap20.ToString("0.0") + ", 85° gap=" + gap85.ToString("0.0") + ")");
                Check(rows20.Length > rows85.Length, "Row count did not increase as the plane turns edge-on");
            }));
            await Case("pitch grid covers zoomed image bounds without unbounded lines", () => Sync(delegate
            {
                var image = new Rect(-8000, -8000, 16000, 16000);
                var lines = PitchGeometry.Build(image, 35).ToArray();
                var horizontal = lines.Where(l => Math.Abs(l.A.Y - l.B.Y) < 0.001).ToArray();
                Check(horizontal.Length > 2 && horizontal.Min(l => l.A.Y) <= image.Top + 100 &&
                    horizontal.Max(l => l.A.Y) >= image.Bottom - 100,
                    "Pitch grid vanished near zoomed image edges");
                Check(lines.Length < 1500, "Pitch grid created too many lines on a zoomed image");
            }));
            await Case("pitch grid is a top-down chessboard at 90 degrees", () => Sync(delegate
            {
                var rect = new Rect(0, 0, 800, 500);
                var lines = PitchGeometry.Build(rect, 90).ToArray();
                var verticals = lines.Where(l => Math.Abs(l.A.X - l.B.X) < 0.001).OrderBy(l => l.A.X).ToArray();
                var horizontals = lines.Where(l => Math.Abs(l.A.Y - l.B.Y) < 0.001).OrderBy(l => l.A.Y).ToArray();
                Check(verticals.Length >= 5 && horizontals.Length >= 5, "90 degree grid lacks both line families");
                double vGap = Math.Abs(verticals[1].A.X - verticals[0].A.X), hGap = Math.Abs(horizontals[1].A.Y - horizontals[0].A.Y);
                for (int i = 1; i < verticals.Length; i++)
                    Check(Math.Abs(Math.Abs(verticals[i].A.X - verticals[i - 1].A.X) - vGap) < 0.01, "Vertical spacing varies at 90 degrees");
                for (int i = 1; i < horizontals.Length; i++)
                    Check(Math.Abs(Math.Abs(horizontals[i].A.Y - horizontals[i - 1].A.Y) - hGap) < 0.01, "Horizontal spacing varies at 90 degrees");
                Check(Math.Abs(vGap - hGap) < 0.01, "90 degree cells are not square");
                Check(verticals.First().A.X < rect.Width / 2 && verticals.Last().A.X > rect.Width / 2 &&
                    horizontals.First().A.Y < rect.Bottom && horizontals.Last().A.Y >= rect.Top, "90 degree grid missed the image");
                var cube = CubeGeometry.Build(rect, "Pitch", 90, new[] { new Point(0.5, 0.5) }).ToArray();
                Check(cube.Length == 4 && cube.All(e => rect.Contains(e.A) && rect.Contains(e.B)), "90 degree cube is not a contained green square");
                var pts = cube.SelectMany(e => new[] { e.A, e.B }).Distinct().ToArray();
                Check(pts.Length == 4, "90 degree square has degenerate corners");
                double side = (pts[0] - pts[1]).Length;
                Check(side > 1 && Math.Abs((pts[1] - pts[2]).Length - side) < 0.01 && Math.Abs((pts[2] - pts[3]).Length - side) < 0.01 && Math.Abs((pts[3] - pts[0]).Length - side) < 0.01,
                    "90 degree square has unequal sides");
                Check(Math.Abs(pts.Min(p => p.X) + pts.Max(p => p.X) - rect.Width) < 0.01, "90 degree square is not centered");
            }));
            await Case("transparent cube follows pitch, isometric, and one-to-three point projection", async delegate
            {
                var cubeToggle = typeof(BeholderWindow).GetMethod("SetCube");
                var cubeFlag = typeof(BeholderWindow).GetProperty("CubeEnabled");
                var geometryType = typeof(BeholderWindow).Assembly.GetType("Beholder.CubeGeometry");
                var build = geometryType == null ? null : geometryType.GetMethods().FirstOrDefault(m => m.Name == "Build" && m.GetParameters().Length == 4);
                Check(cubeToggle != null && cubeFlag != null && build != null, "Wireframe cube API is missing");
                var bounds = new Rect(0, 0, 800, 500);
                var points = new[] { new Point(0.2, 0.36), new Point(0.8, 0.36), new Point(0.5, 0.08), new Point(0.35, 0.8) };
                var iso = ((System.Collections.IEnumerable)build.Invoke(null, new object[] { bounds, "Isometric", 26.565, points })).Cast<PerspectiveLine>().ToArray();
                var pitch = ((System.Collections.IEnumerable)build.Invoke(null, new object[] { bounds, "Pitch", 35.0, points })).Cast<PerspectiveLine>().ToArray();
                Check(iso.Length == 12 && pitch.Length == 12 && !iso[0].A.Equals(pitch[0].A), "Cube lacks 12 mode-dependent edges");
                Check(iso.All(edge => Math.Abs(edge.Width - 3.0) < 0.001) && pitch.All(edge => Math.Abs(edge.Width - 3.0) < 0.001),
                    "Wireframe cube strokes are not 3x the grid width");
                for (int count = 1; count <= 3; count++)
                {
                    var cube = ((System.Collections.IEnumerable)build.Invoke(null, new object[] { bounds, "Vanishing", 35.0, points.Take(count).ToArray() })).Cast<PerspectiveLine>().ToArray();
                    Check(cube.Length == 12, "Vanishing cube should have 12 edges with " + count + " points, got " + cube.Length);
                    foreach (var line in cube)
                    {
                        Check(bounds.Contains(line.A) && bounds.Contains(line.B), "Cube edge escaped its image");
                        int index = line.Axis == "Z" ? 0 : line.Axis == "X" ? 1 : line.Axis == "Y" ? 2 : -1;
                        if (index < 0 || index >= count) continue;
                        Point vanishing = new Point(points[index].X * bounds.Width, points[index].Y * bounds.Height);
                        Vector v = line.B - line.A, to = vanishing - line.A;
                        double distance = Math.Abs(v.X * to.Y - v.Y * to.X) / Math.Max(0.01, v.Length);
                        Check(distance < 0.001, "Cube " + line.Axis + " edge does not converge to its vanishing point");
                    }
                }
                var firstThree = ((System.Collections.IEnumerable)build.Invoke(null, new object[] { bounds, "Vanishing", 35.0, points.Take(3).ToArray() })).Cast<PerspectiveLine>().ToArray();
                var four = ((System.Collections.IEnumerable)build.Invoke(null, new object[] { bounds, "Vanishing", 35.0, points })).Cast<PerspectiveLine>().ToArray();
                Check(firstThree.Zip(four, (a, b) => a.A == b.A && a.B == b.B).All(same => same), "Fourth vanishing point changed the cube");
                window.ClearImages(); window.SetPerspective(false); window.SetPitch(false); window.SetIsometric(false);
                await window.AddFilesAsync(new[] { wide }); Pump();
                string file = Path.Combine(evidence, "cube-on.jpg");
                try
                {
                    window.SetIsometric(true); window.SaveComposite(file); var noCube = Hash(file);
                    cubeToggle.Invoke(window, new object[] { true });
                    Check((bool)cubeFlag.GetValue(window, null), "Cube toggle did not enable");
                    window.SaveComposite(file); Check(!Hash(file).SequenceEqual(noCube), "Cube did not export over isometric grid");
                    window.SetIsometric(false); window.SetPitchAngle(35); window.SetPitch(true); window.SaveComposite(file); var pitched = Hash(file);
                    window.SetPitchAngle(60); window.SaveComposite(file);
                    Check(!Hash(file).SequenceEqual(pitched), "Cube did not follow pitch change");
                    window.SetPitch(false); window.SetPerspective(true);
                    window.Tiles[0].AddPerspectivePoint(points[0]); window.SaveComposite(file); var onePoint = Hash(file);
                    window.Tiles[0].AddPerspectivePoint(points[1]); window.SaveComposite(file);
                    Check(!Hash(file).SequenceEqual(onePoint), "Cube did not follow vanishing point mode");
                }
                finally { cubeToggle.Invoke(window, new object[] { false }); window.SetPerspective(false); window.SetPitch(false); window.SetIsometric(false); window.ClearImages(); }
            });
            await Case("cube instances scale, offset, and expose xyz grab handles", () => Sync(delegate
            {
                var bounds = new Rect(0, 0, 800, 500);
                var points = new Point[0];
                var dflt = CubeGeometry.Build(bounds, "Pitch", 35, points).ToArray();
                var off = CubeGeometry.Build(bounds, "Pitch", 35, points, new CubeInstance(4, 0, 0, 1)).ToArray();
                Check(dflt.Length == 12 && off.Length == 12, "Instance build lost the 12 edges");
                double spacing = 800.0 / 18.0;
                Check(Math.Abs(off[0].A.X - dflt[0].A.X - 4 * spacing) < 0.01, "X offset did not shift the cube by 4 grid cells");
                var big = CubeGeometry.Build(bounds, "Pitch", 35, points, new CubeInstance(0, 0, 0, 2)).ToArray();
                double spanDefault = dflt.Max(e => Math.Max(e.A.X, e.B.X)) - dflt.Min(e => Math.Min(e.A.X, e.B.X));
                double spanBig = big.Max(e => Math.Max(e.A.X, e.B.X)) - big.Min(e => Math.Min(e.A.X, e.B.X));
                Check(Math.Abs(spanBig - 2 * spanDefault) < 0.01, "Size did not double the cube span");
                double rad = 35 * Math.PI / 180.0;
                Point xHandle = new Point(400 + 2 * spacing, 500 - 3 * spacing * Math.Sin(rad));
                Check(CubeInteraction.HandleAt(bounds, "Pitch", 35, points, new CubeInstance(), xHandle, 8) == "X", "X grab handle not hit");
                Check(CubeInteraction.HandleAt(bounds, "Pitch", 35, points, new CubeInstance(), new Point(400, 300), 8) == null, "Handle hit outside the cube");
                Check(CubeInteraction.GridDelta(bounds, "Pitch", 35, new Vector(spacing, 0), "X") - 1.0 < 0.02, "X drag did not map one grid cell");
                Check(CubeInteraction.GridDelta(bounds, "Pitch", 90, new Vector(0, -spacing), "Y") - 1.0 < 0.02, "90 degree Y drag did not map one grid cell");
                Check(CubeInteraction.GridDelta(bounds, "Pitch", 90, new Vector(0, -spacing), "Z") == 0, "Z drag at 90 degrees should be inert");
                var cs = CubeGeometry.Corners(bounds, "Pitch", 35, points, new CubeInstance());
                Point center = new Point(cs.Average(c => c.X), cs.Average(c => c.Y));
                Check(CubeInteraction.SelectAt(bounds, "Pitch", 35, points, new[] { new CubeInstance() }, center, 3) == 0, "Body select missed the cube center");
            }));
            await Case("cube X handle tracks each pointer position without cumulative overshoot", async delegate
            {
                window.ClearImages(); window.SetPerspective(false); window.SetIsometric(false); window.SetPitch(false); window.SetCube(false);
                await window.AddFilesAsync(new[] { wide }); Pump();
                window.SetPitch(true); window.SetCube(true);
                try
                {
                    var tile = window.Tiles[0];
                    Rect image = tile.ImageBounds;
                    Point start = CubeGeometry.Corners(image, "Pitch", window.PitchAngleDegrees, tile.PerspectivePoints, window.Cubes[0])[1];
                    Check(tile.BeginCubeGesture(start), "Cube X handle could not start a drag");
                    double x0 = window.Cubes[0].X;
                    Point at = start + new Vector(20, 0);
                    tile.UpdateCubeGesture(at);
                    double first = window.Cubes[0].X;
                    tile.UpdateCubeGesture(at);
                    double samePointer = window.Cubes[0].X;
                    Check(Math.Abs(samePointer - first) < 0.0001, "Repeated mouse-move at the same pixel moved the cube again");
                    tile.UpdateCubeGesture(start + new Vector(30, 0));
                    double x1 = window.Cubes[0].X;
                    double spacing = PitchGeometry.Spacing(image);
                    Check(Math.Abs((x1 - x0) * spacing - 30) < 0.01, "Cube handle did not follow the mouse by 30 pixels");
                }
                finally { window.Tiles[0].EndCubeGesture(); window.SetCube(false); window.SetPitch(false); window.ClearImages(); }
            });
            await Case("vanishing cube exposes grab handles and follows pointer", async delegate
            {
                window.ClearImages(); window.SetPitch(false); window.SetIsometric(false); window.SetPerspective(false); window.SetCube(false);
                await window.AddFilesAsync(new[] { wide }); Pump();
                window.SetPerspective(true); window.Tiles[0].AddPerspectivePoint(new Point(0.5, 0.35)); window.SetCube(true);
                try
                {
                    var tile = window.Tiles[0]; Rect image = tile.ImageBounds;
                    Point[] corners = CubeGeometry.Corners(image, "Vanishing", 35, tile.PerspectivePoints, window.Cubes[0]);
                    Point start = corners[1];
                    Check(CubeInteraction.HandleAt(image, "Vanishing", 35, tile.PerspectivePoints, window.Cubes[0], start, 10) == "X", "VP cube X handle is missing");
                    Check(tile.BeginCubeGesture(start), "VP cube handle did not capture pointer");
                    double initial = window.Cubes[0].X;
                    Point moved = start + new Vector(20, 0);
                    tile.UpdateCubeGesture(moved); tile.UpdateCubeGesture(moved);
                    Point end = CubeGeometry.Corners(image, "Vanishing", 35, tile.PerspectivePoints, window.Cubes[0])[1];
                    Check(Math.Abs(end.X - start.X - 20) < 0.1 && Math.Abs(window.Cubes[0].X - initial) > 0.01,
                        "VP cube X handle did not follow pointer by 20 pixels");
                }
                finally { window.Tiles[0].EndCubeGesture(); window.SetCube(false); window.SetPerspective(false); window.ClearImages(); }
            });
            await Case("isometric Z handle can cross the initial center in both directions", async delegate
            {
                window.ClearImages(); window.SetPerspective(false); window.SetPitch(false); window.SetIsometric(false); window.SetCube(false);
                await window.AddFilesAsync(new[] { wide }); Pump();
                window.SetIsometric(true); window.SetCube(true);
                try
                {
                    var tile = window.Tiles[0]; Rect image = tile.ImageBounds;
                    var cube = window.Cubes[0];
                    Point start = CubeGeometry.Corners(image, "Isometric", window.IsometricAngleDegrees, tile.PerspectivePoints, cube)[4];
                    Check(CubeInteraction.HandleAt(image, "Isometric", window.IsometricAngleDegrees, tile.PerspectivePoints, cube, start, 10) == "Z", "Isometric left-right Z handle is missing");
                    Check(tile.BeginCubeGesture(start), "Isometric Z handle could not start drag");
                    double unit = Math.Min(image.Width, image.Height), slope = Math.Tan(window.IsometricAngleDegrees * Math.PI / 180.0);
                    Vector backwards = new Vector(unit * 0.19 * 0.45, -unit * 0.19 * slope * 0.45);
                    Point crossing = start + backwards;
                    tile.UpdateCubeGesture(crossing);
                    Check(cube.Z < -0.4, "Isometric Z axis stops at its initial center instead of crossing it");
                    Point followed = CubeGeometry.Corners(image, "Isometric", window.IsometricAngleDegrees, tile.PerspectivePoints, cube)[4];
                    Check((followed - crossing).Length < 0.1, "Isometric Z handle did not follow the pointer past center");
                    tile.UpdateCubeGesture(crossing);
                    Check(Math.Abs(cube.Z + 0.45) < 0.01, "Repeated isometric Z move drifted after crossing center");
                }
                finally { window.Tiles[0].EndCubeGesture(); window.SetCube(false); window.SetIsometric(false); window.ClearImages(); }
            });
            await Case("cube list adds, selects, moves, and resizes cubes", () => Sync(delegate
            {
                window.ClearImages();
                window.SetIsometric(false); window.SetPitch(false); window.SetPerspective(false); window.SetCube(false);
                Check(window.Cubes.Count == 1 && window.SelectedCubeIndex == 0, "Window lacks a default cube");
                window.AddCube();
                Check(window.Cubes.Count == 2 && window.SelectedCubeIndex == 1, "AddCube did not append and select a new cube");
                double bx = window.Cubes[1].X, by = window.Cubes[1].Y, bz = window.Cubes[1].Z;
                window.MoveCube(1, 3, 2, 1);
                Check(Math.Abs(window.Cubes[1].X - bx - 3) < 0.001 && Math.Abs(window.Cubes[1].Y - by - 2) < 0.001 && Math.Abs(window.Cubes[1].Z - bz - 1) < 0.001,
                    "MoveCube did not apply grid-cell deltas");
                window.ResizeCube(1, 2.0);
                Check(Math.Abs(window.Cubes[1].Size - 2.0) < 0.001, "ResizeCube did not scale");
                window.SelectCube(0);
                Check(window.SelectedCubeIndex == 0, "SelectCube failed");
                Check(window.PerspectiveBar.Children.OfType<System.Windows.Controls.Button>().Any(b => Convert.ToString(b.Content).Contains("Cube")),
                    "Perspective row lacks an Add cube button");
                window.SetCube(false); window.SetIsometric(false);
            }));
            await Case("re-enabling any perspective checkbox resets points, angles, and the cube", async delegate
            {
                window.ClearImages(); window.SetIsometric(false); window.SetPitch(false); window.SetPerspective(false); window.SetCube(false);
                await window.AddFilesAsync(new[] { wide }); Pump();
                window.SetPerspective(true); window.Tiles[0].AddPerspectivePoint(new Point(0.4, 0.4));
                window.SetPerspective(false); window.SetPerspective(true);
                Check(window.Tiles[0].PerspectivePoints.Count == 0, "Vanishing points were not reset on re-enable");
                window.SetIsometricAngle(45); window.SetIsometric(true);
                Check(Math.Abs(window.IsometricAngleDegrees - 26.565) < 0.001, "Isometric angle was not reset to 2:1 on enable");
                window.SetIsometricAngle(55); window.SetIsometric(false); window.SetIsometric(true);
                Check(Math.Abs(window.IsometricAngleDegrees - 26.565) < 0.001, "Isometric angle persisted across the toggle");
                window.SetPitchAngle(80); window.SetPitch(true);
                Check(Math.Abs(window.PitchAngleDegrees - 35) < 0.001, "Pitch angle was not reset to default on enable");
                window.SetPitchAngle(60); window.SetPitch(false); window.SetPitch(true);
                Check(Math.Abs(window.PitchAngleDegrees - 35) < 0.001, "Pitch angle persisted across the toggle");
                window.AddCube();
                Check(window.Cubes.Count == 2, "Setup did not add a second cube");
                window.SetCube(false); window.SetCube(true);
                Check(window.Cubes.Count == 1, "Cube list was not reset on re-enable");
                window.SetCube(false); window.SetPitch(false); window.SetIsometric(false); window.ClearImages();
            });
            await Case("re-enabling the wireframe cube resets it to the default position", () => Sync(delegate
            {
                window.ClearImages();
                window.SetIsometric(false); window.SetPitch(false); window.SetPerspective(false); window.SetCube(false);
                window.SetPitch(true); window.SetCube(true);
                Check(window.Cubes.Count == 1 && window.SelectedCubeIndex == 0, "Enabling the cube did not start with the default cube");
                window.AddCube();
                window.MoveCube(1, 50, 30, 5);
                Check(window.Cubes.Count == 2 && window.Cubes[1].X > 40, "Setup did not move a cube far away");
                window.SetCube(false); window.SetCube(true);
                Check(window.Cubes.Count == 1 && Math.Abs(window.Cubes[0].X) < 0.001 && Math.Abs(window.Cubes[0].Y) < 0.001 &&
                    Math.Abs(window.Cubes[0].Z) < 0.001 && Math.Abs(window.Cubes[0].Size - 1) < 0.001,
                    "Toggling the cube off/on did not reset it");
                window.SetCube(false); window.SetPitch(false);
            }));
            await Case("perspective guides draw only on the selected tile unless link views is on", async delegate
            {
                window.ClearImages(); window.SetLink(false);
                await window.AddFilesAsync(new[] { wide, portrait }); Pump();
                Check(window.Tiles.Count == 2 && window.SelectedTile == window.Tiles[1], "Test setup did not select the newest tile");
                Check(!window.ShowGuidesOn(window.Tiles[0]) && window.ShowGuidesOn(window.Tiles[1]),
                    "Unlinked perspective guides are not restricted to the selected tile");
                window.SetLink(true);
                Check(window.ShowGuidesOn(window.Tiles[0]) && window.ShowGuidesOn(window.Tiles[1]), "Linked guides do not cover every tile");
                window.SetLink(false); window.Select(window.Tiles[0]);
                Check(window.ShowGuidesOn(window.Tiles[0]) && !window.ShowGuidesOn(window.Tiles[1]), "Changing selection did not move the guides");
                window.SetLink(false); window.ClearImages();
            });
            await Case("perspective button opens a horizontal row under the toolbar", () => Sync(delegate
            {
                window.SetPerspective(false); window.SetIsometric(false); window.SetPitch(false); window.SetCube(false);
                Pump();
                var opener = Visuals<System.Windows.Controls.Button>(window)
                    .Single(b => Convert.ToString(b.Content).StartsWith("Perspective"));
                Check(opener.ContextMenu == null, "Perspective still uses a popup menu");
                Check(!Visuals<System.Windows.Controls.ScrollViewer>(window.PerspectiveBar).Any(), "Perspective row has a scrollbar");
                Check(!window.PerspectiveBar.IsVisible, "Perspective row is visible before opening");
                opener.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump();
                Check(window.PerspectiveBar.IsVisible && !window.PerspectiveEnabled && !window.IsometricEnabled && !window.PitchEnabled && !window.CubeEnabled,
                    "Opening the perspective row changed a mode");
                var checks = Visuals<System.Windows.Controls.CheckBox>(window.PerspectiveBar).ToArray();
                Func<string, System.Windows.Controls.CheckBox> cb = title => checks.Single(x => Convert.ToString(x.Content) == title);
                var vp = cb("Vanishing points"); var iso = cb("Isometric grid"); var pitch = cb("Pitch grid"); var cube = cb("Wireframe cube");
                var sliders = Visuals<System.Windows.Controls.Slider>(window.PerspectiveBar).ToArray();
                Check(sliders.Length >= 2, "Perspective row lacks both angle sliders");
                var isoSlider = sliders[0]; var pitchSlider = sliders[1];
                Check(isoSlider.Minimum == 5 && isoSlider.Maximum == 60 && isoSlider.TickFrequency == 1 && !isoSlider.IsSnapToTickEnabled &&
                    isoSlider.TickPlacement != System.Windows.Controls.Primitives.TickPlacement.None,
                    "Isometric slider does not follow the mouse (5-60 continuous range with visible notches)");
                Check(pitchSlider.Minimum == 15 && pitchSlider.Maximum == 90 && !pitchSlider.IsSnapToTickEnabled,
                    "Pitch slider must reach 90 degrees and follow the mouse");
                vp.IsChecked = true;
                Check(window.PerspectiveEnabled && vp.IsChecked == true, "Vanishing checkbox failed");
                window.SetPerspective(false); Check(vp.IsChecked == false, "Shortcut/model state did not synchronize VP checkbox");
                iso.IsChecked = true;
                Check(window.IsometricEnabled && iso.IsChecked == true && Math.Abs(window.IsometricAngleDegrees - 26.565) < 0.001, "Isometric grid checkbox failed");
                isoSlider.Value = 45;
                Check(window.IsometricEnabled && Math.Abs(window.IsometricAngleDegrees - 45) < 0.001, "Isometric slider is not continuous");
                isoSlider.Value = 27;
                Check(Math.Abs(window.IsometricAngleDegrees - 26.565) < 0.001, "Isometric slider did not grab the 2:1 preset");
                isoSlider.Value = 30;
                Check(window.IsometricEnabled && iso.IsChecked == true && Math.Abs(window.IsometricAngleDegrees - 30) < 0.001,
                    "Isometric slider did not move to the 30° preset");
                window.CycleIsometric(); Check(!window.IsometricEnabled && iso.IsChecked == false, "Isometric cycle did not leave 30° for Off");
                isoSlider.Value = 30;
                Check(window.IsometricEnabled && iso.IsChecked == true && Math.Abs(window.IsometricAngleDegrees - 30) < 0.001,
                    "Moving the isometric slider while off did not enable the grid at 30°");
                pitch.IsChecked = true;
                Check(window.PitchEnabled && pitch.IsChecked == true, "Pitch checkbox failed");
                pitchSlider.Value = 85;
                Check(Math.Abs(window.PitchAngleDegrees - 85) < 0.001, "Pitch slider did not adjust angle");
                pitchSlider.Value = 90;
                Check(Math.Abs(window.PitchAngleDegrees - 90) < 0.001, "Pitch slider cannot reach top-down 90°");
                cube.IsChecked = true;
                Check(window.CubeEnabled && cube.IsChecked == true, "Cube checkbox failed");
                var hide = Visuals<System.Windows.Controls.Button>(window.PerspectiveBar)
                    .Single(x => Convert.ToString(x.Content) == "Hide all overlays");
                hide.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(!window.PerspectiveEnabled && !window.IsometricEnabled && !window.PitchEnabled && !window.CubeEnabled &&
                    vp.IsChecked == false && iso.IsChecked == false && pitch.IsChecked == false && cube.IsChecked == false,
                    "Hide all overlays did not clear modes and checkboxes");
                opener.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump();
                Check(!window.PerspectiveBar.IsVisible, "Perspective row did not close");
            }));
            await Case("perspective row uses the dark canvas palette", () => Sync(delegate
            {
                var opener = Visuals<System.Windows.Controls.Button>(window)
                    .Single(b => Convert.ToString(b.Content).StartsWith("Perspective"));
                Check(opener.ContextMenu == null, "Perspective row regressed to a popup");
                var host = (System.Windows.Controls.Border)window.PerspectiveBar.Parent;
                var bg = host.Background as SolidColorBrush;
                Check(bg != null && bg.Color.R < 75 && bg.Color.G < 75 && bg.Color.B < 85,
                    "Perspective row is not dark (RGB=" + (bg == null ? "null" : bg.Color.ToString()) + ")");
            }));

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
