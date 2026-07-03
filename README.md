# L402Server.AspNetCore

[![Discord](https://img.shields.io/discord/1405389254892195951?label=community&logo=discord&color=5865F2)](https://discord.gg/rX7NxHY8vx)


[![NuGet](https://img.shields.io/nuget/v/L402Server.AspNetCore.svg)](https://www.nuget.org/packages/L402Server.AspNetCore)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**ASP.NET Core middleware for L402.** Drop one line into any ASP.NET API to charge per-request Lightning payments. Built on [`L402Server`](https://www.nuget.org/packages/L402Server) — Lightning Enable handles invoices, macaroons, and payment verification; your API stays where it is.

## Install

```bash
dotnet add package L402Server.AspNetCore
```

Target: .NET 8.0+.

## 30-second example

```csharp
using L402Server.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddL402AspNetCore(opts =>
{
    opts.ApiKey = builder.Configuration["LightningEnable:ApiKey"]!;
});

var app = builder.Build();
app.UseRouting();
app.UseL402();
app.MapControllers();
app.Run();
```

Then mark any controller action with `[L402(PriceSats = 100)]` and that endpoint is gated behind a 100-sat L402 payment:

```csharp
[ApiController]
[Route("api/premium")]
public class PremiumController : ControllerBase
{
    [HttpGet("weather")]
    [L402(PriceSats = 100)]
    public IActionResult Weather() => Ok(new { temp = 72 });
}
```

That's it. The middleware handles:
- Issuing `402 Payment Required` with a Lightning invoice on unauthenticated requests
- Verifying `Authorization: L402 <macaroon>:<preimage>` on retries
- Stashing the verified credential in `HttpContext.Items[L402HttpContextKeys.VerificationResult]`

Endpoints without `[L402]` pass through ungated.

## Pricing patterns

### Per-route attributes (compile-time prices)

```csharp
[HttpGet("forecast"), L402(PriceSats = 100)]
public IActionResult Forecast() => Ok(...);

[HttpGet("premium-llm"), L402(PriceSats = 500, Description = "GPT-4 backed")]
public IActionResult PremiumLlm() => Ok(...);
```

### Global flat price (gates everything mounted under the middleware)

```csharp
app.UseL402(opts => opts.DefaultPriceSats = 100);
```

### Function-form pricing (variable per request)

```csharp
app.UseL402(opts =>
{
    opts.PriceSelector = ctx => ValueTask.FromResult(
        ctx.Request.Query["model"] == "premium" ? 500 : 100);
});
```

Resolution order: `PriceSelector` > `[L402(PriceSats)]` attribute > `DefaultPriceSats`. If none → request passes through ungated.

## The `[L402]` attribute

The attribute carries three properties:

```csharp
[HttpGet("forecast")]
[L402(PriceSats = 100,                       // required, ≥ 1 (compile-time constant)
      Description = "Premium forecast",     // optional, shown in the payer's wallet
      Resource = "/canonical/forecast")]    // optional, overrides the macaroon-bound resource
public IActionResult Forecast() => Ok(...);
```

- **`PriceSats`** (required): price in satoshis. Attribute values are compile-time constants; for variable per-request pricing use `PriceSelector` on the middleware options.
- **`Description`** (optional): embedded in the Lightning invoice.
- **`Resource`** (optional): overrides the resource bound into the macaroon's path caveat. Defaults to the request path. Useful when several routes should share one canonical paid resource (e.g. normalizing route parameters out), since a token is only valid for the exact resource it was minted for.

The attribute can also be applied **class-level** to gate every action on a controller with one declaration. If both the controller and an action carry `[L402]`, the action-level attribute wins for that endpoint (endpoint metadata is ordered least- to most-specific and the middleware reads the most specific match) — so a class-level default can be repriced per action:

```csharp
[ApiController]
[Route("api/premium")]
[L402(PriceSats = 100)]   // every action below costs 100 sats
public class PremiumController : ControllerBase
{
    [HttpGet("weather")]
    public IActionResult Weather() => Ok(new { temp = 72 });

    [HttpGet("forecast")]
    public IActionResult Forecast() => Ok(new { rain = false });
}
```

## Server-side path-caveat enforcement

Each macaroon is bound to the resource it was minted for. By default the middleware sends the resolved resource (same precedence as minting: `[L402(Resource = ...)]` > `ResourceSelector` > request path) with every verification call, so Lightning Enable rejects a token that was paid for a *different* path — replay protection across endpoints without writing a comparison yourself.

```csharp
// To opt out (and do your own comparison against VerificationResult.Resource):
app.UseL402(opts => opts.EnforceResourceOnVerify = false);
```

Requires `L402Server` ≥ 0.2.0 (a transitive dependency of this package).

## Verified credential on downstream handlers

```csharp
[HttpGet("weather"), L402(PriceSats = 100)]
public IActionResult Weather(HttpContext ctx)
{
    var result = (VerificationResult)ctx.Items[L402HttpContextKeys.VerificationResult]!;
    _logger.LogInformation("Served {Resource} for {Sats} sats ({Hash})",
        result.Resource, result.AmountSats, result.PaymentHash);
    return Ok(new { temp = 72 });
}
```

## Configuration

| Option | Type | Default | Notes |
|---|---|---|---|
| `DefaultPriceSats` | `int?` | `null` | Flat price applied when no `[L402]` attribute is present |
| `PriceSelector` | `Func<HttpContext, ValueTask<int>>?` | `null` | Variable pricing per request; overrides attribute + default |
| `ResourceSelector` | `Func<HttpContext, string>?` | `HttpContext.Request.Path` | Bound as a macaroon caveat |
| `DescriptionSelector` | `Func<HttpContext, string?>?` | `null` | Shown to the payer in their wallet |
| `IdempotencyKeySelector` | `Func<HttpContext, string?>?` | `null` | Sends `X-Idempotency-Key` for retry-safe challenge minting |
| `EnforceResourceOnVerify` | `bool` | `true` | Sends the resolved resource with verification so the producer API enforces the macaroon's path caveat server-side; set `false` to opt out |
| `OnInvalidToken` | `Func<HttpContext, VerificationResult, ValueTask>?` | sends `401` | Custom handler — e.g. send a fresh `402` instead. Your handler must write a response or call the next middleware itself |

## Two integration modes

Lightning Enable supports two integration shapes:

- **Proxy mode** — point Lightning Enable at your API URL; we forward authenticated requests on your behalf.
- **Native mode** — install this middleware in your existing API. Lightning Enable handles payment; your API handles everything else. **This middleware is the Native mode for ASP.NET Core.**

[Documentation →](https://docs.lightningenable.com/products/l402-microtransactions/proxy-setup-walkthrough)

## How the protocol works under the hood

Every paid request makes one round-trip to the Lightning Enable hosted API. The middleware never holds key material, never signs macaroons, never verifies preimages locally — all of that is in the hosted backend. The middleware itself is HTTP-client glue.

What you pay for with your Lightning Enable subscription: the protocol broker that lets this be one line of middleware.

## Sibling packages

- [`L402Server`](https://www.nuget.org/packages/L402Server) — the SDK this middleware is built on. Use directly for non-ASP.NET-Core .NET hosts.
- [`L402Requests`](https://www.nuget.org/packages/L402Requests) — consumer-side HTTP client. Auto-pays L402 challenges.

## Contributing

Open source under MIT. Issues and PRs welcome.

## License

MIT © Refined Element
