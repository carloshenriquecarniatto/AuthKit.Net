# AuthKit for .NET

**English** · [Português](README.pt-BR.md) · [Español](README.es.md)

Authentication screens that are ready in one line, native to .NET.

This repository ships the .NET SDK only. The other platforms live in their own repositories, with
the same base name and their own release cycle:

| Platform | Repository                                                                    |
| -------- | ----------------------------------------------------------------------------- |
| Angular  | [AuthKit.Angular](https://github.com/carloshenriquecarniatto/AuthKit.Angular) |
| Contract | `contracts/authkit.contract.json`, owned here                                 |

```text
AuthKit.Core               contracts, models, state machine, validation, theming   (no third-party dependencies)
AuthKit.Supabase           Supabase Auth provider
AuthKit.Blazor             native Blazor components (WebAssembly, Server, Auto)
contracts/                 authkit.contract.json, the normative contract copied by the TypeScript SDKs
```

## Customizing the UI

There are three levels, and the [customization guide](docs/customization.md) explains each one with
code: theme tokens, `ak-*` CSS classes, and a headless provider.

## Why

- **Native rendering.** Razor components for Blazor. No React inside a webview, no JS interop beyond
  `sessionStorage`.
- **Provider-agnostic.** `IAuthProvider` is the only dependency the UI has.
- **Security is part of the contract.** Server-side tokens never reach the browser, service-role
  keys are rejected at startup, provider error text never reaches the UI, and a pentest suite blocks
  every release.
- **One contract.** `contracts/authkit.contract.json` is normative for every SDK; a parity test in
  each repository fails the build if it drifts.

## Quick start — Blazor with Supabase

```bash
dotnet add package AuthKit.Supabase
dotnet add package AuthKit.Blazor
```

```csharp
builder.Services.AddAuthKitSupabase(options =>
{
    options.Url = builder.Configuration["Supabase:Url"]!;
    options.AnonKey = builder.Configuration["Supabase:AnonKey"]!;
});

builder.Services.AddAuthKitBlazor(AuthSessionStorageMode.BrowserSession);
```

```razor
<AuthKitEmail OnSignInComplete="GoHome" />
<AuthKitSocials Providers="@(new[] { "github", "google" })" />
```

## The contract

`contracts/authkit.contract.json` is the normative contract. The .NET types mirror it and
`ContractParityTests` fails the build on drift. The Angular repository keeps a
committed copy and their CI fails if it differs from this file — no package to publish, no release
ordering. See [`contracts/README.md`](contracts/README.md).

## Penetration test

The offline suite runs on every pull request; the live one needs Docker.

```bash
dotnet test security/AuthKit.Pentest/AuthKit.Pentest.csproj
bash scripts/pentest-up.sh && bash scripts/pentest-live.sh
```

## Status

| Area                       | State                                                                   |
| -------------------------- | ----------------------------------------------------------------------- |
| Contract (`2.0.0`)         | Stable (breaking: provider surface narrowed to authentication)         |
| `AuthKit.Core`             | Email, magic link, OTP (email + phone), recovery, OAuth, capabilities   |
| `AuthKit.Supabase`         | Complete provider, error mapping, session restore                       |
| `AuthKit.Blazor`           | Email, magic link, reset, socials, callback |
| MAUI                       | Planned: same core, XAML shell, `WebAuthenticator`                      |

## Repository layout

```text
contracts/            authkit.contract.json, the normative contract copied by the TypeScript SDKs
src/dotnet/           AuthKit.Core, AuthKit.Supabase, AuthKit.Blazor
tests/dotnet/         xUnit, bUnit and protocol fixtures
security/             offline and live pentest suites
samples/messages/     committed, anonymised protocol fixtures
docs/                 spec, threat model, architecture, UI parity
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Hooks run `dotnet test` before a push; a contract change
must land here first, then in the consuming SDKs with a version bump.

## Security

Read [SECURITY.md](SECURITY.md) before reporting a vulnerability.

## License

MIT — see [LICENSE](LICENSE).
