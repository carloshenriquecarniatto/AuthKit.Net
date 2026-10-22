# Contributing to AuthKit

## Ground rules

1. **Everything is in English** — code, comments, documentation, commit messages, issues and PRs.
2. **The contract is normative.** `contracts/authkit.contract.json` is the single source. A change
   lands here first (with `npm run test:contracts`), then in the Angular and Aurelia repositories
   with a version bump of `@authkit/contracts`.
3. **No business logic in a renderer.** Validation, state transitions and error mapping belong to
   the core.
4. **Security issues are never discussed in a public issue.** See [SECURITY.md](SECURITY.md).
5. **One logical change per pull request.** Small PRs get reviewed; big PRs rot.

## Setup

```bash
# .NET 10 SDK is required (see global.json)
dotnet workload restore

# Contract package tests
npm run test:contracts

# Git hooks (required)
./scripts/setup-hooks.sh
```

The hooks are also installed automatically by `npm install` through the `prepare` script.

## Build and test

```bash
dotnet build
dotnet test
dotnet format AuthKit.slnx --verify-no-changes
npm run test:contracts
```

## Commit messages

Conventional Commits, enforced by `commitlint` in the `commit-msg` hook:

```
feat(blazor): add phone OTP component
fix(supabase): map otp_expired to the correct error code
docs(security): document the XSS residual risk
chore(ci): cache NuGet packages
```

Allowed types: `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `build`, `ci`, `chore`, `revert`.

The version is derived from these messages. `feat` bumps the minor, `fix`/`perf` bump the patch, and
`BREAKING CHANGE:` bumps the major.

## Pull request checklist

- [ ] Tests cover the new behaviour (unit, and integration when the provider is involved).
- [ ] `dotnet test` passes locally.
- [ ] `npm run test:contracts` passes when the contract package changed.
- [ ] No new token, URL or header is written to a log.
- [ ] If the contract changed, it is versioned here and the consuming SDKs are updated separately.
- [ ] Documentation updated (`docs/`, README or XML/TSDoc comments).
- [ ] No secrets, `.env` files or private keys in the diff.

## Hooks

| Hook         | What it blocks                                                                                 |
| ------------ | ---------------------------------------------------------------------------------------------- |
| `commit-msg` | Non-conventional commit messages                                                               |
| `pre-commit` | Secrets, private keys, `.env` files, files over 2 MB, trailing whitespace, generated artifacts |
| `pre-push`   | Failing .NET tests                                                                             |

Escape hatch: `AUTHKIT_SKIP_HOOKS=1 git commit ...`. CI runs the same checks, so skipping only moves
the failure.

## Translations

The README, the specification and the customization guide are maintained in English, Portuguese and
Spanish. English is canonical: change it first, then update both translations in the same pull
request. Keep code blocks, option names and links identical across languages; translate prose only.
The language switcher lives at the top of each file and in the site header.

## Changing the contract

1. Edit `contracts/authkit.contract.json`.
2. Update `AuthKit.Core` models, `AuthContractValues` and the C# parity test fixtures.
3. Update `auth.models.ts`, `AUTHKIT_CONTRACT` and the TS parity test.
4. Update `docs/spec.md` and `docs/ui-parity.md` when behaviour changes.
5. Bump `contractVersion` for breaking changes.

## Releasing

Releases are automatic on `main`. The workflow runs tests and every security scanner first; if any
of them fails, no tag, no package, no changelog entry is produced. Never bump a version by hand.
