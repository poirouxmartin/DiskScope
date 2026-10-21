using System;

namespace DiskScope.Storage.Treemap;

/// <summary>
/// Squarified treemap layout (Bruls, Huizing, van Wijk 2000). Rows are filled along the shorter side
/// while the worst aspect ratio does not increase.
/// </summary>
public static class SquarifiedLayout
{
    /// <summary>
    /// One rect per size, same order, areas proportional to the sizes, tiling <paramref name="bounds"/>.
    /// <paramref name="sizesDescending"/> must be sorted descending. Only the sign is checked (negative throws).
    /// Zero sizes get an empty rect at (bounds.X, bounds.Y). Bounds with non-positive Width or Height give empty rects.
    /// </summary>
    public static LayoutRect[] Compute(ReadOnlySpan<long> sizesDescending, LayoutRect bounds)
    {
        int n = sizesDescending.Length;
        var result = new LayoutRect[n];
        var empty = new LayoutRect(bounds.X, bounds.Y, 0, 0);

        double total = 0;
        for (int i = 0; i < n; i++)
        {
            if (sizesDescending[i] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizesDescending), sizesDescending[i], "Sizes must not be negative.");
            }

            total += sizesDescending[i];
        }

        if (total == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            Array.Fill(result, empty);
            return result;
        }

        // Sorted descending, so zero sizes form a suffix. Only the positive prefix is laid out.
        int m = 0;
        while (m < n && sizesDescending[m] > 0)
        {
            m++;
        }

        for (int i = m; i < n; i++)
        {
            result[i] = empty;
        }

        double scale = bounds.Area / total;
        var area = new double[m];
        for (int i = 0; i < m; i++)
        {
            area[i] = sizesDescending[i] * scale;
        }

        double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
        int start = 0;
        while (start < m)
        {
            double side = Math.Min(w, h);
            if (side <= 0)
            {
                // Rounding exhausted the remaining rectangle before the last items were placed.
                for (int i = start; i < m; i++)
                {
                    result[i] = new LayoutRect(x, y, 0, 0);
                }

                break;
            }

            // Grow the row while the worst aspect ratio does not increase. The first item is always taken,
            // because the initial worst value is +infinity.
            int end = start;
            double sum = 0;
            double worst = double.PositiveInfinity;
            while (end < m)
            {
                double candidateSum = sum + area[end];
                double candidateWorst = WorstRatio(area[start], area[end], candidateSum, side);
                if (candidateWorst > worst)
                {
                    break;
                }

                worst = candidateWorst;
                sum = candidateSum;
                end++;
            }

            if (w >= h)
            {
                // Shorter side is the height: the row becomes a column on the left.
                double thickness = sum / h;
                double cy = y;
                for (int i = start; i < end; i++)
                {
                    double rectHeight = area[i] / thickness;
                    result[i] = new LayoutRect(x, cy, thickness, rectHeight);
                    cy += rectHeight;
                }

                x += thickness;
                w = Math.Max(0.0, w - thickness);
            }
            else
            {
                // Shorter side is the width: the row becomes a strip along the top.
                double thickness = sum / w;
                double cx = x;
                for (int i = start; i < end; i++)
                {
                    double rectWidth = area[i] / thickness;
                    result[i] = new LayoutRect(cx, y, rectWidth, thickness);
                    cx += rectWidth;
                }

                y += thickness;
                h = Math.Max(0.0, h - thickness);
            }

            start = end;
        }

        return result;
    }

    /// <summary>Worst aspect ratio of a row with the given largest and smallest area, total area and side length.</summary>
    private static double WorstRatio(double rowMax, double rowMin, double sum, double side)
    {
        double s2 = sum * sum;
        double w2 = side * side;
        return Math.Max(w2 * rowMax / s2, s2 / (w2 * rowMin));
    }
}
