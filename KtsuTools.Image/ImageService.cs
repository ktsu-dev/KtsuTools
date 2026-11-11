// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Image;

using System.Globalization;
using System.IO;

using ktsu.Semantics.Paths;

using ktsu.Imaging;

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
	/// <param name="padding">Padding in pixels added around the content. Must leave at least one pixel of content: <c>size - 2 * padding &gt;= 1</c>.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The number of successfully processed files.</returns>
	public static async Task<int> ProcessAsync(AbsoluteDirectoryPath inputPath, AbsoluteDirectoryPath outputPath, string color = "#FFFFFF", int size = 128, int padding = 0, CancellationToken ct = default)
	{
		Ensure.NotNull(inputPath);
		Ensure.NotNull(outputPath);
		Ensure.NotNull(color);

		string inputDir = inputPath.ToString();
		string outputDir = outputPath.ToString();

		if (!ValidateArguments(inputDir, outputDir, size, padding))
		{
			return 0;
		}

		Rgba32 targetColor = ParseHexColor(color);
		string[] files = [.. Directory.GetFiles(inputDir).Order(StringComparer.Ordinal)];
		HashSet<string> claimedOutputs = new(StringComparer.OrdinalIgnoreCase);
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
					bool succeeded = ProcessFileWithErrorHandling(file, outputDir, claimedOutputs, targetColor, size, padding);

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
		string? rangeError = ValidateSizeAndPadding(size, padding);
		if (rangeError is not null)
		{
			AnsiConsole.MarkupLine($"[red]Error: {rangeError.EscapeMarkup()}[/]");
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

	/// <summary>
	/// Checks the ranges of <c>--size</c> and <c>--padding</c> on their own, before any file is touched.
	/// Negative padding would make the content larger than <paramref name="size"/>, and the final pad
	/// cannot shrink it, so the output would exceed the documented maximum. Padding on both sides must
	/// leave at least one pixel of content, so <c>--size 1 --padding 0</c> and <c>--size 3 --padding 1</c>
	/// are valid while <c>--size 2 --padding 1</c> is not.
	/// </summary>
	/// <param name="size">The requested <c>--size</c>.</param>
	/// <param name="padding">The requested <c>--padding</c>.</param>
	/// <returns>The message for the first value out of range, or <see langword="null"/> when both are valid.</returns>
	public static string? ValidateSizeAndPadding(int size, int padding)
	{
		if (size < 1)
		{
			return "--size must be at least 1";
		}

		if (padding < 0)
		{
			return "--padding must be zero or greater";
		}

		// Written as a division so a huge --padding cannot overflow 2 * padding.
		int maxPadding = (size - 1) / 2;
		if (padding > maxPadding)
		{
			return $"--padding must be at most {maxPadding} for --size {size}, so at least 1 pixel of content remains";
		}

		return null;
	}

	/// <summary>
	/// The path a processed image is written to. The output is always PNG, so it takes the
	/// <c>.png</c> extension whatever the input's was.
	/// </summary>
	internal static string OutputFilePathFor(string filePath, string outputPath) =>
		Path.Join(outputPath, Path.ChangeExtension(Path.GetFileName(filePath), ".png"));

	private static bool ProcessFileWithErrorHandling(string file, string outputPath, HashSet<string> claimedOutputs, Rgba32 targetColor, int size, int padding)
	{
		if (file.Contains(".new.png", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string outputFilePath = OutputFilePathFor(file, outputPath);
		if (!claimedOutputs.Add(outputFilePath))
		{
			// a.png and a.jpg both become a.png; the first one processed keeps the name.
			AnsiConsole.MarkupLine($"[yellow]Skipped {Path.GetFileName(file).EscapeMarkup()}: another input in this batch is already written to {Path.GetFileName(outputFilePath).EscapeMarkup()}.[/]");
			return false;
		}

		try
		{
			AnsiConsole.MarkupLine($"Processing [blue]{Path.GetFileName(file).EscapeMarkup()}[/]...");
			if (ProcessSingleImage(file, outputFilePath, targetColor, size, padding))
			{
				return true;
			}

			AnsiConsole.MarkupLine($"[yellow]Skipped {Path.GetFileName(file).EscapeMarkup()}: it has no opaque pixels to crop to.[/]");
		}
		catch (InvalidImageDataException e)
		{
			AnsiConsole.MarkupLine($"[yellow]Skipped {Path.GetFileName(file).EscapeMarkup()}: {e.Message.EscapeMarkup()}[/]");
		}
		catch (ArgumentException e)
		{
			// A size or padding the image cannot satisfy fails this one file, not the rest of the batch.
			AnsiConsole.MarkupLine($"[yellow]Skipped {Path.GetFileName(file).EscapeMarkup()}: {e.Message.EscapeMarkup()}[/]");
		}

		return false;
	}

	private static bool ProcessSingleImage(string filePath, string outputFilePath, Rgba32 targetColor, int size, int padding)
	{
		ImagePixels image = ImageDecoder.Load(filePath);

		// Convert to black and white
		ApplyBlackWhite(image);

		// Find the highest tonal value among non-transparent pixels
		byte maxValue = FindMaxTonalValue(image);

		bool isBlack = maxValue == 0;
		maxValue = isBlack ? (byte)255 : maxValue;

		// Recolor pixels and find tight bounding box of non-transparent content
		PixelBounds? bounds = RecolorAndFindBounds(image, targetColor, maxValue, isBlack);
		if (bounds is null)
		{
			return false;
		}

		// Crop, pad to square, resize, and add final padding
		CropResizeAndSave(image, outputFilePath, bounds.Value, size, padding);
		return true;
	}

	/// <summary>
	/// The black and white colour matrix: every colour channel becomes <c>1.5 * (r + g + b) - 1</c>,
	/// clamped to the unit range, and alpha is left alone. It is the filter ImageSharp's
	/// <c>BlackWhite()</c> applied, reproduced here so the tonal values the rest of the pipeline
	/// reads stay the same.
	/// </summary>
	internal static void ApplyBlackWhite(ImagePixels image)
	{
		Span<byte> pixels = image.Pixels;
		for (int i = 0; i < pixels.Length; i += 4)
		{
			float sum = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 255f;
			float value = Math.Clamp((1.5f * sum) - 1f, 0f, 1f);
			byte tone = (byte)((value * 255f) + 0.5f);
			pixels[i] = tone;
			pixels[i + 1] = tone;
			pixels[i + 2] = tone;
		}
	}

	private static byte FindMaxTonalValue(ImagePixels image)
	{
		byte maxValue = 0;
		ReadOnlySpan<byte> pixels = image.ReadOnlyPixels;

		for (int i = 0; i < pixels.Length; i += 4)
		{
			if (pixels[i + 3] != 0)
			{
				maxValue = Math.Max(maxValue, pixels[i]);
			}
		}

		return maxValue;
	}

	/// <summary>
	/// Recolors the image and returns the bounding box of its opaque pixels, or null when it has none.
	/// </summary>
	private static PixelBounds? RecolorAndFindBounds(ImagePixels image, Rgba32 targetColor, byte maxValue, bool isBlack)
	{
		int top = image.Height;
		int left = image.Width;
		int right = 0;
		int bottom = 0;

		for (int y = 0; y < image.Height; y++)
		{
			Span<byte> pixelRow = image.GetRow(y);

			for (int x = 0; x < image.Width; x++)
			{
				Span<byte> pixel = pixelRow.Slice(x * 4, 4);
				byte newValue = ComputeNewValue(pixel[0], maxValue, isBlack);

				if (pixel[3] != 0)
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

				pixel[0] = (byte)(newValue / 255f * targetColor.R);
				pixel[1] = (byte)(newValue / 255f * targetColor.G);
				pixel[2] = (byte)(newValue / 255f * targetColor.B);
			}
		}

		if (right < left || bottom < top)
		{
			return null;
		}

		// right and bottom are the indices of the last opaque column and row, so both are inclusive.
		return new PixelBounds(left, top, right - left + 1, bottom - top + 1);
	}

	private static byte ComputeNewValue(byte tone, byte maxValue, bool isBlack) =>
		(byte)(isBlack ? 255 : 255 - (maxValue - tone));

	private static void CropResizeAndSave(ImagePixels image, string outputFilePath, PixelBounds bounds, int size, int padding)
	{
		int newSize = Math.Max(bounds.Width, bounds.Height);

		// Intentionally only shrink, never grow
		int finalSize = Math.Min(newSize, size);
		int finalContentSize = finalSize - (padding * 2);

		ImagePixels result = image
			.Crop(bounds.X, bounds.Y, bounds.Width, bounds.Height)
			.Pad(newSize, newSize, 0, 0, 0, 0)
			.Resize(finalContentSize, finalContentSize)
			.Pad(finalSize, finalSize, 0, 0, 0, 0);

		PngEncoder.Save(result, outputFilePath, new PngEncoderOptions { ClearTransparentPixels = true });
	}

	internal static Rgba32 ParseHexColor(string hex)
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
