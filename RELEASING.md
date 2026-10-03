# Releasing NIVR

Releases are built locally, because the build needs interop assemblies generated from an owned copy of the game.
GitHub-hosted CI cannot build the plugin without committing game-derived files, which this project never does.

1. Bump `<Version>` in `plugin/Core/NIVR.Core.csproj` and the version in the heading of `plugin/Core/README.md`.
2. Build and package on the Windows dev machine:
   `powershell -ExecutionPolicy Bypass -File plugin\Core\package.ps1`
   This writes `release\NIVR-<version>.zip` and fails if anything outside the four-file allowlist ends up in it.
3. Smoke-test the zip: extract it into a clean BepInEx be.788 game copy, launch, and confirm the log shows NIVR loading.
4. Commit the version bump, tag it and push: `git tag v<version>` then `git push origin main v<version>`.
5. On GitHub open **Releases → Draft a new release**, pick the `v<version>` tag, attach `NIVR-<version>.zip`, paste the
   changes since the last release, and tick **Set as a pre-release** while features still need headset testing.
   With the GitHub CLI the same step is:
   `gh release create v<version> release\NIVR-<version>.zip --title "NIVR <version>" --prerelease --generate-notes`

Never attach anything from `game_copy/`, `recon/`, `logs/`, `backup/` or `BepInEx/interop`, and never ship `NIVR.DevTools`.
