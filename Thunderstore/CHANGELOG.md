# Changelog

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
