# Architecture

## Catalogue assets and authoring

`UpcCatalogImporter` registers `.upcjson` files as editor-only `UpcCatalogAsset` objects with a catalogue icon. Malformed files remain importable so the custom editor can repair them. `CatalogDocument` validates extension, schema, unique IDs, semantic versions, local manifests, Git URLs; it rejects mixed module data. Writes validate the complete document and replace the source file atomically.

`CatalogRegistry` discovers imported project catalogue assets through AssetDatabase. Asset GUIDs form native page identities. There is no external path preference or plain `.json` fallback. Preferences lists every discovered asset, including invalid ones, with selection/ping and edit actions. It contains no package-state diagnostic list or selection-transfer workflow.

`UpcCatalogEditor` edits a draft, checks for external changes before save, and provides Save/Revert. Custom `ScriptedImporterEditor` inspectors host the asset editors directly, hide the duplicate imported-object section, and track unsaved drafts through Inspector Save/Discard hooks. Each importer has a matching script filename so Unity can resolve its MonoScript. Double-clicking selects the asset and focuses the Inspector. Preset Built-in and catalogue registry pickers use the public Package Manager Search API. The owned Asset Store picker uses Unity's purchase-list service, native authentication and purchase cache; it paginates searches and preserves checkbox selection across pages/filters. Picker callbacks modify only the current draft. All Unity-internal reflection stays in `NativeCatalogBridge`.

## Native Package Manager adapter

Each catalogue registers its own `ExtensionPageArgs` filter and group-name callback. Rows move into the existing Sources foldout. Git/local entries missing from Unity's database receive a synthetic uninstalled package/version record. Unity owns installed records, operations, registry metadata, Asset Store metadata, and Download/Import controls. Synthetic records are removed only when the database still contains the exact object created by this tool.

The single-version `UpmVersionList` is allocated without its normal constructor, which expects registry/cache data. The version list and installed/recommended/update indices are populated explicitly. This uses unsupported internals and is isolated to the Unity 6000.6 adapter.

Source refresh waits for active package operations, preserves active page identity, and retains the last valid source set if files become invalid. Duplicate package IDs across catalogues must agree on source and version because Unity shares one installed package set across the project. Native group labels default to Git, Local, UPM, and Asset Store; package group overrides are supported.

## Built-in presets

`UpcBuiltInPresetImporter` imports `.upcbuiltinjson` assets separately. `BuiltInPresetDocument` validates schema and module actions; its recorded Editor version is informative, while public Search/List APIs supply current availability and dependencies. The preset Inspector provides draft editing, searchable module selection, Preview, and Apply. Preferences lists preset assets independently from package catalogues.

Operations accept one explicit preset path and never merge other assets. The dependency planner accounts for direct and indirect installed dependencies and checks exclusions against retained packages. Explicit Apply uses `Client.AddAndRemove`; unlisted modules are unchanged. A SessionState snapshot allows verification after module-triggered domain reloads without repeating mutations. There is no build preprocessor or persistent enforcement. Opposing presets are valid independent actions; only conflicting rules inside one preset are rejected.

The global integration switch controls package Sources only; presets remain usable independently. The package depends on `com.unity.modules.jsonserialize` to protect its JSON reader from exclusion.

## Compatibility boundary

The adapter currently accepts Unity 6000.6. Internal service names, extension fields, and version models can change in other Editor versions. Unsupported versions stop the adapter with a diagnostic; compatibility requires explicit testing and adapters. Automatic remote catalogue cloning/updating is not implemented.
