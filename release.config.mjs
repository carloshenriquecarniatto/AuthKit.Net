/**
 * Semantic Release configuration.
 *
 * One lockstep version for every artifact of this repository. scripts/set-version.mjs stamps the
 * version into src/dotnet/version.props and the @authkit/contracts manifest during prepare.
 *
 * main is a protected branch, so the release job never commits back: it creates the tag and the
 * GitHub release, and the artifacts carry the version. Local sources keep the development
 * fallback version, which is overwritten at pack time by publish.sh.
 */
export default {
  branches: ['main'],
  tagFormat: 'v${version}',
  plugins: [
    '@semantic-release/commit-analyzer',
    '@semantic-release/release-notes-generator',
    ['@semantic-release/changelog', { changelogFile: 'CHANGELOG.md' }],
    [
      '@semantic-release/exec',
      {
        prepareCmd: 'node scripts/set-version.mjs ${nextRelease.version}',
        publishCmd: 'bash scripts/publish.sh ${nextRelease.version}',
      },
    ],
    '@semantic-release/github',
  ],
};
