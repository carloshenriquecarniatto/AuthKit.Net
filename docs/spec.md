# AuthKit v0.1 — Specification

**English** · [Português](spec.pt-BR.md) · [Español](spec.es.md)

Status: normative for the `0.x` line. Changes to the contract require a major contract version bump
and a parity test update on both SDKs.

## 1. Goal

Deliver the "fast login screen" experience of `supabase_auth_ui` to .NET and TypeScript, without
sacrificing native rendering, extensibility or security.

Non-goals for v0.1: MAUI, React interop, admin/user-management UI, passkeys, enterprise SSO UI.

## 2. Packages

| Package            | Target     | Responsibility                                                           |
| ------------------ | ---------- | ------------------------------------------------------------------------ |
| `AuthKit.Core`     | net10.0    | Contract types, `IAuthProvider`, `AuthFlow`, validation, theme, messages |
| `AuthKit.Supabase` | net10.0    | GoTrue integration, error mapping, session restore, DI                   |
| `AuthKit.Blazor`   | net10.0    | Razor components, `AuthKitState`, session stores                         |
| `@authkit/angular` | Angular 22 | Standalone components, signals, Supabase provider                        |

MAUI is deferred. The core contract and provider are platform-neutral so the MAUI shell only adds
XAML rendering plus `WebAuthenticator` and the manifest targets described in ADR-001.

## 3. Contract

`contracts/authkit.contract.json` is the single source of truth for:

- types: `AuthUser`, `AuthSession`, `AuthError`, `AuthResult`, `AuthCapabilities`, `AuthTheme`;
- enums: `AuthErrorCode`, `AuthState`, `AuthNextStep`, `AuthMessageKey`;
- operations: the provider surface.

Rules:

1. Every field is present in both SDKs, with the same name and the same base type.
2. Errors are values (`AuthResult`), never exceptions, for expected failures.
3. The UI may only call operations declared in the contract.
4. Parity tests: `ContractParityTests` (.NET, reflection) and `contract.spec.ts` (TypeScript,
   runtime descriptor). Both must pass for a release.

## 4. Component parity with `supabase_auth_ui`

| Flutter               | AuthKit (.NET / Angular)                     | Notes                                                               |
| --------------------- | -------------------------------------------- | ------------------------------------------------------------------- |
| `SupaEmailAuth`       | `AuthKitEmail` / `ak-email`                  | Sign-in/sign-up toggle, confirm password, metadata fields, recovery |
| `SupaMagicAuth`       | `AuthKitMagicLink` / `ak-magic-link`         | "Check your email" state                                            |
| `SupaResetPassword`   | `AuthKitResetPassword` / `ak-reset-password` | Request + update modes                                              |
| `SupaSocialsAuth`     | `AuthKitSocials` / `ak-socials`              | Provider list, colour toggle, icon/icon+text                        |
| `SupaPhoneAuth`       | Provider method only in v0.1                 | Component in v0.2                                                   |
| `SupaVerifyPhone`     | Provider method only in v0.1                 | Component in v0.2                                                   |
| `MetaDataField`       | `AuthMetadataField`                          | Text and boolean                                                    |
| Theme via `ThemeData` | `AuthTheme` → CSS variables                  | Same token names in both SDKs                                       |
| Localisation delegate | `IAuthMessages` / `AuthMessages`             | English bundled                                                     |

Divergence: minimum password length defaults to 8 (Supabase's floor is 6). Security wins over exact
parity; the value is configurable.

## 5. Security requirements (release blocking)

Every item below is a release gate. A failing item blocks the version.

1. **No credential leakage in logs.** Session `ToString`/serialisation must redact tokens.
2. **No server-side token exposure.** Server Blazor never writes refresh tokens to the browser.
3. **Browser storage is `sessionStorage`.** `localStorage` is forbidden for tokens.
4. **No service-role keys in clients.** Startup fails with an actionable message.
5. **Provider text is not forwarded.** Errors are mapped to AuthKit-owned messages.
6. **Style injection is neutralised.** Theme values cannot escape a CSS declaration.
7. **ReDoS-resistant validation.** Email regex is bounded and non-backtracking.
8. **No double submission.** The flow rejects concurrent operations.
9. **OAuth uses PKCE.** Implicit flow is not used for new integrations.
10. **Callback hygiene.** Codes/tokens are removed from the URL after success.

## 6. Penetration test mandate

Before any `1.0.0` and at least once per year:

- Attempt token extraction from browser storage, DOM, URL, history, logs and error messages.
- Attempt session fixation and cross-user leakage in Blazor Server (two concurrent circuits).
- Attempt privilege escalation through the anon key (must be blocked by RLS; AuthKit must not make
  it easier).
- Attempt style/DOM injection through theme tokens, messages and metadata.
- Attempt replay of OAuth callbacks and PKCE verifiers.

Findings are tracked as security issues, and the release stays blocked until each is fixed or
formally accepted in `docs/threat-model.md`.

The automated half of this mandate lives in `security/AuthKit.Pentest` (offline, blocking on every
pull request) and `security/AuthKit.Pentest.Live` (containerised GoTrue, weekly). A release requires
both the offline suite and the scanners in `.github/workflows/security.yml` to pass.

## 7. Quality gates

- Unit tests for every public behaviour in Core.
- Integration tests against the real Supabase SDK with a stub transport (no network).
- Component tests for rendering, validation, callbacks and theming.
- `dotnet format --verify-no-changes`, Prettier check, TypeScript strict build.
- Semgrep, Trivy, npm audit (production), NuGet audit (transitive, high and above).

## 8. Versioning

Semantic Release derives the version from Conventional Commits. One lockstep version for all
packages in the `0.x` line. Published artifacts: NuGet (`AuthKit.*`), npm (`@authkit/angular`).
