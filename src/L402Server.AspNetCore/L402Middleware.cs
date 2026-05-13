using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace L402Server.AspNetCore;

/// <summary>
/// HttpContext.Items key under which the verified <see cref="VerificationResult"/>
/// is stored after successful verification. Downstream handlers can inspect it
/// to learn which resource was paid for, how much, etc.
/// </summary>
public static class L402HttpContextKeys
{
    public const string VerificationResult = "L402.VerificationResult";
}

/// <summary>
/// ASP.NET Core middleware that gates requests behind L402 Lightning payments.
/// <para>
/// Per-request behavior:
/// 1. Resolve a price for the current request. Resolution order:
///    <see cref="L402AspNetCoreOptions.PriceSelector"/> first — if it returns
///    a non-null value, that's the price. If it returns <see langword="null"/>
///    (or is not configured), fall through to any <see cref="L402Attribute"/>
///    on the matched endpoint. If neither yields a price, fall through to
///    <see cref="L402AspNetCoreOptions.DefaultPriceSats"/>. If all three are
///    null/missing, the request passes through ungated.
/// 2. Parse <c>Authorization: L402 macaroon:preimage</c> from the request.
/// 3. If absent/malformed → mint a fresh challenge via <see cref="L402ServerClient"/>
///    and respond with <c>402 Payment Required</c>.
/// 4. If present → verify via the SDK. On valid, set
///    <c>HttpContext.Items[L402HttpContextKeys.VerificationResult]</c> and
///    call the next middleware. On invalid, respond with <c>401</c> (or
///    delegate to <see cref="L402AspNetCoreOptions.OnInvalidToken"/>).
/// </para>
/// </summary>
public sealed class L402Middleware
{
    private readonly RequestDelegate _next;
    private readonly L402ServerClient _client;
    private readonly L402AspNetCoreOptions _options;
    private readonly ILogger<L402Middleware> _logger;

    // ASP.NET Core's UseMiddleware<T>(args) passes positional args after the
    // RequestDelegate; the rest are resolved from DI. Keep `options` as the
    // first positional arg so callers can `app.UseMiddleware<L402Middleware>(options)`.
    public L402Middleware(
        RequestDelegate next,
        L402AspNetCoreOptions options,
        L402ServerClient client,
        ILogger<L402Middleware> logger)
    {
        _next = next;
        _options = options;
        _client = client;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var attribute = endpoint?.Metadata.GetMetadata<L402Attribute>();

        var price = await ResolvePriceAsync(context, attribute);
        if (price is null)
        {
            // Not a paid endpoint — pass through.
            await _next(context);
            return;
        }

        if (price.Value < 1)
        {
            _logger.LogWarning(
                "L402 middleware refused: resolved price {Price} sats is not ≥ 1. Request passed through ungated.",
                price.Value);
            await _next(context);
            return;
        }

        var parsed = AuthHeaderParser.Parse(context.Request.Headers.Authorization);

        if (parsed is null)
        {
            // No credential — mint and return 402.
            await IssueChallengeAsync(context, price.Value, attribute);
            return;
        }

        VerificationResult verification;
        try
        {
            verification = await _client.VerifyTokenAsync(new VerifyTokenRequest
            {
                Macaroon = parsed.Value.Macaroon,
                Preimage = parsed.Value.Preimage,
            }, context.RequestAborted);
        }
        catch (Exception ex)
        {
            await WriteUpstreamErrorAsync(context, ex);
            return;
        }

        if (!verification.Valid)
        {
            if (_options.OnInvalidToken is not null)
            {
                await _options.OnInvalidToken(context, verification);
                return;
            }
            await WriteUnauthorizedAsync(context, verification);
            return;
        }

        context.Items[L402HttpContextKeys.VerificationResult] = verification;
        await _next(context);
    }

    private async ValueTask<int?> ResolvePriceAsync(HttpContext context, L402Attribute? attribute)
    {
        if (_options.PriceSelector is not null)
        {
            var selectorPrice = await _options.PriceSelector(context);
            if (selectorPrice is not null)
            {
                return selectorPrice;
            }
            // null = "I have no opinion on this request" → fall through to attribute / default.
        }
        if (attribute is not null)
        {
            return attribute.PriceSats;
        }
        return _options.DefaultPriceSats;
    }

    private async Task IssueChallengeAsync(HttpContext context, int priceSats, L402Attribute? attribute)
    {
        string resource;
        if (attribute?.Resource is { Length: > 0 } attrResource)
        {
            resource = attrResource;
        }
        else if (_options.ResourceSelector is not null)
        {
            resource = _options.ResourceSelector(context);
        }
        else
        {
            resource = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
        }

        var description = attribute?.Description
            ?? (_options.DescriptionSelector is not null ? _options.DescriptionSelector(context) : null);
        var idempotencyKey = _options.IdempotencyKeySelector?.Invoke(context);

        Challenge challenge;
        try
        {
            challenge = await _client.CreateChallengeAsync(new CreateChallengeRequest
            {
                Resource = resource,
                PriceSats = priceSats,
                Description = description,
                IdempotencyKey = idempotencyKey,
            }, context.RequestAborted);
        }
        catch (Exception ex)
        {
            await WriteUpstreamErrorAsync(context, ex);
            return;
        }

        var wwwAuth = $"L402 macaroon=\"{challenge.Macaroon}\", invoice=\"{challenge.Invoice}\"";
        context.Response.Headers.WWWAuthenticate = wwwAuth;
        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = "Payment Required",
            l402 = new
            {
                macaroon = challenge.Macaroon,
                invoice = challenge.Invoice,
                amount_sats = challenge.PriceSats,
                payment_hash = challenge.PaymentHash,
                expires_at = challenge.ExpiresAt,
                resource = challenge.Resource,
            },
        }), context.RequestAborted);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, VerificationResult verification)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = "Unauthorized",
            message = verification.Error ?? "Invalid L402 credential.",
        }), context.RequestAborted);
    }

    private async Task WriteUpstreamErrorAsync(HttpContext context, Exception ex)
    {
        _logger.LogWarning(ex, "L402 producer API call failed: {Message}", ex.Message);
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = "Bad Gateway",
            message = ex.Message,
        }), context.RequestAborted);
    }
}
