// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.UI;

using System;
using System.Threading;

/// <summary>
/// Scopes a <see cref="CancellationTokenSource"/> to the lifetime of a command, wiring
/// <see cref="Console.CancelKeyPress"/> on construction and unsubscribing on disposal so the
/// handler does not leak past the scope.
/// </summary>
public sealed class CtrlCScope : IDisposable
{
	private readonly CancellationTokenSource cts = new();
	private readonly ConsoleCancelEventHandler handler;
	private bool disposed;

	public CtrlCScope()
	{
		handler = (_, e) =>
		{
			e.Cancel = true;
			cts.Cancel();
		};
		Console.CancelKeyPress += handler;
	}

	public CancellationToken Token => cts.Token;

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		Console.CancelKeyPress -= handler;
		cts.Dispose();
		disposed = true;
	}
}
