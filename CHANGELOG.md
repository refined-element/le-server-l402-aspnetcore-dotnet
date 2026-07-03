# Changelog

All notable changes to the `L402Server.AspNetCore` NuGet package are
documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Versioning policy (0.x)

This package is pre-1.0. Per [semver](https://semver.org/#spec-item-4), 0.x
releases may include breaking changes in **minor** versions: `0.N` → `0.N+1`
can break the public API or change default behavior (called out explicitly in
the entry); patch releases are always backward compatible. Pin
`[0.2.0,0.3.0)` if you need stability.

## [0.2.0] - 2026-07-03

### Changed (behavior)

- The middleware now sends the resolved resource with every token
  verification — using the same precedence as challenge minting
  (`[L402(Resource = ...)]` > `ResourceSelector` > request path) — so the
  Lightning Enable producer API enforces the macaroon's `path` caveat
  **server-side**. A token minted for a different path now verifies as
  invalid (401) instead of passing. Previously no path enforcement happened
  unless you compared `VerificationResult.Resource` yourself.
- `L402Server` dependency bumped to 0.2.0 (adds `VerifyTokenRequest.Resource`
  / `AmountSats`).

### Added

- `L402AspNetCoreOptions.EnforceResourceOnVerify` (default `true`): set to
  `false` to disable the enforcement above (restoring the 0.1.x behavior)
  and do your own comparison.
- README: documented the `[L402]` attribute's `Resource` property and
  class-level usage (gate a whole controller with one declaration;
  action-level attribute wins when both are present).
- This CHANGELOG.

## [0.1.2] - 2026-06-30

### Fixed

- Middleware DI registration and nullable `PriceSelector`
  (`Func<HttpContext, ValueTask<int?>>`) so a selector can return null to
  fall through to the `[L402]` attribute / `DefaultPriceSats`.

## [0.1.1] - 2026-05-12

### Changed

- `L402Server` dependency bumped to 0.1.1.

## [0.1.0] - 2026-05-12

### Added

- Initial release: `L402Middleware` gating ASP.NET Core endpoints behind
  L402 Lightning payments — 402 challenge issuance with `WWW-Authenticate`,
  `Authorization: L402` parsing and verification via the `L402Server` SDK,
  `[L402]` endpoint attribute, `HttpContext.Items` credential metadata,
  `PriceSelector`/`ResourceSelector`/`DescriptionSelector`/
  `IdempotencyKeySelector` options, `OnInvalidToken` hook, 502 mapping for
  producer-API failures. Targets .NET 8.0+.
