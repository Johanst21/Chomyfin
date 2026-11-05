# Chomyfin

<p align="center">
  <img src="ThumbImage.jpg" alt="Chomyfin Plugin" width="200"/>
</p>

<p align="center">
  <a href="https://www.gnu.org/licenses/gpl-3.0"><img src="https://img.shields.io/badge/License-GPLv3-blue.svg" alt="License: GPL v3"></a>
  <a href="https://github.com/Johanst21/Chomyfin/releases/tag/v1.0.1.0"><img src="https://img.shields.io/badge/Release-v1.0.1.0-green.svg" alt="Release v1.0.1.0"></a>
</p>

An Emby Server plugin (4.10.x) that integrates and manages content directly from Chomikuj.pl, allowing you to browse folders, synchronize `.strm` files, and stream media.

**Status:** Version `v1.0.1.0` — Bug Fixes Release.  
This release fixes episode sibling resolution (supports `1x01`, `T1E1`, `SxxExx`, and flat layouts, with deterministic ordering and no cross-series mixing) and improves orphan cleanup for `.original` files.

## Features
*   **Chomikuj Integration:** Browse your Chomikuj account folders directly.
*   **`.strm` Synchronization:** Scheduled task to generate and update `.strm` files pointing to your local Emby library with atomic writes.
*   **Direct Playback:** Custom `IMediaSourceProvider` adapted for Emby Server 4.10.x.
*   **Self-contained:** Compiled for `netstandard2.0` into a single DLL with no unnecessary external dependencies.
*   **Security & Stability:** Path traversal sanitization, orphan dry-run protection, admin-only endpoints, and background sibling resolution.

## Requirements
*   **Emby Server:** 4.10.x or higher.
*   **.NET SDK:** 8.0 or higher (for compilation).
*   **Target Framework:** `netstandard2.0` (standard required by the Emby plugin portal).

## Compilation
To build the plugin in Release mode:
```bash
dotnet build Chomyfin.csproj -c Release
```
The generated library will be located at:
```
bin/Release/netstandard2.0/Chomyfin.dll
```

## Installation
1. Stop the Emby server.
2. Copy the generated `Chomyfin.dll` into the Emby plugins directory:
   *   **Linux**: `/var/lib/emby/plugins/` (or your Emby profile path).
   *   **Windows**: `%AppData%\Emby-Server\programdata\plugins\`
3. Start Emby Server.
4. Go to **Emby Admin Dashboard > Plugins** to verify and configure Chomyfin credentials and settings.

## Releases
*   **Chomyfin v1.0.1.0**: https://github.com/Johanst21/Chomyfin/releases/tag/v1.0.1.0
*   **Chomyfin v1.0.0.0**: https://github.com/Johanst21/Chomyfin/releases/tag/v1.0.0.0

## License
This project is licensed under the terms of the GNU General Public License v3.0 (GPL-3.0). See the [LICENSE](LICENSE) file for the full license text.
