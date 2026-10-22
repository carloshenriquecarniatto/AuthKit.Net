import test from 'node:test';
import assert from 'node:assert/strict';
import { AUTHKIT_CONTRACT, contract } from '../testing.js';

/**
 * Guards the contract package before it is published. The .NET reflection test and every SDK's
 * parity test compare against the same JSON; this one catches a stale hand-mirrored map first.
 */

test('declares the same contract version as the JSON', () => {
  assert.equal(AUTHKIT_CONTRACT.contractVersion, contract.contractVersion);
});

test('declares every field of every contract type', () => {
  const declared = Object.entries(contract.types)
    .filter(([, type]) => type.fields !== undefined)
    .map(([name]) => name)
    .sort();

  assert.deepEqual(declared, Object.keys(AUTHKIT_CONTRACT.types).sort());

  for (const name of declared) {
    assert.deepEqual(
      [...AUTHKIT_CONTRACT.types[name]].sort(),
      Object.keys(contract.types[name].fields).sort(),
      `${name} fields`,
    );
  }
});

test('declares the same enums', () => {
  assert.deepEqual([...AUTHKIT_CONTRACT.errorCodes], contract.types.AuthErrorCode.enum);
  assert.deepEqual([...AUTHKIT_CONTRACT.states], contract.types.AuthState.enum);
  assert.deepEqual([...AUTHKIT_CONTRACT.nextSteps], contract.types.AuthNextStep.enum);
  assert.deepEqual([...AUTHKIT_CONTRACT.messageKeys], contract.types.AuthMessageKey.enum);
});

test('declares every operation', () => {
  assert.deepEqual(
    [...AUTHKIT_CONTRACT.operations],
    contract.operations.map((operation) => operation.name),
  );
});
