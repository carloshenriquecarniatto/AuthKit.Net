# Architecture

## Layers

```
contracts/authkit.contract.json      normative shapes and operations
        │
        ├── AuthKit.Core (.NET) ─────┬── AuthKit.Supabase ── AuthKit.Blazor
        │                            │
        └── @authkit/angular (TS) ───┴── SupabaseAuthProvider (TS)
```

`AuthKit.Core` has **zero dependencies**. Provider implementations depend on the core, never the
other way round. UI packages depend only on the core (and on the provider through DI), which is what
makes swapping providers a one-line change.

## Why two UI packages instead of one

Flutter renders one widget tree everywhere. .NET does not: MAUI rasterises native views, Blazor
builds a DOM. The honest design is a shared core plus thin renderers. Everything that can be shared
lives in the core:

- validation and messages;
- the `AuthFlow` state machine (idle → submitting → awaiting → authenticated/error);
- `AuthCapabilities`, which decides what the UI renders;
- theme tokens, emitted as CSS custom properties on the web.

A renderer that grows business logic is a bug.

## Hosting models (Blazor)

| Model       | Session store                            | OAuth                     | Notes                                                                                      |
| ----------- | ---------------------------------------- | ------------------------- | ------------------------------------------------------------------------------------------ |
| WebAssembly | `BrowserSessionStore` (`sessionStorage`) | full-page redirect + PKCE | Tokens are in the browser by definition                                                    |
| Server      | `InMemoryAuthSessionStore` (circuit)     | server-initiated redirect | Refresh tokens never cross to the browser                                                  |
| Auto        | store per runtime                        | as above                  | Only `authenticated` + `email` cross the prerender boundary via `PersistentComponentState` |

The store is selected explicitly in `AddAuthKitBlazor(...)`. There is no "auto-detect" that could
silently put a refresh token in a browser on Server.

## Angular and Aurelia

Those SDKs live in their own repositories (`AuthKit.Angular`, `AuthKit.Aurelia`) and consume the
same contract through a committed copy of `contracts/authkit.contract.json`, checked by CI. The
diagram above still describes the shape; only the repository boundary is new.

## Error handling

Expected failures are values. `AuthResult` carries `ok`, `session`, `error` and `nextStep`. This
keeps provider-specific exception types out of the UI, makes error mapping testable, and prevents
accidental leakage of server messages.

## Extending a provider

1. Implement `IAuthProvider` / `AuthProvider` against the contract.
2. Declare honest `AuthCapabilities` — the UI renders what you declare.
3. Map machine-readable error codes; never pass raw server text through.
4. Register it with `provideAuthKit(...)` or the .NET equivalent of `AddAuthKit*`.

## ADR-001 — deferring MAUI to v0.2

MAUI needs three things the web packages do not: a deep-link callback activity (Android), a
`CFBundleURLTypes` fragment (iOS), and an in-app browser adapter. All three are build-time concerns
and are solved with the documented hooks:

- Android: `AndroidManifestOverlay` merged in `_GenerateJavaStubs`.
- iOS/Mac Catalyst: `PartialAppManifest`, added from `CollectAppManifestsDependsOn`.
- Windows: loopback redirect, no manifest change.

They ship as an **opt-in** MSBuild switch because breaking a consumer's build is worse than three
documented lines. The same core contract serves MAUI unchanged.
