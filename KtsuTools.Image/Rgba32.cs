// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Image;

/// <summary>An eight-bit straight-alpha colour, in the channel order ktsu.Imaging stores pixels in.</summary>
/// <param name="R">Red channel.</param>
/// <param name="G">Green channel.</param>
/// <param name="B">Blue channel.</param>
/// <param name="A">Alpha channel.</param>
internal readonly record struct Rgba32(byte R, byte G, byte B, byte A);
