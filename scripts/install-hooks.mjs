#!/usr/bin/env node
/**
 * Points git at the repository hooks. Safe to run repeatedly and in CI.
 */
import { execFileSync } from 'node:child_process';
import { chmodSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const hooksDirectory = join(root, '.githooks');

try {
  execFileSync('git', ['config', 'core.hooksPath', '.githooks'], { cwd: root, stdio: 'ignore' });
  for (const hook of readdirSync(hooksDirectory)) {
    chmodSync(join(hooksDirectory, hook), 0o755);
  }
  console.log('AuthKit: git hooks installed (.githooks).');
} catch (error) {
  console.warn(`AuthKit: could not install git hooks (${error.message}). CI still enforces every check.`);
}
