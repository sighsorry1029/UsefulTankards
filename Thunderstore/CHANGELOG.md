# Changelog

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
