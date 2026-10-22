# Cl@ve integration guide

**English** · Português · Español _(translations pending)_

This guide shows the shortest working path from a blank host to a real Cl@ve login, for every
framework AuthKit supports. The normative details live in [`spec-clave.md`](spec-clave.md),
[`spec-providers.md`](spec-providers.md) and [`spec-host-integration.md`](spec-host-integration.md).

> AuthKit is a library, not a provider. It runs inside the server you already have, produces a
> validated identity, and stops. It never signs anyone in, never writes a cookie and never mints a
> token.

---

## 1. What you get

| Package                    | Runs in      | What it does                                                   |
| -------------------------- | ------------ | -------------------------------------------------------------- |
| `AuthKit.Clave`            | Host server  | Signed `AuthnRequest`, metadata, response validation, identity |
| `AuthKit.Clave.AspNetCore` | ASP.NET Core | `MapClave()`: `/metadata`, `/login`, `/acs`; distributed state |
| `AuthKit.Blazor`           | Blazor       | `<AuthKitClave>`, `<AuthKitClaveCallback>`                     |
| `@authkit/angular`         | Angular      | `<ak-clave>`, `<ak-clave-callback>`, `provideAuthKitClave()`   |
| `@authkit/aurelia`         | Aurelia v1   | plugin, HTTP interceptor, authorize step                       |
| `@authkit/aurelia2`        | Aurelia v2   | configuration, HTTP interceptor, router hook                   |

---

## 2. Two environments, two federations

Preproduction and production are separate federations with separate CAID registrations and separate
trust anchors. A certificate valid in one is meaningless in the other, so the pair is configuration,
never a boolean.

```csharp
clave.Environment = ClaveEnvironment.Preproduction; // se-pasarela
clave.Environment = ClaveEnvironment.Production;    // pasarela
```

Never deploy the preproduction endpoint to production "temporarily": the error the gateway returns
is generic, and the cost of finding out is an afternoon.

---

## 3. ASP.NET Core

```csharp
builder.Services.AddClave(
    clave =>
    {
        clave.Environment = ClaveEnvironment.Preproduction;
        clave.EntityId = "https://sede.example.es/clave/metadata";
        clave.AcsUrl = "https://sede.example.es/clave/acs";
        clave.SigningCertificate = certificates.Current("caib");        // must have a private key
        clave.PasarelaSigningCertificate = trust.Anchors.Clave;         // pinned, never fetched
        clave.ProviderName = "Sede Electrónica";
        clave.Tenant = "caib";
        clave.SubjectKey = secrets.SubjectKey;                          // >= 32 bytes
        clave.DefaultLevel = AuthAssuranceLevel.Substantial;
    },
    http =>
    {
        http.RoutePrefix = "/clave";
        http.FailurePath = "/login-failed";
        http.ReturnUrlPolicy = ClaveReturnUrlPolicy.SameOrigin;
    });
```

```csharp
app.MapClave(async (context, identity) =>
{
    // The host decides what a session is. This is the only sign-in call in the flow.
    await context.SignInAsync(identity.ToClaimsPrincipal("clientes/clave"));
    return Results.Redirect(identity.ReturnUrl ?? "/");
});
```

| Endpoint          | Method | Purpose                                           |
| ----------------- | ------ | ------------------------------------------------- |
| `/clave/metadata` | GET    | SP metadata for CAID                              |
| `/clave/login`    | GET    | `loa`, `method`, `returnUrl` → 302 to the gateway |
| `/clave/acs`      | POST   | Validates the assertion, then calls your callback |

**Startup validation is exhaustive on purpose.** Missing entity id, an ACS on another origin, a
certificate without a private key, a certificate expiring within 30 days, an encryption-only key
usage, a missing `PersonIdentifier`, an impossible level, an invalid tenant: each one fails
`AddClave` with a message that names the field and the fix.

### State, and why the default is not enough

| Resolution order            | When it applies                    | Consequence                                              |
| --------------------------- | ---------------------------------- | -------------------------------------------------------- |
| Your `IClaveRequestStore`   | You registered one                 | Whatever your backend guarantees                         |
| `IDistributedCache` adapter | You registered a distributed cache | Convenient, but not atomic: replay cache is the backstop |
| In-memory (default)         | Neither                            | One instance only; a warning is logged in Production     |

There is no stateless mode. Without the pending request store, `InResponseTo` cannot be bound and a
captured assertion is a valid login. Behind a load balancer, register a store with `SET NX`
semantics (Redis, a SQL unique constraint, a DynamoDB conditional write).

A cache outage fails closed: logins fail while the cache is down. For a sede electrónica that is the
correct trade — plan capacity accordingly.

### Errors

Internal reasons (`SignatureInvalid`, `ReplayDetected`, `AudienceMismatch`, …) are logged, never
shown. Every failure redirects to `FailurePath` with a coarse `error` code, so a browser cannot tell
which check rejected the assertion. `loa_not_met`, `identity_not_verified`, `tenant_mismatch` and
`certificate_invalid` are the only specific codes the user sees.

---

## 4. Blazor

```csharp
// Host code. The descriptor is data, not a component switch.
var clave = new AuthFederationProvider
{
    Id = "clave",
    Kind = "clave",
    DisplayName = "Cl@ve",
    Description = "Accede con Cl@ve",
    Methods =
    [
        new AuthFederationMethod { Id = ClaveMethods.Movil,       DisplayName = "Cl@ve Móvil",   Levels = [AuthAssuranceLevel.Substantial], Recommended = true },
        new AuthFederationMethod { Id = ClaveMethods.Permanente,  DisplayName = "Cl@ve Permanente", Levels = [AuthAssuranceLevel.Substantial] },
        new AuthFederationMethod { Id = ClaveMethods.Certificate, DisplayName = "Certificado",    Levels = [AuthAssuranceLevel.Substantial, AuthAssuranceLevel.High] }
    ],
    Levels = [AuthAssuranceLevel.Substantial, AuthAssuranceLevel.High],
    IsPrimary = true
};
```

```razor
<AuthKitClave Descriptor="clave"
              ReturnUrl="/expedientes"
              OnNavigate="url => Navigation.NavigateTo(url, forceLoad: true)" />
```

```razor
<AuthKitClaveCallback OnCompleted="HandleOutcome" />

@code {
    private void HandleOutcome(AuthKitFederationOutcome outcome)
    {
        if (!outcome.Ok)
        {
            Error = outcome.Error;
        }
    }
}
```

- One control per declared `AuthFederationMethod`, plus a level selector when more than one level
  applies.
- `OnNavigate` is optional. Without it the component renders links, which the browser follows and
  the host still owns.
- `<AuthKitClaveCallback>` only reads `error`/`error_reason` from the redirect. It performs no
  network call and never navigates unless `ReplaceHistoryEntry` is set.

Server-side Blazor keeps the session in the circuit; nothing Cl@ve-related reaches the browser.

---

## 5. Angular

```ts
bootstrapApplication(AppComponent, {
  providers: [
    provideHttpClient(withInterceptors([authKitInterceptor])),
    provideAuthKitClave({
      loginPath: '/clave/login',
      audience: 'clientes',
      providerRef: 'clientes/clave',
      providers: [claveDescriptor],
    }),
    provideRouter(routes),
  ],
});
```

```html
<ak-clave (navigate)="go($event)" returnUrl="/expedientes" />
<ak-clave-callback (completed)="onCompleted($event)" />
```

```ts
export const routes: Routes = [
  {
    path: 'expedientes',
    canActivate: [authKitGuard],
    data: { authKit: { loa: 'high', audience: 'clientes' } },
    loadComponent: () => import('./expedientes.page'),
  },
];
```

Rules the adapter follows:

- `HttpClient` stays `HttpClient`, the guard is a `CanActivateFn`, state is signals.
- The interceptor attaches `Authorization: Bearer` only when a token exists; `AUTHKIT_SKIP` opts a
  request out.
- The guard returns a `UrlTree`, never navigates.
- The browser never parses SAML and never holds a Cl@ve token.

---

## 6. Aurelia

Both packages expose the same surface: `AuthKitConfig`, `AuthKitSession`/`AuthKitSessionStore`,
`AuthKitClient`, the HTTP interceptor and a router guard. The plugin never navigates and never holds
a Cl@ve token.

### v1

```ts
import { PLATFORM } from 'aurelia-pal';
import { registerAuthKitHttpInterceptor } from '@authkit/aurelia';

export function configure(aurelia: Aurelia): void {
  aurelia.use.plugin(PLATFORM.moduleName('@authkit/aurelia'), (config) => {
    config.baseUrl = 'https://sede.example.es'; // origin only
    config.audience = 'clientes';
    config.loginPath = '/clave/login';
    config.loginRoute = 'login';
    config.providers = claveDescriptors; // data, never a hardcoded list
  });

  registerAuthKitHttpInterceptor(aurelia.container.get(HttpClient), aurelia.container);
}
```

```ts
// app.ts — the documented v1 pipeline-step pattern.
configureRouter(config: RouterConfiguration): void {
  config.addPipelineStep('authorize', AuthKitAuthorizeStep);
}
```

`AuthKitSession` exposes plain properties (`session`, `loa`, `isAuthenticated`) plus a
`sessionChanged` notification, because v1 has no signals.

### v2

```ts
import Aurelia from 'aurelia';
import { AuthKitConfiguration, IAuthKitRouteGuard } from '@authkit/aurelia2';
import { resolve } from 'aurelia';

Aurelia.register(
  AuthKitConfiguration.customize({
    baseUrl: 'https://sede.example.es',
    audience: 'clientes',
    loginPath: '/clave/login',
    providers: claveDescriptors,
  }),
)
  .app(MyApp)
  .start();
```

`@aurelia/router@2.0.0-rc.2` has no `IRouter.addHook`, so the package ships the route-level
`canLoad` fallback the specification allows for that case:

```ts
canLoad = resolve(IAuthKitRouteGuard).canLoad;
```

The interceptor attaches the bearer only to `baseUrl`'s origin, never overwrites an existing
`Authorization` header, and is opted out per request with `X-AuthKit-Skip: 1`.

## 7. CAID checklist

Before you ask CAID to register the entity, confirm:

1. `EntityId` matches the metadata URL **literally** — case and trailing slash included.
2. `AcsUrl` is the public HTTPS URL that reaches the host; check it from outside the network.
3. The signing certificate has a private key, allows digital signatures and expires in more than 30
   days.
4. The pinned pasarela certificate matches the environment you registered.
5. `PersonIdentifier` is requested; `DateOfBirth` is **not** required.
6. The metadata you send contains the same ACS URL and certificate the configuration uses.

`GET {prefix}/metadata` is generated from the configuration, so steps 1, 2, 3 and 6 cannot drift.
There is no `doctor` command yet; the startup validator covers the same checks without the DNS/TLS
probe.

---

## 8. Claims

The identity is projected once, by `AuthKitClaimsFactory`, so every platform sees the same shapes:

| Claim              | Source                                   |
| ------------------ | ---------------------------------------- |
| `sub`              | opaque HMAC subject                      |
| `name`             | given + family                           |
| `acr`              | eIDAS LoA URI                            |
| `amr`              | method (`clave_movil`, `certificate`, …) |
| `act`              | represented document                     |
| `authkit:provider` | `clientes/clave`                         |
| `authkit:audience` | `clientes`                               |
| `authkit:tenant`   | `caib`                                   |
| `authkit:doc_type` | `dni`, `nie`, `cif`                      |
| `authkit:doc_id`   | opt-in only — PII                        |

The document number is never a primary key, never a log field and never a claim by default. The
subject is `HMAC(tenant + document)`, so the same citizen in two entities yields two unrelated
subjects.

---

## 9. What to test before going live

- A real login against preproduction, then logout, then login again (replay and state).
- A login started in one tab and completed in another (state store).
- A cancelled login: the user must land on `FailurePath` with a generic code.
- An expired certificate: startup must refuse to boot.
- Two instances behind the load balancer with the in-memory store: logins must fail intermittently —
  this is how you prove you need a distributed store.
- `[Authorize(Policy = "authkit:loa:high")]` (or `authKitGuard` with `loa: 'high'`) on a route that
  a substantial-only login cannot reach.
