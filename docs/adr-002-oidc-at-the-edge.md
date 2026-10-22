# ADR-002 — OIDC at the edge

Status: **superseded by [ADR-003](adr-003-sdk-not-a-provider.md)**. Kept for the record; do not
implement. Supersedes the bespoke wire contract in [`spec-clave.md`](spec-clave.md) §5. Related:
[`spec-providers.md`](spec-providers.md), [`spec-host-integration.md`](spec-host-integration.md).

## Context

The first draft of the Cl@ve module defined a private, OAuth2-shaped HTTP contract between AuthKit
and a federation backend: `/authorize`, `/token`, `/session`, `/logout`, plus a
`/.well-known/authkit-federation` document and an `identity` member in the token response.

Two things made that contract a liability:

1. **It required an SDK per language.** A bespoke contract means every consumer must be written:
   TypeScript, C#, PHP, Java, Go. Each one is a maintenance surface, a security surface and a
   documentation surface.
2. **The product is becoming a hosted service.** If the federation backend is operated as a service
   for many entities, it is no longer an internal detail of one application. It is a provider. A
   provider with a private protocol is a provider nobody can adopt without asking permission.

The alternative is to be a **standard OpenID Connect provider** and keep only the Spain-specific
semantics as a documented extension profile.

## Decision

The Cl@ve Federation Service is an **OIDC Core 1.0 provider** with PKCE, plus a small Cl@ve
extension profile. The private contract is deleted, not wrapped.

| Bespoke (draft)                   | OIDC (decision)                                          |
| --------------------------------- | -------------------------------------------------------- |
| `/.well-known/authkit-federation` | `/.well-known/openid-configuration`                      |
| `GET /authorize`                  | `authorization_endpoint`, `response_type=code`           |
| `POST /token`                     | `token_endpoint`, `authorization_code` + `refresh_token` |
| `GET /session`                    | `userinfo` (and `id_token`)                              |
| `POST /logout`                    | `end_session_endpoint`                                   |
| `GET /jwks`                       | `jwks_uri`                                               |
| `identity` member in the response | `id_token` claims + `userinfo` claims                    |
| Custom `loa` parameter            | `acr_values` → `acr` claim                               |
| Custom method parameter           | `amr` claim (requested via `clave_method`, an extension) |
| Custom `tenant`                   | **Deleted.** The entity is the `client_id`               |

### What falls out for free

- **Every language.** `Microsoft.AspNetCore.Authentication.OpenIdConnect`, Spring Security,
  `league/oauth2-client`, Authlib, `go-oidc`, `oidc-client-ts`, `angular-auth-oidc-client`,
  `react-oidc-context`, `passport-openidconnect`. No SDK to write, no SDK to keep in sync.
- **Framework integration.** `spec-host-integration.md` collapses: a .NET host uses
  `AddOpenIdConnect` and `AddJwtBearer` with `Authority` + `Audience`, exactly as it would with
  Entra ID or Auth0. AuthKit's own adapters become optional ergonomics, not the only way in.
- **Assurance level.** `acr` / `acr_values` is the standard vehicle for LoA; the eIDAS LoA URIs fit
  unchanged.
- **Attribute requests.** OIDC's `claims` parameter (Core §5.5) requests `document_id` without
  inventing a scope language.
- **Future providers.** EUDI wallet is converging on OpenID4VC/OpenID4VP, i.e. OIDC-shaped. The
  roadmap stops being a series of bespoke integrations.

### The tenant simplification

Draft §5.1 published a `tenants` array and took a `tenant` authorization parameter. That was wrong
for two reasons: the tenant became user input (T-14), and the client had to know about entities it
does not belong to.

In OIDC, **the entity is the client**. `client_id = caib` (or `caib-sede`, `caib-ingrest` for
several apps of one entity). The client registration fixes:

- the entity's certificate and, therefore, the SP identity in the CAID;
- the registered `redirect_uris` and the ACS URL;
- the display name shown near the Cl@ve button;
- the allowed `acr_values`.

The token then carries `tenant` as a **derived claim**, never as a request parameter. Tenant
tampering stops being a threat class because there is nothing to tamper with.

If an entity demands issuer-level isolation, the fallback is a per-entity issuer
(`https://auth.example.es/t/caib`) with its own discovery document and JWKS. Same protocol, more
operational overhead. Not the default.

### What stays custom, and why that is fine

| Extension                                   | Why it cannot be standard                                             |
| ------------------------------------------- | --------------------------------------------------------------------- |
| `clave_method` request param                | Cl@ve exposes a user journey choice; OIDC has no equivalent           |
| `authkit:tenant` claim                      | Derived from the client; useful for audit and policy                  |
| `authkit:doc_type`, `authkit:doc_id` claims | Spanish identity semantics, only when the `document` scope is granted |
| `act` claim for representation              | Standard actor claim, used for apoderamiento                          |

OIDC explicitly permits additional parameters and claims. The rule is that a client which ignores
all of them still gets a compliant, working login.

## Consequences

**Positive**

- No SDK per language. The PHP ayuntamiento integrates in an afternoon with a library it already
  has.
- `spec-host-integration.md` shrinks: standard handlers replace custom ones.
- Interoperability is testable against the OIDC conformance suite instead of against our own tests.
- The commercial story is credible: "we are an OIDC provider with a Spanish eIDAS profile", not "we
  have a proprietary protocol, please adopt it".

**Negative / accepted**

- We lose the freedom to reshape the wire format. Token shape becomes a public contract.
- `end_session_endpoint` can only end **our** session; Cl@ve has no single logout and we must not
  imply otherwise.
- Some OIDC clients assume `email` or `picture`. We return neither; `profile` scope exposes `name`
  only. Documented, and libraries tolerate absent claims.
- The `claims` parameter is unevenly implemented across clients; it must be optional, with
  `document` scope as the fallback.

## Deltas to apply to `spec-clave.md`

1. Replace §5 entirely with "OIDC profile v1": discovery, authorization (with `acr_values`, `scope`,
   `claims`, `clave_method`), token, userinfo, jwks, end_session.
2. Delete `tenants` from discovery and the `tenant` authorization parameter. Keep `tenant` as a
   claim.
3. Rename `AuthKit.Clave`'s backend option from `BackendBaseUrl` to `Authority`, matching
   `AddOpenIdConnect` vocabulary.
4. Add an `AuthKit.Clave` compatibility statement: any OIDC client can reach the service; AuthKit
   adds the Cl@ve-specific UI (method buttons, LoA, branding) and nothing else.
5. Demote `IFederatedAuthProvider` from "the way to plug Cl@ve" to "the way to get Cl@ve-specific
   UI"; the protocol path is `AddOpenIdConnect`.
6. Keep §5.3's JWT requirements — they are already OIDC-compatible.
7. In `spec-host-integration.md`, make plain `AddOpenIdConnect` + `AddJwtBearer` the documented
   default and the AuthKit packages the optional convenience.

## Open questions

1. **One issuer or one per entity.** Default is one issuer with `client_id` per entity. Confirm that
   CAIB and Palma accept sharing an issuer, since the SP identity is per client certificate anyway.
2. **Certificate custody per client.** N entities means N private keys. HSM, KMS, or encrypted at
   rest with audit — a decision with a legal weight, not an engineering one.
3. **Who is the data controller.** The entity is the SP and the controller; the service is a
   processor under GDPR art. 28. That requires a DPA per entity and ENS compliance as a provider.
   Confirm before the first pilot.
4. **Conformance target.** OIDC Core certification is optional but marketable in this sector. Decide
   whether v1 aims for "works with all major clients" or "certified".
