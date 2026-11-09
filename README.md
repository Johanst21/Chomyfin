# Chomyfin

<p align="center">
  <img src="ThumbImage.jpg" alt="Chomyfin Plugin" width="200"/>
</p>

<p align="center">
  <a href="https://www.gnu.org/licenses/gpl-3.0"><img src="https://img.shields.io/badge/License-GPLv3-blue.svg" alt="License: GPL v3"></a>
  <a href="https://github.com/Johanst21/Chomyfin/releases"><img src="https://img.shields.io/github/v/release/Johanst21/Chomyfin?color=green" alt="Latest Release"></a>
</p>

An Emby Server plugin (4.10.x) that integrates and manages content directly from Chomikuj.pl, allowing you to browse folders, synchronize `.strm` files, and stream media.

**Status:** Version `v1.2.0.0` — Custom Resolution Controls & UI Overhaul.  
This release introduces advanced resolution controls, fine-grained concurrency and sibling limiting, cleaned and organized configuration UI, and expanded test suite coverage (74 passing tests).

## Features
*   **Chomikuj Integration:** Browse your Chomikuj account folders directly.
*   **`.strm` Synchronization:** Scheduled task to generate and update `.strm` files pointing to your local Emby library with atomic writes.
*   **Direct Playback:** Custom `IMediaSourceProvider` adapted for Emby Server 4.10.x.
*   **Advanced Playback Controls (v1.2.0.0):**
    *   **Resolve Only First Link:** Option to resolve only the primary URL for instant playback start, bypassing sibling checks and slower paths.
    *   **Sibling Version Controls:** Configurable limit toggle (`Enable Sibling Limit`) and custom sibling threshold (`Max Siblings per Open`, 1–20) for background resolution.
    *   **Concurrency Rate Limiter:** Protect against Chomikuj rate-limiting with `Enable Concurrency Limit` and customizable worker limits (`Max Concurrent Resolutions`, 1–20).
    *   **Fast Resolution Path:** Direct fast-path resolver when URLs contain file IDs, now opt-in (`false` by default) with automatic classic fallback.
*   **Clean Settings UI:** Reorganized into logical sections (Credentials, Storage, Playback, Cleanup, Status) with hidden backward-compatibility aliases.
*   **Self-contained:** Compiled for `netstandard2.0` into a single DLL with no unnecessary external dependencies.
*   **Security & Stability:** Path traversal sanitization, orphan dry-run protection, admin-only endpoints, background sibling resolution, and thread-safe file operations.

## Requirements
*   **Emby Server:** 4.10.x or higher.
*   **.NET SDK:** 8.0 or higher (for compilation).
*   **Target Framework:** `netstandard2.0` (standard required by the Emby plugin portal).

## Compilation & Tests
To build the plugin in Release mode:
```bash
dotnet build Chomyfin.csproj -c Release
```
The generated library will be located at:
```
bin/Release/netstandard2.0/Chomyfin.dll
```

To run the automated test suite (74 tests):
```bash
dotnet run --project Chomyfin.Tests/Chomyfin.Tests.csproj
```

## Installation
1. Stop the Emby server.
2. Copy the generated `Chomyfin.dll` into the Emby plugins directory:
   *   **Linux**: `/var/lib/emby/plugins/` (or your Emby profile path).
   *   **Windows**: `%AppData%\Emby-Server\programdata\plugins\`
3. Start Emby Server.
4. Go to **Emby Admin Dashboard > Plugins** to verify and configure Chomyfin credentials and settings.

## Releases
All releases and binary downloads are available at:
https://github.com/Johanst21/Chomyfin/releases

## License
This project is licensed under the terms of the GNU General Public License v3.0 (GPL-3.0). See the [LICENSE](LICENSE) file for the full license text.
