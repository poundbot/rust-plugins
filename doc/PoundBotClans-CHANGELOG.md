# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 2.0.1

### Fixes
- Fixed sending clans. Some clan plugins don't make them available during
  the `OnClanCreate` callback, so set a 1s timer before they are sent.
  Also working around a bug in PB that doesn't handle adding clans
  correctly. There will be another update when PB is updated to handle
  this properly.

## 2.0.0

### Changes
- Uses new PoundBot 2.0 API
