// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Core.Services.Process;

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class ProcessService : IProcessService
{
	public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
		RunAsync(command, arguments, workingDirectory, null, ct);

	public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default) =>
		RunAsync(new ProcessStartInfo { FileName = command, Arguments = arguments }, workingDirectory, environmentVariables, ct);

	public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken ct = default)
	{
		Ensure.NotNull(arguments);

		ProcessStartInfo startInfo = new() { FileName = command };
		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		return RunAsync(startInfo, workingDirectory, null, ct);
	}

	private static async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct)
	{
		List<string> outputLines = [];
		List<string> errorLines = [];

		startInfo.UseShellExecute = false;
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardError = true;
		startInfo.CreateNoWindow = true;

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
