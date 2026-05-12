using FluentAssertions;
using L402Server.AspNetCore;

namespace L402Server.AspNetCore.Tests;

public class AuthHeaderParserTests
{
    [Fact]
    public void ParsesStandardL402Credential()
    {
        var result = AuthHeaderParser.Parse("L402 AgELbWFjYXJvb24=:deadbeef");
        result.Should().NotBeNull();
        result!.Value.Macaroon.Should().Be("AgELbWFjYXJvb24=");
        result.Value.Preimage.Should().Be("deadbeef");
    }

    [Theory]
    [InlineData("L402 mac:pre")]
    [InlineData("l402 mac:pre")]
    [InlineData("L402  mac:pre")]
    [InlineData("  L402 mac:pre  ")]
    public void AcceptsCaseInsensitiveSchemeAndWhitespace(string header)
    {
        var result = AuthHeaderParser.Parse(header);
        result.Should().NotBeNull();
        result!.Value.Macaroon.Should().Be("mac");
        result.Value.Preimage.Should().Be("pre");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bearer abc")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("LSAT mac:pre")]
    [InlineData("L402 nopreimage")]
    [InlineData("L402 :preimage")]
    [InlineData("L402 macaroon:")]
    public void ReturnsNullForMalformedOrAbsent(string? header)
    {
        AuthHeaderParser.Parse(header).Should().BeNull();
    }

    [Fact]
    public void HandlesBase64Padding()
    {
        var result = AuthHeaderParser.Parse("L402 AgEL==:deadbeef");
        result.Should().NotBeNull();
        result!.Value.Macaroon.Should().Be("AgEL==");
    }

    [Fact]
    public void SplitsOnFirstColonOnly()
    {
        // Real macaroons are base64 (no colons), but if a future format
        // included colons in the preimage side we'd still get sensible parsing.
        var result = AuthHeaderParser.Parse("L402 foo:bar:baz");
        result!.Value.Macaroon.Should().Be("foo");
        result.Value.Preimage.Should().Be("bar:baz");
    }
}
