using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using L402Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace L402Server.AspNetCore.Tests;

public class L402MiddlewareTests
{
    private const string Mac = "AgELbWFjYXJvb24=";
    private const string Inv = "lnbc100n1pTEST";

    private static readonly string SampleChallengeJson = $$"""
        {
          "invoice": "{{Inv}}",
          "macaroon": "{{Mac}}",
          "paymentHash": "abc123",
          "expiresAt": "2026-05-12T01:00:00Z",
          "resource": "/api/premium",
          "priceSats": 100,
          "mppChallenge": null
        }
        """;

    private static readonly string ValidVerifyJson = """
        {
          "valid": true,
          "resource": "/api/premium",
          "merchantId": 42,
          "amountSats": 100,
          "paymentHash": "abc123"
        }
        """;

    private static readonly string InvalidVerifyJson = """
        {
          "valid": false,
          "error": "Invalid preimage"
        }
        """;

    private static async Task<(IHost Host, StubHttpHandler Handler)> BuildHostAsync(
        Action<IEndpointRouteBuilder> mapEndpoints,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder,
        Action<L402AspNetCoreOptions>? configure = null)
    {
        var handler = new StubHttpHandler(responder);
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(new L402ServerClient(
                            new L402ServerOptions
                            {
                                ApiKey = "test",
                                BaseUrl = "https://api.example",
                            },
                            new HttpClient(handler)));
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseL402(configure);
                        app.UseEndpoints(mapEndpoints);
                    });
            })
            .StartAsync();
        return (host, handler);
    }

    // ---------- 402 challenge issuance ----------

    [Fact]
    public async Task IssuesChallengeWith402AndWwwAuthenticate_WhenNoAuthHeader()
    {
        var (host, _) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/api/premium", () => "secret")
                .WithMetadata(new L402Attribute { PriceSats = 100 }),
            _ => Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, SampleChallengeJson)));
        using var _ = host;
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/api/premium");

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        response.Headers.WwwAuthenticate.Should().HaveCount(1);
        var wwwAuth = response.Headers.WwwAuthenticate.First().ToString();
        wwwAuth.Should().Contain("L402");
        wwwAuth.Should().Contain($"macaroon=\"{Mac}\"");
        wwwAuth.Should().Contain($"invoice=\"{Inv}\"");

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body.Should().ContainKey("l402");
    }

    [Fact]
    public async Task PassesThrough_WhenNoAttributeAndNoOptionsPrice()
    {
        var (host, handler) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/anonymous", () => "anyone-can-read"),
            _ => Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, SampleChallengeJson)));
        using var _ = host;
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/anonymous");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("anyone-can-read");
        handler.CallCount.Should().Be(0); // never called the producer API
    }

    [Fact]
    public async Task UsesDefaultPriceSats_WhenSetAndNoAttribute()
    {
        var (host, handler) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/x", () => "secret"),
            _ => Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, SampleChallengeJson)),
            opts => opts.DefaultPriceSats = 50);
        using var _ = host;
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/x");

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PriceSelectorWins_OverAttribute()
    {
        string? capturedBody = null;
        var (host, handler) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/x", () => "secret")
                .WithMetadata(new L402Attribute { PriceSats = 100 }),
            async req =>
            {
                if (req.Content != null)
                    capturedBody = await req.Content.ReadAsStringAsync();
                return StubHttpHandler.Json(HttpStatusCode.OK, SampleChallengeJson);
            },
            opts => opts.PriceSelector = _ => ValueTask.FromResult(500));
        using var _ = host;
        using var client = host.GetTestClient();

        await client.GetAsync("/x");

        capturedBody.Should().Contain("\"priceSats\":500",
            "PriceSelector should override the attribute's 100");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task DefaultsResourceToRequestPath()
    {
        string? capturedBody = null;
        var (host, _) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/api/weather/forecast", () => "data")
                .WithMetadata(new L402Attribute { PriceSats = 100 }),
            async req =>
            {
                if (req.Content != null)
                    capturedBody = await req.Content.ReadAsStringAsync();
                return StubHttpHandler.Json(HttpStatusCode.OK, SampleChallengeJson);
            });
        using var _ = host;
        using var client = host.GetTestClient();

        await client.GetAsync("/api/weather/forecast");

        capturedBody.Should().Contain("\"resource\":\"/api/weather/forecast\"");
    }

    [Fact]
    public async Task AttributeResourceOverridesRequestPath()
    {
        string? capturedBody = null;
        var (host, _) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/api/weather/forecast", () => "data")
                .WithMetadata(new L402Attribute
                {
                    PriceSats = 100,
                    Resource = "/canonical/weather",
                }),
            async req =>
            {
                if (req.Content != null)
                    capturedBody = await req.Content.ReadAsStringAsync();
                return StubHttpHandler.Json(HttpStatusCode.OK, SampleChallengeJson);
            });
        using var _ = host;
        using var client = host.GetTestClient();

        await client.GetAsync("/api/weather/forecast");

        capturedBody.Should().Contain("\"resource\":\"/canonical/weather\"");
    }

    // ---------- Verification ----------

    [Fact]
    public async Task PassesThroughOnValidToken_AndStashesResultInHttpContextItems()
    {
        VerificationResult? captured = null;
        var (host, _) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/api/premium", (HttpContext ctx) =>
                {
                    captured = ctx.Items[L402HttpContextKeys.VerificationResult] as VerificationResult;
                    return "secret";
                })
                .WithMetadata(new L402Attribute { PriceSats = 100 }),
            _ => Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, ValidVerifyJson)));
        using var _h = host;
        using var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/premium");
        req.Headers.TryAddWithoutValidation("Authorization", $"L402 {Mac}:deadbeef");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("secret");
        captured.Should().NotBeNull();
        captured!.Valid.Should().BeTrue();
        captured.Resource.Should().Be("/api/premium");
        captured.AmountSats.Should().Be(100);
    }

    [Fact]
    public async Task Returns401_OnInvalidToken_WithDefaultHandler()
    {
        var (host, _) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/api/premium", () => "secret")
                .WithMetadata(new L402Attribute { PriceSats = 100 }),
            _ => Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, InvalidVerifyJson)));
        using var _ = host;
        using var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/premium");
        req.Headers.TryAddWithoutValidation("Authorization", $"L402 {Mac}:bad");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Invalid preimage");
    }

    [Fact]
    public async Task CustomOnInvalidTokenHandler_RunsAndBypassesDefault401()
    {
        var (host, _) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/x", () => "secret")
                .WithMetadata(new L402Attribute { PriceSats = 100 }),
            _ => Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, InvalidVerifyJson)),
            opts => opts.OnInvalidToken = async (ctx, failure) =>
            {
                ctx.Response.StatusCode = StatusCodes.Status418ImATeapot;
                await ctx.Response.WriteAsync($"custom:{failure.Error}");
            });
        using var _h = host;
        using var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/x");
        req.Headers.TryAddWithoutValidation("Authorization", $"L402 {Mac}:bad");
        var response = await client.SendAsync(req);

        ((int)response.StatusCode).Should().Be(418);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("custom:Invalid preimage");
    }

    // ---------- Upstream errors ----------

    [Fact]
    public async Task Returns502_WhenProducerApiFailsOnChallenge()
    {
        var (host, _) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/x", () => "secret")
                .WithMetadata(new L402Attribute { PriceSats = 100 }),
            _ => throw new HttpRequestException("ECONNREFUSED"));
        using var _h = host;
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/x");
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Returns502_WhenProducerApiFailsOnVerify()
    {
        var (host, _) = await BuildHostAsync(
            endpoints => endpoints.MapGet("/x", () => "secret")
                .WithMetadata(new L402Attribute { PriceSats = 100 }),
            _ => throw new HttpRequestException("ECONNREFUSED"));
        using var _h = host;
        using var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/x");
        req.Headers.TryAddWithoutValidation("Authorization", $"L402 {Mac}:deadbeef");
        var response = await client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }
}

