# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 2.0.4

### Changed
- Removed entity ID from destroyed objects when not using `/rat`
- Localized `/rat` command

### Fixed
- `/rat` command now works

## 2.0.3

### Added
- `/rat` command to test raid alerts.
  - Requires `poundbotraidalerts.test` permission to use.

### Changed
- Changed permission name to `poundbotraidalerts.alert` so it works

## 2.0.2

### Added
 - New permissions system, permitted users are now controlled by the `poundbot.raidalerts` permission.

## 2.0.1

### Changed
- Removed debug location message

## 2.0.0

### Changed
- Uses new PoundBot 2.0 API
