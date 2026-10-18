# Releasing and code signing

Windows Smart App Control and SmartScreen block unsigned executables, so every release needs a signed `BetterGlorious.exe`. Signing happens in GitHub Actions through the [SignPath Foundation](https://signpath.org), which offers it free to open source projects and issues the certificate.

## One-time setup

1. **Publish the repository** on GitHub (public, MIT license is already included).
2. **Apply** for the SignPath Foundation program at <https://signpath.org/apply>. They check that:
   - the project is open source under an OSI license and actively maintained,
   - release binaries are built from source by CI (this repo's `Build` workflow),
   - the README has a code signing policy (see below).
3. When accepted, in the SignPath dashboard:
   - create a project with slug `betterglorious`, linked to the GitHub repository as a *trusted build system*,
   - add an artifact configuration and paste [.signpath/artifact-configuration.xml](../.signpath/artifact-configuration.xml),
   - create a signing policy with slug `release-signing` (manual approval is fine),
   - create an API token for a CI user with *submitter* rights on that policy.
4. In the GitHub repository settings:
   - **Secrets → Actions**: `SIGNPATH_API_TOKEN` = the API token.
   - **Variables → Actions**: `SIGNPATH_ORGANIZATION_ID` = your SignPath organization ID. Optionally set `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_POLICY_SLUG` if you chose different slugs.

Until `SIGNPATH_ORGANIZATION_ID` is set, tagged builds are released unsigned.

## Making a release

1. Bump `Version` in [src/Core/AppInfo.cs](../src/Core/AppInfo.cs) and `version` in [web/package.json](../web/package.json).
2. Commit, then tag and push:
   ```
   git tag v0.2.0
   git push origin v0.2.0
   ```
3. The `Build` workflow builds, submits the build to SignPath, waits for approval, and attaches `BetterGlorious-v0.2.0-win-x64.zip` with the signed exe to a new GitHub release.

Every push to `main` also produces an unsigned `BetterGlorious-unsigned` artifact for testing.
