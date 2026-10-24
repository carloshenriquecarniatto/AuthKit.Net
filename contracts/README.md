# The AuthKit contract

`authkit.contract.json` is the normative contract shared by every AuthKit SDK. This repository owns
it: the .NET types in `AuthKit.Core` mirror it, and `ContractParityTests` compares the C# surface
with the JSON on every build.

## How the other SDKs get it

`AuthKit.Angular` keeps a committed copy at `contracts/authkit.contract.json`
and run two checks:

1. **Parity** — each SDK's own test compares its TypeScript declarations with the JSON.
2. **Drift** — CI downloads this file from `main` and fails if the committed copy differs.

To update a TypeScript SDK after a contract change:

```bash
# in AuthKit.Angular, with AuthKit.Net checked out next to it
node scripts/sync-contract.mjs            # copy from ../AuthKit.Net
node scripts/sync-contract.mjs --remote   # or download from GitHub
```

Then update the TypeScript declarations in the same pull request; the parity test fails until the
shapes match.

There is no package to publish and no release ordering: the JSON is copied and verified, not
installed.
