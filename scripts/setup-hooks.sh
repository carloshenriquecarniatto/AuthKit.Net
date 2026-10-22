#!/usr/bin/env bash
# Convenience wrapper for contributors who prefer a shell entry point.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
node scripts/install-hooks.mjs
