---
description:
  'Como personalizar o AuthKit: tokens de tema, CSS e provider headless, para Blazor e Angular.'
lang: pt
alt_lang:
  en: /customization/
  pt: /personalizacao/
  es: /personalizacion/
layout: page
title: Personalizando a interface
permalink: /personalizacao/
---

[English]({{ site.baseurl }}/customization/) · **Português** ·
[Español]({{ site.baseurl }}/personalizacion/)

O AuthKit entrega uma tela de login funcionando em uma linha, mas nada nela é fechado. Existem três
níveis de customização, e você para no que resolver o seu problema.

| Nível             | O que você muda                       | O que você mantém                                 | Esforço |
| ----------------- | ------------------------------------- | ------------------------------------------------- | ------- |
| 1. Tokens de tema | Cores, raio, espaçamento, fonte, logo | Todos os componentes e comportamentos             | Minutos |
| 2. CSS            | Layout, densidade, sombras, animações | Todos os componentes e comportamentos             | Horas   |
| 3. Headless       | A interface inteira                   | Provider, storage, mapeamento de erros, segurança | Dias    |

![Formulário de entrada]({{ site.baseurl }}/assets/login.png)

## Nível 1 — tokens de tema

`AuthTheme` é um record simples com dez tokens. Os dois SDKs usam os mesmos nomes e, na web, eles
viram custom properties de CSS no elemento raiz do componente.

| Token        | Variável CSS       | Padrão           |
| ------------ | ------------------ | ---------------- |
| `Primary`    | `--ak-primary`     | `#4f46e5`        |
| `Background` | `--ak-background`  | `#ffffff`        |
| `Surface`    | `--ak-surface`     | `#ffffff`        |
| `Text`       | `--ak-text`        | `#111827`        |
| `TextMuted`  | `--ak-text-muted`  | `#6b7280`        |
| `Border`     | `--ak-border`      | `#d1d5db`        |
| `Danger`     | `--ak-danger`      | `#dc2626`        |
| `Radius`     | `--ak-radius`      | `8px`            |
| `Spacing`    | `--ak-spacing`     | `16px`           |
| `FontFamily` | `--ak-font-family` | stack do sistema |

### Blazor

```csharp
builder.Services.AddAuthKitSupabase(
    supabase =>
    {
        supabase.Url = "https://seu-projeto.supabase.co";
        supabase.AnonKey = "<anon key>";
    },
    ui =>
    {
        ui.Theme = AuthTheme.Default with
        {
            Primary = "#0f766e",
            Radius = "12px",
            FontFamily = "Inter, system-ui, sans-serif",
            LogoUrl = "/logo.svg"
        };
    });
```

### Angular

```ts
provideAuthKitSupabase(
  { url: environment.supabaseUrl, anonKey: environment.anonKey },
  {
    theme: {
      ...DEFAULT_AUTH_THEME,
      primary: '#0f766e',
      radius: '12px',
      fontFamily: 'Inter, system-ui, sans-serif',
      logoUrl: '/logo.svg',
    },
  },
);
```

Os valores do tema são sanitizados: `; { } < > " ' \` são removidos e `url(`/`expression(` são
recusados, então um tema vindo de entrada do usuário não injeta CSS. `LogoUrl` aceita apenas https
ou caminho relativo.

## Nível 2 — seu próprio CSS

Todas as classes têm o prefixo `ak-`. Sobrescreva globalmente ou desligue o CSS embutido e escreva o
seu.

```css
/* Carregado depois do CSS do AuthKit, ou com IncludeStyles="false" no Blazor. */
.ak-form {
  gap: 20px;
}

.ak-input {
  background: #0b1220;
  color: #e2e8f0;
  border-color: #1e293b;
}

.ak-button {
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
```

Ganchos úteis: `.ak-form`, `.ak-field`, `.ak-label`, `.ak-input`, `.ak-hint`, `.ak-button`,
`.ak-link`, `.ak-error`, `.ak-notice`, `.ak-socials`, `.ak-social`, `.ak-spinner`.

### Blazor

`<AuthKitEmail IncludeStyles="false" />` para de emitir a folha base; inclua seu CSS na página
hospedeira. Todos os componentes aceitam `IncludeStyles`.

### Angular

Os componentes usam `ViewEncapsulation.None`, então um stylesheet global da sua aplicação
sobrescreve as classes `ak-*` diretamente.

## Nível 3 — headless

Quando o design não parece um cartão de login, pare de usar os componentes e use o provider. Você
mantém a parte difícil de acertar: mapeamento de erros, storage da sessão, refresh e validação.

### Blazor

```razor
@inject IAuthProvider Auth
@inject NavigationManager Nav

<form @onsubmit="Submit" @onsubmit:preventDefault class="my-login">
    <input @bind="_email" placeholder="voce@empresa.com" />
    <input type="password" @bind="_password" />
    <button disabled="@_busy">@(_busy ? "…" : "Entrar")</button>
</form>

@if (_error is not null)
{
    <p role="alert">@_error</p>
}

@code {
    private string _email = "";
    private string _password = "";
    private bool _busy;
    private string? _error;

    private async Task Submit()
    {
        _busy = true;
        var result = await Auth.SignInWithPasswordAsync(_email, _password);
        _busy = false;

        if (result.Ok)
        {
            Nav.NavigateTo("/");
        }
        else
        {
            // Já é seguro para exibir: o SDK mapeia o texto do provider.
            _error = result.Error?.Message;
        }
    }
}
```

### Angular

```ts
import { Component, inject } from '@angular/core';
import { AUTH_PROVIDER } from '@authkit/angular';

@Component({
  selector: 'app-sign-in',
  template: `
    <form (submit)="submit($event)">
      <input [value]="email" (input)="email = valueOf($event)" />
      <input type="password" [value]="password" (input)="password = valueOf($event)" />
      <button [disabled]="busy">Entrar</button>
    </form>
  `,
})
export class SignInComponent {
  private readonly auth = inject(AUTH_PROVIDER);

  email = '';
  password = '';
  busy = false;

  valueOf(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.busy = true;
    const result = await this.auth.signInWithPassword(this.email, this.password);
    this.busy = false;
    // result.error?.message é seguro para exibir.
  }
}
```

O provider também expõe `capabilities` (o que o projeto suporta), `StateChanged`/`onStateChange` e
toda a superfície de operações listada em `contracts/authkit.contract.json`.

## Mensagens e idioma

Inglês vem embutido. Todo o resto é um **adapter sobre o mesmo port**, então você escolhe de onde as
strings vêm.

| Port / adapter           | Use quando                                                             |
| ------------------------ | ---------------------------------------------------------------------- |
| `IAuthMessages`          | Você quer resolver as strings do seu jeito (banco, CMS, feature flags) |
| `DictionaryAuthMessages` | As strings já estão no código                                          |
| `JsonAuthMessages`       | Você tem um bundle JSON                                                |
| `FileAuthMessagesSource` | O bundle é um arquivo em disco                                         |
| `HttpAuthMessagesSource` | O bundle está numa URL, CDN ou na sua API                              |

### Bundle JSON

```json
{
  "signIn": "Entrar",
  "email": "Seu e-mail",
  "passwordTooShort": "A senha precisa ter pelo menos {min} caracteres"
}
```

As chaves são `AuthMessageKey` (case-insensitive), chaves desconhecidas são ignoradas, chaves
ausentes caem para o inglês e payloads acima de 1 MB são recusados. Os bundles prontos de português
e espanhol ficam em `samples/messages` e são os que a demo carrega.

**Blazor** — só registro, sem carregamento manual:

```csharp
builder.Services.AddAuthKitMessagesFromJson("messages/pt-BR.json");
// ou: builder.Services.AddAuthKitMessagesFromUrl(new Uri("https://cdn.example.com/pt-BR.json"));

builder.Services.AddAuthKitSupabase(supabase => { /* … */ });
```

O `AddAuthKitBlazor` aquece o catálogo no startup do host, antes do primeiro componente renderizar.
Outros hosts (MAUI, console) chamam `await app.Services.EnsureAuthKitMessagesAsync();` uma vez. A
ordem de registro não importa, e `ui.Messages` continua tendo prioridade quando você passa.

**Angular** — mesma ideia:

```ts
providers: [
  provideAuthKitSupabase(config),
  provideAuthKitMessagesFromUrl('/assets/authkit.pt-BR.json'),
];
```

Um app initializer espera o fetch antes do primeiro render, então não existe flash de texto sem
tradução. Se o carregamento falhar, o fallback em inglês é mantido e um warning é logado, sem
quebrar o login.

Se você preferir controlar o momento, `AuthMessages.FromFileAsync` / `AuthMessages.FromUrlAsync` /
`loadAuthMessages` continuam devolvendo um catálogo para passar como `ui.Messages` / `{ messages }`.

### Estendendo com a sua fonte

Implemente o port e plugue em qualquer lugar:

```csharp
public sealed class DatabaseMessages : IAuthMessagesSource
{
    public async ValueTask<IAuthMessages> LoadAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _repository.LoadStringsAsync(cancellationToken);
        return AuthMessages.FromDictionary(rows, fallback: AuthMessages.English);
    }
}
```

```ts
export class FeatureFlagMessages implements AuthMessagesSource {
  async load(): Promise<AuthMessages> {
    const bundle = await this.flags.get<string>('authkit.messages');
    return new JsonAuthMessages(bundle);
  }
}
```

### Só dicionário

```csharp
var messages = AuthMessages.FromDictionary(new Dictionary<AuthMessageKey, string>
{
    [AuthMessageKey.SignIn] = "Entrar"
});
```

```ts
const messages = new DictionaryAuthMessages({ signIn: 'Entrar' }, ENGLISH_AUTH_MESSAGES);
```

A lista completa de chaves é `AuthMessageKey`, no arquivo de contrato, e o catálogo inglês embutido
é a referência de texto.

## Referência dos componentes

### Blazor

| Componente             | Parâmetros                                                                                                         | Callbacks                                                                |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------ |
| `AuthKitEmail`         | `ShowConfirmPasswordField`, `MetadataFields`, `PrefilledEmail`, `PrefilledPassword`, `RedirectTo`, `IncludeStyles` | `OnSignInComplete`, `OnSignUpComplete`, `OnPasswordResetSent`, `OnError` |
| `AuthKitMagicLink`     | `PrefilledEmail`, `RedirectTo`, `IncludeStyles`                                                                    | `OnSent`, `OnError`                                                      |
| `AuthKitResetPassword` | `PrefilledEmail`, `UpdateMode`, `RedirectTo`, `IncludeStyles`                                                      | `OnResetRequested`, `OnPasswordUpdated`, `OnError`                       |
| `AuthKitSocials`       | `Providers`, `Colored`, `Variant`, `RedirectTo`, `IncludeStyles`                                                   | `OnOAuthRequested`, `OnError`                                            |
| `AuthKitCallback`      | `RedirectTo`, `IncludeStyles`                                                                                      | `OnCompleted`, `OnError`                                                 |

### Angular

| Componente          | Inputs                                                                                            | Outputs                                                           |
| ------------------- | ------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| `ak-email`          | `metadataFields`, `showConfirmPasswordField`, `prefilledEmail`, `prefilledPassword`, `redirectTo` | `signInComplete`, `signUpComplete`, `passwordResetSent`, `failed` |
| `ak-magic-link`     | `prefilledEmail`, `redirectTo`                                                                    | `sentLink`, `failed`                                              |
| `ak-reset-password` | `updateMode`, `prefilledEmail`, `redirectTo`                                                      | `resetRequested`, `passwordUpdated`, `failed`                     |
| `ak-socials`        | `providers`, `colored`, `variant`, `redirectTo`                                                   | `oauthRequested`, `failed`                                        |
| `ak-callback`       | `redirectTo`                                                                                      | `completedSession`, `failed`                                      |

## Regras que preservam as garantias de segurança

1. **Nunca guarde tokens por conta própria.** Use o provider; ele cuida do storage e do refresh.
2. **Nunca exiba a mensagem da exceção do provider.** `AuthResult.Error.Message` já vem mapeada;
   texto cru do provider vaza detalhes internos.
3. **Não navegue de dentro do componente.** Emita o callback e deixe o host decidir.
4. **Mantenha a URL de redirect na configuração**, nunca em parâmetro de query.
5. **Blazor Server**: nunca mova a sessão para `sessionStorage`; o refresh token fica no servidor.

## Veja rodando

```bash
bash scripts/poc-up.sh   # http://127.0.0.1:8088
```

A demo em `projects/demo` usa a biblioteca com tema customizado e serve de referência para todos os
níveis acima.

---

[Markdown source](https://github.com/carloshenriquecarniatto/AuthKit/blob/main/docs/customization.pt-BR.md)
· [llms.txt]({{ site.baseurl }}/llms.txt)
