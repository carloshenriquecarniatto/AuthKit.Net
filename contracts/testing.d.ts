import type { AuthErrorCode, AuthMessageKey, AuthNextStep, AuthState } from './index.js';

/** The parsed contents of authkit.contract.json. */
export declare const contract: unknown;

/** Hand-mirrored description of the TypeScript declarations, consumed by the parity tests. */
export declare const AUTHKIT_CONTRACT: {
  readonly contractVersion: string;
  readonly types: Readonly<Record<string, readonly string[]>>;
  readonly errorCodes: readonly AuthErrorCode[];
  readonly states: readonly AuthState[];
  readonly nextSteps: readonly AuthNextStep[];
  readonly messageKeys: readonly AuthMessageKey[];
  readonly operations: readonly string[];
};

/** Loads the canonical JSON, for tests. */
export declare const loadContract: () => unknown;
