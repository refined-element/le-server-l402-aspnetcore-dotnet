using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace L402Server.AspNetCore;

/// <summary>
/// DI + pipeline registration helpers for <see cref="L402Middleware"/>.
/// </summary>
public static class L402ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the L402 middleware to the request pipeline. Place AFTER
    /// <c>UseRouting()</c> so the middleware can read <see cref="L402Attribute"/>
    /// metadata from the matched endpoint, and BEFORE <c>MapControllers()</c>.
    /// </summary>
    /// <example>
    /// Attribute-driven gating (only routes with <c>[L402]</c> are gated):
    /// <code>
    /// builder.Services.AddL402Server(opts =&gt; opts.ApiKey = "...");
    /// // ...
    /// app.UseRouting();
    /// app.UseL402();
    /// app.MapControllers();
    /// </code>
    ///
    /// Global flat-price gating (every request through this middleware is gated):
    /// <code>
    /// app.UseL402(opts =&gt; opts.DefaultPriceSats = 100);
    /// </code>
    ///
    /// Function-form pricing:
    /// <code>
    /// app.UseL402(opts =&gt;
    /// {
    ///     opts.PriceSelector = async ctx =&gt;
    ///         ctx.Request.Query["model"] == "premium" ? 500 : 100;
    /// });
    /// </code>
    /// </example>
    public static IApplicationBuilder UseL402(
        this IApplicationBuilder app,
        Action<L402AspNetCoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = new L402AspNetCoreOptions();
        configure?.Invoke(options);

        return app.UseMiddleware<L402Middleware>(options);
    }

    /// <summary>
    /// Registers <see cref="L402Middleware"/>'s dependencies in DI. Required
    /// before calling <see cref="UseL402"/>. Wraps
    /// <see cref="L402Server.ServiceCollectionExtensions.AddL402Server"/>.
    /// </summary>
    public static IServiceCollection AddL402AspNetCore(
        this IServiceCollection services,
        Action<L402ServerOptions> configureServer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureServer);

        services.AddL402Server(configureServer);
        services.TryAddTransient<L402Middleware>();
        return services;
    }
}
