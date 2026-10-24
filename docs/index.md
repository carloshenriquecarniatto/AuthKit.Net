---
description: 'Native authentication UI for .NET, with Supabase as the bundled provider.'
layout: page
title: AuthKit
permalink: /
---

Authentication screens that are ready in one line, native to your stack.

```text
AuthKit.Core               contract, state machine, theming   (no third-party dependencies)
AuthKit.Supabase           Supabase Auth provider
AuthKit.Blazor             Blazor components (WASM, Server, Auto)
```

![Sign in form]({{ site.baseurl }}/assets/login.png)

## Start here

- [Customizing the UI (English)]({{ site.baseurl }}/customization/)
- [Personalizando a interface (Português)]({{ site.baseurl }}/personalizacao/)
- [Personalizar la interfaz (Español)]({{ site.baseurl }}/personalizacion/)

## Try it locally

```bash
git clone https://github.com/carloshenriquecarniatto/AuthKit
cd AuthKit
bash scripts/poc-up.sh     # http://127.0.0.1:8088
```

The stack is disposable: credentials are generated per run, the database lives in `tmpfs`, and
`scripts/poc-down.sh` removes everything.

## Why AuthKit

- **One contract, two SDKs.** `contracts/authkit.contract.json` is normative and a parity test in
  each language fails the build if either side drifts.
- **Native rendering.** Razor components for Blazor. No React inside a webview, no JS interop beyond
  `sessionStorage`.
- **Provider-agnostic.** `IAuthProvider` / `AuthProvider` is the only dependency the UI has.
- **Security is part of the contract.** Server-side tokens never reach the browser, sessions live in
  `sessionStorage`, service-role keys are rejected at startup, provider error text never reaches the
  UI, and a pentest suite blocks every release.

## Documentation

| Document                                                                                                                                                                                                                                                                | What it covers                                             |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| [README](https://github.com/carloshenriquecarniatto/AuthKit#readme)                                                                                                                                                                                                     | Install, quick start, repository layout                    |
| [Specification](https://github.com/carloshenriquecarniatto/AuthKit/blob/main/docs/spec.md) · [PT](https://github.com/carloshenriquecarniatto/AuthKit/blob/main/docs/spec.pt-BR.md) · [ES](https://github.com/carloshenriquecarniatto/AuthKit/blob/main/docs/spec.es.md) | Normative scope, gates and quality rules                   |
| [Architecture](https://github.com/carloshenriquecarniatto/AuthKit/blob/main/docs/architecture.md)                                                                                                                                                                       | Layers, hosting models and ADR-001 (MAUI)                  |
| [Threat model](https://github.com/carloshenriquecarniatto/AuthKit/blob/main/docs/threat-model.md)                                                                                                                                                                       | 14 threats with the mitigation and the test that covers it |
| [Penetration test](https://github.com/carloshenriquecarniatto/AuthKit/blob/main/docs/pentest.md)                                                                                                                                                                        | Offline, live and browser attacks                          |
| [UI parity](https://github.com/carloshenriquecarniatto/AuthKit/blob/main/docs/ui-parity.md)                                                                                                                                                                             | Widget-by-widget mapping from supabase_auth_ui             |
| [Contributing](https://github.com/carloshenriquecarniatto/AuthKit/blob/main/CONTRIBUTING.md)                                                                                                                                                                            | Hooks, commits, contract changes                           |

For machine readers, [`/llms.txt`]({{ site.baseurl }}/llms.txt) lists the canonical Markdown sources
and the contract.
