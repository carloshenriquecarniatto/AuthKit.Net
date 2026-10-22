#!/usr/bin/env bash
# Publishes every artifact for the given version. Safe to run without credentials:
# missing secrets skip the corresponding registry instead of failing the release.
set -euo pipefail

version="${1:?publish: version argument is required}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "publish: version ${version}"

if [ -n "${NUGET_API_KEY:-}" ]; then
  echo "publish: packing and pushing NuGet packages..."
  dotnet pack "$root/AuthKit.slnx" -c Release -o "$root/artifacts" \
    -p:Version="$version" -p:ContinuousIntegrationBuild=true

  for package in "$root"/artifacts/*.nupkg; do
    dotnet nuget push "$package" \
      --api-key "$NUGET_API_KEY" \
      --source "${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}" \
      --skip-duplicate
  done
else
  echo "publish: NUGET_API_KEY is not set, skipping NuGet."
fi

echo "publish: done."
