# Changelog
Full EOSTransport Changelog.

## [3.0.0] - 2026-03-??
First major release of EOSTransport.
### Added
- Added support on Android for 2021.3.41f1 and beyond (any version that has JDK 11 support), instead of 6000.0.38f1 and beyond.
- Added new Player Data Storage/Title Storage Utility script (`DataStorageUtility.cs`), so that both PDS/TS can be used much easier than before.

### Changed
- Updated EOS SDK to 1.19.0.3.
- Removed 4-character lobby name cap by adding blank spaces only when the lobby id string is below 4 characters.

### Fixed
- Fixed an EOSSDKException that would happen when you were using Epic Portal login and no "DisplayName" was provided.

## [3.0.0b3] - 2026-01-10
### Fixed
- Fixed compiler error when Windows Server Unity Module was not installed.
- Fixed compiler error when using Mirror v96 and `MIRROR_90_OR_NEWER` was not added as a define symbol.

## [3.0.0b2] - 2025-12-28
### Changed
- Changed `EOSTransport.PromoteMember()` to a void, now with a callback instead to return a success

### Fixed
- Improved Host Migration by a ton.
- Fixed shutdown disposal issues.

## [3.0.0b1] - 2025-12-04
- Initial Release