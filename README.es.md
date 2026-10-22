# AuthKit para .NET

**Inglés** · [Português](README.pt-BR.md) · Español

Pantallas de autenticación listas en una línea, nativas para .NET.

Este repositorio contiene solo el SDK .NET. Las otras plataformas viven en repositorios propios, con
el mismo nombre base y ciclos de release independientes:

| Plataforma | Repositorio                                                                              |
| ---------- | ---------------------------------------------------------------------------------------- |
| Angular    | [AuthKit.Angular](https://github.com/carloshenriquecarniatto/AuthKit.Angular)            |
| Aurelia    | [AuthKit.Aurelia](https://github.com/carloshenriquecarniatto/AuthKit.Aurelia)            |
| Contrato   | [`@authkit/contracts`](https://www.npmjs.com/package/@authkit/contracts), publicado aquí |

```text
AuthKit.Core               contratos, modelos, máquina de estados, validación, tema   (sin dependencias de terceros)
AuthKit.Supabase           provider de Supabase Auth
AuthKit.Blazor             componentes Blazor nativos (WebAssembly, Server, Auto)
AuthKit.Clave              federación Cl@ve / eIDAS: petición, validación, identidad
AuthKit.Clave.AspNetCore   MapClave(): endpoints de metadata, login y ACS
contracts/                 @authkit/contracts, el contrato normativo que consumen los SDKs TypeScript
```

## Personalización de la interfaz

Hay tres niveles, y la [guía de personalización](docs/customization.es.md) explica cada uno con
código: tokens de tema, clases CSS `ak-*` y un provider sin interfaz.

## Por qué

- **Renderizado nativo.** Componentes Razor para Blazor. Sin React dentro de una webview, sin
  interop JS más allá de `sessionStorage`.
- **Agnóstico de provider.** `IAuthProvider` es la única dependencia de la interfaz.
- **La federación es una biblioteca, no un servicio.** `AuthKit.Clave` corre dentro del host, valida
  la aserción y se detiene en "aquí hay una identidad validada". Nunca inicia sesión por ti.
- **La seguridad forma parte del contrato.** Los tokens del servidor nunca llegan al navegador, las
  claves service-role se rechazan al arrancar, el texto de error del provider nunca llega a la
  interfaz, y una suite de pentest bloquea cada release.
- **Un contrato.** `contracts/authkit.contract.json` es normativo para todos los SDKs; una prueba de
  paridad en cada repositorio rompe el build si hay desviación.

## Inicio rápido — Blazor con Supabase

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

## Inicio rápido — Cl@ve

```csharp
builder.Services.AddClave(
    clave =>
    {
        clave.Environment = ClaveEnvironment.Preproduction;
        clave.EntityId = "https://sede.example.es/clave/metadata";
        clave.AcsUrl = "https://sede.example.es/clave/acs";
        clave.SigningCertificate = certificates.Current("caib");
        clave.PasarelaSigningCertificate = trust.Anchors.Clave;
        clave.Tenant = "caib";
        clave.SubjectKey = secrets.SubjectKey;
    },
    http => http.RoutePrefix = "/clave");

app.MapClave(async (context, identity) =>
{
    await context.SignInAsync(identity.ToClaimsPrincipal("clientes/clave"));
    return Results.Redirect(identity.ReturnUrl ?? "/");
});
```

La guía completa — checklist de CAID, dos entornos, estado distribuido y la tabla de claims — está
en [`docs/clave.md`](docs/clave.md).

## Paquete de contrato

`@authkit/contracts` se publica desde este repositorio durante el release. Los repositorios Angular
y Aurelia lo consumen por versión, así que el contrato tiene un dueño y tres consumidores:

```bash
cd contracts && npm test     # paridad de campos, enums y operaciones con authkit.contract.json
```

## Prueba de intrusión

La suite offline corre en cada pull request; la live necesita Docker.

```bash
dotnet test security/AuthKit.Pentest/AuthKit.Pentest.csproj
bash scripts/pentest-up.sh && bash scripts/pentest-live.sh
```

## Estado

| Área                       | Estado                                                                     |
| -------------------------- | -------------------------------------------------------------------------- |
| Contrato (`1.1.0`)         | Estable (aditivo sobre 1.0.0)                                              |
| `AuthKit.Core`             | Email, magic link, OTP (email + teléfono), recuperación, OAuth             |
| `AuthKit.Supabase`         | Provider completo, mapeo de errores, restauración de sesión                |
| `AuthKit.Blazor`           | Email, magic link, reset, socials, callback, métodos y callback de Cl@ve   |
| `AuthKit.Clave`            | Petición/respuesta eIDAS, certificado fijado, replay, LoA, identidad       |
| `AuthKit.Clave.AspNetCore` | `MapClave()` metadata/login/ACS, estado distribuido, política de returnUrl |
| MAUI                       | Planeado: mismo core, shell XAML, `WebAuthenticator`                       |

## Estructura del repositorio

```text
contracts/            @authkit/contracts: authkit.contract.json + declaraciones TypeScript
src/dotnet/           AuthKit.Core, AuthKit.Supabase, AuthKit.Blazor, AuthKit.Clave*
tests/dotnet/         xUnit, bUnit y fixtures de protocolo
security/             suites de pentest offline y live
samples/messages/     fixtures de protocolo anonimizadas y versionadas
docs/                 spec, modelo de amenazas, arquitectura, guía Cl@ve, paridad de UI
```

## Contribuir

Consulta [CONTRIBUTING.md](CONTRIBUTING.md). Los hooks ejecutan `dotnet test` antes del push; un
cambio de contrato debe entrar aquí primero y después en los SDKs consumidores con subida de
versión.

## Seguridad

Lee [SECURITY.md](SECURITY.md) antes de reportar una vulnerabilidad.

## Licencia

MIT — consulta [LICENSE](LICENSE).
