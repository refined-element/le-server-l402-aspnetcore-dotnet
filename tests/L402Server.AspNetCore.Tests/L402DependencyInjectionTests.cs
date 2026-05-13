using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace L402Server.AspNetCore.Tests;

/// <summary>
/// Regression tests for DI registration shape. The L402 middleware is
/// CONVENTION-BASED (its constructor takes <c>RequestDelegate next</c>) — it
/// must NOT be registered in the DI container as a concrete service.
/// <para>
/// In 0.1.1 we accidentally did <c>services.TryAddTransient&lt;L402Middleware&gt;()</c>
/// inside <see cref="L402ApplicationBuilderExtensions.AddL402AspNetCore"/>.
/// That caused <c>WebApplication.CreateBuilder().Build()</c> to throw at startup
/// because ASP.NET Core's <c>ValidateOnBuild</c> (on by default in Development)
/// tries to construct every registered service, and <see cref="Microsoft.AspNetCore.Http.RequestDelegate"/>
/// is not in DI — it's supplied by <c>UseMiddleware&lt;T&gt;()</c> at pipeline time.
/// </para>
/// These tests lock the invariant: the published DI graph must validate cleanly.
/// </summary>
public class L402DependencyInjectionTests
{
    [Fact]
    public void AddL402AspNetCore_ProducesValidatableServiceProvider()
    {
        // This is the build that ASP.NET Core does in Development mode. It throws
        // if any registered service cannot be constructed via its DI-discovered
        // constructor. If L402Middleware is in DI it dies here, because no DI
        // container can hand it a RequestDelegate.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddL402AspNetCore(opts => opts.ApiKey = "fixture-apikey-not-real");

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        act.Should().NotThrow(
            because: "L402Middleware is convention-based and must not be in DI — " +
                     "UseMiddleware<T>() instantiates it via ActivatorUtilities");
    }

    [Fact]
    public void WebApplicationBuilder_BuildsCleanly_WithL402AspNetCore()
    {
        // The end-to-end harness that the original 0.1.1 bug actually broke.
        // Build a minimal WebApplication and ensure neither AddL402AspNetCore
        // nor UseL402 throws during composition.
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Development"; // turns on ValidateOnBuild

        builder.Services.AddL402AspNetCore(opts => opts.ApiKey = "fixture-apikey-not-real");

        var act = () =>
        {
            var app = builder.Build();
            app.UseRouting();
            app.UseL402();
        };

        act.Should().NotThrow(
            because: "0.1.1 threw here with: 'Unable to resolve service for type Microsoft.AspNetCore.Http.RequestDelegate while attempting to activate L402Middleware'");
    }
}
