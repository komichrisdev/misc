using System;
using System.Collections.Generic;
using System.Windows;

namespace Beholder
{
    // Contiguous justified rows keep reading order stable. Row widths follow image
    // proportions; choose the row partition that uses the most uncropped pixels.
    public static class TileLayout
    {
        public const double CaptionHeight = 24;

        public static List<Rect> Arrange(double width, double height, IList<double> aspects, bool equal)
        {
            var output = new List<Rect>();
            int n = aspects.Count;
            if (n == 0 || width <= 0 || height <= 0) return output;
            double gutter = Gap(width, height, aspects.Count);
            if (equal)
            {
                int bestColumns = 1;
                double bestScore = double.NegativeInfinity;
                for (int cols = 1; cols <= n; cols++)
                {
                    int rows = (n + cols - 1) / cols;
                    double w = Math.Max(1, (width - gutter * (cols - 1)) / cols);
                    double h = Math.Max(1, (height - gutter * (rows - 1)) / rows);
                    double score = 0;
                    for (int i = 0; i < n; i++) score += VisibleArea(w, Math.Max(1, h - CaptionHeight), SafeAspect(aspects[i]));
                    if (score > bestScore) { bestScore = score; bestColumns = cols; }
                }
                int rowCount = (n + bestColumns - 1) / bestColumns;
                double cellW = Math.Max(0.0000001, (width - gutter * (bestColumns - 1)) / bestColumns);
                double cellH = Math.Max(0.0000001, (height - gutter * (rowCount - 1)) / rowCount);
                for (int i = 0; i < n; i++) output.Add(new Rect((i % bestColumns) * (cellW + gutter), (i / bestColumns) * (cellH + gutter), cellW, cellH));
                return output;
            }
            // Two-image comparison is always side-by-side, even for tall portraits.
            if (n <= 2)
            {
                double total = 0;
                foreach (double a in aspects) total += SafeAspect(a);
                double x = 0;
                for (int i = 0; i < n; i++)
                {
                    double w = (width - gutter * (n - 1)) * SafeAspect(aspects[i]) / total;
                    output.Add(new Rect(x, 0, Math.Max(0.0000001, w), height));
                    x += w + gutter;
                }
                return output;
            }
            var prefix = new double[n + 1];
            for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + SafeAspect(aspects[i]);
            List<int> bestBreaks = null;
            double bestArea = double.NegativeInfinity;
            int maxRows = Math.Min(n, Math.Max(1, (int)(height / 64)));
            for (int rows = 1; rows <= maxRows; rows++)
            {
                double targetHeight = Math.Max(1, (height - gutter * (rows - 1)) / rows);
                var cost = new double[rows + 1, n + 1];
                var previous = new int[rows + 1, n + 1];
                for (int r = 0; r <= rows; r++) for (int end = 0; end <= n; end++) cost[r, end] = double.PositiveInfinity;
                cost[0, 0] = 0;
                for (int r = 1; r <= rows; r++)
                {
                    for (int end = r; end <= n; end++)
                    {
                        for (int start = r - 1; start < end; start++)
                        {
                            if (double.IsInfinity(cost[r - 1, start])) continue;
                            double available = width - gutter * (end - start - 1);
                            if (available <= 0) continue;
                            double idealHeight = available / (prefix[end] - prefix[start]) + CaptionHeight;
                            double mismatch = Math.Log(idealHeight / targetHeight);
                            double candidate = cost[r - 1, start] + mismatch * mismatch;
                            if (candidate < cost[r, end]) { cost[r, end] = candidate; previous[r, end] = start; }
                        }
                    }
                }
                if (double.IsInfinity(cost[rows, n])) continue;
                var breaks = new List<int>();
                int e = n;
                for (int r = rows; r > 0; r--) { breaks.Insert(0, e); e = previous[r, e]; }
                var rects = Rows(width, height, aspects, breaks);
                double area = 0;
                for (int i = 0; i < n; i++) area += VisibleArea(rects[i].Width, Math.Max(1, rects[i].Height - CaptionHeight), SafeAspect(aspects[i]));
                if (area > bestArea) { bestArea = area; bestBreaks = breaks; }
            }
            return Rows(width, height, aspects, bestBreaks ?? new List<int> { n });
        }

        private static List<Rect> Rows(double width, double height, IList<double> aspects, List<int> breaks)
        {
            double gutter = Gap(width, height, aspects.Count);
            var result = new List<Rect>();
            var ideals = new List<double>();
            int start = 0;
            double total = 0;
            foreach (int end in breaks)
            {
                double sum = 0;
                for (int i = start; i < end; i++) sum += SafeAspect(aspects[i]);
                double h = Math.Max(0.0000001, width - gutter * (end - start - 1)) / sum + CaptionHeight;
                ideals.Add(h); total += h; start = end;
            }
            double availableH = Math.Max(0.0000001, height - gutter * (breaks.Count - 1));
            double y = 0;
            start = 0;
            for (int row = 0; row < breaks.Count; row++)
            {
                int end = breaks[row];
                double sum = 0;
                for (int i = start; i < end; i++) sum += SafeAspect(aspects[i]);
                double h = ideals[row] / total * availableH;
                double availableW = Math.Max(0.0000001, width - gutter * (end - start - 1));
                double x = 0;
                for (int i = start; i < end; i++)
                {
                    double w = availableW * SafeAspect(aspects[i]) / sum;
                    result.Add(new Rect(x, y, w, h)); x += w + gutter;
                }
                y += h + gutter; start = end;
            }
            return result;
        }

        private static double Gap(double width, double height, int count)
        {
            return Math.Max(0, Math.Min(8, Math.Min((width - 0.001) / Math.Max(1, count - 1), (height - 0.001) / Math.Max(1, count - 1))));
        }

        public static double SafeAspect(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? 1 : Math.Max(0.08, Math.Min(12, value));
        }

        private static double VisibleArea(double width, double height, double aspect)
        {
            double imageW = Math.Min(width, height * aspect);
            return imageW * imageW / aspect;
        }
    }
}
