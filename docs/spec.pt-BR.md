# AuthKit v0.1 — Especificação

[English](spec.md) · **Português** · [Español](spec.es.md)

Status: normativa para a linha `0.x`. Mudanças no contrato exigem um bump de versão major do
contrato e atualização dos testes de paridade nos dois SDKs.

## 1. Objetivo

Entregar a experiência de "tela de login rápida" do `supabase_auth_ui` para .NET e TypeScript, sem
sacrificar renderização nativa, extensibilidade ou segurança.

Fora do escopo da v0.1: MAUI, interop de React, UI de administração/gestão de usuários, passkeys e
UI de SSO empresarial.

## 2. Pacotes

| Pacote             | Alvo       | Responsabilidade                                                           |
| ------------------ | ---------- | -------------------------------------------------------------------------- |
| `AuthKit.Core`     | net10.0    | Tipos do contrato, `IAuthProvider`, `AuthFlow`, validação, tema, mensagens |
| `AuthKit.Supabase` | net10.0    | Integração com GoTrue, mapeamento de erros, restauração de sessão, DI      |
| `AuthKit.Blazor`   | net10.0    | Componentes Razor, `AuthKitState`, stores de sessão                        |
| `@authkit/angular` | Angular 22 | Componentes standalone, signals, provider Supabase                         |

MAUI fica adiado. O contrato e o provider são neutros de plataforma, então a casca MAUI só adiciona
renderização XAML, `WebAuthenticator` e os targets de manifest descritos no ADR-001.

## 3. Contrato

`contracts/authkit.contract.json` é a fonte única de verdade para:

- tipos: `AuthUser`, `AuthSession`, `AuthError`, `AuthResult`, `AuthCapabilities`, `AuthTheme`;
- enums: `AuthErrorCode`, `AuthState`, `AuthNextStep`, `AuthMessageKey`;
- operações: a superfície do provider.

Regras:

1. Todo campo existe nos dois SDKs, com o mesmo nome e o mesmo tipo base.
2. Erros esperados são valores (`AuthResult`), nunca exceções.
3. A UI só pode chamar operações declaradas no contrato.
4. Testes de paridade: `ContractParityTests` (.NET, reflexão) e `contract.spec.ts` (TypeScript,
   descritor em runtime). Os dois precisam passar para um release.

## 4. Paridade de componentes com `supabase_auth_ui`

| Flutter              | AuthKit (.NET / Angular)                     | Observações                                          |
| -------------------- | -------------------------------------------- | ---------------------------------------------------- |
| `SupaEmailAuth`      | `AuthKitEmail` / `ak-email`                  | Toggle sign-in/sign-up, confirmação, metadata, reset |
| `SupaMagicAuth`      | `AuthKitMagicLink` / `ak-magic-link`         | Estado "check your email"                            |
| `SupaResetPassword`  | `AuthKitResetPassword` / `ak-reset-password` | Modos request + update                               |
| `SupaSocialsAuth`    | `AuthKitSocials` / `ak-socials`              | Lista de providers, cor, ícone/ícone+texto           |
| `SupaPhoneAuth`      | Só método do provider na v0.1                | Componente na v0.2                                   |
| `SupaVerifyPhone`    | Só método do provider na v0.1                | Componente na v0.2                                   |
| `MetaDataField`      | `AuthMetadataField`                          | Texto e booleano                                     |
| Tema via `ThemeData` | `AuthTheme` → variáveis CSS                  | Mesmos nomes de token nos dois SDKs                  |
| Delegate de i18n     | `IAuthMessages` / `AuthMessages`             | Inglês embutido                                      |

Divergência: o tamanho mínimo de senha é 8 (o piso do Supabase é 6). Segurança ganha da paridade
exata; o valor é configurável.

## 5. Requisitos de segurança (bloqueiam release)

Cada item abaixo é um gate. Um item falhando bloqueia a versão.

1. **Nenhum vazamento de credencial em log.** `ToString`/serialização da sessão precisam redigir
   tokens.
2. **Nenhuma exposição de token no servidor.** Blazor Server nunca escreve refresh token no
   navegador.
3. **Storage do navegador é `sessionStorage`.** `localStorage` é proibido para tokens.
4. **Nenhuma chave service-role em cliente.** O startup falha com mensagem acionável.
5. **Texto do provider não é repassado.** Erros são mapeados para mensagens do AuthKit.
6. **Injeção de estilo é neutralizada.** Valores de tema não escapam de uma declaração CSS.
7. **Validação resistente a ReDoS.** O regex de e-mail é limitado e não-backtracking.
8. **Sem submissão dupla.** O fluxo recusa operações concorrentes.
9. **OAuth usa PKCE.** Fluxo implícito não é usado em integrações novas.
10. **Higiene de callback.** Códigos/tokens são removidos da URL após sucesso.

## 6. Mandato de pentest

Antes de qualquer `1.0.0` e pelo menos uma vez por ano:

- Tentar extrair token de storage do navegador, DOM, URL, histórico, logs e mensagens de erro.
- Tentar fixação de sessão e vazamento entre usuários no Blazor Server (dois circuits simultâneos).
- Tentar escalar privilégio com a anon key (deve ser bloqueado por RLS; o AuthKit não pode
  facilitar).
- Tentar injeção de estilo/DOM via tokens de tema, mensagens e metadata.
- Tentar replay de callback OAuth e de verificador PKCE.

Achados viram issues de segurança e o release continua bloqueado até cada um ser corrigido ou
formalmente aceito em `docs/threat-model.md`.

A metade automatizada desse mandato vive em `security/AuthKit.Pentest` (offline, bloqueante em todo
pull request) e `security/AuthKit.Pentest.Live` (GoTrue containerizado, semanal). Um release exige a
suíte offline e os scanners de `.github/workflows/security.yml` verdes.

## 7. Gates de qualidade

- Testes unitários para todo comportamento público do Core.
- Testes de integração contra o SDK real do Supabase com transporte stub (sem rede).
- Testes de componente para renderização, validação, callbacks e tema.
- `dotnet format --verify-no-changes`, Prettier e build TypeScript strict.
- Semgrep, Trivy, npm audit (produção) e NuGet audit (transitivo, high e acima).

## 8. Versionamento

O Semantic Release deriva a versão dos Conventional Commits. Uma versão lockstep para todos os
pacotes na linha `0.x`. Artefatos publicados: NuGet (`AuthKit.*`) e npm (`@authkit/angular`).
