# Testing Unity Package Catalog

Current automated run: **55 EditMode tests passed, 0 failed** on **Unity 6000.6.0f1**. Other Unity minor versions are not supported by the current Package Manager adapter.

## Verified behaviour

| Check | Evidence |
|---|---|
| Catalogue schema, IDs, sources, versions, and local manifest validation | Automated tests |
| Plain `.json` rejection; discovery, move/delete, and GUID stability | Automated tests |
| Invalid edits retain the last valid source set; conflicting package definitions rejected | Automated tests |
| `.upcbuiltinjson` validation, opposing preset independence, availability, version notice, and dependency planning | Automated tests |
| Unlisted modules unchanged by the planner; preset preview works with catalogue integration disabled | Automated tests and live preview |
| Importer/main-asset MonoScript references; inline Inspector controls, save, and dirty-state tracking | Automated attached-panel tests and live Inspector checks |
| Sidebar rebuild preserves one catalogue row under Sources | Regression test and live undock/recompile check |
| Mixed Git/local/registry/Asset Store catalogue with native groups, details, Install, and Download controls | Live disposable consumer; README screenshots |
| Discovery of a catalogue inside an installed package | Live consumer import/discovery check, then temporary files removed |
| Multiple source entries, Preferences asset selection, scrolling, and recompile/reopen recovery | Live consumer; earlier implementation checks |
| Local package Install/Remove; removed package remains listed | Earlier live consumer checks |
| Private GitHub installation through SSH, removal, and reinstall | Earlier prototype checks |
| Owned Asset Store purchase picker, filtering/paging, and selection retention | Earlier live checks; README picker capture |
| Native Import control for an already downloaded owned product | Earlier live UI check; complete download/import lifecycle remains a release follow-up |
| Built-in mutation and reload verification | Earlier service flow tested by disabling/restoring Vehicles; current independent-preset preview and planner are verified |

These results describe exercised behaviour, not support for every Unity version or all authentication/network states. Screenshot preparation did not install packages, download Asset Store content, or apply engine-module changes.

## Development example

Open the repository's development project in Unity 6000.6.0f1. `Assets/PackageCatalogs/Example Packages.upcjson` references two harmless documentation-only local packages. In Package Manager, choose **Sources → Example Packages**.

1. Select Alpha/Beta and verify native details and Install.
2. Install a demo, then use **Manage → Remove**. It should remain listed and available to reinstall.
3. Search for one name, clear the search, switch sources, undock/redock, and close/reopen Package Manager. Each catalogue should appear once under Sources.
4. Select the catalogue in the Project window. Editing should happen in the Inspector without a missing-script warning or separate catalogue window.
5. Turn **Show catalogues in Package Manager** off/on in Package Catalog Settings. Existing installed packages should remain installed.
6. Remove demo packages after testing so temporary local paths do not remain in the public project's manifest/lock file.

Consumers can also import the **Local catalogue smoke test** sample from Package Manager. Import its catalogue and both fixture folders together so relative sources resolve.

## Authoring and preset checks

Use a disposable consumer for installation/module changes.

- Create two `.upcjson` assets. Add Git, local, Unity Registry, and owned Asset Store entries using the Inspector. Check duplicate selection prevention, Save, Revert, external-edit detection, and unsaved-change prompts when changing selection.
- Save malformed JSON and mismatched local package IDs/versions. The asset should remain selectable for repair; the native source set should retain its last valid state.
- Create opposing `.upcbuiltinjson` presets. Both should be valid independent assets, with no custom Package Manager source or build enforcement.
- Preview modules unavailable in the current Editor and modules needed by retained dependencies; expect clear errors. Preview must not change the manifest.
- Apply a harmless preset in the disposable project, wait for resolution/recompile, verify its result through Built-in, then restore the original module state with another explicit preset.
- Try catalogue files distributed inside an installed collection package; only imported assets outside ignored folders should appear.

## Automated tests

In Unity, open **Window → General → Test Runner**, choose **EditMode**, and run `UnityPackageCatalog.Tests`.

If Unity CLI is installed, run against the development project:

```sh
unity test /path/to/UnityPackageCatalog --mode EditMode --output /tmp/upc-tests.xml --timeout 240
```

Keep generated XML/logs and test-only catalogues out of the public repository. Automated tests use documentation-only fixtures and public Search/List for module preview; they do not install private Git packages or download/import Asset Store content.

## Release follow-ups

- Full Editor restart and recovery with current file types.
- Installation through the published Git URL into another clean consumer and, separately, another machine.
- Current independent Built-in preset Apply/restore, including recompilation.
- Complete Asset Store download/import in a disposable project; signed-out, unowned, unavailable/hidden product, network failure, and retry states.
- Explicit compatibility testing before introducing adapters for other Unity minor versions.

For issues, include exact Unity/OS versions, reproduction steps, expected/actual behaviour, and redacted Console diagnostics. Do not attach private catalogues, credential-bearing URLs, or personal manifests.
