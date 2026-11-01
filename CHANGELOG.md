# Changelog

All notable changes to this project will be documented in this file.

## [v0.9.3.0] - 2026-11-01

### Security Audit (P0/P1)
- **Cookies Removed**: Removed sensitive session cookies and credential exposure in logs and client responses.
- **Admin Endpoints**: Secured all sensitive plugin API endpoints, restricting access strictly to Emby administrators.
- **Sanitizer**: Hardened path traversal and filename sanitizer against malicious or crafted folder/file names.
- **Orphans DryRun**: Introduced dry-run protection mode for orphan file deletion to prevent unintended media loss.
- **Atomic Writes**: Implemented atomic writes for generated `.strm` files using temporary files and atomic replacement to prevent file corruption during sync.
- **Background Sibling Resolution**: Offloaded sibling item resolution and remote calls to asynchronous background tasks, preventing UI thread blocking and sync stalls.

### UI
- **Hardcoded English UI**: Transitioned configuration interface to clean, hardcoded English (removed i18n resources to resolve persistent Emby dashboard caching glitches).
- **Last Sync Status**: Added a read-only "Last Sync Status" indicator at the bottom of the plugin configuration page.

### Migration Notes
- **CustomScanPath**: Users must review and re-enter `CustomScanPath` if migrating from earlier versions.
- **OrphansDryRun**: Please review the `OrphansDryRun` setting in configuration; verify orphan items before disabling dry-run mode.
