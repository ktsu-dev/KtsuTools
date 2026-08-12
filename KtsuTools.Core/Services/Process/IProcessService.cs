// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Core.Services.Process;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public record ProcessResult(int ExitCode, IReadOnlyList<string> Output, IReadOnlyList<string> Errors);

public interface IProcessService
{
	public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default);
	public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default);
}
