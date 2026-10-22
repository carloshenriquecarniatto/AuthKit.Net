# AuthKit v0.2 — Host Integration Specification

**English** · Português · Español _(translations pending)_

Status: **draft, normative once accepted**. Target contract version: **1.1.0** (additive). Extends
[`spec.md`](spec.md). Complements [`spec-providers.md`](spec-providers.md) and
[`spec-clave.md`](spec-clave.md).

---

## 1. Principle

AuthKit must not be a parallel auth universe. Every host already has an opinionated, tested way to
express "who is this user" — and AuthKit's job is to feed it, not to replace it.

> **AuthKit produces and consumes platform primitives. It never asks the host to learn a second
> security model.**

| Platform | Primitive the host already uses                                    | AuthKit produces                                  |
| -------- | ------------------------------------------------------------------ | ------------------------------------------------- |
| .NET     | `ClaimsPrincipal`, `AuthenticationTicket`, `[Authorize]`           | a claims principal and an authentication scheme   |
| .NET     | ASP.NET Core Identity (`UserManager`, `SignInManager`)             | an external login: `(LoginProvider, ProviderKey)` |
| Blazor   | `AuthenticationStateProvider`, `IAccessTokenProvider`              | both interfaces, backed by the session            |
| Angular  | `HttpInterceptorFn`, `CanActivateFn`, `EnvironmentProviders`       | one interceptor, one guard, one `provide*`        |
| Aurelia  | plugin `configure`, router pipeline step, `HttpClient` interceptor | one plugin, one step, one interceptor             |

If a host has to write glue code to make `[Authorize]` work, the integration is wrong.

## 2. Canonical claims

The session and the principal are two views of the same facts. The mapping is normative, so that
policy written once works on every platform.

| Claim              | Source                          | Notes                                               |
| ------------------ | ------------------------------- | --------------------------------------------------- |
| `sub`              | `AuthIdentity.subject`          | Opaque. Never the DNI                               |
| `name`             | `givenName` + `familyName`      | Display only                                        |
| `acr`              | `loa`                           | `http://eidas.europa.eu/LoA/{low,substantial,high}` |
| `amr`              | `method`                        | `clave_movil`, `clave_permanente`, `certificate`    |
| `act`              | `representedDocumentId`         | Present only when acting on behalf of someone       |
| `authkit:provider` | `AuthProviderSnapshot.Ref`      | `clients/clave`                                     |
| `authkit:audience` | `AuthProviderSnapshot.Audience` | `clients`                                           |
| `authkit:tenant`   | `AuthProviderSnapshot.Tenant`   | `caib`, `palma`                                     |
| `authkit:doc_type` | `documentType`                  | `dni`, `nie`                                        |
| `authkit:doc_id`   | `documentId`                    | Normalised. PII: never a log field (R-09)           |

`acr` and `amr` are used because they are the standard place for assurance level and method. A host
that later federates Entra ID or an EUDI wallet gets the same claim shapes for free.

`ClaimsPrincipal` construction is single-sourced: one `AuthKitClaimsFactory` produces a
`ClaimsIdentity` with `authenticationType` set, and every platform adapter (cookie, bearer, Blazor,
Angular) derives from it. Two independent mappings would drift, and a drifted `acr` is an
authorization bypass.

## 3. .NET

### 3.1 Two schemes, one factory

```csharp
// API / resource server.
// There is no AuthKit issuer: the host mints its own token after validating Cl@ve,
// and validates it with the platform's own handler.
builder.Services
    .AddAuthentication(AuthKitDefaults.BearerScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://sede.example.es";   // the host's own OIDC provider,
                                                          // if it already has one
        options.Audience = "sede-api";
        options.RequireHttpsMetadata = true;
    });

// Interactive server app: cookie issued after a federation round trip.
builder.Services
    .AddAuthentication(AuthKitDefaults.CookieScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-authkit";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.SlidingExpiration = false;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
```

If the host has no issuer of its own and still needs bearer tokens for a separate API, two supported
paths exist:

1. **Cookie only** — the API is the same origin and reads the cookie. Simplest, and correct for a
   server-rendered sede.
2. **Host-minted JWT** — `AuthKit.Clave` returns a `ClaimsPrincipal`; the host signs it with its own
   key (`JwtSecurityTokenHandler` or `Microsoft.IdentityModel.JsonWebTokens`) and the API validates
   it with `AddJwtBearer`. AuthKit does not supply the signing, the key or the endpoint.

The cookie bridge is the piece that makes the whole thing work: after the ACS handler returns, the
host calls the standard `HttpContext.SignInAsync(scheme, principal)` and from that moment
`HttpContext.User` is populated, `[Authorize]` works, and nothing in the application knows that
Cl@ve exists.

AuthKit never calls `SignInAsync` itself. Sign-in is the host's decision, expressed with the
platform's own API (R-14 of `spec-clave.md`).

### 3.2 ASP.NET Core Identity

A federated identity is, to Identity, exactly what an external login already is. The bridge uses the
existing table, not a new one:

```csharp
var refId = session.Provider.Ref;        // clients/clave
var key   = session.User.Id;             // opaque subject

var result = await signInManager.ExternalLoginSignInAsync(refId, key, isPersistent: false);
if (result.Succeeded)
{
    return LocalRedirect(returnUrl);
}

if (result.IsNotAllowed)
{
    // The local account exists but cannot sign in: lockout, disabled, unconfirmed email.
    return Forbid();
}

var user = new ApplicationUser { UserName = $"clave:{key}", DisplayName = session.User.DisplayName };
await userManager.CreateAsync(user);
await userManager.AddLoginAsync(user, new UserLoginInfo(refId, key, "AuthKit"));
await signInManager.SignInAsync(user, isPersistent: false);
```

`LoginProvider` is the canonical ref, so `internal/entra-staff` and `clients/clave` are distinct
external logins for the same `ApplicationUser` if the host chooses to link them. That linking is a
product decision, not an AuthKit one — but the schema already supports it, which is the point.

Rules:

1. AuthKit never owns a user table, never hashes a password, never issues a cookie of its own.
2. `ProviderKey` is the opaque `sub`. The DNI is **not** a login key: it changes spelling, it is
   PII, and it cannot be a stable identifier across identity providers.
3. Roles and permissions stay in Identity (`AspNetRoles`, `AspNetUserClaims`). AuthKit supplies
   identity, not authorization.
4. `IUserClaimsPrincipalFactory` is extended — not replaced — to add §2 claims to the Identity
   principal, so `acr` survives into the cookie.

### 3.3 Hosts migrating from an existing store

A host that already stores DNI/NIE in its own user table must be able to link without re-creating
accounts:

```csharp
services.AddAuthKitIdentityBridge<ApplicationUser>(bridge =>
{
    bridge.LinkBy(claims => claims.DocumentId,           // 1st: existing document column
                  users  => users.SingleOrDefault(u => u.Documento == documentId));
    bridge.LinkBy(claims => claims.Email,                // 2nd: existing email
                  users  => users.SingleOrDefault(u => u.Email == email));
    bridge.UnlinkedPolicy = UnlinkedPolicy.CreateAndLink; // or Reject
});
```

Order is explicit and audited. `UnlinkedPolicy.Reject` exists because some administrations must not
auto-provision accounts.

### 3.4 Authorization policies

Policy is the host's language. AuthKit ships requirements, not a custom attribute:

```csharp
builder.Services.AddAuthKitPolicies(policies =>
{
    policies.RequireMinimumLoa(AuthAssuranceLevel.High);   // policy "authkit:loa:high"
    policies.RequireProvider("clients/clave");             // policy "authkit:provider:clients/clave"
    policies.RequireAudience("internal");                  // policy "authkit:audience:internal"
    policies.RequireRepresentation();                      // policy "authkit:representative"
});
```

Usage is then plain ASP.NET Core, with no AuthKit in sight:

```csharp
[Authorize(Policy = "authkit:loa:high")]
public IActionResult Firmar() => View();
```

`RequireMinimumLoa` compares `acr` ordinally against an ordered list (`low < substantial < high`)
and fails closed when `acr` is absent. A token without `acr` never satisfies a LoA policy (R-28).

### 3.5 Blazor

| Hosting       | What the host registers                                                                                   | What the browser sees       |
| ------------- | --------------------------------------------------------------------------------------------------------- | --------------------------- |
| Blazor Server | `AddAuthKitBlazorServer()` → `AuthenticationStateProvider` over the Identity cookie                       | No token at all             |
| Blazor WASM   | `AddAuthKitBlazorWebAssembly()` → `AuthenticationStateProvider` + `IAccessTokenProvider` over the session | Access token in memory only |
| Blazor Hybrid | `AddAuthKitBlazorHybrid()` → `AuthenticationStateProvider` over the session                               | Platform secure storage     |

```razor
<CascadingAuthenticationState>
    <Router AppAssembly="typeof(App).Assembly">…</Router>
</CascadingAuthenticationState>
```

`<AuthorizeView>` then works unmodified, and in WASM `AuthorizationMessageHandler` /
`BaseAddressAuthorizationMessageHandler` attach the bearer token through the standard
`IAccessTokenProvider` — no custom `DelegatingHandler` written by the host.

### 3.6 HttpClient

Server-side calls use the standard factory with a handler that forwards the _current_ session's
token, resolved by ref, never "whichever token is available":

```csharp
builder.Services.AddHttpClient("carpeta", client =>
{
    client.BaseAddress = new Uri("https://api.example.es");
}).AddAuthKitTokenForwarding("clients/clave");
```

The ref is mandatory. A handler that picks a token without knowing which provider issued it is
exactly T-20.

## 4. Angular

Standard mechanisms, one provider function:

```ts
bootstrapApplication(AppComponent, {
  providers: [
    provideHttpClient(withInterceptors([authKitInterceptor])),
    provideAuthKit({
      baseUrl: 'https://auth.example.es',
      clientId: 'portal-caib',
      audience: 'clients',
      restoreOnStartup: true,
    }),
    provideRouter(routes),
  ],
});
```

That single call registers, using Angular's own primitives:

| Angular primitive       | What AuthKit registers                                  |
| ----------------------- | ------------------------------------------------------- |
| `EnvironmentProviders`  | `AuthKitClient`, `AuthKitSessionStore`, registry        |
| `provideAppInitializer` | session restore before the first render                 |
| `HttpInterceptorFn`     | `authKitInterceptor` — attaches `Authorization: Bearer` |
| `CanActivateFn`         | `authKitGuard` — returns `UrlTree` to the login route   |
| `Signal`                | `session`, `user`, `loa`, `isAuthenticated`             |
| `HttpContext` token     | `AUTHKIT_SKIP` to opt a request out of the header       |

Usage stays idiomatic:

```ts
export const routes: Routes = [
  {
    path: 'expedientes',
    canActivate: [authKitGuard],
    data: { authKit: { loa: 'high', audience: 'clients' } },
    loadComponent: () => import('./expedientes.page'),
  },
];
```

```ts
@Component({/* … */})
export class ShellComponent {
  private readonly auth = inject(AuthKitClient);
  readonly user = this.auth.user; // Signal<AuthKitUser | null>
  readonly loa = this.auth.loa; // Signal<'low' | 'substantial' | 'high' | null>
}
```

Rules:

1. No custom `HttpClient` wrapper. `HttpClient` stays `HttpClient`.
2. No custom guard base class. Guards are `CanActivateFn`.
3. Zoneless by default: state is signals, not `BehaviorSubject`.
4. The multi-provider registry surfaces as `authKitProviders` (a signal of descriptors) so a picker
   is a loop over data, never a hardcoded list.

## 5. Aurelia

### 5.1 v1

```ts
import { PLATFORM } from 'aurelia-pal';

export function configure(aurelia: Aurelia): void {
  aurelia.use.plugin(PLATFORM.moduleName('@authkit/aurelia'), (config) => {
    config.baseUrl = 'https://auth.example.es';
    config.audience = 'clients';
  });
}
```

Registers, through Aurelia's own DI and router:

| Aurelia v1 primitive          | What AuthKit registers                                                                                                                            |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `container.registerSingleton` | `AuthKitClient`, `AuthKitSession`                                                                                                                 |
| Router pipeline step          | `config.addPipelineStep('authorize', AuthKitAuthorizeStep)` — the documented v1 pattern, a `RouteStep`-style class the host also adds to `app.ts` |
| `aurelia-fetch-client`        | `client.configure(c => c.withInterceptor(new AuthKitHttpInterceptor()))`                                                                          |
| `BindingEngine` / observables | `AuthKitSession` exposes `sessionChanged` for bindings                                                                                            |

v1 has no signals and no composition API. The service exposes plain properties plus a subscription,
which is what a v1 application already understands.

### 5.2 v2

```ts
import Aurelia from 'aurelia';
import { AuthKitConfiguration } from '@authkit/aurelia2';

Aurelia.register(
  AuthKitConfiguration.customize({
    baseUrl: 'https://auth.example.es',
    audience: 'clients',
  }),
)
  .app(MyApp)
  .start();
```

| Aurelia v2 primitive                    | What AuthKit registers                                       |
| --------------------------------------- | ------------------------------------------------------------ |
| `IContainer` + `Registration.singleton` | `IAuthKitClient`, session store                              |
| `IHttpClient` configuration             | an interceptor via `withInterceptor`                         |
| Router hook                             | `IRouter.addHook` for `canLoad`/`canActivate` by ref and LoA |
| `@observable` / `IObserverLocator`      | session state notifications                                  |

Exact router-hook signature to be confirmed against `aurelia@2.0.0-rc.2` (see §8).

## 6. What AuthKit must never do

| Never                                                 | Why                                                             |
| ----------------------------------------------------- | --------------------------------------------------------------- |
| Own the user table                                    | Identity already does; two stores diverge on day one            |
| Hash, verify or reset a password                      | Not an AuthKit flow; doing it badly is a liability              |
| Issue its own cookie format                           | Browsers and proxies already understand `Set-Cookie`            |
| Require `[AuthKitAuthorize]` or an SDK-specific guard | Policies and `CanActivateFn` are the platform's answer          |
| Wrap `HttpClient` or `Router` in its own type         | The host's tooling, tests and lint rules assume the real one    |
| Navigate on its own                                   | The host owns navigation; AuthKit returns a `UrlTree` or a URL  |
| Cache tokens outside the platform store               | WASM memory, Identity cookie, or the OS keychain — nothing else |

## 7. Token requirement when the host uses bearer

There is no AuthKit issuer. If the host chooses bearer tokens, it mints them, and it must mint them
in a shape that the platform's own validator accepts:

- `RS256` with a `kid`, keys published by the host (a JWKS endpoint or a configured key);
- `iss` = the host's issuer; `aud` = the resource server identifier;
- `sub`, `acr`, `amr`, `act` and the `authkit:*` claims of §2;
- short TTL.

An opaque token minted by the host would force introspection on every request — possible, but the
host should know it is choosing that.

When the host already runs an OIDC provider (Entra, Keycloak, an internal one), nothing here
applies: Cl@ve produces a `ClaimsPrincipal`, the host links it as an external login, and the
existing provider issues the token exactly as it does for every other login.

## 8. Quality gates

1. **No glue test.** A sample host authenticates, calls `[Authorize(Policy = "authkit:loa:high")]`,
   and reads `User.FindFirst("authkit:provider")` without referencing any AuthKit type in the
   controller.
2. **Identity bridge tests.** New user, existing user, linked by document, linked by email, rejected
   when `UnlinkedPolicy.Reject`, and an Identity lockout that must not be bypassed by a valid token.
3. **Claim mapping parity.** The .NET factory and the TypeScript mapper produce identical §2 claims
   for the same fixture; a missing claim fails the build.
4. **Angular gate.** The interceptor is registered through `provideHttpClient`; the guard returns a
   `UrlTree`; `AuthKitClient` is tree-shakeable and the package does not import `@angular/router`
   when the guard entry point is unused (secondary entry point if needed).
5. **Aurelia parity.** v1 and v2 expose the same service surface, tested with the real container
   (`aurelia-dependency-injection` and `DI.createContainer`), not with mocks.
6. **Fail-closed tests.** Missing `acr` fails a LoA policy; missing `sub` fails the principal build;
   a token whose `authkit:provider` is unknown fails authorization.

## 9. Open questions

1. **Bearer versus cookie.** A thin `AddAuthKitBearer` wrapper over `JwtBearer` is convenient, but
   the host already has `AddJwtBearer` and a signing key of its own. Proposal: ship no wrapper;
   document the cookie path as the default and the host-minted JWT path as the advanced one.
2. **Identity vs. no-Identity hosts.** Not every host uses ASP.NET Core Identity. The bridge must be
   an optional package (`AuthKit.Identity`) so `AuthKit.Core` never references
   `Microsoft.AspNetCore.Identity`.
3. **Angular secondary entry point.** If the guard pulls `@angular/router` into apps that only use
   the interceptor, expose `@authkit/angular/clave` as a secondary entry point.
4. **Aurelia v2 router API.** Confirm `IRouter.addHook` against the installed RC before writing the
   v2 adapter; if it does not exist, fall back to a route-level `canLoad` helper the host adds
   explicitly.
5. **Cookie versus bearer in Blazor Server.** Both are valid; the spec assumes cookie for
   interactive apps and bearer for APIs. Confirm no host needs both in the same pipeline.
