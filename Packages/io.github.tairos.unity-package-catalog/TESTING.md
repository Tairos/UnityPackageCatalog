# Test guide — experimental milestones 3 and 4

## Verified during implementation

In the running **Unity 6000.6.0f1** Editor:

| Check | Result |
|---|---|
| Embedded package compiles | Passed |
| Catalogue validation and update tests | 44 passed, 0 failed |
| Named source beneath Sources | Passed; inspected in the actual window |
| Two uninstalled entries in Unity's native list/details | Passed |
| Native search filters to one matching entry | Passed |
| Native Install on a local demo | Passed; actual installed state confirmed |
| Native Manage → Remove | Passed; entry returns as available |
| Script recompilation/domain reload | Passed; source and entries recover |
| Close and reopen Package Manager | Passed |
| Disable integration | Passed |
| Private GitHub installation via SSH | Verified in the earlier prototype session recorded in the handoff, including removal and reinstall |
| Full Editor restart / another Unity patch / another machine | Manual checks below |

An initial compiler warning was corrected. The final compilation check reports no compilation errors. This table records exercised behaviour, not support for all Unity versions.

## Repeat the local UI test

1. Open this project with Unity 6000.6. Let compilation finish.
2. Open **Window → Package Management → Package Catalog Settings**.
3. Click **Use local demo catalogue**. Expect Package Manager to open on **Sources → Example Packages**.
4. Expect **Catalog Demo Alpha** and **Catalog Demo Beta**, version 1.0.0. Select either: its native details and Install control should appear.
5. Click **Install**. Expect an installed indicator and **Manage** controls. The other package should remain available.
6. Select **Manage → Remove** and confirm. Expect the entry to remain in this source, with Install available again. No game assets are deleted; these are disposable documentation-only fixtures.
7. Search for `Beta`. Only Beta should remain visible. Clear the search.
8. Switch to **All Packages**, **Unity Registry**, and **Built-in**, then return. The catalogue should remain selectable without duplicate source rows.
9. Close Package Manager and reopen it using **Window → Package Management → Package Catalog**.
10. Trigger a script recompile or restart the Editor. Open the catalogue again; expect exactly one source and both entries. The adapter can return you to All Packages during teardown; reselect the source if necessary.
11. In settings, click **Disable integration**. Expect the source to disappear. Installed packages, if any, must remain installed in All Packages. Re-enable with **Use local demo catalogue**.
12. Remove demo packages after testing. Their temporary absolute local paths should disappear from the project's manifest/lock file.

The demo's source name is deliberately generic. To test your own label, copy the catalogue and its alpha/beta folders outside the public checkout, change `displayName`, and select that JSON in settings.

## Editor tests

Open **Window → General → Test Runner**, select **EditMode**, and run `UnityPackageCatalog.Tests`. Tests cover duplicate IDs, schema version, Git tag/subfolder preservation, credential-bearing URL rejection, local path resolution, local identity mismatch, and empty catalogues. They do not install packages or contact GitHub.

For CLI users with Pipeline installed, run `unity command run_tests --mode editor --filter UnityPackageCatalog --async_tests true --project-path <project>` and poll `unity command test_status --project-path <project>`.

## Private Git test — use a separate consumer project

Do not put private dependency URLs into the public development project's manifest.

1. Create a disposable Unity 6000.6 project and install this tool from disk (choose its `package.json`) or your published Git URL.
2. Create a small package in your own private Git repository. Its root (or selected subfolder) needs `package.json`; start with a README-only package. Set its name and version, and create a matching Git tag.
3. Confirm your Git client can access that repository non-interactively using SSH/credential-manager authentication. Do not put passwords or tokens in the catalogue.
4. In a separate private catalogue checkout, create the JSON described in README. Use the exact package name, version, Git URL, and tag.
5. Choose that JSON in Package Catalog Settings, enable integration, and Apply / Reload.
6. Open the catalogue. Select the uninstalled entry, inspect its metadata, and click the native Install button.
7. Verify the actual installed package's name, version and Git source. It must show up under All Packages too.
8. Use Manage → Remove. Expect the package to uninstall and remain available in the catalogue.
9. Restart Unity and repeat. Report any Console error or incorrect state.

If the same URL fails through Unity's normal **Install package from Git URL**, resolve the package/authentication problem first. If normal installation works but catalogue installation fails, that is an adapter issue.

## Reporting an issue

Include the exact Unity version, OS, settings diagnostic, and which numbered step failed. Redact private URLs, local personal paths, and credentials from logs/screenshots. Do not include private catalogues or project manifests in public issues.

Catalogue edits are checked while Package Manager is open (approximately every 0.75 seconds). Valid changes rebuild the source and its synthetic records after active package operations finish. Invalid edits keep the last valid catalogue active and show the error in settings; fix the JSON or use Apply / Reload to retry. Installed package version and source discrepancies are shown in settings diagnostics; the tool never replaces installed packages automatically. Git URL normalization can produce a source warning, so verify the installed source before acting.

## Asset Store catalogue checks (Unity 6000.6)

- Validation and file-update suite: 24 EditMode cases, including mixed Git/Asset Store entries, positive/unique product IDs, rejecting ambiguous UPM metadata, preserving Git entries, deduplicating selections, and preserving the file on invalid additions.
- In the disposable consumer, the signed-in account's owned native asset record appeared under the custom Sources entry with native Download enabled.
- The My Assets selection action added an owned item to a temporary external catalogue. No asset was downloaded or imported as part of this check.
- An already downloaded owned asset showed an enabled, visible native Import control under the custom source. Complete an actual download/import in a disposable project before release.
- Check sign-out/sign-in, an unowned or unavailable product ID, network failure/retry, hidden purchases, and removal/reimport before release. These lifecycle scenarios are not claimed as verified by validation tests.

## Built-in module requirement checks

- Full EditMode suite: 44 passed, including schema validation, required/excluded plans, unavailable modules, direct and indirect dependency conflicts, removing dependent modules together, the tool's JSONSerialize dependency, build enforcement, safe rule editing/clearing, and preservation of rules when adding Asset Store entries.
- An asynchronous discovery/preview test exercised Unity's real SearchAll/List APIs without mutating the development project's modules.
- Disposable consumer smoke test: excluded `com.unity.modules.vehicles`, verified it absent, required it again, and verified restoration through the catalogue Apply service and native UPM. The consumer manifest returned to its original contents. The apply service keeps the rule snapshot across domain reload and verifies the result rather than repeating the mutation.
- Manual UI check: browse Built-in, select modules, save required/excluded rules, preview the plan, apply it, and confirm the same native module records appear in the catalogue. Violate a rule with native controls and confirm the build check reports it. Clear the rule or disable integration and confirm the module state is not changed automatically.
- The supplied demo catalogue intentionally has no module rules, so trying the demo never changes engine modules.
