# Chomyfin

An Emby Server plugin (4.10.x) that integrates and manages content directly from Chomikuj.pl, allowing you to browse folders, synchronize `.strm` files, and stream media.

**Status:** Initial Beta Release (`v0.8.7.0-beta`). 
This version represents a stable, functional baseline prior to applying security and performance audit patches.

## Features
*   **Chomikuj Integration:** Browse your Chomikuj account folders directly.
*   **`.strm` Synchronization:** Scheduled task to generate and update `.strm` files pointing to your local Emby library.
*   **Direct Playback:** Custom `IMediaSourceProvider` adapted for Emby Server 4.10.x.
*   **Self-contained:** Compiled for `netstandard2.0` into a single DLL with no unnecessary external dependencies.

## Requirements
*   **Emby Server:** 4.10.x or higher.
*   **.NET SDK:** 8.0 or higher (for compilation).
*   **Target Framework:** `netstandard2.0` (standard required by the Emby plugin portal).

## Compilation
*   To build the plugin in Release mode:
*   The generated library will be located at: bin/Release/netstandard2.0/Chomyfin.dll

## Installation
*   Stop the Emby server.
*   Copy the generated Chomyfin.dll into the Emby plugins directory:
     *   Linux: /var/lib/emby/plugins/ (or your Emby profile path).
     *   Windows: %AppData%\Emby-Server\programdata\plugins\
*   Start Emby Server.
*   Go to Emby Admin Dashboard > Plugins to verify and configure Chomyfin credentials and settings.

## License
*   Distributed under the project's software use license.
