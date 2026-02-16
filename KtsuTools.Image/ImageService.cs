// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Image;

using System.Globalization;
using System.IO;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using Spectre.Console;

/// <summary>
/// Provides image processing capabilities ported from the IconHelper tool.
/// Converts images to a target color, crops to content bounds, and resizes to a target size.
/// </summary>
public static class ImageService
{
	/// <summary>
	/// Processes all image files in the input directory, applying color conversion,
	/// cropping, resizing, and padding, then saves the results to the output directory.
	/// </summary>
	/// <param name="inputPath">Directory containing source image files.</param>
	/// <param name="outputPath">Directory where processed images will be saved.</param>
	/// <param name="color">Target color as a hex string (e.g., "#FFFFFF").</param>
	/// <param name="size">Maximum output size in pixels.</param>
	/// <param name="padding">Padding in pixels added around the content. Must be less than size / 2.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The number of successfully processed files.</returns>
	public static async Task<int> ProcessAsync(string inputPath, string outputPath, string color = "#FFFFFF", int size = 128, int padding = 0, CancellationToken ct = default)
	{
		Ensure.NotNull(color);

		if (!ValidateArguments(inputPath, outputPath, size, padding))
		{
			return 0;
		}

		Rgba32 targetColor = ParseHexColor(color);
		string[] files = Directory.GetFiles(inputPath);
		int processedCount = 0;

		await AnsiConsole.Progress()
			.AutoClear(false)
			.Columns(
				new TaskDescriptionColumn(),
				new ProgressBarColumn(),
				new PercentageColumn(),
				new SpinnerColumn())
			.StartAsync(async progressContext =>
			{
				ProgressTask task = progressContext.AddTask("[green]Processing images[/]", maxValue: files.Length);

				foreach (string file in files)
				{
					ct.ThrowIfCancellationRequested();
					bool succeeded = ProcessFileWithErrorHandling(file, outputPath, targetColor, size, padding);

					if (succeeded)
					{
						processedCount++;
					}

					task.Increment(1);
				}

				await Task.CompletedTask.ConfigureAwait(false);
			}).ConfigureAwait(false);

		AnsiConsole.MarkupLine($"[green]Done. Processed {processedCount} file(s).[/]");
		return processedCount;
	}

	private static bool ValidateArguments(string inputPath, string outputPath, int size, int padding)
	{
		if (padding >= size / 2)
		{
			AnsiConsole.MarkupLine("[red]Error: Padding must be less than half the size of the image.[/]");
			return false;
		}

		if (!Directory.Exists(inputPath))
		{
			AnsiConsole.MarkupLine($"[red]Error: Input directory does not exist: {inputPath.EscapeMarkup()}[/]");
			return false;
		}

		if (!Directory.Exists(outputPath))
		{
			Directory.CreateDirectory(outputPath);
		}

		return true;
	}

	private static bool ProcessFileWithErrorHandling(string file, string outputPath, Rgba32 targetColor, int size, int padding)
	{
		if (file.Contains(".new.png", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		try
		{
			AnsiConsole.MarkupLine($"Processing [blue]{Path.GetFileName(file).EscapeMarkup()}[/]...");
			ProcessSingleImage(file, outputPath, targetColor, size, padding);
			return true;
		}
		catch (ImageProcessingException e)
		{
			AnsiConsole.MarkupLine($"[yellow]Skipped {Path.GetFileName(file).EscapeMarkup()}: {e.Message.EscapeMarkup()}[/]");
		}
		catch (InvalidImageContentException e)
		{
			AnsiConsole.MarkupLine($"[yellow]Skipped {Path.GetFileName(file).EscapeMarkup()}: {e.Message.EscapeMarkup()}[/]");
		}
		catch (UnknownImageFormatException e)
		{
			AnsiConsole.MarkupLine($"[yellow]Skipped {Path.GetFileName(file).EscapeMarkup()}: {e.Message.EscapeMarkup()}[/]");
		}

		return false;
	}

	private static void ProcessSingleImage(string filePath, string outputPath, Rgba32 targetColor, int size, int padding)
	{
		Image<Rgba32> image = Image.Load<Rgba32>(filePath);

		// Convert to black and white
		image.Mutate(ctx => ctx.BlackWhite());

		// Find the highest tonal value among non-transparent pixels
		byte maxValue = FindMaxTonalValue(image);

		bool isBlack = maxValue == 0;
		maxValue = isBlack ? (byte)255 : maxValue;

		// Recolor pixels and find tight bounding box of non-transparent content
		Rectangle bounds = RecolorAndFindBounds(image, targetColor, maxValue, isBlack);

		// Crop, pad to square, resize, and add final padding
		CropResizeAndSave(image, filePath, outputPath, bounds, size, padding);
	}

	private static byte FindMaxTonalValue(Image<Rgba32> image)
	{
		byte maxValue = 0;

		image.ProcessPixelRows(accessor =>
		{
			for (int y = 0; y < accessor.Height; y++)
			{
				Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

				for (int x = 0; x < pixelRow.Length; x++)
				{
					ref Rgba32 pixel = ref pixelRow[x];

					if (pixel.A != 0)
					{
						maxValue = Math.Max(maxValue, pixel.R);
					}
				}
			}
		});

		return maxValue;
	}

	private static Rectangle RecolorAndFindBounds(Image<Rgba32> image, Rgba32 targetColor, byte maxValue, bool isBlack)
	{
		int top = image.Height;
		int left = image.Width;
		int right = 0;
		int bottom = 0;

		image.ProcessPixelRows(accessor =>
		{
			for (int y = 0; y < accessor.Height; y++)
			{
				Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

				for (int x = 0; x < pixelRow.Length; x++)
				{
					ref Rgba32 pixel = ref pixelRow[x];
					byte newValue = ComputeNewValue(pixel, maxValue, isBlack);

					if (pixel.A != 0)
					{
						left = Math.Min(left, x);
						top = Math.Min(top, y);
						right = Math.Max(right, x);
						bottom = Math.Max(bottom, y);
					}
					else
					{
						newValue = 0;
					}

					pixel.R = (byte)(newValue / 255f * targetColor.R);
					pixel.G = (byte)(newValue / 255f * targetColor.G);
					pixel.B = (byte)(newValue / 255f * targetColor.B);
				}
			}
		});

		int minWidth = right - left;
		int minHeight = bottom - top;
		return new Rectangle(left, top, minWidth, minHeight);
	}

	private static byte ComputeNewValue(Rgba32 pixel, byte maxValue, bool isBlack) =>
		(byte)(isBlack ? 255 : 255 - (maxValue - pixel.R));

	private static void CropResizeAndSave(Image<Rgba32> image, string filePath, string outputPath, Rectangle bounds, int size, int padding)
	{
		int newSize = Math.Max(bounds.Width, bounds.Height);

		// Intentionally only shrink, never grow
		int finalSize = Math.Min(newSize, size);
		int finalContentSize = finalSize - (padding * 2);
		Rgba32 paddingColor = Rgba32.ParseHex("00000000");

		image.Mutate(ctx => ctx
			.Crop(new Rectangle
			{
				Width = bounds.Width,
				Height = bounds.Height,
				X = bounds.X,
				Y = bounds.Y,
			})
			.Pad(newSize, newSize, paddingColor)
			.Resize(finalContentSize, finalContentSize)
			.Pad(finalSize, finalSize, paddingColor));

		string outputFilePath = Path.Join(outputPath, Path.GetFileName(filePath));

		image.SaveAsPng(outputFilePath, new PngEncoder
		{
			BitDepth = PngBitDepth.Bit8,
			ColorType = PngColorType.RgbWithAlpha,
			TransparentColorMode = PngTransparentColorMode.Clear,
		});
	}

	private static Rgba32 ParseHexColor(string hex)
	{
		string normalized = hex.TrimStart('#');

		if (normalized.Length == 3)
		{
			// Expand shorthand (#RGB -> #RRGGBB)
			normalized = string.Create(6, normalized, (span, src) =>
			{
				span[0] = src[0];
				span[1] = src[0];
				span[2] = src[1];
				span[3] = src[1];
				span[4] = src[2];
				span[5] = src[2];
			});
		}

		if (normalized.Length != 6)
		{
			throw new ArgumentException($"Invalid hex color format: {hex}", nameof(hex));
		}

		byte r = byte.Parse(normalized.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		byte g = byte.Parse(normalized.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		byte b = byte.Parse(normalized.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

		return new Rgba32(r, g, b, 255);
	}
}
