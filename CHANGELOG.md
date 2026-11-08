# Changelog

All notable changes to this project will be documented in this file.

## [v1.1.0.0] - 2026-11-08

### Quality Improvements
- Code quality improvements, 0 compiler warnings, thread-safe AssignStrmFileNames, and 3 new regression tests.

## [v1.0.1.0] - 2026-11-05

### Bug Fixes
- Fixed episode sibling resolution (supports 1x01, T1E1, flat layouts, and deterministic ordering) and improved orphan cleanup for .original files.

## [v1.0.0.0] - 2026-11-05

### Production Ready Release
- **Technical Debt (P2)**:
  - `SiblingFinder`: replaced `int.Parse` with `int.TryParse` so corrupt remote IDs (SxxExx patterns) are handled gracefully instead of throwing.
  - `StrmGenerator`: introduced dedicated `StrmGeneratorAbortException` to replace message-based exception filtering (`"[StrmGenerator] Abortando..."`).
  - `ChomikBoxClient`: `_dumpCounter` is now per-instance (non-static) so the diagnostics dump counter no longer sticks globally across executions.
  - `StrmGenerator.CleanOrphans`: orphan cleanup errors now go through the Emby logger instead of `Console.WriteLine`.
- **SMB/NFS Network Filesystem Support**:
  - `AtomicFile`: temporary files now use a `.tmp` suffix (`filename.ext.{guid}.tmp`).
  - `AtomicFile`: `File.Replace` failures (`IOException` / `PlatformNotSupportedException` on SMB/NFS shares) are handled with a safe `File.Delete` + `File.Move` fallback.

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
