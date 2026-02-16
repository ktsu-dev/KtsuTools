// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace KtsuTools.Infrastructure;

using System;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
	private readonly IServiceCollection _services = services;

	public ITypeResolver Build() => new TypeResolver(_services.BuildServiceProvider());

	public void Register(Type service, Type implementation) => _services.AddSingleton(service, implementation);

	public void RegisterInstance(Type service, object implementation) => _services.AddSingleton(service, implementation);

	public void RegisterLazy(Type service, Func<object> factory) => _services.AddSingleton(service, _ => factory());
}

public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
	private readonly IServiceProvider _provider = provider;

	public object? Resolve(Type? type) =>
		type is null ? null : _provider.GetService(type);

	public void Dispose()
	{
		if (_provider is IDisposable disposable)
		{
			disposable.Dispose();
		}
	}
}
