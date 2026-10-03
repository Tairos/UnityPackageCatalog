# Unity Package Catalog

A personal package catalogue inside **Unity's existing Package Manager**. Add a named source, browse your tools using Unity's native list and details panel, and install/remove packages using its normal controls. Packages can live in private Git repositories. No registry server is required.

**Experimental prototype — tested in Unity 6000.6.0f1.** The sidebar, filtering, and uninstalled package records use undocumented Unity internals. The adapter currently refuses other Unity minor versions. This is not a supported Unity extension API or a production compatibility guarantee.

## Try it in this project

1. Open this development project in Unity 6000.6.
2. Open **Window → Package Management → Package Catalog Settings**.
3. Click **Use local demo catalogue**.
4. Package Manager opens with **Example Packages** under **Sources**.
5. Select **Catalog Demo Alpha** or **Catalog Demo Beta**, then click Unity's **Install** button.
6. Remove it using **Manage → Remove**. It should remain available in the catalogue.

The two demo packages contain documentation only. Nothing installs until you click Install. The integration is disabled by default on a new machine/project. This development session may already have the demo enabled.

See [TESTING.md](TESTING.md) for the full checklist, verified results, and private Git testing.

## Install the tool in another project

This repository is a full development project with an embedded distributable package:

```text
Assets/                                  Development project content
Packages/io.github.tairos.unity-package-catalog/
  package.json
  Editor/                                Catalogue, preferences, native adapter
  Tests/Editor/                          Catalogue validation tests
  Samples~/LocalCatalog/                 Two local demo packages
ProjectSettings/
```

Source repository: [Tairos/UnityPackageCatalog](https://github.com/Tairos/UnityPackageCatalog).

Use **Package Manager → + → Install package from Git URL** (wording may be “Add package from Git URL”) with:

```text
https://github.com/Tairos/UnityPackageCatalog.git?path=/Packages/io.github.tairos.unity-package-catalog
```

Append `#YOUR-RELEASE-TAG` to pin a published release. The selected Git revision must contain the package at the path above. The consumer receives only the package subfolder. The Unity CLI/Pipeline package is a development dependency of this project, not a dependency of the distributed tool.

## Your private catalogue

Keep your catalogue in a **separate private repository**, outside this public development checkout. Clone it using your normal Git client, then choose its `catalog.json` in Package Catalog Settings. Enable the integration and click **Apply / Reload**, then **Open in Package Manager**.

Example format (the owner/repository below are placeholders):

```json
{
  "schemaVersion": 1,
  "displayName": "Tairos Packages",
  "packages": [
    {
      "name": "com.yourname.editor-tool",
      "displayName": "My Editor Tool",
      "description": "What this tool does.",
      "version": "1.0.0",
      "source": "ssh://git@github.com/YOUR-ACCOUNT/YOUR-PRIVATE-TOOL.git#v1.0.0"
    }
  ]
}
```

- `displayName` at the top sets the source label in Package Manager.
- Every Git/local package needs a unique UPM `name`, a `displayName`, a semantic `version`, and a `source`.
- `name` and `version` must match the target package's `package.json` at the chosen revision. Local entries are checked immediately; Git entries are your responsibility until fetched.
- Git sources accept `https://` or `ssh://` URLs, optionally with `git+`. Use `?path=/Packages/tool#v1.0.0` for a package in a repository subfolder.
- `file:./folder` supports local development packages; paths resolve relative to the catalogue JSON.
- `description` is optional. Uninstalled Git metadata comes from this file; this prototype does not fetch release history or repository metadata.
- An empty `packages` array is allowed.

The prototype reads a **local checkout**. Pull remote catalogue changes yourself; the local JSON refreshes while Package Manager is open. Automatic cloning, pulling remote catalogues, and GitHub repository discovery are not implemented.

### Authentication and privacy

Use your computer's existing Git credential manager or SSH agent. Unity must be able to access the Git repository without an interactive password prompt. Do not embed tokens/passwords in URLs or catalogue JSON. The tool rejects HTTPS user-info and SSH password syntax.

The catalogue path and enablement are stored in Unity EditorPrefs, keyed to the local project. They are not written to a tracked project settings file. Package Manager itself records installed dependency URLs in `Packages/manifest.json` and potentially lock/cache files, so **test private packages in a separate private/disposable consumer project**, not in this public development repository.

No private repository is bundled or accessed by the demo. Example URLs are fictional placeholders. The tool does not manage GitHub accounts or store credentials. Unity's normal Package Manager diagnostics/telemetry still apply.

## What is implemented

- Native extension page, moved into the existing **Sources** group.
- Native searchable/sortable package list and details view.
- Native Install and Manage → Remove controls.
- Synthetic metadata only for catalogue entries missing from Unity's package database.
- Real installed package records remain owned by Unity, including actual metadata and progress.
- Re-registration after compilation and window reopening; local enable/disable control.
- Validation tests and a credentials-free local demo.

## Prototype limits

- The integration uses reflection into Unity 6000.6 internals (`ExtensionPageArgs`, `PageManager`, `PackageDatabase`, and native version models). Unity updates can break it.
- Uninstalled entries contain only one catalogue-supplied version. Automatic updates, Git tag browsing, and changing the preferred source/version of an already installed package are not implemented. Remove and reinstall to deliberately switch source/revision.
- A package already known to Unity is shown with Unity's existing data; catalogue metadata does not overwrite it. Check the native Source/Installed From information if the same package ID exists elsewhere.
- Native labels such as “Installed From” may appear for available local/Git entries before installation. The Install/Manage controls and installed indicator determine actual state.
- The catalogue uses native buttons, not the original checkbox/batch-preset idea.
- Asset Store entries use Unity-owned records and its normal Download/Import controls. Built-in engine modules can be required/excluded through catalogue rules; registry hosting is not implemented.
- This milestone does not validate private GitHub credentials or claim compatibility beyond the tested Editor.

To disable, use **Package Catalog Settings → Disable integration**. That removes this tool's source and synthetic entries, not packages you installed. If an Editor update breaks the adapter, it stops and reports a diagnostic in settings/Console. See [the architecture notes](Documentation~/ARCHITECTURE.md) before porting it.

Catalogue edits are checked while Package Manager is open (approximately every 0.75 seconds). Valid changes rebuild the source and its synthetic records after active package operations finish. Invalid edits keep the last valid catalogue active and show the error in settings; fix the JSON or use Apply / Reload to retry. Installed package version and source discrepancies are shown in settings diagnostics; the tool never replaces installed packages automatically. Git URL normalization can produce a source warning, so verify the installed source before acting.

## Add your Asset Store purchases

1. Sign into Unity with the account that owns the assets.
2. Open **Package Catalog Settings**, then choose your private catalogue or **Create private catalogue…**. Keep it outside the public tool repository; the bundled demo cannot be edited by the selection action.
3. Click **Browse My Assets**. Use Unity's search to find and select one or more purchased/free assets.
4. Return to settings and click **Add selected My Assets items to catalogue**.
5. Click **Open in Package Manager**. Your catalogue now includes those assets alongside your Git/local packages.

Asset Store entries use a stable numeric product ID, for example this fictional placeholder:

```json
{ "assetStoreProductId": 12345, "displayName": "Example purchased asset" }
```

Omit `name`, `source` and `version` for these entries. The display name is optional and only a note in the catalogue; Unity supplies the displayed title, metadata and available version. Entries are filtered by purchase information for the signed-in account, including hidden purchases fetched by Unity. Missing ownership, sign-in and metadata failures appear in settings diagnostics. Use **Apply / Reload** to retry failed fetches.

This is a curated selection, rather than an automatic mirror of your entire account. Catalogue files contain references, not licensed asset files or credentials. Unity performs all entitlement checks, downloads and imports. Adding an entry never downloads or imports it automatically. Import opens Unity's usual asset import workflow; removal follows whichever native controls Unity provides for that asset rather than UPM dependency removal. Disabling the integration leaves Unity's asset records and imported content intact.

Package identity: `io.github.tairos.unity-package-catalog`. Maintained by [Tairos](https://github.com/Tairos). The lowercase reverse-domain identifier is distinct from the display name and the Git repository URL.

## Require or exclude built-in modules

In Package Catalog Settings, choose an external catalogue, click **Browse Built-in modules**, and select modules in Unity's native Built-in list. Back in settings, choose **Require selected Built-in modules** or **Exclude selected Built-in modules**. Use **Clear requirements for selected Built-in modules** to leave their state unmanaged again.

These actions save rules and enable the catalogue; they do not enable or disable modules immediately. Click **Check module requirements / preview changes**, then **Apply module requirements** to apply the valid plan through Unity's Package Manager. Ruled modules appear alongside packages in the catalogue using Unity's normal details and Enable/Disable controls.

Rules are optional top-level JSON metadata and can coexist with Git/local and Asset Store entries:

```json
"builtInModules": [
  { "name": "com.unity.modules.audio", "requirement": "required" },
  { "name": "com.unity.modules.vehicles", "requirement": "excluded" }
]
```

`required` means enabled; `excluded` means absent from the resolved dependency graph, including indirect dependencies. Only built-in engine IDs under `com.unity.modules.*` are supported. Duplicate/conflicting rules and unknown requirements are rejected. The installed Editor supplies the module version.

The tool reports conflicts before applying anything when a retained package or another required module needs an excluded module. Exclude dependent built-in modules together where appropriate; other dependent packages must be managed separately. This tool needs JSONSerialize and declares it as a dependency, so `com.unity.modules.jsonserialize` cannot be excluded while the tool is installed.

Native Enable/Disable controls remain available. When the active catalogue is enabled, a build check stops builds if its required/excluded rules are violated. Loading, reloading, or editing a catalogue never applies module changes automatically. Disabling the catalogue stops enforcement and leaves module states unchanged. Settings remain local to this computer/project; CI or another user's Editor must configure and enable the intended catalogue too. Disabling an engine module can make project scripts that use its APIs fail compilation, so preview the dependency plan and test changes in a disposable project first.
