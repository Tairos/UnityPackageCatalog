# Native adapter notes

Target inspected and exercised: Unity 6000.6.0f1. No Unity reference-source implementation is copied into this package.

`CatalogDocument` validates explicit catalogue metadata and resolves local sources relative to the catalogue. `CatalogSettings` stores only a local location and enablement in EditorPrefs. The private repository is cloned/updated by the user, not by this prototype.

`NativeCatalogBridge` contains all reflection dependencies. On demand while Package Manager is open, it resolves native services and registers `ExtensionPageArgs` with a package-ID filter. Unity creates its own `ExtensionPage`, list, selection, search, sorting, details and action controls. Extension rows normally go under Cloud; the existing native row is reparented beneath the foldout containing the Built-in row, without relying on localized text.

Missing package IDs get a native `Package`/`UpmVersionList`/`UpmPackageVersion` record carrying catalogue metadata and `name@source` as its installation ID. The package has no installed version. Native actions dispatch the install/remove requests through Unity's Package Manager. Once installed, Unity replaces the synthetic record with its real record. No manifest editing or replacement install button is used.

Synthetic records are marked non-discoverable, Git/local, and uninstalled so they don't look like registry packages or installed dependencies. The adapter tracks exact object identities and removes only records it owns during teardown. Real packages are never removed by disabling the integration. The native operation dispatcher gates re-injection while installs/removals run.

An Editor update can change constructors, fields, service contracts, serialized data or sidebar structure. Type/method/field errors stop the adapter and surface a diagnostic; settings remain available to disable it. Registration is rebuilt after domain reload, so private catalogue metadata is not a package asset.

The single-version `UpmVersionList` is allocated without its normal constructor because that constructor expects registry/cache data for a registry-backed package. Its list and installed/recommended/update indices are initialized explicitly. This is a deliberate proof-of-concept technique, not a public API guarantee.

Catalogue JSON content is checked on each adapter tick while Package Manager is open. Valid edits rebuild the native page after active package operations finish; invalid or unreadable edits preserve the last valid document and report the error. Settings diagnostics compare registered package identity, version and source with catalogue entries. Git source comparisons are conservative because Unity can normalize locators.

Future work: authentication-friendly remote catalogue sync, more Editor adapters, Git version history, integration regression tests, and a release/license decision before publishing.

Asset Store entries carry `assetStoreProductId` instead of UPM identity/source/version. The adapter requests purchase/product metadata through Unity's `IAssetStoreClient.ExtraFetch`, filters native product records by ID and cached ownership, and never constructs synthetic Asset Store versions. Unity creates and updates its own records and supplies its download/import actions. Source refresh options include local-download and imported-asset scans. Settings can append the native My Assets selection to an external catalogue, validate the complete document, and replace the file atomically. No account tokens or purchased asset content are serialized by the tool.
