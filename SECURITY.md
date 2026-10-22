# Security policy

## Supported versions

The `0.x` line is supported on a best-effort basis. Security fixes are released as patch versions
from `main`.

## Reporting a vulnerability

Do **not** open a public issue for a suspected vulnerability.

Use GitHub's private reporting: **Security → Advisories → Report a vulnerability** on
`https://github.com/carloshenriquecarniatto/AuthKit`.

Include:

- affected package and version;
- hosting model (Blazor Server, WebAssembly, Auto, Angular);
- a minimal reproduction, ideally a failing test;
- the impact you believe it has (token exposure, session fixation, XSS, etc.).

You can expect an acknowledgement within 72 hours and a remediation plan within 7 days. Credit is
given in the advisory unless you prefer to stay anonymous.

## What AuthKit guarantees

- No SDK logs access or refresh tokens.
- Server-side Blazor never sends a refresh token to the browser.
- Only `sessionStorage` is used in browsers, never `localStorage`.
- A service-role key in the client configuration fails startup.
- Provider error text is mapped before reaching the UI.
- Theme and logo values are sanitised against style injection.

## What AuthKit cannot guarantee

- **Your row level security policies.** The anon key is public by design; RLS is the security
  boundary. An AuthKit screen does not make an unprotected table safe.
- **XSS in the host application.** Script running on your origin can read `sessionStorage`. Use a
  strict Content-Security-Policy and keep access tokens short-lived.
- **OAuth client secrets.** They belong in the provider console and on the server, never in a client
  application.

## Hardening checklist for hosts

1. `Content-Security-Policy` without `unsafe-inline` for scripts; allow only the Supabase origin in
   `connect-src`.
2. Keep the access-token lifetime short and enable refresh-token rotation in Supabase.
3. Add every redirect URL to the Supabase allowlist; wildcard hostnames are forbidden.
4. Run `AuthKitState.InitializeAsync()` once at startup so expired sessions are discarded early.
5. On Server hosting, use `AuthSessionStorageMode.Memory`.
