# ADR-003 — SDK, not a provider

Status: **accepted**. Supersedes [ADR-002](adr-002-oidc-at-the-edge.md). Related:
[`spec-clave.md`](spec-clave.md), [`spec-providers.md`](spec-providers.md),
[`spec-host-integration.md`](spec-host-integration.md).

## Context

ADR-002 proposed operating an OIDC provider as a hosted service. That decision is rejected.

It would place AuthKit in direct competition with Keycloak, Zitadel, Ory, Authentik, Supabase and
Entra ID — projects with years of head start, paid teams and thousands of production deployments.
Competing there requires operating infrastructure, supporting SLAs, and carrying the liability of a
running identity provider. That is a different company, not a different feature.

The observed gap is narrower and real: **nobody publishes a Cl@ve/eIDAS implementation as a
package.** The official kit is a ZIP of sample code (Java, PHP, .NET) behind a downloads page; the
Keycloak connector that exists is an unmaintained AI-generated repository. A developer in Spain
today reimplements the same eIDAS extensions, the same `PersonIdentifier` prefix handling and the
same error mapping, in every project, from a 40-page PDF.

That gap is a **library** gap, not a hosting gap.

## Decision

AuthKit is a **development kit**. It ships code that runs inside the host's existing server and
browser. It operates nothing.

### The non-negotiable fact

Cl@ve cannot be implemented in a browser. The SP needs a private key to sign the `AuthnRequest`, a
fixed ACS URL registered in the CAID, and a place to validate the assertion signature. A library
cannot change that — but it does not need to.

| Hosted provider (rejected)             | Library (accepted)                                    |
| -------------------------------------- | ----------------------------------------------------- |
| AuthKit operates the process           | The host operates its own process, as today           |
| AuthKit holds the entity's certificate | The host holds its own certificate, in its own config |
| AuthKit issues tokens                  | The host issues whatever it already issues            |
| AuthKit carries the SLA                | The host owns its availability                        |
| AuthKit competes with Keycloak         | AuthKit plugs into Keycloak                           |

The library provides the two endpoints' _logic_. The routes live in the host's application:

```csharp
// ASP.NET Core: the endpoints belong to the host's app, not to AuthKit.
app.MapClave(o =>
{
    o.EntityId   = "https://sede.example.es/clave/metadata";
    o.AcsUrl     = "https://sede.example.es/clave/acs";
    o.Certificate = certificates.Current("caib");   // host-owned
});

// After validation, the host decides what a session means.
app.MapGet("/clave/acs", async (HttpContext ctx, ClaveAuthenticator clave) =>
{
    var identity = await clave.ValidateResponseAsync(ctx.Request);
    await ctx.SignInAsync(identity.ToClaimsPrincipal());   // standard ASP.NET Core
    return Results.Redirect("/");
});
```

## Packages

| Package                                                   | Runs in               | Responsibility                                                   |
| --------------------------------------------------------- | --------------------- | ---------------------------------------------------------------- |
| `@clavejs/core` / `AuthKit.Clave.Core`                    | Host server           | eIDAS `AuthnRequest`, assertion validation, DNI/NIE, errors, LoA |
| `AuthKit.Clave.AspNetCore`                                | Host ASP.NET Core app | Endpoint helpers, DI, claims factory                             |
| `@clavejs/express`, `@clavejs/hono`, `@clavejs/next`      | Host Node app         | The same two endpoints as middleware                             |
| `@clavejs/keycloak`                                       | Host's Keycloak       | Identity provider extension (JAR) — still the host's server      |
| `@clavejs/react`, `@clavejs/angular`, `@clavejs/aurelia*` | Browser               | Method buttons, callback plumbing, session state                 |
| `AuthKit.Blazor`, `@authkit/angular`                      | Browser / host app    | Ready screens over every provider                                |

No package listens on a port by itself. Every one of them is instantiated by a host that already
exists.

## Consequences

**Positive**

- Complementary, not competitive: a Keycloak shop, a Supabase shop and an ASP.NET Identity shop all
  need the same missing piece.
- No infrastructure, no SLA, no on-call. The author's liability stays at the library boundary.
- Adoption is by developers, which is the audience that produces reputation.
- Open sourcing everything (Apache-2.0) costs nothing strategically, because there is no hosted
  asset to protect.

**Negative / accepted**

- The hard part of Cl@ve is **not** the protocol — it is the CAID onboarding. A library alone
  therefore solves half the problem, and users will still arrive with questions.
- The library only helps hosts that _have_ a server. A pure SPA, or a Supabase-only project, needs a
  serverless function before it can use Cl@ve. This must be documented as a prerequisite, not
  discovered.
- A Keycloak user needs a JAR, not a NuGet package. That is still "their server", but it is a second
  implementation to maintain.

## The real moat: onboarding, not SAML

`node-saml` and `ITfoxtec.Identity.Saml2` already validate SAML correctly, and reimplementing
signature validation from scratch is both slow and risky. The library must build on them.

What neither of them does — and what actually costs a Spanish team two weeks — is:

| Pain today                                         | What the kit must do                                           |
| -------------------------------------------------- | -------------------------------------------------------------- |
| Which certificate, in which format, registered how | Validate the certificate and the metadata locally, before CAID |
| Which `entityId`, which ACS, which environment     | A config validator with an actionable error per field          |
| `eidas:SPType` and `RequestedAttributes` shape     | Generated correctly, per attribute `isRequired`                |
| `PersonIdentifier` arriving as `ES/ES/12345678Z`   | Normalised, with DNI/NIE checksum validation                   |
| Pasarela error codes                               | Typed taxonomy with a user-facing message                      |
| Preproducción versus producción                    | A CLI/harness that runs the flow against `se-pasarela`         |

So the kit ships three things, and the third is the differentiator:

1. the protocol profile;
2. the framework adapters;
3. **the onboarding path** — validator, CLI, checklist and fixtures.

A library that saves two weeks of protocol work but leaves the developer lost in the CAID process
has solved half of the problem.

## Deltas

1. ADR-002 is superseded: no hosted provider, no `openid-configuration` authored by AuthKit, no
   AuthKit-issued tokens.
2. `spec-clave.md` §5 becomes "the host's backend contract" — what the library exposes to the host's
   own session mechanism — not an OAuth2 contract between AuthKit and a service.
3. The tenant parameter returns, because the host configures the entity locally; it is not derived
   from a `client_id` that AuthKit controls.
4. `spec-host-integration.md` stays valid: the host still gets a `ClaimsPrincipal` and uses
   `AddOpenIdConnect`/`AddJwtBearer` **if it already has an OIDC provider**. For Cl@ve alone, it
   uses the library's result directly.
5. `AuthKit.Clave` (browser) is reduced to UI: method buttons, LoA display, callback handling.
6. The project name and README must say "kit", never "service" or "provider" — the distinction is
   the positioning.

## Open questions

1. **Base library choice.** Depend on `ITfoxtec.Identity.Saml2` / `node-saml` (fast, proven, but
   their abstractions leak) or embed a minimal validator (controlled, but signature validation is a
   bug farm). Proposal: depend, and keep our layer strictly above them.
2. **Keycloak scope.** A JAR is a second codebase in another language. Ship it only if the demand is
   demonstrated; until then, document the "run the library in a small service and point Keycloak at
   it" path.
3. **Serverless.** Edge Functions are the Supabase answer. Confirm the `core` package runs there
   (WebCrypto, no filesystem, cold-start budget).
4. **Naming.** `@clavejs` for the protocol packages and `AuthKit.*` for the UI may read as two
   brands. Decide before the first publish.
