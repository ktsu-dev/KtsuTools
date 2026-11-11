// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Image;

/// <summary>A rectangle of whole pixels: the left and top edges, and the size.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
internal readonly record struct PixelBounds(int X, int Y, int Width, int Height);
