// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Core.UI;

using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;

public abstract class LiveDashboard<TData>
{
	protected virtual int RefreshMs => 1000;

	protected abstract Task<TData> FetchDataAsync(CancellationToken ct);
	protected abstract IRenderable Render(TData data);

	public async Task RunAsync(CancellationToken ct)
	{
		TData data = await FetchDataAsync(ct).ConfigureAwait(false);

		await AnsiConsole.Live(Render(data))
			.AutoClear(false)
			.Overflow(VerticalOverflow.Ellipsis)
			.StartAsync(async ctx =>
			{
				while (!ct.IsCancellationRequested)
				{
					try
					{
						data = await FetchDataAsync(ct).ConfigureAwait(false);
						ctx.UpdateTarget(Render(data));
					}
					catch (TaskCanceledException)
					{
						break;
					}

					await Task.Delay(RefreshMs, ct).ConfigureAwait(false);
				}
			}).ConfigureAwait(false);
	}
}
