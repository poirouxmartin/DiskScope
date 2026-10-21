using System;

namespace DiskScope.Storage.Treemap;

/// <summary>
/// Axis-aligned rectangle in layout units. Width and Height are never negative when produced by this code.
/// </summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;

    /// <summary>Half-open containment: left and top edges are inside, right and bottom edges are outside.</summary>
    public bool Contains(double px, double py) => px >= X && px < X + Width && py >= Y && py < Y + Height;

    /// <summary>Shrinks each side by <paramref name="d"/>. Width and Height are clamped at 0.</summary>
    public LayoutRect Inset(double d) =>
        new(X + d, Y + d, Math.Max(0.0, Width - 2 * d), Math.Max(0.0, Height - 2 * d));
}
