# Releasing

1. Update `VersionPrefix` in `Directory.Build.props` and add `CHANGELOG.md` entries.
2. Run Release restore/build/test and `scripts/package.ps1 -Version X.Y.Z` on Windows.
3. Smoke-test the portable output and installer using `docs/manual-test-checklist.md`.
4. Commit and create an annotated semantic tag such as `v0.1.0` or `v0.2.0-beta.1`.
5. Push the tag. The release workflow rebuilds, tests, packages, calculates SHA-256 checksums, and creates a GitHub release. Hyphenated versions are marked prerelease.

Binaries are unsigned until project-owned signing credentials exist. Signing belongs after publish and after installer compilation, before checksums and upload. CI requires no secrets beyond GitHub's release token.

For a non-interactive lifecycle/provider smoke test, run `SysWatt.App.exe --smoke-test`. It performs live collection for 2.5 seconds and then follows the normal deterministic shutdown path.
