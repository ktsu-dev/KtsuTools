// Copyright (c) 2023-2026 ktsu-dev contributors

namespace KtsuTools.Core.Services.Process;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public record ProcessResult(int ExitCode, IReadOnlyList<string> Output, IReadOnlyList<string> Errors);

public interface IProcessService
{
	public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default);
	public Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory, IDictionary<string, string>? environmentVariables, CancellationToken ct = default);

	/// <summary>
	/// Runs <paramref name="command"/> with each element of <paramref name="arguments"/> passed as exactly one
	/// argument, so a value containing spaces or quotes reaches the process intact instead of being split.
	/// </summary>
	/// <remarks>
	/// The default quotes each argument by the rules process start-up uses to split an argument string, and
	/// passes the result to the string overload, so an implementation that only records or scripts string
	/// arguments keeps working. <see cref="ProcessService"/> overrides it to pass the list through unchanged.
	/// </remarks>
	public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken ct = default)
	{
		Ensure.NotNull(arguments);
		return RunAsync(command, string.Join(" ", arguments.Select(QuoteArgument)), workingDirectory, ct);
	}

	/// <summary>
	/// Quotes <paramref name="argument"/> so that splitting an argument string yields it back unchanged: it is
	/// wrapped in quotes when empty or when it contains whitespace or a quote, an embedded quote is escaped,
	/// and the backslashes before a quote or the closing quote are doubled.
	/// </summary>
	public static string QuoteArgument(string argument)
	{
		Ensure.NotNull(argument);

		if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
		{
			return argument;
		}

		StringBuilder quoted = new("\"");
		int backslashes = 0;
		foreach (char c in argument)
		{
			if (c == '\\')
			{
				backslashes++;
				continue;
			}

			_ = quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
			_ = quoted.Append(c);
			backslashes = 0;
		}

		return quoted.Append('\\', backslashes * 2).Append('"').ToString();
	}
}
