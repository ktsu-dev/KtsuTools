// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Test;

using KtsuTools.Image;
using SixLabors.ImageSharp.PixelFormats;

[TestClass]
public class ImageServiceTests
{
	[TestMethod]
	public void ParseHexColorAcceptsSixDigitForm()
	{
		Rgba32 color = ImageService.ParseHexColor("#FF8800");
		Assert.AreEqual((byte)0xFF, color.R);
		Assert.AreEqual((byte)0x88, color.G);
		Assert.AreEqual((byte)0x00, color.B);
		Assert.AreEqual((byte)0xFF, color.A);
	}

	[TestMethod]
	public void ParseHexColorAcceptsThreeDigitShorthand()
	{
		Rgba32 color = ImageService.ParseHexColor("#F80");
		Assert.AreEqual((byte)0xFF, color.R);
		Assert.AreEqual((byte)0x88, color.G);
		Assert.AreEqual((byte)0x00, color.B);
	}

	[TestMethod]
	public void ParseHexColorAcceptsHexWithoutHashPrefix()
	{
		Rgba32 color = ImageService.ParseHexColor("ABCDEF");
		Assert.AreEqual((byte)0xAB, color.R);
		Assert.AreEqual((byte)0xCD, color.G);
		Assert.AreEqual((byte)0xEF, color.B);
	}

	[TestMethod]
	public void ParseHexColorRejectsInvalidLength() =>
		Assert.ThrowsExactly<ArgumentException>(() => ImageService.ParseHexColor("#1234"));
}
