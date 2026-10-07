using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Beholder
{
    public sealed class PerspectiveLine
    {
        public readonly string Axis;
        public readonly Point A;
        public readonly Point B;
        public readonly double Width;
        public PerspectiveLine(string axis, Point a, Point b) : this(axis, a, b, 1.0) { }
        public PerspectiveLine(string axis, Point a, Point b, double width) { Axis = axis; A = a; B = b; Width = width; }
    }

    public sealed class PerspectiveMarker
    {
        public readonly string Axis;
        public readonly Point At;
        public readonly Brush Color;
        public PerspectiveMarker(string axis, Point at, Brush color) { Axis = axis; At = at; Color = color; }
    }

    public static class PerspectiveGeometry
    {
        private static readonly string[] Names = new[] { "Z", "X", "Y", "Z+", "X+", "Y+", "Z+", "X+", "Y+" };
        private static readonly Brush[] PointColors = new[]
        {
            BeholderWindow.Paint("#3F7FC4"), BeholderWindow.Paint("#D2533B"), BeholderWindow.Paint("#4FA06A"), BeholderWindow.Paint("#E4B84E"),
            BeholderWindow.Paint("#BA7FD8"), BeholderWindow.Paint("#E08A3C"), BeholderWindow.Paint("#5FB0B8"), BeholderWindow.Paint("#C43F8E")
        };

        public static IEnumerable<PerspectiveLine> Build(IEnumerable<Point> points, Rect rect)
        {
            var result = new List<PerspectiveLine>();
            Point[] list = ToArray(points);
            if (list.Length == 0) return result;

            // Horizon: horizontal through one point; through the first two points otherwise.
            if (list.Length == 1)
            {
                double y = rect.Y + list[0].Y * rect.Height;
                result.Add(new PerspectiveLine("Horizon", new Point(rect.X, y), new Point(rect.Right, y), 2.0));
            }
            else
            {
                Point a = At(list[0], rect), b = At(list[1], rect);
                Vector d = b - a;
                if (d.Length < 0.001) d = new Vector(1, 0);
                PerspectiveLine h = ClipSegment(a - d * 1.0E6, a + d * 1.0E6, rect);
                if (h != null) result.Add(new PerspectiveLine("Horizon", h.A, h.B, 2.0));
            }

            // Floor depth bands for the first vanishing point (closer together near the horizon).
            Point first = At(list[0], rect);
            if (list.Length >= 1 && first.Y < rect.Bottom - 6)
                for (int k = 1; k <= 6; k++)
                {
                    double t = 1.0 - Math.Pow(0.5, k / 4.0);
                    double y = first.Y + (rect.Bottom - first.Y) * t;
                    result.Add(new PerspectiveLine("Y", new Point(rect.X, y), new Point(rect.Right, y), 1.1));
                }

            // Every vanishing point gets its own converging set rising from the bottom of the image,
            // plus labeled X (bottom-left), Y (bottom-right), Z (bottom-center) axis lines.
            for (int p = 0; p < list.Length; p++)
            {
                Point vp = At(list[p], rect);
                string tag = Names[Math.Min(p, Names.Length - 1)];
                if (list.Length <= 8)
                {
                    for (int k = 0; k <= 10; k++)
                    {
                        Point start = new Point(rect.X + rect.Width * k / 10.0, rect.Bottom);
                        AddRay(result, tag, start, vp, 1.0);
                    }
                    for (int k = 1; k <= 5; k++)
                    {
                        double t = k / 6.0;
                        AddRay(result, tag, new Point(rect.X, rect.Y + rect.Height * t), vp, 1.0);
                        AddRay(result, tag, new Point(rect.Right, rect.Y + rect.Height * t), vp, 1.0);
                    }
                }
                result.Add(AxisRay("X", new Point(rect.X, rect.Bottom), vp));
                result.Add(AxisRay("Y", new Point(rect.Right, rect.Bottom), vp));
                result.Add(AxisRay("Z", new Point(rect.X + rect.Width / 2, rect.Bottom), vp));
                result.Add(new PerspectiveLine(tag, vp, vp));
            }
            return result;
        }

        public static IEnumerable<PerspectiveMarker> Markers(IEnumerable<Point> points, Rect image)
        {
            var result = new List<PerspectiveMarker>();
            Point[] list = ToArray(points);
            for (int i = 0; i < list.Length; i++)
                result.Add(new PerspectiveMarker(Names[Math.Min(i, Names.Length - 1)], At(list[i], image), PointColors[i % PointColors.Length]));
            return result;
        }

        public static Brush PointColor(int index) { return PointColors[index % PointColors.Length]; }
        public static string PointName(int index) { return Names[Math.Min(index, Names.Length - 1)]; }

        private static Point At(Point p, Rect rect) { return rect.TopLeft + new Vector(p.X * rect.Width, p.Y * rect.Height); }

        private static Point[] ToArray(IEnumerable<Point> points)
        {
            var list = new Point[0];
            foreach (var point in points)
            {
                var grew = new Point[list.Length + 1];
                for (int i = 0; i < list.Length; i++) grew[i] = list[i];
                grew[list.Length] = point; list = grew;
            }
            return list;
        }

        private static void AddRay(List<PerspectiveLine> result, string axis, Point start, Point to, double width)
        {
            PerspectiveLine ray = ClipSegment(start, to, new Rect(Math.Min(start.X, to.X) - 1, Math.Min(start.Y, to.Y) - 1, Math.Abs(to.X - start.X) + 2, Math.Abs(to.Y - start.Y) + 2));
            if (ray != null) result.Add(new PerspectiveLine(axis, ray.A, ray.B, width));
            else result.Add(new PerspectiveLine(axis, start, to, width));
        }

        private static PerspectiveLine AxisRay(string axis, Point start, Point to)
        {
            PerspectiveLine ray = ClipSegment(start, to, new Rect(Math.Min(start.X, to.X) - 1, Math.Min(start.Y, to.Y) - 1, Math.Abs(to.X - start.X) + 2, Math.Abs(to.Y - start.Y) + 2));
            return ray != null ? new PerspectiveLine(axis, ray.A, ray.B, 2.4) : new PerspectiveLine(axis, start, to, 2.4);
        }

        public static PerspectiveLine ClipSegment(Point a, Point b, Rect rect)
        {
            double t0 = 0, t1 = 1;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            if (ClipAxis(-dx, a.X - rect.X, ref t0, ref t1) && ClipAxis(dx, rect.Right - a.X, ref t0, ref t1) &&
                ClipAxis(-dy, a.Y - rect.Y, ref t0, ref t1) && ClipAxis(dy, rect.Bottom - a.Y, ref t0, ref t1))
            {
                Point p0 = new Point(a.X + dx * t0, a.Y + dy * t0);
                Point p1 = new Point(a.X + dx * t1, a.Y + dy * t1);
                if ((p1 - p0).Length < 0.01) return null;
                return new PerspectiveLine("", p0, p1);
            }
            return null;
        }

        private static bool ClipAxis(double denominator, double numerator, ref double t0, ref double t1)
        {
            if (Math.Abs(denominator) < 1.0E-12) return numerator >= 0;
            double t = numerator / denominator;
            if (denominator < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
    }

    public sealed class TileOverlay : FrameworkElement
    {
        private readonly ImageTile tile;
        private readonly BeholderWindow owner;
        public TileOverlay(ImageTile image, BeholderWindow window) { tile = image; owner = window; }
        protected override void OnRender(DrawingContext dc)
        {
            bool showGuides = owner.LinkViews || owner.SelectedTile == tile;
            if (showGuides)
            {
                if (owner.IsometricEnabled)
                {
                    Rect viewport = new Rect(0, 0, tile.Viewport.ActualWidth, tile.Viewport.ActualHeight);
                    dc.DrawRectangle(CheckerBrush(), null, viewport);
                    Drawing.Isometric(dc, viewport, owner.IsometricAngleDegrees);
                }
                if (owner.PitchEnabled) Drawing.Pitch(dc, tile.ImageBounds, owner.PitchAngleDegrees);
                if (owner.CubeEnabled) Drawing.Cube(dc, tile.ImageBounds, owner.CubeProjection, owner.CubeProjection == "Isometric" ? owner.IsometricAngleDegrees : owner.PitchAngleDegrees, tile.PerspectivePoints, owner.Cubes, owner.SelectedCubeIndex, true);
            }
            if (!owner.PerspectiveEnabled) return;
            Drawing.Perspective(dc, tile, tile.PerspectivePoints, false);
        }

        private static Brush CheckerBrush()
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(9, 255, 255, 255)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(14, 0, 0, 0)), null, new RectangleGeometry(new Rect(8, 0, 8, 8))));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(14, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 8, 8, 8))));
            var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            brush.Freeze(); return brush;
        }
    }

    public static class PitchGeometry
    {
        // The pitch grid is a flat checkerboard plane rotated about the screen
        // horizontal axis. Lines stay parallel (no vanishing point); the row
        // spacing foreshortens by sin(angle) while columns stay evenly spaced,
        // so lowering the pitch is exactly like rotating the plane edge-on.
        internal static double Spacing(Rect image)
        {
            return Math.Max(40, image.Width / 18.0);
        }

        public static Point Project(Rect image, double degrees, double column, double depth, double height)
        {
            double spacing = Spacing(image);
            if (degrees >= 89.5)
            {
                // Top-down: columns straight across, depth recedes straight up the
                // screen, and height is invisible (cos 90). The cube becomes a square.
                return new Point(image.X + image.Width / 2 + spacing * column, image.Bottom - spacing * depth);
            }
            double radians = degrees * Math.PI / 180.0;
            double y = image.Bottom - depth * spacing * Math.Sin(radians) - height * spacing * Math.Cos(radians);
            return new Point(image.X + image.Width / 2 + column * spacing, y);
        }

        public static IEnumerable<PerspectiveLine> Build(Rect image, double degrees)
        {
            var lines = new List<PerspectiveLine>();
            if (image.Width <= 0 || image.Height <= 0 || double.IsNaN(degrees) || double.IsInfinity(degrees) || degrees < 15 || degrees > 90) return lines;
            double spacing = Spacing(image);
            if (degrees >= 89.5)
            {
                // Exact top-down board: even spacing along both axes, anchored at the
                // top so every band has the same width as the columns.
                int topColumns = Math.Min(256, (int)Math.Ceiling(image.Width / (2.0 * spacing)) + 2);
                for (int column = -topColumns; column <= topColumns; column++)
                {
                    double x = image.X + image.Width / 2 + column * spacing;
                    if (x >= image.X && x <= image.Right)
                        lines.Add(new PerspectiveLine("Grid", new Point(x, image.Top), new Point(x, image.Bottom)));
                }
                int topRows = (int)Math.Ceiling(image.Height / spacing);
                for (int row = 0; row <= topRows; row++)
                {
                    double y = image.Top + row * spacing;
                    if (y <= image.Bottom)
                        lines.Add(new PerspectiveLine("Grid", new Point(image.X, y), new Point(image.Right, y)));
                }
                return lines;
            }
            double sine = Math.Sin(degrees * Math.PI / 180.0);
            int columns = Math.Min(256, (int)Math.Ceiling(image.Width / (2.0 * spacing)) + 2);
            for (int column = -columns; column <= columns; column++)
            {
                double x = image.X + image.Width / 2 + column * spacing;
                if (x >= image.X && x <= image.Right)
                    lines.Add(new PerspectiveLine("Grid", new Point(x, image.Top), new Point(x, image.Bottom)));
            }
            int rows = Math.Min(256, (int)Math.Ceiling(image.Height / (spacing * Math.Max(sine, 0.2))) + 2);
            for (int row = 0; row <= rows; row++)
            {
                double y = image.Bottom - row * spacing * sine;
                double clamped = Math.Max(image.Top, Math.Min(image.Bottom, y));
                lines.Add(new PerspectiveLine("Grid", new Point(image.X, clamped), new Point(image.Right, clamped)));
                if (clamped <= image.Top) break;
            }
            return lines;
        }
    }

    public sealed class CubeInstance
    {
        // One wireframe cube. X/Y/Z are offsets in grid cells (columns, depth,
        // height); Size scales the whole cube around its offset point.
        public double X;
        public double Y;
        public double Z;
        public double Size;
        public CubeInstance() { X = 0; Y = 0; Z = 0; Size = 1.0; }
        public CubeInstance(double x, double y, double z, double size) { X = x; Y = y; Z = z; Size = size; }
    }

    public static class CubeGeometry
    {
        // A cube has eight corners and twelve visible-through edges, never a face fill.
        // The vanishing projection is homogeneous: a finite axis direction is
        // proportional to (vanishingPoint.X, vanishingPoint.Y, 1). Corner bit order is
        // consistent across modes: bit0 = X (columns/right), bit1 = Y (depth/vertical),
        // bit2 = Z (height/left).
        public static IEnumerable<PerspectiveLine> Build(Rect image, string mode, double angle, IEnumerable<Point> points)
        {
            return Build(image, mode, angle, points, new CubeInstance());
        }

        public static IEnumerable<PerspectiveLine> Build(Rect image, string mode, double angle, IEnumerable<Point> points, CubeInstance cube)
        {
            var edges = new List<PerspectiveLine>();
            if (image.Width <= 0 || image.Height <= 0 || (mode != "Isometric" && mode != "Pitch" && mode != "Vanishing")) return edges;
            if (cube == null) cube = new CubeInstance();
            if (mode == "Pitch")
            {
                if (double.IsNaN(angle) || double.IsInfinity(angle) || angle < 15 || angle > 90) return edges;
                if (angle >= 89.5)
                {
                    // Top-down: draw a green square on the chessboard. Side 4 grid cells
                    // and depth rows 3..7 so every edge sits on a grid line.
                    double sp = PitchGeometry.Spacing(image);
                    double cx = image.X + image.Width / 2 + sp * cube.X;
                    double cy = image.Bottom - sp * cube.Y;
                    double half = 2 * sp * cube.Size;
                    Point a = new Point(cx - half, cy - 3 * sp * cube.Size);
                    Point b = new Point(cx + half, cy - 3 * sp * cube.Size);
                    Point c = new Point(cx + half, cy - 7 * sp * cube.Size);
                    Point d = new Point(cx - half, cy - 7 * sp * cube.Size);
                    PerspectiveLine ka = PerspectiveGeometry.ClipSegment(a, b, image);
                    PerspectiveLine kb = PerspectiveGeometry.ClipSegment(b, c, image);
                    PerspectiveLine kc = PerspectiveGeometry.ClipSegment(c, d, image);
                    PerspectiveLine kd = PerspectiveGeometry.ClipSegment(d, a, image);
                    if (ka != null) edges.Add(new PerspectiveLine("X", ka.A, ka.B, 2.55));
                    if (kb != null) edges.Add(new PerspectiveLine("Y", kb.A, kb.B, 2.55));
                    if (kc != null) edges.Add(new PerspectiveLine("X", kc.A, kc.B, 2.55));
                    if (kd != null) edges.Add(new PerspectiveLine("Y", kd.A, kd.B, 2.55));
                    return edges;
                }
            }
            double unit = Math.Min(image.Width, image.Height);
            Point anchor = new Point();
            Point[] pitchCorners = null;
            Vector[] axes = new Vector[3]; // X, Y, Z
            double[] weights = new double[3];
            if (mode == "Isometric")
            {
                double slope = Math.Tan(angle * Math.PI / 180.0);
                anchor = new Point(image.X + image.Width * 0.5, image.Y + image.Height * 0.46);
                axes[0] = new Vector(unit * 0.19, unit * 0.19 * slope);
                axes[1] = new Vector(0, -unit * 0.23);
                axes[2] = new Vector(-unit * 0.19, unit * 0.19 * slope);
            }
            else if (mode == "Pitch")
            {
                pitchCorners = new Point[8];
                for (int bits = 0; bits < 8; bits++)
                    pitchCorners[bits] = PitchGeometry.Project(image, angle,
                        cube.X + cube.Size * (-2 + 4 * (bits & 1)),
                        cube.Y + cube.Size * (3 + 3 * ((bits >> 1) & 1)),
                        cube.Z + cube.Size * (3 * ((bits >> 2) & 1)));
            }
            else
            {
                anchor = new Point(image.X + image.Width * 0.43, image.Y + image.Height * 0.69);
                axes[0] = new Vector(unit * 0.25, 0);
                axes[1] = new Vector(0, -unit * 0.25);
                Point[] vanishing = new Point[3]; int count = 0;
                foreach (var point in points) { if (count == 3) break; vanishing[count++] = point; }
                if (count == 0) { vanishing[0] = new Point(0.5, 0.35); count = 1; }
                // Placement order is Z, X, Y, matching the vanishing guides.
                int[] index = new[] { 2, 0, 1 };
                for (int n = 0; n < count; n++)
                {
                    Point vp = new Point(image.X + vanishing[n].X * image.Width, image.Y + vanishing[n].Y * image.Height);
                    int axis = index[n];
                    if (double.IsNaN(vp.X) || double.IsNaN(vp.Y) || double.IsInfinity(vp.X) || double.IsInfinity(vp.Y) || (vp - anchor).Length < 1) continue;
                    weights[axis] = 0.65;
                    axes[axis] = new Vector(vp.X * weights[axis], vp.Y * weights[axis]);
                }
                if (weights[2] == 0) axes[2] = new Vector(-unit * 0.20, -unit * 0.12);
            }
            Point[] corners = pitchCorners ?? new Point[8];
            if (pitchCorners == null)
                for (int bits = 0; bits < 8; bits++)
                {
                    int x = bits & 1, y = (bits >> 1) & 1, z = (bits >> 2) & 1;
                    double xo = cube.X + cube.Size * x, yo = cube.Y + cube.Size * y, zo = cube.Z + cube.Size * z;
                    double w = 1 + x * weights[0] + y * weights[1] + z * weights[2];
                    corners[bits] = new Point((anchor.X + xo * axes[0].X + yo * axes[1].X + zo * axes[2].X) / w,
                        (anchor.Y + xo * axes[0].Y + yo * axes[1].Y + zo * axes[2].Y) / w);
                }
            string[] names = new[] { "X", "Y", "Z" };
            for (int axis = 0; axis < 3; axis++)
                for (int bits = 0; bits < 8; bits++)
                {
                    int mask = 1 << axis;
                    if ((bits & mask) != 0) continue;
                    if ((corners[bits] - corners[bits | mask]).Length < 0.01) continue;
                    PerspectiveLine segment = PerspectiveGeometry.ClipSegment(corners[bits], corners[bits | mask], image);
                    if (segment != null) edges.Add(new PerspectiveLine(names[axis], segment.A, segment.B, 2.55));
                }
            return edges;
        }

        // Projected corner positions for one cube, used by drawing, drag handles,
        // and hit tests. At 90 degrees the flat square maps corners 0..3 onto the
        // square and 4..7 onto duplicates so handle lookup stays uniform.
        public static Point[] Corners(Rect image, string mode, double angle, IEnumerable<Point> points, CubeInstance cube)
        {
            var result = new Point[8];
            if (image.Width <= 0 || image.Height <= 0 || cube == null) return result;
            if (mode == "Pitch" && angle >= 89.5)
            {
                double sp = PitchGeometry.Spacing(image);
                double cx = image.X + image.Width / 2 + sp * cube.X;
                double cy = image.Bottom - sp * cube.Y;
                double half = 2 * sp * cube.Size;
                Point a = new Point(cx - half, cy - 3 * sp * cube.Size);
                Point b = new Point(cx + half, cy - 3 * sp * cube.Size);
                Point c = new Point(cx + half, cy - 7 * sp * cube.Size);
                Point d = new Point(cx - half, cy - 7 * sp * cube.Size);
                result[0] = a; result[1] = b; result[2] = d; result[3] = c;
                result[4] = a; result[5] = b; result[6] = d; result[7] = c;
                return result;
            }
            if (mode == "Pitch")
            {
                for (int bits = 0; bits < 8; bits++)
                    result[bits] = PitchGeometry.Project(image, angle,
                        cube.X + cube.Size * (-2 + 4 * (bits & 1)),
                        cube.Y + cube.Size * (3 + 3 * ((bits >> 1) & 1)),
                        cube.Z + cube.Size * (3 * ((bits >> 2) & 1)));
                return result;
            }
            double unit = Math.Min(image.Width, image.Height);
            Point anchor = new Point();
            Vector[] axes = new Vector[3];
            double[] weights = new double[3];
            if (mode == "Isometric")
            {
                double slope = Math.Tan(angle * Math.PI / 180.0);
                anchor = new Point(image.X + image.Width * 0.5, image.Y + image.Height * 0.46);
                axes[0] = new Vector(unit * 0.19, unit * 0.19 * slope);
                axes[1] = new Vector(0, -unit * 0.23);
                axes[2] = new Vector(-unit * 0.19, unit * 0.19 * slope);
            }
            else
            {
                anchor = new Point(image.X + image.Width * 0.43, image.Y + image.Height * 0.69);
                axes[0] = new Vector(unit * 0.25, 0);
                axes[1] = new Vector(0, -unit * 0.25);
                Point[] vanishing = new Point[3]; int count = 0;
                foreach (var point in points) { if (count == 3) break; vanishing[count++] = point; }
                if (count == 0) { vanishing[0] = new Point(0.5, 0.35); count = 1; }
                int[] index = new[] { 2, 0, 1 };
                for (int n = 0; n < count; n++)
                {
                    Point vp = new Point(image.X + vanishing[n].X * image.Width, image.Y + vanishing[n].Y * image.Height);
                    int axis = index[n];
                    if (double.IsNaN(vp.X) || double.IsNaN(vp.Y) || double.IsInfinity(vp.X) || double.IsInfinity(vp.Y) || (vp - anchor).Length < 1) continue;
                    weights[axis] = 0.65;
                    axes[axis] = new Vector(vp.X * weights[axis], vp.Y * weights[axis]);
                }
                if (weights[2] == 0) axes[2] = new Vector(-unit * 0.20, -unit * 0.12);
            }
            for (int bits = 0; bits < 8; bits++)
            {
                int x = bits & 1, y = (bits >> 1) & 1, z = (bits >> 2) & 1;
                double xo = cube.X + cube.Size * x, yo = cube.Y + cube.Size * y, zo = cube.Z + cube.Size * z;
                double w = 1 + x * weights[0] + y * weights[1] + z * weights[2];
                result[bits] = new Point((anchor.X + xo * axes[0].X + yo * axes[1].X + zo * axes[2].X) / w,
                    (anchor.Y + xo * axes[0].Y + yo * axes[1].Y + zo * axes[2].Y) / w);
            }
            return result;
        }
    }

    public static class CubeInteraction
    {
        // Convert a screen-space drag on the selected cube into grid-cell deltas
        // along one axis, using the same projection the cube is drawn with.
        public static double GridDelta(Rect image, string mode, double angle, Vector delta, string axis)
        {
            if (mode == "Pitch")
            {
                double s = PitchGeometry.Spacing(image);
                if (axis == "X") return delta.X / s;
                double rad = angle * Math.PI / 180.0;
                double sin = Math.Sin(rad), cos = Math.Cos(rad);
                if (axis == "Y") return Math.Abs(sin) < 0.02 ? 0 : -delta.Y / (s * sin);
                if (axis == "Z") return Math.Abs(cos) < 0.02 ? 0 : -delta.Y / (s * cos);
                return 0;
            }
            if (mode == "Isometric")
            {
                double unit = Math.Min(image.Width, image.Height);
                double k = Math.Max(0.05, Math.Tan(angle * Math.PI / 180.0));
                double ux = unit * 0.19, uy = unit * 0.19 * k;
                double len2 = ux * ux + uy * uy;
                if (axis == "X") return len2 < 0.02 ? 0 : (delta.X * ux + delta.Y * uy) / len2;
                if (axis == "Y") return delta.Y / (-unit * 0.23);
                if (axis == "Z") return len2 < 0.02 ? 0 : (-delta.X * ux + delta.Y * uy) / len2;
                return 0;
            }
            return 0;
        }

        // Vanishing-mode axis offsets are projected through the same homogeneous
        // transform as the cube. Derive drag speed from the selected handle's own
        // projected position, including its denominator for two/three-point modes.
        public static double GridDelta(Rect image, string mode, double angle, IEnumerable<Point> points, CubeInstance cube, Vector delta, string axis)
        {
            if (mode != "Vanishing") return GridDelta(image, mode, angle, delta, axis);
            if (cube == null) return 0;
            int corner = axis == "X" ? 1 : axis == "Y" ? 2 : axis == "Z" ? 4 : 7;
            Point before = CubeGeometry.Corners(image, mode, angle, points, cube)[corner];
            var shifted = new CubeInstance(cube.X + (axis == "X" ? 1 : 0),
                cube.Y + (axis == "Y" ? 1 : 0), cube.Z + (axis == "Z" ? 1 : 0), cube.Size);
            Point after = CubeGeometry.Corners(image, mode, angle, points, shifted)[corner];
            Vector direction = after - before;
            double lengthSquared = direction.X * direction.X + direction.Y * direction.Y;
            return lengthSquared < 0.0001 ? 0 : (delta.X * direction.X + delta.Y * direction.Y) / lengthSquared;
        }

        // Hit-test one cube's grab handles. Returns "X", "Y", "Z", or "Resize",
        // or null when the pointer is not over any handle.
        public static string HandleAt(Rect image, string mode, double angle, IEnumerable<Point> points, CubeInstance cube, Point at, double radius)
        {
            if (cube == null || (mode != "Pitch" && mode != "Isometric" && mode != "Vanishing")) return null;
            Point[] corners = CubeGeometry.Corners(image, mode, angle, points, cube);
            string[] axes = new string[4];
            Point[] spots = new Point[4];
            int count = 0;
            bool square = mode == "Pitch" && angle >= 89.5;
            axes[count] = "X"; spots[count] = corners[1]; count++;
            axes[count] = "Y"; spots[count] = corners[2]; count++;
            if (!square) { axes[count] = "Z"; spots[count] = corners[4]; count++; }
            axes[count] = "Resize"; spots[count] = corners[7]; count++;
            int nearest = -1; double best = radius;
            for (int i = 0; i < count; i++)
            {
                double d = (spots[i] - at).Length;
                if (d <= best) { best = d; nearest = i; }
            }
            return nearest < 0 ? null : axes[nearest];
        }

        // Pick the nearest cube by its projected center within the radius.
        public static int SelectAt(Rect image, string mode, double angle, IEnumerable<Point> points, IList<CubeInstance> cubes, Point at, double radius)
        {
            if (cubes == null || (mode != "Pitch" && mode != "Isometric" && mode != "Vanishing")) return -1;
            int nearest = -1; double best = radius;
            for (int i = 0; i < cubes.Count; i++)
            {
                Point[] cs = CubeGeometry.Corners(image, mode, angle, points, cubes[i]);
                double sx = 0, sy = 0;
                for (int k = 0; k < cs.Length; k++) { sx += cs[k].X; sy += cs[k].Y; }
                Point center = new Point(sx / cs.Length, sy / cs.Length);
                double d = (center - at).Length;
                if (d <= best) { best = d; nearest = i; }
            }
            return nearest;
        }
    }

    public static class Drawing
    {
        public static void Perspective(DrawingContext dc, ImageTile tile, IEnumerable<Point> points, bool quiet)
        {
            Rect image = tile.ImageBounds;
            Point[] list = new Point[0];
            foreach (var point in points) { var grew = new Point[list.Length + 1]; for (int i = 0; i < list.Length; i++) grew[i] = list[i]; grew[list.Length] = point; list = grew; }
            var lines = PerspectiveGeometry.Build(list, image);
            foreach (var line in lines)
            {
                if ((line.A - line.B).Length < 0.01) continue;
                Brush color = AxisColor(line.Axis, list.Length);
                dc.DrawLine(new Pen(color, line.Width), line.A, line.B);
            }
            if (quiet) return;
            var markers = PerspectiveGeometry.Markers(list, image);
            foreach (var marker in markers)
            {
                if (marker.Axis.Contains("+")) continue;
                dc.DrawText(new FormattedText(marker.Axis, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, BeholderWindow.Text, 1.0), marker.At + new Vector(8, -16));
                dc.DrawEllipse(marker.Color, new Pen(Brushes.Black, 2), marker.At, 5, 5);
                dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), marker.At, 2.6, 2.6);
            }
        }

        public static Brush AxisColor(string axis, int count)
        {
            if (axis.Equals("Horizon")) return BeholderWindow.Paint("#E4B84E");
            if (axis.Equals("X")) return BeholderWindow.Paint("#D2533B");
            if (axis.Equals("Y")) return BeholderWindow.Paint("#5FB0B8");
            if (axis.Equals("Z")) return BeholderWindow.Paint("#3F7FC4");
            if (axis.Equals("Z+")) return BeholderWindow.Paint("#BA7FD8");
            if (axis.Equals("X+")) return BeholderWindow.Paint("#E08A3C");
            return BeholderWindow.Paint("#5FB0B8");
        }

        public static void Cube(DrawingContext dc, Rect image, string mode, double degrees, IEnumerable<Point> points, IList<CubeInstance> cubes, int selectedIndex, bool interactive)
        {
            Brush green = BeholderWindow.Paint("#A030C868");
            Brush fill = BeholderWindow.Paint("#2E30C868");
            if (cubes == null || cubes.Count == 0) return;
            for (int i = 0; i < cubes.Count; i++)
            {
                CubeInstance cube = cubes[i];
                Point[] cs = CubeGeometry.Corners(image, mode, degrees, points, cube);
                if (mode == "Pitch" && degrees >= 89.5)
                {
                    dc.DrawGeometry(fill, null, Face(cs[0], cs[1], cs[3], cs[2]));
                }
                else
                {
                    dc.DrawGeometry(fill, null, Face(cs[0], cs[2], cs[6], cs[4]));
                    dc.DrawGeometry(fill, null, Face(cs[1], cs[3], cs[7], cs[5]));
                    dc.DrawGeometry(fill, null, Face(cs[0], cs[1], cs[5], cs[4]));
                    dc.DrawGeometry(fill, null, Face(cs[2], cs[3], cs[7], cs[6]));
                    dc.DrawGeometry(fill, null, Face(cs[0], cs[1], cs[3], cs[2]));
                    dc.DrawGeometry(fill, null, Face(cs[4], cs[5], cs[7], cs[6]));
                }
                foreach (var edge in CubeGeometry.Build(image, mode, degrees, points, cube))
                    dc.DrawLine(new Pen(green, edge.Width), edge.A, edge.B);
            }
            if (!interactive || selectedIndex < 0 || selectedIndex >= cubes.Count) return;
            Point[] corners = CubeGeometry.Corners(image, mode, degrees, points, cubes[selectedIndex]);
            if (mode == "Pitch" && degrees >= 89.5)
            {
                DrawHandle(dc, corners[1], AxisColor("X", 1));
                DrawHandle(dc, corners[2], AxisColor("Y", 1));
                DrawResizeHandle(dc, corners[7]);
            }
            else
            {
                DrawHandle(dc, corners[1], AxisColor("X", 1));
                DrawHandle(dc, corners[2], AxisColor("Y", 1));
                DrawHandle(dc, corners[4], AxisColor("Z", 1));
                DrawResizeHandle(dc, corners[7]);
            }
        }

        private static Geometry Face(Point a, Point b, Point c, Point d)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(a, true, true);
                context.LineTo(b, true, false);
                context.LineTo(c, true, false);
                context.LineTo(d, true, false);
            }
            geometry.Freeze(); return geometry;
        }

        private static void DrawHandle(DrawingContext dc, Point at, Brush fill)
        {
            if (double.IsNaN(at.X) || double.IsNaN(at.Y)) return;
            dc.DrawRectangle(fill, new Pen(Brushes.Black, 1.2), new Rect(at.X - 5, at.Y - 5, 10, 10));
        }

        private static void DrawResizeHandle(DrawingContext dc, Point at)
        {
            if (double.IsNaN(at.X) || double.IsNaN(at.Y)) return;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(at + new Vector(0, -7), true, true);
                context.LineTo(at + new Vector(7, 0), true, false);
                context.LineTo(at + new Vector(0, 7), true, false);
                context.LineTo(at + new Vector(-7, 0), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.2), geometry);
        }

        public static void Pitch(DrawingContext dc, Rect image, double degrees)
        {
            foreach (var line in PitchGeometry.Build(image, degrees)) DrawGrid(dc, line);
        }

        public static void Isometric(DrawingContext dc, Rect viewport, double degrees)
        {
            double s = 40;
            double k = Math.Max(0.05, Math.Tan(degrees * Math.PI / 180.0));
            int span = (int)((viewport.Height + viewport.Width) / s) + 4;
            double x0 = viewport.X, x1 = viewport.Right;
            for (int i = -span; i <= span; i++)
            {
                double c = i * s;
                PerspectiveLine line;
                line = PerspectiveGeometry.ClipSegment(new Point(x0 - 4 * s, c), new Point(x1 + 4 * s, c), viewport);
                if (line != null) DrawGrid(dc, line);
                line = PerspectiveGeometry.ClipSegment(new Point(x0 - 4 * s, c + k * (x0 - 4 * s)), new Point(x1 + 4 * s, c + k * (x1 + 4 * s)), viewport);
                if (line != null) DrawGrid(dc, line);
                line = PerspectiveGeometry.ClipSegment(new Point(x0 - 4 * s, c - k * (x0 - 4 * s)), new Point(x1 + 4 * s, c - k * (x1 + 4 * s)), viewport);
                if (line != null) DrawGrid(dc, line);
            }
        }

        private static void DrawGrid(DrawingContext dc, PerspectiveLine line)
        {
            dc.DrawLine(new Pen(Brushes.Black, 1.0), line.A, line.B);
        }

        public static void MarkerDot(DrawingContext dc, Point at, bool selected)
        {
            dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), at, 3.5, 3.5);
        }
    }
}
