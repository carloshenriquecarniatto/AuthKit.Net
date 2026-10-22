# AuthKit v0.1 — Especificación

[English](spec.md) · [Português](spec.pt-BR.md) · **Español**

Estado: normativa para la línea `0.x`. Los cambios en el contrato requieren un bump de versión major
del contrato y actualización de los tests de paridad en ambos SDK.

## 1. Objetivo

Entregar la experiencia de "pantalla de login rápida" de `supabase_auth_ui` a .NET y TypeScript, sin
sacrificar renderizado nativo, extensibilidad ni seguridad.

Fuera del alcance de la v0.1: MAUI, interop de React, UI de administración/gestión de usuarios,
passkeys y UI de SSO empresarial.

## 2. Paquetes

| Paquete            | Objetivo   | Responsabilidad                                                           |
| ------------------ | ---------- | ------------------------------------------------------------------------- |
| `AuthKit.Core`     | net10.0    | Tipos del contrato, `IAuthProvider`, `AuthFlow`, validación, tema, textos |
| `AuthKit.Supabase` | net10.0    | Integración con GoTrue, mapeo de errores, restauración de sesión, DI      |
| `AuthKit.Blazor`   | net10.0    | Componentes Razor, `AuthKitState`, stores de sesión                       |
| `@authkit/angular` | Angular 22 | Componentes standalone, signals, provider Supabase                        |

MAUI queda aplazado. El contrato y el provider son neutros de plataforma, así que la capa MAUI solo
añade renderizado XAML, `WebAuthenticator` y los targets de manifest descritos en el ADR-001.

## 3. Contrato

`contracts/authkit.contract.json` es la fuente única de verdad para:

- tipos: `AuthUser`, `AuthSession`, `AuthError`, `AuthResult`, `AuthCapabilities`, `AuthTheme`;
- enums: `AuthErrorCode`, `AuthState`, `AuthNextStep`, `AuthMessageKey`;
- operaciones: la superficie del provider.

Reglas:

1. Todo campo existe en ambos SDK, con el mismo nombre y el mismo tipo base.
2. Los errores esperados son valores (`AuthResult`), nunca excepciones.
3. La UI solo puede llamar operaciones declaradas en el contrato.
4. Tests de paridad: `ContractParityTests` (.NET, reflexión) y `contract.spec.ts` (TypeScript,
   descriptor en runtime). Ambos deben pasar para un release.

## 4. Paridad de componentes con `supabase_auth_ui`

| Flutter              | AuthKit (.NET / Angular)                     | Notas                                                 |
| -------------------- | -------------------------------------------- | ----------------------------------------------------- |
| `SupaEmailAuth`      | `AuthKitEmail` / `ak-email`                  | Toggle sign-in/sign-up, confirmación, metadata, reset |
| `SupaMagicAuth`      | `AuthKitMagicLink` / `ak-magic-link`         | Estado "check your email"                             |
| `SupaResetPassword`  | `AuthKitResetPassword` / `ak-reset-password` | Modos request + update                                |
| `SupaSocialsAuth`    | `AuthKitSocials` / `ak-socials`              | Lista de providers, color, icono/icono+texto          |
| `SupaPhoneAuth`      | Solo método del provider en v0.1             | Componente en v0.2                                    |
| `SupaVerifyPhone`    | Solo método del provider en v0.1             | Componente en v0.2                                    |
| `MetaDataField`      | `AuthMetadataField`                          | Texto y booleano                                      |
| Tema vía `ThemeData` | `AuthTheme` → variables CSS                  | Mismos nombres de token en ambos SDK                  |
| Delegate de i18n     | `IAuthMessages` / `AuthMessages`             | Inglés incluido                                       |

Divergencia: la longitud mínima de contraseña es 8 (el piso de Supabase es 6). La seguridad gana a
la paridad exacta; el valor es configurable.

## 5. Requisitos de seguridad (bloquean el release)

Cada punto es un gate. Un punto que falla bloquea la versión.

1. **Sin fuga de credenciales en logs.** `ToString`/serialización de la sesión deben redactar
   tokens.
2. **Sin exposición del token en el servidor.** Blazor Server nunca escribe refresh token en el
   navegador.
3. **El storage del navegador es `sessionStorage`.** `localStorage` está prohibido para tokens.
4. **Sin clave service-role en el cliente.** El arranque falla con un mensaje accionable.
5. **El texto del provider no se reenvía.** Los errores se mapean a mensajes de AuthKit.
6. **La inyección de estilos se neutraliza.** Los valores del tema no escapan de una declaración
   CSS.
7. **Validación resistente a ReDoS.** El regex de email es acotado y sin backtracking.
8. **Sin doble envío.** El flujo rechaza operaciones concurrentes.
9. **OAuth usa PKCE.** El flujo implícito no se usa en integraciones nuevas.
10. **Higiene de callback.** Códigos/tokens se eliminan de la URL tras el éxito.

## 6. Mandato de pentest

Antes de cualquier `1.0.0` y al menos una vez al año:

- Intentar extraer tokens del storage del navegador, DOM, URL, historial, logs y mensajes de error.
- Intentar fijación de sesión y fuga entre usuarios en Blazor Server (dos circuits simultáneos).
- Intentar escalar privilegios con la anon key (debe bloquearlo RLS; AuthKit no puede facilitarlo).
- Intentar inyección de estilo/DOM vía tokens de tema, mensajes y metadata.
- Intentar replay de callback OAuth y de verificador PKCE.

Los hallazgos se registran como issues de seguridad y el release sigue bloqueado hasta que cada uno
se corrija o se acepte formalmente en `docs/threat-model.md`.

La mitad automatizada de ese mandato vive en `security/AuthKit.Pentest` (offline, bloqueante en cada
pull request) y `security/AuthKit.Pentest.Live` (GoTrue en contenedor, semanal). Un release exige la
suite offline y los scanners de `.github/workflows/security.yml` en verde.

## 7. Gates de calidad

- Tests unitarios para todo comportamiento público del Core.
- Tests de integración contra el SDK real de Supabase con transporte stub (sin red).
- Tests de componente para renderizado, validación, callbacks y tema.
- `dotnet format --verify-no-changes`, Prettier y build TypeScript strict.
- Semgrep, Trivy, npm audit (producción) y NuGet audit (transitivo, high y superior).

## 8. Versionado

Semantic Release deriva la versión de los Conventional Commits. Una versión lockstep para todos los
paquetes en la línea `0.x`. Artefactos publicados: NuGet (`AuthKit.*`) y npm (`@authkit/angular`).
