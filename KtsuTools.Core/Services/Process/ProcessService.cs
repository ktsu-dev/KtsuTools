// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.Services.Process;

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class ProcessService : IProcessService
{
	public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
		RunAsync(command, arguments, workingDirectory, null, ct);

	public async Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default)
	{
		List<string> outputLines = [];
		List<string> errorLines = [];

		ProcessStartInfo startInfo = new()
		{
			FileName = command,
			Arguments = arguments,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};

		if (workingDirectory is not null)
		{
			startInfo.WorkingDirectory = workingDirectory;
		}

		if (environmentVariables is not null)
		{
			foreach ((string key, string value) in environmentVariables)
			{
				startInfo.Environment[key] = value;
			}
		}

		using Process process = new() { StartInfo = startInfo };

		process.OutputDataReceived += (_, e) =>
		{
			if (e.Data is not null)
			{
				lock (outputLines)
				{
					outputLines.Add(e.Data);
				}
			}
		};

		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data is not null)
			{
				lock (errorLines)
				{
					errorLines.Add(e.Data);
				}
			}
		};

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		await process.WaitForExitAsync(ct).ConfigureAwait(false);

		return new ProcessResult(process.ExitCode, outputLines, errorLines);
	}
}
