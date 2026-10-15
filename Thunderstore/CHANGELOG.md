# Changelog

## 1.0.9

- Updated tankard storage for Valheim 1.0.20's item save format 110, while retaining support for format 109.
- Matched the game's version-specific cheated-item flag handling so valid stored meads are not rejected when reopening a tankard.
- Retained item identity, quantity, and metadata validation and preservation of original storage data on failure. Added regression checks for both supported formats and repeated save/reopen cycles.

## 1.0.8

- Fixed a false durability mismatch that could prevent filled tankards from reopening under Mono's extended-precision arithmetic.
- Compare restored durability as rounded single-precision values while retaining checks for actual item changes and preserving the original storage data on failure.
- Added isolated checks using the original game item reader and client/server Mono runtimes, including repeated save/reopen cycles and changed or invalid durability values.
- Write package manifests as UTF-8 without a byte-order mark.

## 1.0.7

- Validate each stored mead's identity, quantity, quality, variant, crafter information, and custom data after loading, including other persisted item fields.
- Preserve the original storage data and block opening or consuming its contents when restoration loses or changes item information, even if item counts and total quantities still match.
- Show a localized warning when storage cannot be restored safely and log the differing field. Normal item ordering, custom-data ordering, and supported game normalization remain accepted.
- Added metadata-preservation regression coverage and expanded checks against the original Valheim item reader. This protection does not recover storage data already removed by another process.
- Updated the packaged BepInEx dependency to `denikson-BepInExPack_Valheim-5.4.2351`.

## 1.0.6

- Added optional EpicLoot 0.14.5 input compatibility so Use opens tankard storage before EpicLoot consumes the key.
- Preserved EpicLoot socket modifier and confirmation-prompt behavior, and safely closes the socket UI before opening tankard storage.
- Kept tankard tooltip hints orange when EpicLoot rewrites named color tags.
- Added real Harmony class-processing checks to catch effective patch-order regressions.

## 1.0.5

- Updated Harmony targets and private game access for Valheim 1.0.7, including the new tooltip, inventory insertion, crafting refresh, and inventory change signatures.
- Updated tankard storage parsing for Valheim's compact v109 inventory format while preserving unsupported or incomplete payloads without rewriting them.
- Rebuilt the bundled ServerSync library against the original Valheim 1.0.7 assemblies to support the new `ZRoutedRpc.Everybody` constant and current admin checks.
- Switched compilation and ILRepack resolution from publicized assemblies to the original game assemblies.
- Updated the packaged BepInEx dependency to `denikson-BepInExPack_Valheim-5.4.2350` and added original-game contract checks for client and dedicated-server APIs.

## 1.0.4

- Added opt-in local deployment for Debug builds with DeployToGame=true, copying only the final ServerSync-merged plugin DLL after successful post-processing.
- Added DeployToGameDirectory for custom plugin destinations and documented the Debug build and deployment workflow.
- Kept local Debug deployment separate from Release packaging and version publishing.

## 1.0.3

- Reused an open tankard inventory for drinking and tooltip queries, preventing its old contents from overwriting a drink consumption when the storage closes.
- Checked the attacking player's ownership and tankard inventory membership before consuming stored drinks, including after inventory callbacks.
- Routed tankard quick-stacking through the local inventory UI instead of world-container RPCs.
- Consolidated storage registration and skipped unused weight calculations during storage validation.
- Detached configuration callbacks during plugin cleanup and prevented duplicate subscriptions when profiles are registered again.
- Fixed builds that specify only GamePath while preserving individually overridden dependency paths.
- Added storage regression checks for saved-data preservation, live inventory consumption, ownership changes, and cleanup.

## 1.0.2

- Prevented stored-mead weight checks from instantiating item prefabs and consuming another mod's pending item-upgrade data.
- Cached stored drink weight in tankard metadata, with automatic non-instantiating migration for existing filled tankards.
- Removed repeated storage reconstruction from routine carry-weight calculations.

## 1.0.1

- Hardened tankard storage loading and lifecycle cleanup to preserve stored meads across missing data, item drops, and UI transitions.
- Fixed tankard use-context, animation cleanup, and recipe validation edge cases.
- Simplified configuration, localization, and patch wiring while aligning documentation with actual behavior.
- Updated Release packaging to derive the package version from the DLL, sync the manifest, and package the root README.

## 1.0.0

- Initial release
