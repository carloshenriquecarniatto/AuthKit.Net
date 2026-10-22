# @authkit/contracts

The normative AuthKit contract, shared by every SDK. `authkit.contract.json` is the source of truth;
this package also ships the TypeScript declarations and the four runtime helpers.

```bash
npm install @authkit/contracts
```

```ts
import { authError, fail, type AuthSession } from '@authkit/contracts';

// Tests only: the parsed JSON plus the hand-mirrored declaration map.
import { AUTHKIT_CONTRACT, loadContract } from '@authkit/contracts/testing';
```

## Why a package

Each SDK lives in its own repository with its own release cycle. Shipping the contract as a package
means a change is versioned once and consumed by a fixed version, instead of three copies drifting
apart.

The .NET SDK is the canonical owner: `authkit.contract.json` lives next to `AuthKit.Core` and its
`ContractParityTests` read it directly. Publishing this package is part of the .NET release.

## Parity

`AUTHKIT_CONTRACT` mirrors the TypeScript declarations by hand. Every SDK's test suite compares it
with the JSON: add a field to a type and forget the map, and the build fails.

Only `@authkit/contracts/testing` reads the filesystem. Browser bundles import the root entry point,
which has no Node dependencies.
