using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

[assembly: AssemblyTitle("Beholder")]
[assembly: AssemblyDescription("A private, lightweight image comparison canvas")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace Beholder
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application();
            var window = new BeholderWindow();
            if (args.Length > 0) window.Loaded += async delegate { await window.AddFilesAsync(args); };
            app.Run(window);
        }
    }

    public sealed class BeholderWindow : Window
    {
        public readonly List<ImageTile> Tiles = new List<ImageTile>();
        public readonly Canvas Workspace = new Canvas();
        public bool EqualTiles { get; private set; }
        public bool LinkViews { get; private set; }
        public bool Monochrome { get; private set; }
        public string LastMessage { get; private set; }
        public ImageTile FocusedTile { get; private set; }
        private readonly Dictionary<string, int> pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly TextBlock status = new TextBlock();
        private readonly TextBlock count = new TextBlock();
        private readonly Border canvasBorder = new Border();
        private readonly Grid canvasHost = new Grid();
        private readonly StackPanel empty = new StackPanel();
        private readonly Button layoutButton;
        private readonly Button linkButton;
        private readonly Button backgroundButton;
        private readonly Button monochromeButton;
        private ImageTile selected;
        private int importGeneration;
        private int clipboardNumber;
        private int backgroundIndex;
        private bool fullscreen;
        private WindowState oldWindowState;
        private readonly Brush[] backgrounds = { Paint("#14171C"), Paint("#797F86"), Paint("#F3F2EF") };
        public static readonly Brush Text = Paint("#E7EBF0");
        public static readonly Brush Muted = Paint("#939EAD");
        public static readonly Brush Accent = Paint("#8FB4EF");
        private const string TileFormat = "Beholder.ImageTile";

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public BeholderWindow()
        {
            Title = "Beholder";
            Width = 1280; Height = 820; MinWidth = 760; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Paint("#1A1E24"); Foreground = Text;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            AllowDrop = true;
            var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Beholder.Icon.ico");
            if (icon != null) { using (icon) { Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); } }
            SourceInitialized += delegate
            {
                int enabled = 1;
                // Unsupported OS versions safely return a nonzero HRESULT; nothing to mutate.
                DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref enabled, 4);
            };
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            Content = root;
            var bar = new DockPanel { LastChildFill = false, Margin = new Thickness(18, 10, 14, 10) };
            Grid.SetRow(bar, 0); root.Children.Add(bar);
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(Eye(28, Accent));
            brand.Children.Add(new TextBlock { Text = "Beholder", FontWeight = FontWeights.SemiBold, FontSize = 19, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(brand, Dock.Left); bar.Children.Add(brand);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(actions, Dock.Right); bar.Children.Add(actions);
            actions.Children.Add(ActionButton("+ Add images", "Choose one or more images (Ctrl+O)", async delegate { await PickFilesAsync(); }, true));
            layoutButton = ActionButton("Auto tiles", "Switch between aspect-aware tiles and equal-size comparison cells", delegate { SetEqual(!EqualTiles); });
            actions.Children.Add(layoutButton);
            actions.Children.Add(ActionButton("Fit all", "Reset zoom and pan (Ctrl+0)", delegate { ResetAll(); }));
            linkButton = ActionButton("Link views", "Zoom and pan all images together (Ctrl+L)", delegate { SetLink(!LinkViews); });
            actions.Children.Add(linkButton);
            monochromeButton = ActionButton("B/W", "Toggle black and white for every image (Ctrl+B)", async delegate { await SetMonochromeAsync(!Monochrome); });
            actions.Children.Add(monochromeButton);
            backgroundButton = ActionButton("Dark", "Cycle the canvas: dark, grey, light", delegate { CycleBackground(); });
            actions.Children.Add(backgroundButton);
            actions.Children.Add(ActionButton("Clear", "Remove all tiles, never delete original files (Ctrl+Shift+X)", delegate { ClearImages(); }));
            canvasBorder.Margin = new Thickness(12, 0, 12, 0);
            canvasBorder.Background = backgrounds[0];
            canvasBorder.BorderBrush = Paint("#303740"); canvasBorder.BorderThickness = new Thickness(1); canvasBorder.CornerRadius = new CornerRadius(10);
            Grid.SetRow(canvasBorder, 1); root.Children.Add(canvasBorder);
            canvasBorder.Child = canvasHost;
            Workspace.Margin = new Thickness(10);
            Workspace.Background = Brushes.Transparent;
            Workspace.ClipToBounds = true;
            canvasHost.Children.Add(Workspace);
            empty.VerticalAlignment = VerticalAlignment.Center; empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(Eye(76, Paint("#58677A")));
            empty.Children.Add(new TextBlock { Text = "Drop images here", Foreground = Text, FontSize = 25, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 10) });
            empty.Children.Add(new TextBlock { Text = "A canvas that makes room for every image.", Foreground = Muted, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
            var choose = ActionButton("Choose images", "Browse files (Ctrl+O)", async delegate { await PickFilesAsync(); }, true);
            choose.HorizontalAlignment = HorizontalAlignment.Center; choose.Margin = new Thickness(0, 22, 0, 16);
            empty.Children.Add(choose);
            empty.Children.Add(new TextBlock { Text = "PNG  /  JPEG  /  WEBP  /  GIF  /  BMP  /  TIFF", Foreground = Paint("#667487"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            canvasHost.Children.Add(empty);
            var bottom = new DockPanel { Margin = new Thickness(20, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(bottom, 2); root.Children.Add(bottom);
            count.Foreground = Muted; count.FontSize = 11; DockPanel.SetDock(count, Dock.Right); bottom.Children.Add(count);
            status.Foreground = Muted; status.FontSize = 11; status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.ToolTip = "Wheel: zoom  |  Drag image: pan  |  Drag filename: reorder  |  Double-click: focus  |  Delete: remove selected  |  F11: fullscreen  |  Ctrl+V: paste";
            bottom.Children.Add(status);
            SetStatus("Drop anywhere  /  Wheel to zoom  /  Drag to pan  /  F11 fullscreen");
            UpdateCount();
            Workspace.SizeChanged += delegate { Reflow(); };
            PreviewDragOver += OnDragOver;
            PreviewDrop += OnDrop;
            DragLeave += delegate { canvasBorder.BorderBrush = Paint("#303740"); };
            PreviewKeyDown += OnKey;
            Closed += delegate { importGeneration++; Tiles.Clear(); pending.Clear(); Workspace.Children.Clear(); };
        }

        public static Brush Paint(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b;
        }

        private static FrameworkElement Eye(double width, Brush brush)
        {
            var view = new Viewbox { Width = width, Height = width * 0.62, HorizontalAlignment = HorizontalAlignment.Center };
            var canvas = new Canvas { Width = 100, Height = 62 };
            var outline = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 5,31 C 25,0 75,0 95,31 C 75,62 25,62 5,31 Z"), Stroke = brush, StrokeThickness = 3, Fill = Brushes.Transparent };
            canvas.Children.Add(outline);
            var pupil = new Ellipse { Width = 26, Height = 26, Stroke = brush, StrokeThickness = 3 };
            Canvas.SetLeft(pupil, 37); Canvas.SetTop(pupil, 18); canvas.Children.Add(pupil);
            view.Child = canvas; return view;
        }

        public static Button ActionButton(string text, string hint, Action action, bool primary = false)
        {
            var b = new Button { Content = text, ToolTip = hint, Height = 38, MinWidth = 50, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(5, 0, 0, 0), Foreground = primary ? Paint("#142034") : Text, Background = primary ? Accent : Paint("#292F38"), BorderBrush = primary ? Accent : Paint("#3D4653"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FontSize = 12 };
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.AppendChild(content); template.VisualTree = border;
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Button.BorderBrushProperty, Accent)); template.Triggers.Add(hover);
            var press = new Trigger { Property = Button.IsPressedProperty, Value = true }; press.Setters.Add(new Setter(Button.OpacityProperty, 0.72)); template.Triggers.Add(press);
            b.Template = template;
            b.Click += delegate { action(); };
            System.Windows.Automation.AutomationProperties.SetName(b, text);
            return b;
        }

        private async Task PickFilesAsync()
        {
            var dialog = new OpenFileDialog { Title = "Add images to Beholder", Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.avif;*.ico|All files|*.*" };
            if (dialog.ShowDialog(this) == true) await AddFilesAsync(dialog.FileNames);
        }

        public async Task AddFilesAsync(IEnumerable<string> paths)
        {
            int generation = importGeneration;
            int added = 0, duplicate = 0;
            var errors = new List<string>();
            foreach (string original in paths)
            {
                if (generation != importGeneration) return;
                if (Directory.Exists(original))
                {
                    try { await AddFilesAsync(Directory.EnumerateFiles(original).Where(IsImageExtension).Take(65).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)); }
                    catch (IOException ex) { errors.Add("Folder: " + ex.Message); }
                    catch (UnauthorizedAccessException ex) { errors.Add("Folder: " + ex.Message); }
                    continue;
                }
                string path;
                try { path = System.IO.Path.GetFullPath(original); }
                catch (Exception ex) { errors.Add(System.IO.Path.GetFileName(original) + ": " + ex.Message); continue; }
                if (pending.ContainsKey(path) || Tiles.Any(t => string.Equals(t.Item.Path, path, StringComparison.OrdinalIgnoreCase))) { duplicate++; continue; }
                if (Tiles.Count + pending.Count >= 64) { errors.Add("Canvas limit: 64 images. Remove some tiles to add more."); break; }
                pending.Add(path, generation);
                SetStatus("Loading " + System.IO.Path.GetFileName(path) + "...");
                try
                {
                    var loaded = await Task.Run(() => ImageLoader.Load(path));
                    if (generation != importGeneration) return;
                    AddImage(loaded); added++;
                }
                catch (Exception ex) { if (generation == importGeneration) errors.Add(System.IO.Path.GetFileName(path) + ": " + ex.Message); }
                finally { int current; if (pending.TryGetValue(path, out current) && current == generation) pending.Remove(path); }
            }
            if (generation != importGeneration) return;
            if (errors.Count > 0) SetStatus("Added " + added + "; skipped " + errors.Count + ". " + errors[0], true);
            else if (added > 0) SetStatus("Added " + added + " image" + (added == 1 ? "" : "s") + ". Originals stay untouched.");
            else if (duplicate > 0) SetStatus("Already on the canvas. No duplicate tiles added.");
        }

        private static bool IsImageExtension(string path)
        {
            return new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".avif", ".ico" }.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
        }

        public void AddImage(LoadedImage item)
        {
            if (Tiles.Count >= 64) { SetStatus("Canvas limit: 64 images.", true); return; }
            var tile = new ImageTile(item, this);
            Tiles.Add(tile); Workspace.Children.Add(tile);
            FocusedTile = null; Select(tile); UpdateCount(); Reflow();
            ApplyCurrentFilter(tile);
        }

        public void Select(ImageTile tile)
        {
            selected = tile;
            foreach (var t in Tiles) t.SetSelected(t == selected);
        }

        public void Remove(ImageTile tile)
        {
            if (FocusedTile == tile) FocusedTile = null;
            Tiles.Remove(tile); Workspace.Children.Remove(tile);
            if (selected == tile) Select(Tiles.LastOrDefault());
            UpdateCount(); Reflow();
        }

        public void ClearImages()
        {
            importGeneration++; Tiles.Clear(); Workspace.Children.Clear(); pending.Clear(); selected = null; FocusedTile = null;
            UpdateCount(); Reflow(); SetStatus("Canvas cleared. Original files were not changed.");
        }

        public void SetEqual(bool equal)
        {
            EqualTiles = equal; layoutButton.Content = equal ? "Equal tiles" : "Auto tiles"; Reflow();
        }

        public void SetLink(bool enabled)
        {
            LinkViews = enabled; linkButton.Content = enabled ? "Linked" : "Link views"; linkButton.BorderBrush = enabled ? Accent : Paint("#3D4653");
            if (enabled && selected != null) ViewChanged(selected);
        }

        private async void ApplyCurrentFilter(ImageTile tile)
        {
            if (Monochrome) await tile.ApplyFilterAsync();
        }

        public async Task SetMonochromeAsync(bool enabled)
        {
            Monochrome = enabled;
            monochromeButton.Content = enabled ? "B/W on" : "B/W";
            monochromeButton.BorderBrush = enabled ? Accent : Paint("#3D4653");
            var snapshot = Tiles.ToArray();
            if (!enabled) { foreach (var tile in snapshot) tile.ShowOriginal(); return; }
            foreach (var tile in snapshot)
            {
                if (!Monochrome) return;
                await tile.ApplyFilterAsync();
            }
        }

        public void ResetAll()
        {
            foreach (var t in Tiles) t.SetView(1, 0, 0, false);
            SetStatus("All images fit their tiles.");
        }

        public void ToggleFocus(ImageTile tile)
        {
            FocusedTile = FocusedTile == tile ? null : tile; Reflow();
            SetStatus(FocusedTile == null ? "Back to the canvas." : "Focused image. Double-click or Esc to return to the grid.");
        }

        public void MoveTile(ImageTile source, ImageTile target)
        {
            int from = Tiles.IndexOf(source), to = Tiles.IndexOf(target);
            if (from < 0 || to < 0 || from == to) return;
            Tiles.RemoveAt(from); Tiles.Insert(to, source); Reflow();
        }

        public void ViewChanged(ImageTile source)
        {
            if (!LinkViews) return;
            Vector normalized = source.NormalizedPan;
            foreach (var t in Tiles) if (t != source) t.SetNormalizedView(source.Zoom, normalized);
        }

        private void UpdateCount()
        {
            empty.Visibility = Tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            count.Text = Tiles.Count == 0 ? "Local only. No uploads." : Tiles.Count + " image" + (Tiles.Count == 1 ? "" : "s") + "  /  local only";
        }

        private void CycleBackground()
        {
            backgroundIndex = (backgroundIndex + 1) % backgrounds.Length;
            canvasBorder.Background = backgrounds[backgroundIndex];
            backgroundButton.Content = new[] { "Dark", "Grey", "Light" }[backgroundIndex];
            foreach (var t in Tiles) t.SetBackground(backgrounds[backgroundIndex]);
            foreach (var label in empty.Children.OfType<TextBlock>()) label.Foreground = backgroundIndex == 0 ? (label.FontSize >= 20 ? Text : Muted) : Paint("#233144");
        }

        public Brush CanvasBackground { get { return backgrounds[backgroundIndex]; } }

        public void Reflow()
        {
            double width = Workspace.ActualWidth, height = Workspace.ActualHeight;
            if (width <= 0 || height <= 0) return;
            var rects = TileLayout.Arrange(width, height, Tiles.Select(t => t.Item.Aspect).ToList(), EqualTiles);
            for (int i = 0; i < Tiles.Count; i++)
            {
                var tile = Tiles[i];
                tile.Visibility = FocusedTile == null || FocusedTile == tile ? Visibility.Visible : Visibility.Collapsed;
                Rect rect = FocusedTile == tile ? new Rect(0, 0, width, height) : rects[i];
                Canvas.SetLeft(tile, rect.X); Canvas.SetTop(tile, rect.Y); tile.Width = rect.Width; tile.Height = rect.Height;
            }
        }

        private void SetStatus(string text, bool error = false)
        {
            LastMessage = text; status.Text = text; status.ToolTip = text; status.Foreground = error ? Paint("#FFC39E") : Muted;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            bool supported = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(TileFormat);
            e.Effects = supported ? (e.Data.GetDataPresent(TileFormat) ? DragDropEffects.Move : DragDropEffects.Copy) : DragDropEffects.None;
            canvasBorder.BorderBrush = supported ? Accent : Paint("#303740");
            e.Handled = true;
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            canvasBorder.BorderBrush = Paint("#303740"); e.Handled = true;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) await AddFilesAsync(files);
            }
            else if (e.Data.GetDataPresent(TileFormat))
            {
                var source = e.Data.GetData(TileFormat) as ImageTile;
                DependencyObject node = e.OriginalSource as DependencyObject;
                while (node != null && !(node is ImageTile)) node = VisualTreeHelper.GetParent(node);
                if (source != null && node is ImageTile) MoveTile(source, (ImageTile)node);
            }
        }

        public void BeginTileDrag(ImageTile tile)
        {
            DragDrop.DoDragDrop(tile, new DataObject(TileFormat, tile), DragDropEffects.Move);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.O) { OpenFromKey(); e.Handled = true; }
            else if (ctrl && e.Key == Key.V) { Paste(); e.Handled = true; }
            else if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0)) { ResetAll(); e.Handled = true; }
            else if (ctrl && e.Key == Key.B) { ToggleMonochromeFromKey(); e.Handled = true; }
            else if (ctrl && e.Key == Key.L) { SetLink(!LinkViews); e.Handled = true; }
            else if (ctrl && e.Key == Key.X && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) { ClearImages(); e.Handled = true; }
            else if (e.Key == Key.Delete && selected != null) { Remove(selected); e.Handled = true; }
            else if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
            else if (e.Key == Key.Escape) { if (FocusedTile != null) { FocusedTile = null; Reflow(); } else if (fullscreen) ToggleFullscreen(); e.Handled = true; }
        }

        private async void ToggleMonochromeFromKey() { await SetMonochromeAsync(!Monochrome); }

        private async void OpenFromKey() { await PickFilesAsync(); }

        private async void Paste()
        {
            try
            {
                if (Clipboard.ContainsFileDropList()) await AddFilesAsync(Clipboard.GetFileDropList().Cast<string>());
                else if (Clipboard.ContainsImage()) { AddImage(ImageLoader.FromClipboard(Clipboard.GetImage(), ++clipboardNumber)); SetStatus("Pasted image. Clipboard pixels stay local."); }
                else SetStatus("Clipboard contains no image or image files.");
            }
            catch (ExternalException) { SetStatus("Clipboard is busy. Try again.", true); }
        }

        private void ToggleFullscreen()
        {
            if (!fullscreen) { oldWindowState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; fullscreen = true; }
            else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = oldWindowState; fullscreen = false; }
        }
    }

    public sealed class ImageTile : Border
    {
        public readonly LoadedImage Item;
        public readonly Grid Viewport = new Grid();
        public double Zoom { get; private set; }
        public double PanX { get; private set; }
        public double PanY { get; private set; }
        private readonly Image image = new Image();
        private readonly ScaleTransform scale = new ScaleTransform(1, 1);
        private readonly TranslateTransform translate = new TranslateTransform();
        private readonly TextBlock zoomText = new TextBlock();
        private readonly BeholderWindow owner;
        private bool panning;
        private Point dragStart;
        private Vector initialPan;
        private Point headerStart;
        private bool headerPressed;

        public ImageTile(LoadedImage item, BeholderWindow window)
        {
            Item = item; owner = window; Zoom = 1;
            BorderBrush = BeholderWindow.Paint("#35404E"); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(7);
            Background = window.CanvasBackground; ClipToBounds = true;
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); Child = grid;
            Viewport.Background = Checker(); Viewport.ClipToBounds = true; Viewport.Cursor = Cursors.Hand;
            grid.Children.Add(Viewport);
            image.Source = item.Bitmap; image.Stretch = Stretch.Uniform; image.Margin = new Thickness(6);
            image.RenderTransformOrigin = new Point(0.5, 0.5);
            var transforms = new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(translate); image.RenderTransform = transforms;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality); Viewport.Children.Add(image);
            System.Windows.Automation.AutomationProperties.SetName(image, item.Name);
            var header = new DockPanel { Background = BeholderWindow.Paint("#242A33"), LastChildFill = true, Cursor = Cursors.SizeAll };
            Grid.SetRow(header, 1); grid.Children.Add(header);
            var remove = BeholderWindow.ActionButton("x", "Remove this image (original is untouched)", delegate { owner.Remove(this); });
            remove.Width = 30; remove.MinWidth = 30; remove.Height = 28; remove.Padding = new Thickness(0); remove.Margin = new Thickness(0, 0, 2, 0); remove.Background = Brushes.Transparent; remove.BorderBrush = Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetName(remove, "Remove " + item.Name);
            DockPanel.SetDock(remove, Dock.Right); header.Children.Add(remove);
            zoomText.Foreground = BeholderWindow.Muted; zoomText.FontSize = 10; zoomText.VerticalAlignment = VerticalAlignment.Center; zoomText.Margin = new Thickness(6, 0, 8, 0); zoomText.Text = "Fit";
            DockPanel.SetDock(zoomText, Dock.Right); header.Children.Add(zoomText);
            var name = new TextBlock { Text = item.Name, Foreground = BeholderWindow.Text, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Name + "\n" + item.Width + " x " + item.Height + " px\nDrag this label to reorder" };
            header.Children.Add(name);
            Viewport.MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                owner.Select(this); Point at = e.GetPosition(Viewport);
                ZoomAt(Zoom * Math.Pow(1.15, e.Delta / 120.0), at); e.Handled = true;
            };
            Viewport.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                owner.Select(this);
                if (e.ClickCount == 2) { owner.ToggleFocus(this); e.Handled = true; return; }
                panning = true; dragStart = e.GetPosition(Viewport); initialPan = new Vector(PanX, PanY); Viewport.CaptureMouse(); Viewport.Cursor = Cursors.ScrollAll; e.Handled = true;
            };
            Viewport.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!panning || e.LeftButton != MouseButtonState.Pressed) return;
                Vector delta = e.GetPosition(Viewport) - dragStart;
                SetView(Zoom, initialPan.X + delta.X, initialPan.Y + delta.Y, true);
            };
            Viewport.MouseLeftButtonUp += delegate { panning = false; Viewport.ReleaseMouseCapture(); Viewport.Cursor = Cursors.Hand; };
            Viewport.LostMouseCapture += delegate { panning = false; Viewport.Cursor = Cursors.Hand; };
            Viewport.SizeChanged += delegate { SetView(Zoom, PanX, PanY, false); };
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { owner.Select(this); headerStart = e.GetPosition(header); headerPressed = true; };
            header.MouseLeftButtonUp += delegate { headerPressed = false; };
            header.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!headerPressed || e.LeftButton != MouseButtonState.Pressed) return;
                Point point = e.GetPosition(header);
                if (Math.Abs(point.X - headerStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(point.Y - headerStart.Y) > SystemParameters.MinimumVerticalDragDistance) { headerPressed = false; owner.BeginTileDrag(this); }
            };
        }

        private static Brush Checker()
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
            var alternating = new GeometryGroup(); alternating.Children.Add(new RectangleGeometry(new Rect(0, 0, 10, 10))); alternating.Children.Add(new RectangleGeometry(new Rect(10, 10, 10, 10)));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(12, 0, 0, 0)), null, alternating));
            var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 20, 20), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            brush.Freeze(); return brush;
        }

        public BitmapSource DisplayBitmap { get { return image.Source as BitmapSource; } }
        public void ShowOriginal() { image.Source = Item.Bitmap; }
        public async Task ApplyFilterAsync()
        {
            if (!owner.Monochrome) { ShowOriginal(); return; }
            BitmapSource grey = await Task.Run(() => ImageLoader.Greyscale(Item.Bitmap));
            if (owner.Monochrome && owner.Tiles.Contains(this)) image.Source = grey;
        }

        public void SetBackground(Brush brush) { Background = brush; }
        public void SetSelected(bool value) { BorderBrush = value ? BeholderWindow.Accent : BeholderWindow.Paint("#35404E"); }

        private Size FitSize
        {
            get
            {
                double width = Math.Max(1, Viewport.ActualWidth - 12), height = Math.Max(1, Viewport.ActualHeight - 12);
                double fit = Math.Min(width / Item.Width, height / Item.Height);
                return new Size(Item.Width * fit, Item.Height * fit);
            }
        }

        public Vector NormalizedPan { get { Size fit = FitSize; return new Vector(PanX / fit.Width, PanY / fit.Height); } }
        public void SetNormalizedView(double zoom, Vector pan) { Size fit = FitSize; SetView(zoom, pan.X * fit.Width, pan.Y * fit.Height, false); }

        public void ZoomAt(double zoom, Point pointer)
        {
            double next = Math.Max(1, Math.Min(32, zoom));
            double ratio = next / Zoom;
            double x = pointer.X - Viewport.ActualWidth / 2, y = pointer.Y - Viewport.ActualHeight / 2;
            SetView(next, x - (x - PanX) * ratio, y - (y - PanY) * ratio, true);
        }

        public void SetView(double zoom, double panX, double panY, bool notify)
        {
            Zoom = Math.Max(1, Math.Min(32, zoom));
            Size fit = FitSize;
            double maxX = Math.Max(0, (fit.Width * Zoom - Viewport.ActualWidth) / 2 + 6);
            double maxY = Math.Max(0, (fit.Height * Zoom - Viewport.ActualHeight) / 2 + 6);
            PanX = Math.Max(-maxX, Math.Min(maxX, panX)); PanY = Math.Max(-maxY, Math.Min(maxY, panY));
            scale.ScaleX = Zoom; scale.ScaleY = Zoom; translate.X = PanX; translate.Y = PanY;
            zoomText.Text = Zoom <= 1.001 ? "Fit" : Zoom.ToString("0.0") + "x";
            if (notify) owner.ViewChanged(this);
        }
    }
}
