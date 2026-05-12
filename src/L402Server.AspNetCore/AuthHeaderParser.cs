using System.Text.RegularExpressions;

namespace L402Server.AspNetCore;

/// <summary>
/// Parses incoming <c>Authorization: L402 macaroon:preimage</c> headers.
/// Returns <see langword="null"/> for any malformed input — the caller should
/// treat that as "no credential present" and issue a fresh 402 challenge.
/// </summary>
internal static partial class AuthHeaderParser
{
    [GeneratedRegex(@"^L402\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemeRegex();

    public static (string Macaroon, string Preimage)? Parse(string? authHeader)
    {
        if (string.IsNullOrWhiteSpace(authHeader)) return null;

        var match = SchemeRegex().Match(authHeader.Trim());
        if (!match.Success) return null;

        var credential = match.Groups[1].Value.Trim();
        var colonIdx = credential.IndexOf(':');
        if (colonIdx <= 0 || colonIdx == credential.Length - 1) return null;

        var macaroon = credential[..colonIdx];
        var preimage = credential[(colonIdx + 1)..];

        if (macaroon.Length == 0 || preimage.Length == 0) return null;

        return (macaroon, preimage);
    }
}
