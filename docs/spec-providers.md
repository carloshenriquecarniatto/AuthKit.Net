# AuthKit v0.2 — Multi-Provider Registry Specification

**English** · Português · Español _(translations pending)_

Status: **draft, normative once accepted**. Target contract version: **2.0.0**. Extends
[`spec.md`](spec.md).
---

## 1. Problem

AuthKit v0.1 assumes one provider per host: a single `IAuthProvider` is registered, a single session
exists, and every component binds to it.

Real hosts do not have one audience. A Spanish public-sector backoffice typically needs:

- **Citizens** entering through a certificate, an EUDI wallet or another OIDC provider;
- **Employees** entering through the organisation's Entra ID tenant, LDAP or a certificate;
- **Supplier or partner organisations** entering through a separate federated tenant.

These are different people, in different browsers, on the same deployment, sometimes in the same
session (a clerk acting on behalf of a citizen). One provider cannot express that, and one session
cannot hold it.

This specification introduces a registry of providers with **explicit routing**, while keeping the
single-provider API fully working.

## 2. Model

A provider is **not** identified by its technology. Two Supabase projects, in the same audience, for
two different populations, are two providers. Identity is therefore always composite.

| Concept      | Meaning                                                                                 |
| ------------ | --------------------------------------------------------------------------------------- |
| **Provider** | One registered identity source. Named by the host, never auto-derived                   |
| **Kind**     | The technology behind it: `supabase`, `entra`, `oidc`, `certificate`, `ldap`, `wallet` |
| **Audience** | **Any identifier the host declares.** No enum, no reserved catalogue — see §2.1         |
| **Origin**   | Where it runs: `self_hosted`, `cloud`, `third_party` — audit metadata, not routing      |
| **Ref**      | The canonical identity: `audience/providerId`, e.g. `clientes/supabase-clientes`        |
| **Scope**    | The UI context that selects a provider for a given screen                               |

```text
audience: intranet               audience: clientes                audience: socios
├── supabase-dev  (Supabase)     ├── supabase-clientes (Supabase)  ├── supabase-socios (Supabase)
└── entra-staff   (Entra ID)     ├── oidc              (OIDC)      └── entra-b2b (Entra External ID)
                                 └── certificate       (certificate)
```

The names above are **illustrative only**. `intranet`, `clientes` and `socios` are strings the host
invented; AuthKit never ships a catalogue and never validates an audience against a fixed list. The
same kind appears three times, in three audiences — that is legal, expected, and must be readable at
a glance, which is why the ref starts with the audience.

Invariants:

1. `providerId` is unique **within** an audience; the canonical `audience/providerId` is unique
   across the registry.
2. A provider instance belongs to exactly **one** audience. Serving two populations means two
   registrations with different refs — otherwise one login could satisfy two routes.
3. Two providers in the same audience **must not** share `DisplayName`. A citizen cannot be offered
   two buttons that read "Supabase".
4. `Kind` is descriptive only. Routing never resolves by kind, because a kind may be ambiguous
   (T-23).

### 2.1 Audiences are declared by the host

An audience is an opaque identifier chosen by the application author, because it will be referenced
from code: in a policy name, a route's `data`, a Razor attribute, a test assertion. AuthKit
therefore ships **no constants, no enum and no semantic assumptions** about what an audience means.

Format, validated at startup so that a ref is always parseable:

| Rule            | Value                                                |
| --------------- | ---------------------------------------------------- |
| Pattern         | `^[a-z0-9][a-z0-9._-]{0,63}$`                        |
| Separator       | `/` is **forbidden** in both parts of a ref          |
| Comparison      | Ordinal, case-sensitive                              |
| Reserved values | Exactly one: `default`, used by single-provider mode |

Declaring them in code is the host's job, and it is the only place they should be spelled:

```csharp
// Host code, not AuthKit.
public static class Audiences
{
    public const string Intranet = "intranet";
    public const string Clientes = "clientes";
    public const string Socios   = "socios";
}

services.AddAuthKit(kit =>
{
    kit.AddSupabase("supabase-clientes", o => { … }, audience: Audiences.Clientes);
    kit.AddEntra("entra-staff", o => { … }, audience: Audiences.Intranet);
});

[Authorize(Policy = "authkit:audience:clientes")]
public IActionResult Panel() => View();
```

```ts
// Host code, not AuthKit.
export const AUDIENCES = {
  intranet: 'intranet',
  clientes: 'clientes',
  socios: 'socios',
} as const;

export type Audience = (typeof AUDIENCES)[keyof typeof AUDIENCES];
```

An unknown audience is a startup error, not a runtime fallback: a typo in a policy must fail the
build or the boot, never let a request through.

## 3. Routing

Routing must be deterministic and auditable. Six rules, evaluated in order, and nothing else:

| #   | Rule                                                               | Example                                              |
| --- | ------------------------------------------------------------------ | ---------------------------------------------------- |
| 1   | An explicit `providerRef` (`audience/id`) or `providerId` wins     | `<ak-email provider="clientes/supabase-clientes" />` |
| 2   | Otherwise, the `Audience` in scope wins                            | page declares `audience="intranet"`                  |
| 3   | Otherwise, the audience's provider marked `IsPrimary` wins         | `supabase-clientes` is primary for `clientes`                    |
| 4   | Otherwise, if the audience has exactly one provider, that one wins | single-provider audiences are unambiguous            |
| 5   | Otherwise, resolution fails with `provider_ambiguous`              | never guessed, never first-registered                |
| 6   | Resolution never crosses an audience boundary                      | a `clientes` screen never falls back to `intranet`   |

Explicitly **forbidden**, because each one produces unattributable behaviour in production:

- Selecting by `Kind` ("the Supabase one", "the Entra one") — the same kind may exist twice in the
  same audience (T-23).
- Selecting by capability sniffing ("whichever provider can do a magic link").
- Selecting by host name, subdomain or request header.
- Selecting by registration order.

A registration that cannot satisfy rules 4–5 must fail at **startup**, not at login:
`AuthKitConfigurationException` naming the audience and the candidate providers.

## 4. Sessions

Sessions are **per provider**, and more than one may be alive at once.

```csharp
public interface IAuthProviderRegistry
{
    /// The default provider used when no routing rule applies. Null when no audience has one.
    IAuthProvider? Primary { get; }

    /// Every registered provider, in registration order.
    IReadOnlyList<AuthProviderRegistration> Providers { get; }

    /// Resolves a provider using §3, or throws AuthKitConfigurationException.
    IAuthProvider Resolve(string? providerRef = null, string? audience = null);

    /// Resolves without throwing; returns false with a reason code when ambiguous or missing.
    bool TryResolve(string? providerRef, string? audience, out IAuthProvider provider, out AuthError? error);

    /// Registration that owns a provider instance, for display and audit.
    AuthProviderRegistration Describe(IAuthProvider provider);

    /// Live sessions keyed by canonical ref (`audience/providerId`). Read-only snapshot.
    IReadOnlyDictionary<string, AuthSession> Sessions { get; }

    /// Signs out one provider by ref, or every provider when the ref is null.
    Task SignOutAsync(string? providerRef = null, CancellationToken cancellationToken = default);

    /// Raised for every provider transition; carries the originating ref.
    event EventHandler<AuthStateChangedEventArgs>? StateChanged;
}

public sealed record AuthProviderRegistration
{
    /// Unique within the audience. `^[a-z0-9][a-z0-9-]{1,47}$`.
    public required string ProviderId { get; init; }

    /// The population served. An instance belongs to exactly one audience.
    public required string Audience { get; init; }

    /// The technology. Descriptive only; never a routing key.
    public required string Kind { get; init; }

    /// Where it runs, for audit: self_hosted, cloud, third_party.
    public string Origin { get; init; } = "self_hosted";

    /// Human label. Unique within the audience.
    public required string DisplayName { get; init; }

    public string? Description { get; init; }
    public string? IconUrl { get; init; }

    public required IAuthProvider Provider { get; init; }
    public required AuthCapabilities Capabilities { get; init; }

    /// Default for the audience. At most one per audience.
    public bool IsPrimary { get; init; }

    /// Canonical identity: `audience/providerId`.
    public string Ref => $"{Audience}/{ProviderId}";
}
```

`IAuthProvider` does **not** change. `CurrentSession` on an individual provider keeps its current
meaning: the session that provider holds. The registry is the aggregate view.

Because the ref is computed, it cannot drift from `Audience` and `ProviderId`, and there is no path
where a provider is identified by kind alone.

### 4.1 Two live sessions

When a citizen session and a staff session are both alive:

- `Sessions` contains both, keyed by canonical ref (`clientes/supabase-clientes`, `intranet/entra-staff`).
- `StateChanged` fires once per transition, with the ref set, so a listener can tell who changed.
- The UI never merges them. A screen is bound to one provider at a time through its scope.
- `SignOutAsync("clientes/supabase-clientes")` leaves the staff session intact. `SignOutAsync()` clears both.

### 4.2 The session carries its own provenance

A session is only trustworthy if it can say where it came from without consulting the registry. The
registry may change between the moment a session was issued and the moment it is displayed or
audited — a provider can be de-registered, renamed, or re-pointed at another tenant.

`AuthSession` therefore carries an immutable snapshot, written at issue time and never recomputed:

```csharp
public sealed record AuthProviderSnapshot
{
    public required string Ref { get; init; }        // clientes/supabase-clientes
    public required string Audience { get; init; }   // clientes
    public required string ProviderId { get; init; } // supabase-clientes
    public required string Kind { get; init; }       // supabase-clientes
    public required string DisplayName { get; init; }// Supabase
    public required string Origin { get; init; }     // third_party
    public string? Tenant { get; init; }             // caib — provider-level, optional
    public DateTimeOffset CapturedAt { get; init; }
}
```

Rules:

1. `AuthSession.Provider` is required and immutable; the contract tests assert nothing mutates it.
2. A session whose `Provider.Ref` is not in the registry is still displayable and still auditable,
   but cannot be refreshed — `provider_not_configured` on the next refresh attempt.
3. Every log line, audit record and support ticket that mentions a session uses `Provider.Ref`,
   never the kind, never the display name alone (R-23).

## 5. Storage

`IAuthSessionStore` is single-keyed in v0.1. Making it keyed would break every implementation, so
the registry introduces a second abstraction and adapts the old one:

```csharp
/// Keyed session storage. One entry per ProviderId.
public interface IAuthSessionVault
{
    ValueTask<AuthSession?> LoadAsync(string providerId, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(string providerId, AuthSession session, CancellationToken cancellationToken = default);
    ValueTask ClearAsync(string providerId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyDictionary<string, AuthSession>> LoadAllAsync(CancellationToken cancellationToken = default);
}
```

- `IAuthSessionStore` remains supported: the registry wraps it in a single-key vault bound to the
  primary provider. Existing hosts are unaffected.
- Keys are namespaced (`authkit:{providerId}`) so two providers cannot overwrite each other.
- Loading is tolerant: a corrupt entry clears that provider only, never the whole vault.
- A persisted session whose `providerId` does not match its vault key is discarded (T-19).

## 6. Registration

```csharp
builder.Services.AddAuthKit(kit =>
{
    // Internal dev team — a Supabase project, self-hosted or cloud.
    kit.AddSupabase("supabase-dev", o =>
    {
        o.Url = "https://dev.internal.example";
        o.AnonKey = "…";
    }, audience: "intranet", kind: "supabase", origin: "self_hosted", isPrimary: true);

    // Internal staff — the corporate directory.
    kit.AddEntra("entra-staff", o =>
    {
        o.TenantId = "…";
        o.ClientId = "…";
    }, audience: "intranet", origin: "third_party");

    // External clients — a separate Supabase project, same kind, same audience as the other client provider.
    kit.AddSupabase("supabase-clientes", o =>
    {
        o.Url = "https://clients.example";
        o.AnonKey = "…";
    }, audience: "clientes", kind: "supabase", isPrimary: true);


    // Partner organisations — a third instance of the same kind, different audience.
    kit.AddSupabase("supabase-socios", o => { … }, audience: "socios", kind: "supabase");
});
```

The ref of each is computed, not typed: `intranet/supabase-dev`, `intranet/entra-staff`,
`clientes/supabase-clientes`, `clientes/supabase-clientes`, `socios/supabase-socios`.

Rules enforced at startup:

1. `ProviderId` is required, matches `^[a-z0-9][a-z0-9-]{1,47}$`, and is **unique within its
   audience**; a duplicate ref is fatal (T-18).
2. `Audience` is required, non-empty, compared case-sensitively.
3. `DisplayName` is required and **unique within the audience**; two providers in the same audience
   cannot both read "Supabase" (T-23).
4. At most one `IsPrimary` per audience; two primaries in the same audience is fatal.
5. If an audience has more than one provider and none is primary, that is fatal (rule 4 of §3 cannot
   be satisfied).
6. The same `IAuthProvider` instance may not be registered twice, under any two refs (invariant 2 of
   §2).
7. A provider whose `Capabilities` declare nothing renderable is fatal — a registered provider that
   the UI can never show is a configuration bug, not a feature.

`AddAuthKit(provider => …)` with a single provider stays supported and is equivalent to registering
one provider as primary with audience `default` and `providerId` `default`.

## 7. UI

### 7.1 Scope

Components gain two optional parameters: `provider` and `audience`.

```razor
<AuthKitAudience Name="intranet">
  <AuthKitEmail OnSignInComplete="GoHome" />
</AuthKitAudience>

<ak-email audience="clientes" />
```

`<AuthKitAudience>` / `<ak-audience>` sets the ambient audience for its subtree through a cascading
value (Blazor) or an injection token (Angular). It is the only ambient state in AuthKit, it never
crosses a provider boundary, and it carries no credentials.

### 7.2 Provider picker

When an audience declares more than one provider, the host may render a picker:

```razor
<AuthKitProviderPicker Audience="clients" OnSelected="…" />
```

It renders `DisplayName`, `Description` and `IconUrl` from the registration — **never** from a
hardcoded table. Because `DisplayName` is unique per audience, the citizen never sees two
indistinguishable buttons.

### 7.3 Badge on multi-session screens

When `Sessions.Count > 1`, components that display identity must show which provider the session
belongs to, using `AuthProviderSnapshot.DisplayName` plus the audience. In a host with `intranet`
and `clientes` audiences, "Supabase" alone is meaningless — the badge reads `Clientes · Supabase`,
or `Intranet · Supabase`, and the audit trail records the full ref. Silent ambiguity about "who am I
acting as" is a security problem in administrative software, not a design preference (R-17, R-23).

## 8. Contract changes — 2.0.0

| Type                        | Change                                                                                           |
| --------------------------- | ------------------------------------------------------------------------------------------------ |
| `AuthProviderRef`           | new type: `{ audience, providerId }`, canonical string `audience/providerId`                     |
| `AuthProviderSnapshot`      | new type (§4.2): immutable provenance stored on the session                                      |
| `AuthSession`               | `provider: AuthProviderSnapshot?` — the provider that issued it                                  |
| `AuthStateChangedEventArgs` | `providerRef: string?` — always set when raised by the registry                                  |
| `AuthProviderRegistration`  | new type (§4)                                                                                    |
| `AuthProviderDescriptor`    | new type: registry projection of `AuthProviderRegistration` for the UI                           |
| `AuthAudience`              | `string`, host-declared, validated by §2.1. No enum, no catalogue, one reserved value: `default` |
| `AuthCapabilities`          | unchanged                                                                                        |
| `AuthErrorCode`             | `provider_not_configured`, `provider_ambiguous`, `session_conflict`, `provider_ref_malformed`    |

The ref and the snapshot are immutable on a session. Reassigning either is a bug, and the contract
tests assert that no code path mutates them.

## 9. Security requirements

| ID   | Requirement                                                                                                                                                              |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| R-16 | Duplicate ref, duplicate display name per audience, duplicate primary, or an unrenderable provider fails at startup                                                      |
| R-17 | Screens with more than one live session display which provider is in use, by audience and display name                                                                   |
| R-18 | Sessions are never merged, compared as interchangeable, or passed across providers                                                                                       |
| R-19 | The OAuth state store is keyed by ref; two concurrent flows must not collide                                                                                  |
| R-20 | Routing never consults kind, host name, headers, capability shape or registration order                                                                                  |
| R-21 | A token issued by one provider is never sent to another provider's backend                                                                                               |
| R-22 | `SignOutAsync(ref)` clears exactly one provider; `SignOutAsync()` clears all, in reverse registration order                                                              |
| R-23 | Every log, audit record and support surface identifies a session by its full ref, never by kind or display name alone                                                    |
| R-24 | A provider instance belongs to exactly one audience; registering the same instance under two refs is fatal                                                               |
| R-25 | The access token carries the canonical ref, so a resource server can reject a token minted for another population                                                        |
| R-26 | A session whose ref is no longer registered stays readable and auditable, but cannot be refreshed                                                                        |
| R-27 | AuthKit ships no audience constants and reserves only `default`. An audience referenced by a policy but absent from the registry fails at startup, never at request time |

## 10. Threat model additions

Appended to `docs/threat-model.md` as T-18 … T-23.

| ID   | Threat                                                                                                                            | Control                                                                                               | Test                                                  |
| ---- | --------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| T-18 | Provider shadowing — a second registration overrides the real one                                                                 | Duplicate ref is fatal at startup (R-16)                                                              | `duplicate_provider_ref_fails_at_startup`             |
| T-19 | Vault key confusion — a session loaded under the wrong provider                                                                   | Session ref compared to the vault key on load (R-18)                                                  | `session_under_wrong_key_is_discarded`                |
| T-20 | Cross-provider token use — client token sent to an internal API                                                                   | Ref carried in the token; resource servers validate it; no reuse across providers (R-21, R-25)        | `token_is_never_sent_to_another_provider`             |
| T-21 | State collision between two concurrent OAuth flows                                                                                | State store namespaced per ref (R-19)                                                                 | `two_concurrent_oauth_flows_do_not_collide`           |
| T-22 | Ambiguous routing silently picks the wrong audience                                                                               | Rules 5–6 fail loudly; no fallback (R-20)                                                             | `ambiguous_audience_returns_provider_ambiguous`       |
| T-23 | **Indistinguishable providers** — two same-kind providers in one audience, and the citizen or the operator cannot tell them apart | Unique display name per audience (R-16); kind never a routing key; badge shows audience + name (R-17) | `duplicate_display_name_in_audience_fails_at_startup` |

## 11. Backward compatibility

| v0.1 usage                               | Behaviour in v0.2                                                      |
| ---------------------------------------- | ---------------------------------------------------------------------- |
| `AddAuthKitSupabase(...)`                | Works unchanged; becomes the primary provider of audience `default`    |
| `IAuthProvider` injected directly        | Works; resolves to the primary provider                                |
| `IAuthSessionStore` implementation       | Works; wrapped in a single-key vault                                   |
| Components without `provider`/`audience` | Resolve to primary, exactly as today                                   |
| `StateChanged` without `ProviderId`      | `ProviderId` is null for single-provider deployments and set otherwise |

No source change is required for a host that uses one provider. That is the entire point of
introducing the registry additively rather than replacing `IAuthProvider`.

## 12. Quality gates

1. **Registry unit tests**: duplicate id, duplicate primary, missing primary with several providers,
   unrenderable provider, case sensitivity of audience, resolution order for all five rules.
2. **Two-session integration test**: sign in as staff and as citizen against two stub providers;
   assert both sessions coexist, both events carry the right `ProviderId`, and signing out one
   leaves the other.
3. **Vault tests**: namespacing, corrupt entry isolation, key/session mismatch, concurrent writes.
4. **Contract parity** for §8 in both SDKs.
5. **Pentest**: T-18 … T-22 in `security/AuthKit.Pentest`, including a Blazor Server two-circuit
   variant of T-20 (cross-circuit, cross-provider).

## 13. Open questions

1. **Audience on the server.** In Blazor Server the circuit holds both sessions, so the ambient
   audience is safe. In WebAssembly it lives in the browser and can be tampered with — but it only
   selects a button, and the provider still validates. Confirm this is acceptable, or require the
   audience to come from a server-issued page model.
2. **`AuthProviderRegistration` in the contract.** The registry is a .NET concept today. The Angular
   SDK needs the same shape for its picker. Confirm the descriptor belongs in the shared contract
   (§8) rather than in each SDK.
3. **Provider-scoped stores on Blazor Server.** Two providers, one circuit, one vault: the vault
   must stay in memory on Server hosting, exactly as `InMemoryAuthSessionStore` does today. Confirm
   the same rule applies per provider.

Resolved during review: the ambient component is named `AuthKitAudience` / `<ak-audience>`, not
`AuthKitScope`, because "scope" already means something else in DI.
