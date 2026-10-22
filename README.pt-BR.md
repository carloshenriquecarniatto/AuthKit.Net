# AuthKit para .NET

**Inglês** · Português · [Español](README.es.md)

Telas de autenticação prontas em uma linha, nativas para .NET.

Este repositório contém apenas o SDK .NET. As outras plataformas vivem em repositórios próprios, com
o mesmo nome base e ciclos de release independentes:

| Plataforma | Repositório                                                                   |
| ---------- | ----------------------------------------------------------------------------- |
| Angular    | [AuthKit.Angular](https://github.com/carloshenriquecarniatto/AuthKit.Angular) |
| Aurelia    | [AuthKit.Aurelia](https://github.com/carloshenriquecarniatto/AuthKit.Aurelia) |
| Contrato   | `contracts/authkit.contract.json`, mantido aqui                               |

```text
AuthKit.Core               contratos, modelos, máquina de estados, validação, tema   (sem dependências de terceiros)
AuthKit.Supabase           provider Supabase Auth
AuthKit.Blazor             componentes Blazor nativos (WebAssembly, Server, Auto)
AuthKit.Clave              federação Cl@ve / eIDAS: pedido, validação, identidade
AuthKit.Clave.AspNetCore   MapClave(): endpoints de metadata, login e ACS
contracts/                 authkit.contract.json, o contrato normativo copiado pelos SDKs TypeScript
```

## Personalização da interface

Há três níveis, e o [guia de personalização](docs/customization.pt-BR.md) explica cada um com
código: tokens de tema, classes CSS `ak-*` e um provider sem interface.

## Por quê

- **Renderização nativa.** Componentes Razor para Blazor. Sem React dentro de uma webview, sem
  interop JS além do `sessionStorage`.
- **Agnóstico de provider.** `IAuthProvider` é a única dependência da interface.
- **Federação é biblioteca, não serviço.** `AuthKit.Clave` corre dentro do host, valida a asserção e
  para em "aqui está uma identidade validada". Nunca autentica ninguém.
- **Segurança faz parte do contrato.** Tokens do servidor nunca chegam ao navegador, chaves
  service-role são recusadas no arranque, texto de erro do provider nunca chega à interface, e uma
  suite de pentest bloqueia cada release.
- **Um contrato.** `contracts/authkit.contract.json` é normativo para todos os SDKs; um teste de
  paridade em cada repositório falha o build se houver desvio.

## Início rápido — Blazor com Supabase

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

## Início rápido — Cl@ve

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

O guia completo — checklist CAID, dois ambientes, estado distribuído e a tabela de claims — está em
[`docs/clave.md`](docs/clave.md).

## O contrato

`contracts/authkit.contract.json` é o contrato normativo. Os tipos .NET espelham-no e o
`ContractParityTests` falha o build em caso de deriva. Os repositórios Angular e Aurelia mantêm uma
cópia versionada e o CI deles falha se divergir deste ficheiro — sem pacote para publicar e sem
ordem de release. Ver [`contracts/README.md`](contracts/README.md).

## Teste de intrusão

A suite offline corre em cada pull request; a live precisa de Docker.

```bash
dotnet test security/AuthKit.Pentest/AuthKit.Pentest.csproj
bash scripts/pentest-up.sh && bash scripts/pentest-live.sh
```

## Estado

| Área                       | Estado                                                                     |
| -------------------------- | -------------------------------------------------------------------------- |
| Contrato (`1.1.0`)         | Estável (aditivo sobre 1.0.0)                                              |
| `AuthKit.Core`             | E-mail, magic link, OTP (e-mail + telefone), recuperação, OAuth            |
| `AuthKit.Supabase`         | Provider completo, mapeamento de erros, restauro de sessão                 |
| `AuthKit.Blazor`           | E-mail, magic link, reset, socials, callback, métodos e callback Cl@ve     |
| `AuthKit.Clave`            | Pedido/resposta eIDAS, certificado fixado, replay, LoA, identidade         |
| `AuthKit.Clave.AspNetCore` | `MapClave()` metadata/login/ACS, estado distribuído, política de returnUrl |
| MAUI                       | Planeado: mesmo core, shell XAML, `WebAuthenticator`                       |

## Estrutura do repositório

```text
contracts/            authkit.contract.json, o contrato normativo copiado pelos SDKs TypeScript
src/dotnet/           AuthKit.Core, AuthKit.Supabase, AuthKit.Blazor, AuthKit.Clave*
tests/dotnet/         xUnit, bUnit e fixtures de protocolo
security/             suites de pentest offline e live
samples/messages/     fixtures de protocolo anonimizadas e versionadas
docs/                 spec, modelo de ameaças, arquitetura, guia Cl@ve, paridade de UI
```

## Contribuir

Veja [CONTRIBUTING.md](CONTRIBUTING.md). Os hooks correm `dotnet test` antes do push; uma mudança de
contrato tem de entrar aqui primeiro e depois nos SDKs consumidores com bump de versão.

## Segurança

Leia [SECURITY.md](SECURITY.md) antes de reportar uma vulnerabilidade.

## Licença

MIT — veja [LICENSE](LICENSE).
