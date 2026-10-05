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
            if (owner.IsometricEnabled)
            {
                Rect viewport = new Rect(0, 0, tile.Viewport.ActualWidth, tile.Viewport.ActualHeight);
                dc.DrawRectangle(CheckerBrush(), null, viewport);
                Drawing.Isometric(dc, viewport, owner.IsometricAngleDegrees);
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
            if (axis.Equals("Y")) return BeholderWindow.Paint("#4FA06A");
            if (axis.Equals("Z")) return BeholderWindow.Paint("#3F7FC4");
            if (axis.Equals("Z+")) return BeholderWindow.Paint("#BA7FD8");
            if (axis.Equals("X+")) return BeholderWindow.Paint("#E08A3C");
            return BeholderWindow.Paint("#5FB0B8");
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
            dc.DrawLine(new Pen(BeholderWindow.Paint("#7EE07E"), 1.0), line.A, line.B);
        }

        public static void MarkerDot(DrawingContext dc, Point at, bool selected)
        {
            dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), at, 3.5, 3.5);
        }
    }
}
