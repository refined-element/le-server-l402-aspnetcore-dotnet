namespace L402Server.AspNetCore;

/// <summary>
/// Marks an ASP.NET Core controller or action as requiring an L402 Lightning
/// payment to access. The <see cref="L402Middleware"/> reads this attribute
/// from the matched endpoint and gates the request accordingly.
/// </summary>
/// <example>
/// <code>
/// [ApiController]
/// [Route("api/premium")]
/// public class PremiumController : ControllerBase
/// {
///     [HttpGet("forecast")]
///     [L402(PriceSats = 100)]
///     public IActionResult Forecast() => Ok(new { temp = 72 });
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class L402Attribute : Attribute
{
    /// <summary>
    /// Price in satoshis for accessing this endpoint. Must be ≥ 1.
    /// Attribute values are compile-time constants, so this is the only price
    /// shape available via attribute. For variable per-request pricing, use
    /// <see cref="L402AspNetCoreOptions.PriceSelector"/> on the middleware
    /// options instead.
    /// </summary>
    public required int PriceSats { get; init; }

    /// <summary>
    /// Optional description embedded in the Lightning invoice. Shown to the
    /// payer in their wallet UI.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional resource override bound into the macaroon's path caveat.
    /// Defaults to <c>HttpContext.Request.Path</c> when omitted.
    /// </summary>
    public string? Resource { get; init; }
}
