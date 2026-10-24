# AuthKit para .NET

**Inglés** · [Português](README.pt-BR.md) · Español

Pantallas de autenticación listas en una línea, nativas para .NET.

Este repositorio contiene solo el SDK .NET. Las otras plataformas viven en repositorios propios, con
el mismo nombre base y ciclos de release independientes:

| Plataforma | Repositorio                                                                   |
| ---------- | ----------------------------------------------------------------------------- |
| Angular    | [AuthKit.Angular](https://github.com/carloshenriquecarniatto/AuthKit.Angular) |
| Contrato   | `contracts/authkit.contract.json`, mantenido aqui                             |

```text
AuthKit.Core               contratos, modelos, máquina de estados, validación, tema   (sin dependencias de terceros)
AuthKit.Supabase           provider de Supabase Auth
AuthKit.Blazor             componentes Blazor nativos (WebAssembly, Server, Auto)
contracts/                 authkit.contract.json, el contrato normativo que copian los SDKs TypeScript
```

## Personalización de la interfaz

Hay tres niveles, y la [guía de personalización](docs/customization.es.md) explica cada uno con
código: tokens de tema, clases CSS `ak-*` y un provider sin interfaz.

## Por qué

- **Renderizado nativo.** Componentes Razor para Blazor. Sin React dentro de una webview, sin
  interop JS más allá de `sessionStorage`.
- **Agnóstico de provider.** `IAuthProvider` es la única dependencia de la interfaz.
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

## El contrato

`contracts/authkit.contract.json` es el contrato normativo. Los tipos .NET lo reflejan y
`ContractParityTests` rompe el build si hay desviación. El repositorio Angular mantiene
una copia versionada y su CI falla si difiere de este fichero — sin paquete que publicar y sin orden
de release. Ver [`contracts/README.md`](contracts/README.md).

## Prueba de intrusión

La suite offline corre en cada pull request; la live necesita Docker.

```bash
dotnet test security/AuthKit.Pentest/AuthKit.Pentest.csproj
bash scripts/pentest-up.sh && bash scripts/pentest-live.sh
```

## Estado

| Área                       | Estado                                                                     |
| -------------------------- | -------------------------------------------------------------------------- |
| Contrato (`2.0.0`)         | Estable (ruptura: federación e identidad legal eliminadas)                |
| `AuthKit.Core`             | Email, magic link, OTP (email + teléfono), recuperación, OAuth             |
| `AuthKit.Supabase`         | Provider completo, mapeo de errores, restauración de sesión                |
| `AuthKit.Blazor`           | Email, magic link, reset, socials, callback   |
| MAUI                       | Planeado: mismo core, shell XAML, `WebAuthenticator`                       |

## Estructura del repositorio

```text
contracts/            authkit.contract.json, el contrato normativo que copian los SDKs TypeScript
src/dotnet/           AuthKit.Core, AuthKit.Supabase, AuthKit.Blazor
tests/dotnet/         xUnit, bUnit y fixtures de protocolo
security/             suites de pentest offline y live
samples/messages/     fixtures de protocolo anonimizadas y versionadas
docs/                 spec, modelo de amenazas, arquitectura, paridad de UI
```

## Contribuir

Consulta [CONTRIBUTING.md](CONTRIBUTING.md). Los hooks ejecutan `dotnet test` antes del push; un
cambio de contrato debe entrar aquí primero y después en los SDKs consumidores con subida de
versión.

## Seguridad

Lee [SECURITY.md](SECURITY.md) antes de reportar una vulnerabilidad.

## Licencia

MIT — consulta [LICENSE](LICENSE).
