# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 2.0.1

### Added
- Added full request body to DebugURI

### Fixed
- Fixed lang file for `/pbreg` usage. In `lang/en/PoundBot.json`, `usage.pbreg` should now be:

  `Usage: {0} \"<discord name>\"\n Example: {0} \"Fancy Guy#8080\"`

## 2.0.0

### Added
- `/pbreg check` command to allow players to check if they're registered.
- `pb.set_debug_uri` console command to help with debugging issues
- `pb.set_api_key` console command to set the api key without restarting PoundBot

### Changed
- All API URIs and calls to the PoundBot API are now in `PoundBot.cs`
  This means all PoundBot plugins must be updated to use the new API.

### Removed
- `API_(Get|Post|Put|Delete)` methods

### Fixes
- `/pbreg` command now expects properly formatted discord names, and shows usage if it is incorrect.
- `/pbreg` registration attempts are logged to the console.
