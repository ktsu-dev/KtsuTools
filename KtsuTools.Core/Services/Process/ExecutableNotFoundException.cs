// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Core.Services.Process;

using System;

/// <summary>
/// Thrown by <see cref="ProcessService"/> when the executable it was asked to run could not be found,
/// so a caller can say which tool is missing instead of reporting the operating system's error, which
/// names the working directory and reads as though that folder were missing.
/// </summary>
public class ExecutableNotFoundException(string command, Exception? innerException)
	: Exception($"'{command}' was not found. Install it, or add it to PATH.", innerException)
{
	public ExecutableNotFoundException()
		: this(string.Empty)
	{
	}

	public ExecutableNotFoundException(string command)
		: this(command, null)
	{
	}

	/// <summary>Gets the executable that could not be started, as it was asked for.</summary>
	public string Command { get; } = command;
}
