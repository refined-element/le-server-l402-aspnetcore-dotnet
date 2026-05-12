using Microsoft.AspNetCore.Http;

namespace L402Server.AspNetCore;

/// <summary>
/// Configuration for <see cref="L402Middleware"/>. Set via
/// <see cref="L402ApplicationBuilderExtensions.UseL402(Microsoft.AspNetCore.Builder.IApplicationBuilder, Action{L402AspNetCoreOptions}?)"/>.
/// </summary>
public sealed class L402AspNetCoreOptions
{
    /// <summary>
    /// Optional global default price applied to ANY request reaching the
    /// middleware that lacks an <see cref="L402Attribute"/>. Leave null to
    /// only gate attribute-marked endpoints.
    /// </summary>
    public int? DefaultPriceSats { get; set; }

    /// <summary>
    /// Optional function-form price selector. When set, overrides
    /// <see cref="DefaultPriceSats"/> and any attribute value — useful for
    /// pricing that varies per request (e.g., "premium model costs more").
    /// Must resolve to an integer ≥ 1.
    /// </summary>
    public Func<HttpContext, ValueTask<int>>? PriceSelector { get; set; }

    /// <summary>
    /// Optional resource selector. Defaults to <c>HttpContext.Request.Path</c>
    /// when not set. Useful for normalizing trailing slashes / query strings
    /// out of the macaroon-bound resource.
    /// </summary>
    public Func<HttpContext, string>? ResourceSelector { get; set; }

    /// <summary>
    /// Optional description selector for the Lightning invoice (visible to
    /// the payer in their wallet UI).
    /// </summary>
    public Func<HttpContext, string?>? DescriptionSelector { get; set; }

    /// <summary>
    /// Optional idempotency key derivation. If set, the value is sent as
    /// <c>X-Idempotency-Key</c> on challenge creation so retries within the
    /// invoice expiry return the same challenge.
    /// </summary>
    public Func<HttpContext, string?>? IdempotencyKeySelector { get; set; }

    /// <summary>
    /// Optional custom handler for failed verification. Defaults to sending
    /// a 401 JSON response. If supplied, the handler is responsible for
    /// either writing a response or calling the next middleware itself.
    /// </summary>
    public Func<HttpContext, VerificationResult, ValueTask>? OnInvalidToken { get; set; }
}
