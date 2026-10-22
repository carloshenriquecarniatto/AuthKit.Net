# AuthKit Cl@ve — Module Specification

**English** · Português · Español _(translations pending)_

Status: **draft, normative once accepted**. Target contract version: **1.1.0** (additive). Extends
[`spec.md`](spec.md). Governed by [`adr-003-sdk-not-a-provider.md`](adr-003-sdk-not-a-provider.md).
Complements [`spec-providers.md`](spec-providers.md) and
[`spec-host-integration.md`](spec-host-integration.md).

---

## 1. Goal

Let a Spanish public-sector team integrate Cl@ve in an afternoon, with a package, instead of two
weeks with a 40-page PDF.

AuthKit Cl@ve is a **library**. It runs inside the server the host already has, and inside the
browser the host already ships. It operates nothing, hosts nothing and issues no tokens of its own.

```text
┌────────────────────┐          ┌──────────────────────────────┐          ┌──────────────┐
│ Browser            │  HTML/   │ Host application             │   SAML   │ Pasarela     │
│ AuthKit.Clave (UI) │ ◄──────► │ + AuthKit.Clave (library)    │ ◄──────► │ Cl@ve        │
└────────────────────┘ redirect └──────────────────────────────┘          └──────────────┘
                                   key, ACS, validation — all host-owned
```

The gap this fills is real and narrow: the official Spanish integration kit is a ZIP of sample code
behind a downloads page, and no NuGet or npm package implements the eIDAS profile. Every team
reimplements the same extensions, the same `PersonIdentifier` prefix handling and the same error
mapping, from scratch.

## 2. Why this is not a social provider

`BeginOAuthAsync("clave")` is rejected because Cl@ve differs from a social IdP in four ways the
model must express:

| Difference                     | Consequence                                                                            |
| ------------------------------ | -------------------------------------------------------------------------------------- |
| Assurance level is a request   | The caller chooses `low` / `substantial` / `high`; the result carries what was granted |
| Method is a request            | Móvil, Permanente, PIN, certificate and DNIe are different journeys, not providers     |
| The browser never holds tokens | The pasarela posts the assertion to the server; the server decides the session         |
| The identity is a legal one    | The subject is a DNI/NIE with a document type, never an email and never a key          |

Encoding the method in a provider string (`"clave:high:movil"`) would hide all of that in a
`string`.

## 3. Packages

| Package                                              | Runs in          | Responsibility                                                          |
| ---------------------------------------------------- | ---------------- | ----------------------------------------------------------------------- |
| `AuthKit.Clave`                                      | Host server      | eIDAS request, assertion validation, identity, errors, config validator |
| `AuthKit.Clave.AspNetCore`                           | Host ASP.NET app | DI, `MapClave()`, endpoint handlers, claims factory                     |
| `@clavejs/core`                                      | Host Node app    | The same, without ASP.NET                                               |
| `@clavejs/express`, `@clavejs/hono`, `@clavejs/next` | Host Node app    | Endpoint adapters over `core`                                           |
| `AuthKit.Clave.Browser`                              | Browser          | Method buttons, LoA display, callback plumbing                          |
| `@authkit/angular` (extend)                          | Browser          | `<ak-clave>`, `<ak-clave-callback>`, `provideAuthKitClave()`            |

Versioning stays lockstep per `spec.md` §8. No package listens on a port by itself.

### 3.1 Non-goals

- Hosting, operating, or acting as a service provider on anyone's behalf.
- Issuing access tokens, refresh tokens or OIDC discovery documents.
- Managing certificates beyond reading and validating the one the host supplies.
- Automating the CAID registration (the kit prepares the artefacts; the entity still applies).
- Cl@ve Firma (document signature). Signature is a three-phase flow and belongs to
  `spec-signature.md`.
- Employee login, LDAP, or internal directory federation.
- Keycloak. A JAR is a second codebase in another language; see §13.

## 4. Architecture

### 4.1 Hard boundary: no SAML in the browser

| Artifact                   | May parse XML/SAML | May touch the private key  |
| -------------------------- | ------------------ | -------------------------- |
| `AuthKit.Clave` (server)   | **Yes**            | Yes, via the host's config |
| `AuthKit.Clave.AspNetCore` | No — delegates     | No                         |
| `AuthKit.Clave.Browser`    | **No**             | **No**                     |
| `@clavejs/*` (browser)     | **No**             | **No**                     |

The browser package receives a redirect, at most an error code, and nothing else. Signature
validation happens once, on the server, in the host's own process. R-01 makes this testable: a build
assertion fails if a browser package references an XML or SAML dependency.

### 4.2 Flow, server-driven

```text
1. User clicks a method button
   UI ──► GET /clave/login?loa=substantial&method=clave_movil&returnUrl=/expedientes
   server validates the request, stores {requestId, state, loa}, builds a signed AuthnRequest
   server ──► 302 to the pasarela (POST or Redirect binding, host-configured)

2. User authenticates at Cl@ve

3. Pasarela ──► POST /clave/acs   (SAMLResponse, RelayState)
   server validates: signature, InResponseTo, Destination, Audience, time window,
   StatusCode, replay; then extracts the identity

4. The host decides what a session is
   ctx.SignInAsync(identity.ToClaimsPrincipal())     // ASP.NET Core: Identity, cookie, whatever
   server ──► 302 /expedientes

5. The UI is already authenticated, because the host's own session exists
```

Step 4 is the whole point: the library stops at "here is a validated identity". It never signs a
cookie, never mints a JWT, never writes a session. That is what keeps it a kit and not a provider.

## 5. The library surface

### 5.1 Configuration

```csharp
builder.Services.AddAuthKitClave(clave =>
{
    clave.Environment   = ClaveEnvironment.Preproduction;      // se-pasarela | pasarela
    clave.EntityId      = "https://sede.example.es/clave/metadata";
    clave.AcsUrl        = "https://sede.example.es/clave/acs";
    clave.SigningCertificate = hostCertificates.Current("caib"); // must have a private key
    clave.PasarelaSigningCertificate = hostTrust.Anchors.Clave;  // pinned, not fetched
    clave.RequestedAttributes =
    [
        ClaveAttributes.PersonIdentifier,   // required
        ClaveAttributes.GivenName,
        ClaveAttributes.FamilyName,
        ClaveAttributes.DateOfBirth,        // optional on purpose — see R-08
    ];
    clave.DefaultLevel  = AuthAssuranceLevel.Substantial;
    clave.Tenant        = "caib";            // local label, used in claims and audit
    clave.ReturnUrlPolicy = ReturnUrlPolicy.SameOrigin;
});
```

### 5.2 Startup validation

Every item below fails `AddAuthKitClave` with a message naming the field and the fix. This is the
component that replaces two weeks of confusion, so it must be exhaustive:

| Check                                                         | Message intent                                   |
| ------------------------------------------------------------- | ------------------------------------------------ |
| `EntityId` absolute, `https`, no trailing slash inconsistency | "CAID compares this string literally"            |
| `AcsUrl` absolute `https`, same origin as the app             | "the pasarela posts here; it must be reachable"  |
| Certificate has a private key                                 | "a public-only certificate cannot sign"          |
| Certificate `NotAfter` > 30 days                              | "expires in N days"                              |
| Certificate key usage allows digital signature                | "this certificate is for encryption"             |
| `Environment` and endpoint pair are consistent                | "preproduction and production use different CAs" |
| `RequestedAttributes` contains `PersonIdentifier`             | "without it there is no subject"                 |
| Levels requested are a subset of what Cl@ve can grant         | "`low` is not offered by Cl@ve 2"                |
| `Tenant` matches the pattern `^[a-z0-9][a-z0-9._-]{0,63}$`    | —                                                |

A `doctor` command (`dotnet authkit-clave doctor`) runs the same checks plus DNS, TCP and TLS
against the configured pasarela, and prints the CAID checklist with the artefacts already filled in.

### 5.3 Endpoints the host maps

The library supplies handlers; the host owns the routes.

```csharp
app.MapClave();          // /clave/metadata, /clave/login, /clave/acs
app.MapClave("/saml");   // any prefix the host prefers
```

| Endpoint            | Method | Purpose                                                       |
| ------------------- | ------ | ------------------------------------------------------------- |
| `{prefix}/metadata` | GET    | SP metadata XML to hand to CAID, generated from the config    |
| `{prefix}/login`    | GET    | Validates parameters, builds the signed `AuthnRequest`, 302   |
| `{prefix}/acs`      | POST   | Validates the response, resolves the identity, calls the host |

The ACS handler does not sign anyone in. It invokes a host callback and returns a redirect:

```csharp
app.MapClave(onAuthenticated: async (ctx, identity) =>
{
    await ctx.SignInAsync(identity.ToClaimsPrincipal());   // or Identity, or nothing
    return Results.Redirect(identity.ReturnUrl ?? "/");
});
```

### 5.4 The eIDAS request

Generated, not assembled by hand:

- `Issuer` = configured `EntityId`;
- `Destination` = environment endpoint;
- `eidas:SPType` = `public` (configurable for private entities);
- `eidas:RequestedAttributes` with `isRequired` **per attribute** — never globally true, because a
  required `DateOfBirth` breaks login for citizens whose Cl@ve profile lacks it (R-08);
- `AuthnContextClassRef` = `http://eidas.europa.eu/LoA/{level}` with `Comparison="minimum"` when a
  minimum is intended, `exact` when the host demands exactly one level;
- `ProviderName` from configuration, shown by the pasarela;
- `forceAuthn` for `prompt=login`;
- signature over the whole request, `RSA-SHA256`, with the `KeyInfo` the CAID expects.

### 5.5 Response validation

Ordered, fail-closed, and each failure maps to a distinct internal reason (never to a user string):

1. Maximum size, and DTD/XXE processing disabled.
2. Signature valid against the **pinned** pasarela certificate (never fetched at runtime).
3. `Issuer` is the expected Cl@ve entity for the environment.
4. `InResponseTo` matches a pending request created by this host and is not expired.
5. `Destination` equals the configured `AcsUrl`.
6. `Audience` restriction contains `EntityId`.
7. `StatusCode` is `Success`; otherwise map the second-level status.
8. `NotOnOrAfter` and `IssueInstant` within a configurable skew (default ±180 s).
9. Assertion ID not seen before (replay cache, keyed by ID, TTL from the assertion lifetime).
10. Subject present; `PersonIdentifier` present and normalisable.
11. Granted `acr` ≥ requested level; otherwise `loa_not_met` (R-09).

### 5.6 Identity

```csharp
public sealed record ClaveIdentity
{
    /// Opaque, stable per (tenant, document). HMAC-SHA256 of the normalised document,
    /// truncated — never reversible, never the DNI itself.
    public required string Subject { get; init; }
    public required AuthDocumentType DocumentType { get; init; }
    public required string DocumentId { get; init; }        // normalised: 12345678Z
    public string? DocumentIdRaw { get; init; }             // ES/ES/12345678Z, for audit
    public string? GivenName { get; init; }
    public string? FamilyName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public required AuthAssuranceLevel Level { get; init; }
    public required string Method { get; init; }            // clave_movil, certificate, …
    public string? Tenant { get; init; }
    public string? RepresentedDocumentId { get; init; }
    public DateTimeOffset AuthenticatedAt { get; init; }

    public ClaimsPrincipal ToClaimsPrincipal();             // §5.7
}
```

Normalisation covers the cases the ecosystem gets wrong: `ESP/ES/`, `ES/ES/`, mixed case, spaces and
hyphens; NIE with `X/Y/Z`; and DNI/NIE checksum validation. An unrecognised shape is an error, not a
silent pass-through.

`Subject` is derived with an HMAC keyed by a host secret, so the same citizen in two entities
produces two different subjects and no cross-entity correlation is possible without the key.

### 5.7 Claims

Mapping is normative so that policy written once works everywhere; see
[`spec-host-integration.md`](spec-host-integration.md) §2.

| Claim              | Value                                            |
| ------------------ | ------------------------------------------------ |
| `sub`              | `Subject`                                        |
| `name`             | `GivenName` + `FamilyName`                       |
| `acr`              | `http://eidas.europa.eu/LoA/{level}`             |
| `amr`              | `Method`                                         |
| `act`              | `RepresentedDocumentId`, when present            |
| `authkit:tenant`   | `Tenant`                                         |
| `authkit:provider` | the provider ref from the registry               |
| `authkit:doc_type` | `dni`, `nie`                                     |
| `authkit:doc_id`   | `DocumentId` — PII, opt-in, never in logs (R-10) |

### 5.8 Errors

Two layers, deliberately separate:

- **Internal reasons** (`SignatureInvalid`, `InResponseToMismatch`, `ReplayDetected`,
  `AudienceMismatch`, `LoaBelowRequested`, `CertificateUnknown`, …) — logged with the request ID,
  never shown.
- **Host-facing `AuthErrorCode`** — the taxonomy in `spec-providers.md`: `federation_failed`,
  `federation_cancelled`, `loa_not_met`, `certificate_invalid`, `identity_not_verified`,
  `tenant_mismatch`.

A `SignatureInvalid` becomes a generic `federation_failed` outward (R-15): telling a remote user
that a signature failed is telling an attacker where to aim.

### 5.9 Server-side state

Two pieces of state are mandatory. There is no stateless mode: without them, `InResponseTo` cannot
be bound and a captured assertion is a valid login.

```csharp
public interface IClaveRequestStore
{
    ValueTask SaveAsync(PendingClaveRequest request, CancellationToken cancellationToken = default);

    /// At-most-once consumption. Returns null when the id is unknown or already taken.
    ValueTask<PendingClaveRequest?> TakeAsync(string requestId, CancellationToken cancellationToken = default);
}

public interface IClaveReplayCache
{
    /// Returns false when the assertion id was already registered.
    ValueTask<bool> TryRegisterAsync(string assertionId, DateTimeOffset notOnOrAfter, CancellationToken cancellationToken = default);
}
```

Resolution order, decided at registration time and logged once at startup:

1. The host registered an implementation — use it.
2. `IDistributedCache` is registered — adapt it, prefixing every key with `authkit:clave:`.
3. Neither — in-memory, plus a **warning** in `Production` naming the consequence: with more than
   one instance, logins fail intermittently.

Normative rules:

| Rule              | Value                                                                         |
| ----------------- | ----------------------------------------------------------------------------- |
| Request lifetime  | `ClaveOptions.RequestLifetime`, default 15 minutes                            |
| Replay retention  | assertion `NotOnOrAfter` plus the configured skew                             |
| Key namespace     | `authkit:clave:{tenant}:req:{id}` and `authkit:clave:{tenant}:rpl:{id}`       |
| Contents          | request id, return URL, state, requested level, method — **no document data** |
| Cache unavailable | **fail closed**: reject with `federation_failed`, log the cause               |

`TakeAsync` is at-most-once only as far as the store allows. `IDistributedCache` has no atomic
get-and-delete, so two concurrent ACS posts for the same request may both read it. The replay cache
is the backstop, and it works because both posts carry the **same** assertion id: exactly one
registers. That is why the replay cache is not optional.

Because `IDistributedCache` cannot distinguish "first write" from "overwrite", a host that needs
true replay protection behind a load balancer registers its own `IClaveReplayCache` over a store
with `SET NX` semantics (Redis, SQL with a unique constraint, DynamoDB conditional write). The
adapter is a convenience for single-instance deployments, and it says so.

Failing closed means a cache outage is a login outage. For a sede electrónica that is the correct
trade, and it must be stated in `docs/clave.md` so operators size the cache accordingly.

## 6. Browser side

`AuthKit.Clave.Browser` renders and navigates. It never completes a flow, because the host already
did:

```csharp
public interface IServerDrivenAuthProvider : IAuthProvider
{
    /// Builds the host's own login URL. The host navigates; AuthKit never navigates.
    Task<string> BeginFederationAsync(AuthFederationRequest request, CancellationToken cancellationToken = default);

    /// Reads the outcome the host put in the redirect. No network call, no token exchange.
    AuthResult CompleteFromCallback(string callbackUrl);
}
```

`CompleteFromCallback` inspects only `error` / `error_reason` and strips them; success is inferred
from the host's session, which exists by then. That is why the browser package has no token handling
at all.

## 7. Contract changes — 1.1.0 (additive)

| Type                     | Change                                                                                                                        |
| ------------------------ | ----------------------------------------------------------------------------------------------------------------------------- |
| `AuthIdentity`           | new: document type/value, names, birth date, level, method, tenant                                                            |
| `AuthDocumentType`       | new enum: `dni`, `nie`, `cif`, `unknown`                                                                                      |
| `AuthAssuranceLevel`     | new enum: `low`, `substantial`, `high`                                                                                        |
| `AuthFederationProvider` | new: id, kind, display name, methods, levels                                                                                  |
| `AuthFederationMethod`   | new: id, display name, description, levels, recommended                                                                       |
| `AuthUser`               | `identity: AuthIdentity?` — additive                                                                                          |
| `AuthCapabilities`       | `federation: AuthFederationProvider[]` — additive                                                                             |
| `AuthErrorCode`          | `federation_cancelled`, `federation_failed`, `loa_not_met`, `certificate_invalid`, `identity_not_verified`, `tenant_mismatch` |
| `AuthState`              | `redirecting` — additive                                                                                                      |

`AuthUser.id` equals `AuthIdentity.subject`. The document identifier appears only in
`AuthIdentity.documentId`.

## 8. UI

| Blazor                   | Angular               | Behaviour                                                                        |
| ------------------------ | --------------------- | -------------------------------------------------------------------------------- |
| `<AuthKitClave>`         | `<ak-clave>`          | One button per declared method, plus a level selector when more than one applies |
| `<AuthKitClaveCallback>` | `<ak-clave-callback>` | Reads the redirect outcome, strips the query, raises navigation                  |

Rendering comes from the provider descriptor in [`spec-providers.md`](spec-providers.md) §2.1 and
`spec-host-integration.md` §7 — **no `switch` on method or provider id exists** (R-16). Labels,
descriptions, ordering and the recommended default all arrive as data.

### 8.1 Branding

- The official logotype comes from the entity, never redrawn or recoloured.
- Cl@ve and AuthKit are never visually merged.
- The `ProviderName` returned by the pasarela is displayed unchanged.
- No government mark is bundled in the package; hosts supply the logo URL.

## 9. Security requirements (release blocking)

| ID   | Requirement                                                                                           |
| ---- | ----------------------------------------------------------------------------------------------------- |
| R-01 | Browser packages contain no XML/SAML parser and no signing capability; a build assertion enforces it  |
| R-02 | The pasarela signing certificate is pinned in configuration; never fetched, never `trustAll`          |
| R-03 | DTD and external entity processing are disabled; response size is bounded                             |
| R-04 | `InResponseTo` is bound to a pending request created by this host and is single-use                   |
| R-05 | `Destination`, `Audience` and `Issuer` are all validated; any mismatch is fatal                       |
| R-06 | Assertion IDs are cached for replay detection with a TTL equal to the assertion lifetime              |
| R-07 | Clock skew is bounded and configurable; expired assertions are rejected                               |
| R-08 | `isRequired` is per attribute; the default configuration never marks `DateOfBirth` required           |
| R-09 | A granted level below the requested one fails (`loa_not_met`); no silent downgrade                    |
| R-10 | The document identifier is never a log field, metric tag or exception message                         |
| R-11 | `Subject` is derived by HMAC with a host secret; the DNI is never a primary key or a claim by default |
| R-12 | `returnUrl` is same-origin and validated against a policy; open redirect is impossible                |
| R-13 | `SignatureInvalid` and every internal reason surface as a generic error; details stay in server logs  |
| R-14 | The library never calls `SignInAsync`, never writes a cookie, never mints a token                     |
| R-15 | The private key is read from the host's configuration or key store; the library never persists it     |
| R-16 | The UI contains no `switch` on provider or method; everything renders from descriptors                |

## 10. Threat model additions

Appended to `docs/threat-model.md` as T-10 … T-19.

| ID   | Threat                                                          | Control                                                       | Test                                                                       |
| ---- | --------------------------------------------------------------- | ------------------------------------------------------------- | -------------------------------------------------------------------------- |
| T-10 | Forged assertion reaches the host                               | R-02, R-03, full validation chain §5.5                        | `unsigned_assertion_is_rejected`, `signature_from_unknown_key_is_rejected` |
| T-11 | XXE / billion laughs through the response                       | DTD and external entities disabled, size bounded              | `dtd_is_rejected`                                                          |
| T-12 | Replay of a valid assertion                                     | R-04, R-06                                                    | `replayed_assertion_is_rejected`                                           |
| T-13 | Assertion minted for another SP                                 | R-05 (Audience + Destination)                                 | `audience_mismatch_is_rejected`                                            |
| T-14 | LoA downgrade — weaker credential accepted for a stronger route | R-09                                                          | `granted_level_below_requested_fails`                                      |
| T-15 | DNI leakage through logs, metrics or errors                     | R-10, R-11                                                    | `document_id_is_not_logged`                                                |
| T-16 | Open redirect after ACS                                         | R-12                                                          | `foreign_return_url_is_rejected`                                           |
| T-17 | Certificate expiry discovered in production                     | Startup check + `doctor` command                              | `expiring_certificate_fails_startup`                                       |
| T-18 | Key exfiltration through process memory dumps                   | Key held by the host's key store, never copied by the library | Design review                                                              |
| T-19 | Error oracle — distinct responses reveal which check failed     | R-13                                                          | `all_validation_failures_share_one_status`                                 |

## 11. Quality gates

1. **Protocol fixtures**, committed and anonymised, in `samples/messages/clave/`: valid response,
   tampered signature, wrong audience, replayed ID, expired assertion, LoA below request, malformed
   DNI, pasarela error, and a cancelled login.
2. **No network in tests.** Everything replays from fixtures through the library's public API.
3. **Purity assertions** per §4.1: browser packages have no XML dependency and no private-key type.
4. **Contract parity** for §7 in both SDKs.
5. **Pentest** T-10 … T-19 in `security/AuthKit.Pentest`.
6. **Doctor** covered by tests: each startup check has a failing case with the expected message.
7. **Docs**: `docs/clave.md` in ES/PT/EN, including the CAID checklist and the two-environment
   setup.

## 12. Delivery plan

| Milestone | Content                                                                                                                                              | Exit criterion                                                                          |
| --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `0.1.0`   | `AuthKit.Clave` core: request, validation chain, identity, errors, fixtures; `AuthKit.Clave.AspNetCore` with `MapClave`; metadata endpoint; `doctor` | Against fixtures, all T-10…T-19 pass                                                    |
| `0.2.0`   | Browser package and UI components; registry integration per `spec-providers.md`                                                                      | Login works in the sample app against preproduction                                     |
| `0.3.0`   | `@clavejs/core` + Express/Hono/Next adapters                                                                                                         | Same fixtures pass in Node                                                              |
| `0.4.0`   | Host integration per `spec-host-integration.md`: claims factory, Identity bridge, policies                                                           | `[Authorize(Policy = "authkit:loa:high")]` works with no AuthKit type in the controller |
| deferred  | Keycloak JAR; `eudi_wallet` as another federation kind; `spec-signature.md`                                                                          | Demand-driven                                                                           |

## 13. Open questions

1. **Base library.** Depend on `ITfoxtec.Identity.Saml2` (proven, but its abstractions leak) or
   embed a minimal validator (controlled, but signature validation is a bug farm). Proposal: depend,
   and keep our layer strictly above it — the value is the profile and the onboarding, not the
   crypto.
2. **Keycloak.** A JAR is a second implementation. Ship only on demonstrated demand; until then,
   document "run `@clavejs/core` in a small service and point Keycloak at it".
3. **Naming.** `@clavejs/*` for the protocol and `AuthKit.*` for the UI may read as two brands.
   Decide before the first publish.
4. **Two entities in one deployment.** With no hosted service, a host serving CAIB and Palma needs
   two configurations and two ACS routes. Confirm `MapClave` supports multiple named instances, or
   whether that is explicitly out of scope.

Resolved: the pending-request store and the replay cache are both mandatory and both specified in
§5.9. There is no stateless mode.
