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

[assembly: AssemblyTitle("Beholder v3")]
[assembly: AssemblyDescription("A private, lightweight image comparison canvas")]
[assembly: AssemblyVersion("3.0.0.0")]
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
        public bool PerspectiveEnabled { get; private set; }
        public bool IsometricEnabled { get; private set; }
        public double IsometricAngleDegrees { get; private set; }
        public bool PitchEnabled { get; private set; }
        public double PitchAngleDegrees { get; private set; }
        public bool CubeEnabled { get; private set; }
        public string CubeProjection { get { return PerspectiveEnabled ? "Vanishing" : PitchEnabled ? "Pitch" : IsometricEnabled ? "Isometric" : ""; } }
        public readonly List<CubeInstance> Cubes = new List<CubeInstance>(new[] { new CubeInstance() });
        public int SelectedCubeIndex { get; private set; }
        public ImageTile SelectedTile { get { return selected; } }
        public bool ShowGuidesOn(ImageTile tile) { return LinkViews || tile == selected; }
        public string LastMessage { get; private set; }
        public ImageTile FocusedTile { get; private set; }
        private readonly Dictionary<string, int> pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly TextBlock errorText = new TextBlock();
        private readonly Border errorNotice = new Border();
        private readonly System.Windows.Threading.DispatcherTimer noticeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        private readonly Border canvasBorder = new Border();
        private readonly Grid canvasHost = new Grid();
        private readonly StackPanel empty = new StackPanel();
        private readonly Button layoutButton;
        private readonly Button linkButton;
        private readonly Button monochromeButton;
        private readonly Button perspectiveButton;
        private readonly Border perspectiveBarHost = new Border();
        public readonly WrapPanel PerspectiveBar = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly System.Windows.Controls.CheckBox vanishingCheck = new System.Windows.Controls.CheckBox { Content = "Vanishing points" };
        private readonly System.Windows.Controls.CheckBox isoCheck = new System.Windows.Controls.CheckBox { Content = "Isometric grid" };
        private readonly System.Windows.Controls.CheckBox pitchCheck = new System.Windows.Controls.CheckBox { Content = "Pitch grid" };
        private readonly System.Windows.Controls.CheckBox cubeCheck = new System.Windows.Controls.CheckBox { Content = "Wireframe cube" };
        private readonly Slider isoSlider = new Slider { Minimum = 5, Maximum = 60, TickFrequency = 1, TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight, IsSnapToTickEnabled = false, SmallChange = 1, LargeChange = 1, Width = 150 };
        private readonly TextBlock isoAngleLabel = new TextBlock { Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        private readonly Slider pitchSlider = new Slider { Minimum = 15, Maximum = 90, TickFrequency = 5, IsSnapToTickEnabled = false, SmallChange = 1, LargeChange = 5, Width = 150 };
        private readonly TextBlock pitchAngleLabel = new TextBlock { Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        private bool syncingIsoSlider;
        private bool syncingPitchSlider;
        private int angleIndex = 0;
        private static readonly double[] AnglePresets = new[] { 26.565, 30 };
        private ImageTile selected;
        private int importGeneration;
        private int clipboardNumber;
        private bool fullscreen;
        private WindowState oldWindowState;
        private readonly Brush canvasBackground = Paint("#14171C");
        public static readonly Brush Text = Paint("#E7EBF0");
        public static readonly Brush Muted = Paint("#939EAD");
        public static readonly Brush Accent = Paint("#8FB4EF");
        private const string TileFormat = "Beholder.ImageTile";

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public BeholderWindow()
        {
            Title = "Beholder v3";
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
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            Content = root;
            var bar = new Grid { Margin = new Thickness(18, 10, 14, 10), MinHeight = 44 };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(bar, 0); root.Children.Add(bar);
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(Eye(28, Accent));
            brand.Children.Add(new TextBlock { Text = "Beholder v3", FontWeight = FontWeights.SemiBold, FontSize = 19, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(brand, 0); bar.Children.Add(brand);
            var actions = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(actions, 1); bar.Children.Add(actions);
            actions.Children.Add(ActionButton("+ Add images", "Choose one or more images (Ctrl+O)", async delegate { await PickFilesAsync(); }, true));
            actions.Children.Add(ActionButton("Save JPG", "Save every image as one composite JPEG (Ctrl+S)", delegate { PickSave(); }));
            layoutButton = ActionButton("Auto tiles", "Switch between aspect-aware tiles and equal-size comparison cells", delegate { SetEqual(!EqualTiles); });
            actions.Children.Add(layoutButton);
            actions.Children.Add(ActionButton("Fit all", "Reset zoom and pan (Ctrl+0)", delegate { ResetAll(); }));
            linkButton = ActionButton("Link views", "Link zoom, pan, and perspective points. Enabling copies the selected image's points to every image (Ctrl+L)", delegate { SetLink(!LinkViews); });
            actions.Children.Add(linkButton);
            monochromeButton = ActionButton("B/W", "Toggle black and white for every image (Ctrl+B)", async delegate { await SetMonochromeAsync(!Monochrome); });
            actions.Children.Add(monochromeButton);
            perspectiveButton = ActionButton("Perspective ▾", "Choose vanishing, isometric, pitch-grid, and cube overlays. The row below appears while open (Ctrl+P: vanishing; Ctrl+I: isometric).", delegate
            {
                perspectiveBarHost.Visibility = perspectiveBarHost.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (perspectiveBarHost.Visibility == Visibility.Visible) SyncPerspectiveControls();
            });
            System.Windows.Automation.AutomationProperties.SetName(perspectiveButton, "Perspective options");
            actions.Children.Add(perspectiveButton);
            IsometricAngleDegrees = 26.565; PitchAngleDegrees = 35;
            perspectiveBarHost.Background = Paint("#1A1E24");
            perspectiveBarHost.BorderBrush = Paint("#3D4653");
            perspectiveBarHost.BorderThickness = new Thickness(1);
            perspectiveBarHost.CornerRadius = new CornerRadius(8);
            perspectiveBarHost.Margin = new Thickness(12, 0, 12, 8);
            perspectiveBarHost.Padding = new Thickness(10, 5, 10, 5);
            perspectiveBarHost.Child = PerspectiveBar;
            perspectiveBarHost.Visibility = Visibility.Collapsed;
            Grid.SetRow(perspectiveBarHost, 1); root.Children.Add(perspectiveBarHost);
            Func<System.Windows.Controls.CheckBox, string, string, System.Windows.Controls.CheckBox> barCheck = (box, hint, gesture) =>
            {
                box.Foreground = Text; box.FontSize = 12; box.Margin = new Thickness(6, 0, 16, 0);
                box.VerticalAlignment = VerticalAlignment.Center; box.ToolTip = hint + (string.IsNullOrEmpty(gesture) ? "" : " (" + gesture + ")");
                return box;
            };
            PerspectiveBar.Children.Add(barCheck(vanishingCheck, "Toggle vanishing-point construction guides", "Ctrl+P"));
            PerspectiveBar.Children.Add(barCheck(isoCheck, "Toggle the isometric grid", "Ctrl+I"));
            var isoStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            isoStack.Children.Add(isoAngleLabel); isoStack.Children.Add(isoSlider);
            PerspectiveBar.Children.Add(isoStack);
            PerspectiveBar.Children.Add(barCheck(pitchCheck, "Toggle the projective pitch grid", null));
            var pitchStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            pitchStack.Children.Add(pitchAngleLabel); pitchStack.Children.Add(pitchSlider);
            PerspectiveBar.Children.Add(pitchStack);
            PerspectiveBar.Children.Add(barCheck(cubeCheck, "Show a transparent wireframe cube that follows the active perspective", null));
            var addCube = ActionButton("+ Cube", "Add another wireframe cube. Drag its axis handles (red X, teal Y, blue Z) to move it and the white diamond to resize; click a cube to select it.", delegate { AddCube(); });
            addCube.Height = 32; addCube.Margin = new Thickness(6, 0, 0, 0);
            PerspectiveBar.Children.Add(addCube);
            var hideOverlays = ActionButton("Hide all overlays", "Turn every perspective overlay off without closing the bar", delegate
            {
                SetCube(false); SetPitch(false); SetIsometric(false); SetPerspective(false);
            });
            hideOverlays.Height = 32; hideOverlays.Margin = new Thickness(6, 0, 0, 0);
            PerspectiveBar.Children.Add(hideOverlays);
            vanishingCheck.Checked += delegate { SetPerspective(true); };
            vanishingCheck.Unchecked += delegate { SetPerspective(false); };
            isoCheck.Checked += delegate { SetIsometric(true); };
            isoCheck.Unchecked += delegate { SetIsometric(false); };
            pitchCheck.Checked += delegate { SetPitch(true); };
            pitchCheck.Unchecked += delegate { SetPitch(false); };
            cubeCheck.Checked += delegate { SetCube(true); };
            cubeCheck.Unchecked += delegate { SetCube(false); };
            isoSlider.ValueChanged += delegate
            {
                if (syncingIsoSlider) return;
                double value = isoSlider.Value, target = Math.Round(value);
                if (Math.Abs(value - 26.565) < 1.2) target = 26.565;
                else if (Math.Abs(value - 30) < 1.2) target = 30;
                SetIsometric(true);
                SetIsometricAngle(target);
            };
            pitchSlider.ValueChanged += delegate { if (syncingPitchSlider) return; SetPitchAngle(pitchSlider.Value); };
            SyncPerspectiveControls();
            actions.Children.Add(ActionButton("Clear", "Remove all tiles, never delete original files (Ctrl+Shift+X)", delegate { ClearImages(); }));
            canvasBorder.Margin = new Thickness(12, 0, 12, 8);
            canvasBorder.Background = canvasBackground;
            canvasBorder.BorderBrush = Paint("#303740"); canvasBorder.BorderThickness = new Thickness(1); canvasBorder.CornerRadius = new CornerRadius(10);
            Grid.SetRow(canvasBorder, 2); root.Children.Add(canvasBorder);
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
            // Errors float above the canvas only when needed, never reserve image space.
            errorNotice.Background = Paint("#302725"); errorNotice.BorderBrush = Paint("#725444"); errorNotice.BorderThickness = new Thickness(1);
            errorNotice.CornerRadius = new CornerRadius(6); errorNotice.Padding = new Thickness(12, 8, 12, 8); errorNotice.Margin = new Thickness(16);
            errorNotice.HorizontalAlignment = HorizontalAlignment.Right; errorNotice.VerticalAlignment = VerticalAlignment.Top;
            errorNotice.MaxWidth = 480; errorNotice.MaxHeight = 120; errorNotice.ClipToBounds = true; errorNotice.Visibility = Visibility.Collapsed;
            errorText.Foreground = Paint("#FFC39E"); errorText.FontSize = 12; errorText.TextWrapping = TextWrapping.Wrap;
            errorNotice.Child = errorText; canvasHost.Children.Add(errorNotice);
            errorNotice.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { errorNotice.Visibility = Visibility.Collapsed; noticeTimer.Stop(); e.Handled = true; };
            noticeTimer.Tick += delegate { errorNotice.Visibility = Visibility.Collapsed; noticeTimer.Stop(); };
            UpdateEmptyState();
            Workspace.SizeChanged += delegate { Reflow(); };
            PreviewDragOver += OnDragOver;
            PreviewDrop += OnDrop;
            DragLeave += delegate { canvasBorder.BorderBrush = Paint("#303740"); };
            PreviewKeyDown += OnKey;
            Closed += delegate { importGeneration++; noticeTimer.Stop(); Tiles.Clear(); pending.Clear(); Workspace.Children.Clear(); };
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
            var b = new Button { Content = text, ToolTip = hint, Height = 38, MinWidth = 50, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(5, 2, 0, 2), Foreground = primary ? Paint("#142034") : Text, Background = primary ? Accent : Paint("#292F38"), BorderBrush = primary ? Accent : Paint("#3D4653"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FontSize = 12 };
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

        public void SaveComposite(string path) { CompositeExporter.Save(this, path); }

        private void PickSave()
        {
            if (Tiles.Count == 0) { SetStatus("Add images before saving a composite.", true); return; }
            var dialog = new SaveFileDialog { Title = "Save composite JPEG", Filter = "JPEG image|*.jpg;*.jpeg", DefaultExt = ".jpg", AddExtension = true, FileName = "Beholder-composite.jpg", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            try { SaveComposite(dialog.FileName); SetStatus("Saved " + System.IO.Path.GetFileName(dialog.FileName) + ". Originals stay untouched."); }
            catch (Exception ex) { SetStatus("Could not save: " + ex.Message, true); }
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
            var linkSource = selected ?? Tiles.FirstOrDefault();
            if (LinkViews && linkSource != null) tile.ReplacePerspectivePoints(linkSource.PerspectivePoints);
            Tiles.Add(tile); Workspace.Children.Add(tile);
            FocusedTile = null; Select(tile); UpdateEmptyState(); Reflow();
            ApplyCurrentFilter(tile);
            if (LinkViews && linkSource != null) tile.SetNormalizedView(linkSource.Zoom, linkSource.NormalizedPan);
        }

        public void Select(ImageTile tile)
        {
            selected = tile;
            foreach (var t in Tiles) { t.SetSelected(t == selected); t.RefreshPerspective(); }
        }

        public void Remove(ImageTile tile)
        {
            if (FocusedTile == tile) FocusedTile = null;
            Tiles.Remove(tile); Workspace.Children.Remove(tile);
            if (selected == tile) Select(Tiles.LastOrDefault());
            UpdateEmptyState(); Reflow();
        }

        public void ClearImages()
        {
            importGeneration++; Tiles.Clear(); Workspace.Children.Clear(); pending.Clear(); selected = null; FocusedTile = null;
            Cubes.Clear(); Cubes.Add(new CubeInstance()); SelectedCubeIndex = 0;
            UpdateEmptyState(); Reflow(); SetStatus("Canvas cleared. Original files were not changed.");
        }

        public void SetEqual(bool equal)
        {
            EqualTiles = equal; layoutButton.Content = equal ? "Equal tiles" : "Auto tiles"; Reflow();
        }

        public void SetLink(bool enabled)
        {
            LinkViews = enabled; linkButton.Content = enabled ? "Linked" : "Link views"; linkButton.BorderBrush = enabled ? Accent : Paint("#3D4653");
            if (enabled && selected != null) { ViewChanged(selected); PerspectiveChanged(selected); }
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void SetIsometric(bool enabled)
        {
            bool wasEnabled = IsometricEnabled;
            if (enabled && !wasEnabled) { IsometricAngleDegrees = 26.565; angleIndex = 0; }
            IsometricEnabled = enabled;
            SyncPerspectiveControls();
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void CycleIsometric()
        {
            if (!IsometricEnabled) { SetIsometric(true); return; }
            angleIndex = (angleIndex + 1) % AnglePresets.Length;
            if (angleIndex == 0) { SetIsometricAngle(AnglePresets[0]); SetIsometric(false); return; }
            SetIsometricAngle(AnglePresets[angleIndex]);
        }

        public void SetIsometricAngle(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees) || degrees < 5 || degrees > 85) return;
            IsometricAngleDegrees = degrees;
            angleIndex = Math.Abs(degrees - 30) < 0.01 ? 1 : 0;
            SyncPerspectiveControls();
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void SetCube(bool enabled)
        {
            bool wasEnabled = CubeEnabled;
            if (enabled && !wasEnabled)
            {
                // Re-enabling always restores the default single cube, so a cube that
                // was dragged far off the plane comes back into view.
                Cubes.Clear(); Cubes.Add(new CubeInstance()); SelectedCubeIndex = 0;
            }
            if (enabled && CubeProjection == "") SetIsometric(true);
            CubeEnabled = enabled;
            SyncPerspectiveControls();
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void SetPitch(bool enabled)
        {
            bool wasEnabled = PitchEnabled;
            if (enabled && !wasEnabled) PitchAngleDegrees = 35;
            PitchEnabled = enabled;
            SyncPerspectiveControls();
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void SetPitchAngle(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees) || degrees < 15 || degrees > 90) return;
            PitchAngleDegrees = degrees;
            SyncPerspectiveControls();
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void AddCube()
        {
            CubeInstance last = Cubes.Count > 0 ? Cubes[Cubes.Count - 1] : new CubeInstance();
            Cubes.Add(new CubeInstance(Math.Min(60, last.X + 4), Math.Min(60, last.Y + 1), last.Z, 1));
            SelectedCubeIndex = Cubes.Count - 1;
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void SelectCube(int index)
        {
            SelectedCubeIndex = index >= 0 && index < Cubes.Count ? index : -1;
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void MoveCube(int index, double dx, double dy, double dz)
        {
            if (index < 0 || index >= Cubes.Count || double.IsNaN(dx) || double.IsNaN(dy) || double.IsNaN(dz) || double.IsInfinity(dx) || double.IsInfinity(dy) || double.IsInfinity(dz)) return;
            CubeInstance cube = Cubes[index];
            cube.X = Math.Max(-60, Math.Min(60, cube.X + dx));
            cube.Y = Math.Max(-20, Math.Min(60, cube.Y + dy));
            cube.Z = Math.Max(-60, Math.Min(60, cube.Z + dz));
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void ResizeCube(int index, double factor)
        {
            if (index < 0 || index >= Cubes.Count || double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0) return;
            Cubes[index].Size = Math.Max(0.25, Math.Min(5.0, Cubes[index].Size * factor));
            foreach (var tile in Tiles) tile.RefreshPerspective();
        }

        public void SetPerspective(bool enabled)
        {
            bool wasEnabled = PerspectiveEnabled;
            if (enabled && !wasEnabled) foreach (var tile in Tiles) tile.ReplacePerspectivePoints(new Point[0]);
            PerspectiveEnabled = enabled;
            SyncPerspectiveControls();
            foreach (var tile in Tiles) { tile.EndPerspectiveGesture(); tile.RefreshPerspective(); }
        }

        private void SyncPerspectiveControls()
        {
            perspectiveButton.Content = "Perspective ▾";
            perspectiveButton.BorderBrush = (PerspectiveEnabled || IsometricEnabled || PitchEnabled || CubeEnabled) ? Accent : Paint("#3D4653");
            vanishingCheck.IsChecked = PerspectiveEnabled;
            isoCheck.IsChecked = IsometricEnabled;
            pitchCheck.IsChecked = PitchEnabled;
            cubeCheck.IsChecked = CubeEnabled;
            isoAngleLabel.Text = "Isometric angle · " + (Math.Abs(IsometricAngleDegrees - 26.565) < 0.01 ? "2:1" : IsometricAngleDegrees.ToString("0") + (char)176);
            pitchAngleLabel.Text = "Pitch angle · " + PitchAngleDegrees.ToString("0") + (char)176;
            if (Math.Abs(isoSlider.Value - IsometricAngleDegrees) > 0.01)
            {
                syncingIsoSlider = true;
                try { isoSlider.Value = Math.Max(5, Math.Min(60, IsometricAngleDegrees)); }
                finally { syncingIsoSlider = false; }
            }
            if (Math.Abs(pitchSlider.Value - PitchAngleDegrees) > 0.01)
            {
                syncingPitchSlider = true;
                try { pitchSlider.Value = Math.Max(15, Math.Min(90, PitchAngleDegrees)); }
                finally { syncingPitchSlider = false; }
            }
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

        public void PerspectiveChanged(ImageTile source)
        {
            if (!LinkViews) return;
            var snapshot = source.PerspectivePoints.ToArray();
            foreach (var tile in Tiles) if (tile != source) tile.ReplacePerspectivePoints(snapshot);
        }

        private void UpdateEmptyState()
        {
            empty.Visibility = Tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public Brush CanvasBackground { get { return canvasBackground; } }

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
            LastMessage = text; noticeTimer.Stop();
            errorNotice.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
            if (error) { errorText.Text = text; errorNotice.ToolTip = text + "\nClick to dismiss"; noticeTimer.Start(); }
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
            if (perspectiveBarHost.Visibility == Visibility.Visible && e.Key == Key.Escape) { perspectiveBarHost.Visibility = Visibility.Collapsed; perspectiveButton.Focus(); e.Handled = true; return; }
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.S) { PickSave(); e.Handled = true; }
            else if (ctrl && e.Key == Key.O) { OpenFromKey(); e.Handled = true; }
            else if (ctrl && e.Key == Key.V) { Paste(); e.Handled = true; }
            else if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0)) { ResetAll(); e.Handled = true; }
            else if (ctrl && e.Key == Key.B) { ToggleMonochromeFromKey(); e.Handled = true; }
            else if (ctrl && e.Key == Key.I) { SetIsometric(!IsometricEnabled); e.Handled = true; }
            else if (ctrl && e.Key == Key.P) { SetPerspective(!PerspectiveEnabled); e.Handled = true; }
            else if (ctrl && e.Key == Key.L) { SetLink(!LinkViews); e.Handled = true; }
            else if (ctrl && e.Key == Key.X && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) { ClearImages(); e.Handled = true; }
            else if (e.Key == Key.Delete && selected != null)
            {
                if (PerspectiveEnabled) selected.DeleteLastClickedPerspectivePoint();
                else Remove(selected);
                e.Handled = true;
            }
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
                else
                {
                    var item = ImageLoader.FromClipboardData(Clipboard.GetDataObject(), ++clipboardNumber);
                    if (item != null) { AddImage(item); SetStatus("Pasted image. Clipboard pixels stay local."); }
                    else SetStatus("Clipboard contains no image or image files.");
                }
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
        private readonly List<Point> perspectivePoints = new List<Point>();
        private int selectedPerspectivePoint = -1;
        private bool draggingPerspectivePoint;
        private readonly TileOverlay perspectiveOverlay;
        private int cubeDragIndex = -1;
        private string cubeDragHandle;
        private Point cubeDragStart;
        public IList<Point> PerspectivePoints { get { return perspectivePoints.AsReadOnly(); } }
        public int SelectedPerspectivePoint { get { return selectedPerspectivePoint; } }
        public Rect ImageBounds { get { return CompositeExporter.ImageBounds(new Size(Viewport.ActualWidth, Viewport.ActualHeight), Item.Aspect, Zoom, NormalizedPan); } }
        public Point ImageToViewport(Point normalized)
        {
            Rect bounds = ImageBounds;
            return new Point(bounds.X + normalized.X * bounds.Width, bounds.Y + normalized.Y * bounds.Height);
        }
        public Point ViewportToImage(Point position)
        {
            Rect bounds = ImageBounds;
            return new Point((position.X - bounds.X) / bounds.Width, (position.Y - bounds.Y) / bounds.Height);
        }
        private static void ValidatePoint(Point point)
        {
            if (double.IsNaN(point.X) || double.IsNaN(point.Y) || double.IsInfinity(point.X) || double.IsInfinity(point.Y))
                throw new ArgumentException("Vanishing points must have finite coordinates.");
        }
        public void AddPerspectivePoint(Point point)
        {
            ValidatePoint(point); owner.Select(this); perspectivePoints.Add(point);
            selectedPerspectivePoint = perspectivePoints.Count - 1; RefreshPerspective(); owner.PerspectiveChanged(this);
        }
        public void MovePerspectivePoint(int index, Point point)
        {
            ValidatePoint(point); if (index < 0 || index >= perspectivePoints.Count) return;
            perspectivePoints[index] = point; selectedPerspectivePoint = index; RefreshPerspective(); owner.PerspectiveChanged(this);
        }
        public void DeleteSelectedPerspectivePoint()
        {
            if (selectedPerspectivePoint < 0 || selectedPerspectivePoint >= perspectivePoints.Count) return;
            perspectivePoints.RemoveAt(selectedPerspectivePoint); selectedPerspectivePoint = -1;
            EndPerspectiveGesture(); RefreshPerspective(); owner.PerspectiveChanged(this);
        }
        // Delete should remove the point the user last clicked; when nothing is
        // selected it falls back to the most recently placed point.
        public void DeleteLastClickedPerspectivePoint()
        {
            if (perspectivePoints.Count == 0) return;
            int index = selectedPerspectivePoint >= 0 && selectedPerspectivePoint < perspectivePoints.Count
                ? selectedPerspectivePoint : perspectivePoints.Count - 1;
            perspectivePoints.RemoveAt(index); selectedPerspectivePoint = -1;
            EndPerspectiveGesture(); RefreshPerspective(); owner.PerspectiveChanged(this);
        }
        internal void ReplacePerspectivePoints(IEnumerable<Point> points)
        {
            var snapshot = points.ToArray();
            perspectivePoints.Clear(); perspectivePoints.AddRange(snapshot);
            selectedPerspectivePoint = -1; RefreshPerspective();
        }
        public bool BeginPerspectiveGesture(Point at)
        {
            if (!owner.PerspectiveEnabled) return false;
            owner.Select(this); selectedPerspectivePoint = -1;
            double nearest = 12;
            for (int i = 0; i < perspectivePoints.Count; i++)
            {
                double distance = (ImageToViewport(perspectivePoints[i]) - at).Length;
                if (distance <= nearest) { nearest = distance; selectedPerspectivePoint = i; }
            }
            if (selectedPerspectivePoint < 0)
            {
                if (!ImageBounds.Contains(at)) { RefreshPerspective(); return false; }
                AddPerspectivePoint(ViewportToImage(at));
            }
            draggingPerspectivePoint = true; RefreshPerspective(); return true;
        }
        public void UpdatePerspectiveGesture(Point at)
        {
            if (draggingPerspectivePoint && owner.PerspectiveEnabled) MovePerspectivePoint(selectedPerspectivePoint, ViewportToImage(at));
        }
        public void EndPerspectiveGesture()
        {
            draggingPerspectivePoint = false; panning = false;
            if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
            Viewport.Cursor = owner.PerspectiveEnabled ? Cursors.Cross : Cursors.Hand;
        }
        public bool BeginCubeGesture(Point at)
        {
            if (!owner.CubeEnabled) return false;
            string mode = owner.CubeProjection;
            if (mode != "Pitch" && mode != "Isometric" && mode != "Vanishing") return false;
            double angle = mode == "Isometric" ? owner.IsometricAngleDegrees : owner.PitchAngleDegrees;
            int index = owner.SelectedCubeIndex;
            if (index >= 0 && index < owner.Cubes.Count)
            {
                string handle = CubeInteraction.HandleAt(ImageBounds, mode, angle, PerspectivePoints, owner.Cubes[index], at, 10);
                if (handle != null)
                {
                    cubeDragIndex = index; cubeDragHandle = handle; cubeDragStart = at;
                    Viewport.CaptureMouse(); RefreshPerspective(); return true;
                }
            }
            int body = CubeInteraction.SelectAt(ImageBounds, mode, angle, PerspectivePoints, owner.Cubes, at, 16);
            if (body >= 0) { owner.SelectCube(body); RefreshPerspective(); return true; }
            return false;
        }
        public void UpdateCubeGesture(Point at)
        {
            if (cubeDragIndex < 0 || cubeDragHandle == null) return;
            string mode = owner.CubeProjection;
            double angle = mode == "Isometric" ? owner.IsometricAngleDegrees : owner.PitchAngleDegrees;
            Vector delta = at - cubeDragStart;
            if (cubeDragHandle == "Resize")
            {
                double cells = CubeInteraction.GridDelta(ImageBounds, mode, angle, PerspectivePoints, owner.Cubes[cubeDragIndex], delta, "X");
                owner.ResizeCube(cubeDragIndex, 1 + cells * 0.25);
            }
            else
            {
                double dx = cubeDragHandle == "X" ? CubeInteraction.GridDelta(ImageBounds, mode, angle, PerspectivePoints, owner.Cubes[cubeDragIndex], delta, "X") : 0;
                double dy = cubeDragHandle == "Y" ? CubeInteraction.GridDelta(ImageBounds, mode, angle, PerspectivePoints, owner.Cubes[cubeDragIndex], delta, "Y") : 0;
                double dz = cubeDragHandle == "Z" ? CubeInteraction.GridDelta(ImageBounds, mode, angle, PerspectivePoints, owner.Cubes[cubeDragIndex], delta, "Z") : 0;
                owner.MoveCube(cubeDragIndex, dx, dy, dz);
            }
            // Apply only the new pointer movement. Reapplying the displacement from
            // mouse-down on every event compounds the offset and overshoots.
            cubeDragStart = at;
        }
        public void EndCubeGesture()
        {
            if (cubeDragIndex >= 0 && Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
            cubeDragIndex = -1; cubeDragHandle = null;
        }
        public void RefreshPerspective() { perspectiveOverlay.InvalidateVisual(); Viewport.Cursor = owner.PerspectiveEnabled ? Cursors.Cross : Cursors.Hand; }

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
            perspectiveOverlay = new TileOverlay(this, window) { IsHitTestVisible = false };
            BorderBrush = BeholderWindow.Paint("#35404E"); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(7);
            Background = window.CanvasBackground; ClipToBounds = true;
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TileLayout.CaptionHeight) }); Child = grid;
            Viewport.Background = Checker(); Viewport.ClipToBounds = true; Viewport.Cursor = Cursors.Hand;
            grid.Children.Add(Viewport);
            image.Source = item.Bitmap; image.Stretch = Stretch.Uniform; image.Margin = new Thickness(6);
            image.RenderTransformOrigin = new Point(0.5, 0.5);
            var transforms = new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(translate); image.RenderTransform = transforms;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality); Viewport.Children.Add(image); Viewport.Children.Add(perspectiveOverlay);
            System.Windows.Automation.AutomationProperties.SetName(image, item.Name);
            var header = new DockPanel { Background = BeholderWindow.Paint("#242A33"), LastChildFill = true, Cursor = Cursors.SizeAll };
            Grid.SetRow(header, 1); grid.Children.Add(header);
            var remove = BeholderWindow.ActionButton("x", "Remove this image (original is untouched)", delegate { owner.Remove(this); });
            remove.Width = 24; remove.MinWidth = 24; remove.Height = TileLayout.CaptionHeight; remove.Padding = new Thickness(0); remove.Margin = new Thickness(0, 0, 2, 0); remove.Background = Brushes.Transparent; remove.BorderBrush = Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetName(remove, "Remove " + item.Name);
            DockPanel.SetDock(remove, Dock.Right); header.Children.Add(remove);
            zoomText.Foreground = BeholderWindow.Muted; zoomText.FontSize = 10; zoomText.VerticalAlignment = VerticalAlignment.Center; zoomText.Margin = new Thickness(4, 0, 5, 0); zoomText.Text = "Fit";
            DockPanel.SetDock(zoomText, Dock.Right); header.Children.Add(zoomText);
            var name = new TextBlock { Text = item.Name, Foreground = BeholderWindow.Text, FontSize = 11, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Name + "\n" + item.Width + " x " + item.Height + " px\nDrag this label to reorder" };
            header.Children.Add(name);
            Viewport.MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                owner.Select(this); Point at = e.GetPosition(Viewport);
                ZoomAt(Zoom * Math.Pow(1.15, e.Delta / 120.0), at); e.Handled = true;
            };
            Viewport.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                owner.Select(this); owner.Focus();
                if (e.ClickCount == 1 && BeginCubeGesture(e.GetPosition(Viewport))) { e.Handled = true; return; }
                if (owner.PerspectiveEnabled)
                {
                    if (e.ClickCount == 1 && BeginPerspectiveGesture(e.GetPosition(Viewport))) Viewport.CaptureMouse();
                    e.Handled = true; return;
                }
                if (e.ClickCount == 2) { owner.ToggleFocus(this); e.Handled = true; return; }
                panning = true; dragStart = e.GetPosition(Viewport); initialPan = new Vector(PanX, PanY); Viewport.CaptureMouse(); Viewport.Cursor = Cursors.ScrollAll; e.Handled = true;
            };
            Viewport.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (cubeDragIndex >= 0)
                {
                    if (e.LeftButton == MouseButtonState.Pressed) UpdateCubeGesture(e.GetPosition(Viewport));
                    e.Handled = true; return;
                }
                if (draggingPerspectivePoint)
                {
                    if (e.LeftButton == MouseButtonState.Pressed) UpdatePerspectiveGesture(e.GetPosition(Viewport));
                    e.Handled = true; return;
                }
                if (!panning || e.LeftButton != MouseButtonState.Pressed) return;
                Vector delta = e.GetPosition(Viewport) - dragStart;
                SetView(Zoom, initialPan.X + delta.X, initialPan.Y + delta.Y, true);
            };
            Viewport.MouseLeftButtonUp += delegate { EndCubeGesture(); EndPerspectiveGesture(); };
            Viewport.LostMouseCapture += delegate { EndCubeGesture(); draggingPerspectivePoint = false; panning = false; Viewport.Cursor = owner.PerspectiveEnabled ? Cursors.Cross : Cursors.Hand; };
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

        public void SetSelected(bool value)
        {
            BorderBrush = value ? BeholderWindow.Accent : BeholderWindow.Paint("#35404E");
            if (!value) { selectedPerspectivePoint = -1; EndPerspectiveGesture(); RefreshPerspective(); }
        }

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
            RefreshPerspective();
            if (notify) owner.ViewChanged(this);
        }
    }
}
