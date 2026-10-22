# UI parity with `supabase_auth_ui`

AuthKit reproduces the composition of the Flutter package widget by widget: same fields, same
toggles, same states. It does not copy its code; it copies the interaction model, because that is
what makes the screen "fast to add".

## Email and password

| Aspect               | `SupaEmailAuth`                         | `AuthKitEmail` / `ak-email`                                              |
| -------------------- | --------------------------------------- | ------------------------------------------------------------------------ |
| Modes                | sign in / sign up                       | same                                                                     |
| Email validation     | `email_validator` package               | bounded non-backtracking regex                                           |
| Password             | min 6, configurable                     | min 8 by default, configurable                                           |
| Confirm password     | `showConfirmPasswordField`              | same parameter                                                           |
| Prefill              | `prefilledEmail`, `prefilledPassword`   | same inputs                                                              |
| Forgot password      | toggle to recovery form                 | same                                                                     |
| Metadata             | `MetaDataField`, `BooleanMetaDataField` | `AuthMetadataField` (`text` / `boolean`)                                 |
| Callbacks            | `onSignInComplete`, `onSignUpComplete`  | `OnSignInComplete`/`signInComplete`, `OnSignUpComplete`/`signUpComplete` |
| Busy state           | spinner inside the button               | same                                                                     |
| Error display        | snack bar                               | inline `role="alert"` region                                             |
| Auto submit on Enter | `enableAutomaticFormSubmission`         | native `<form>` submit on web                                            |

## Magic link

Same single-field form, same "Check your email inbox!" confirmation state, same "back" link.

## Password reset

`SupaResetPassword` is split into two explicit modes in AuthKit: `request` (send the email) and
`update` (set the new password when the recovery link lands). Web hosts choose the mode; the
callback component can drive it.

## Socials

| Aspect        | `SupaSocialsAuth`      | `AuthKitSocials` / `ak-socials`                             |
| ------------- | ---------------------- | ----------------------------------------------------------- |
| Provider list | `socialProviders`      | same                                                        |
| Variant       | icon / icon + text     | same                                                        |
| Brand colours | `colored`              | same                                                        |
| Labels        | `continueWithProvider` | same message key                                            |
| Navigation    | built in               | emits `OnOAuthRequested`/`oauthRequested`; the host decides |
| Icons         | Font Awesome assets    | initial letter in v0.1; SVG icons in v0.2                   |

Divergences are deliberate:

1. **Minimum password of 8**, not 6. The default is a security decision, and it is configurable.
2. **Navigation is never owned by a component.** Flutter widgets can push routes; a library cannot
   know your router. Components emit URLs and results.
3. **No snack bars.** Inline regions are accessible, testable and do not need a `ScaffoldMessenger`.
