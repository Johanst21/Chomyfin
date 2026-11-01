using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Chomyfin.Models;
using Chomyfin.Services;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.Logging;

namespace Chomyfin.Configuration
{
    public class ChomyfinOptions : EditableOptionsBase
    {
        public ChomyfinOptions()
        {
            MaxConcurrentResolutions = 3;
            EnableCookieCache = true;
            CookieCacheDurationMinutes = 30;

            // Valores por defecto Tarea 2
            SyncMode = StrmSyncMode.FullAccount;
            CustomScanPath = string.Empty;
            AllowedVideoExtensions = "mkv,mp4,avi,m4v,mov,wmv,ts,mpg,mpeg,flv,webm";
            OverwritePolicy = StrmOverwritePolicy.SkipIfExists;
            DeleteOrphans = false;
            OrphansDryRun = true;
            AutoSyncEnabled = false;
            RefreshLibraryAfterSync = false;
            EnableSoapDiagnostics = false;

            // Opciones Tarea 1: Versiones hermanas
            ResolveSiblingVersions = true;
            MaxSiblingsPerOpen = 8;

            // Mejora 3: Vía rápida de resolución
            UseFastResolvePath = true;

            LastSyncStatus = ChomyfinServices.SyncStatusStore?.GetSummaryString() ?? "No synchronization has been run yet.";
        }

        public override string EditorTitle => "Chomyfin Options";

        public override string EditorDescription => "Configure Chomikuj.pl access, .strm file synchronization, and advanced settings.";

        // --- Credenciales y acceso web ---

        [DisplayName("Chomikuj Username")]
        [Description("Your Chomikuj username or email address.")]
        public string Username { get; set; }

        [DisplayName("Chomikuj Password")]
        [Description("Your Chomikuj account password (hidden in the interface).")]
        [IsPassword]
        public string Password { get; set; }

        [DisplayName("Protected Folders Password (Optional)")]
        [Description("Password to access private protected folders such as 'Prywatne'.")]
        [IsPassword]
        public string FolderPassword { get; set; }

        // --- Generación y sincronización de .strm (Tarea 2) ---

        [DisplayName("STRM Destination Folder")]
        [Description("Local directory on the server where the .strm library will be generated.")]
        [EditFolderPicker]
        public string StrmDestinationFolder { get; set; }

        [DisplayName("Sync Mode")]
        [Description("Choose whether to synchronize the entire Chomikuj account or only the specified folders.")]
        public StrmSyncMode SyncMode { get; set; }

        [DisplayName("Selected Chomikuj Folders")]
        [Description("Comma or semicolon separated paths (e.g. /Prywatne/Servers/Chomyfin/, /Other/Path).")]
        public string CustomScanPath { get; set; } = string.Empty;

        [DisplayName("Allowed Video Extensions")]
        [Description("Comma-separated list of extensions (without dot). Only files with these extensions will generate a .strm file.")]
        public string AllowedVideoExtensions { get; set; }

        [DisplayName("STRM Overwrite Policy")]
        [Description("Skip if the .strm file already exists (recommended) or update if the Chomikuj file URL has changed.")]
        public StrmOverwritePolicy OverwritePolicy { get; set; }

        [DisplayName("Delete Orphans")]
        [Description("If enabled, .strm files in the destination folder that are no longer present on Chomikuj will be deleted.")]
        public bool DeleteOrphans { get; set; }

        [DisplayName("Orphans: Dry Run Only")]
        [Description("While enabled, only logs which orphan .strm files would be deleted without removing them from disk.")]
        public bool OrphansDryRun { get; set; }

        [DisplayName("Enable Automatic Scheduled Sync")]
        [Description("Enables recurring automatic execution of the sync task. If disabled, you can always run sync manually from Emby Scheduled Tasks.")]
        public bool AutoSyncEnabled { get; set; }

        [DisplayName("Refresh Emby Library After Sync")]
        [Description("Notifies Emby about added or deleted files when synchronization finishes.")]
        public bool RefreshLibraryAfterSync { get; set; }

        [DisplayName("Enable SOAP Diagnostics")]
        [Description("Logs raw XML from the SOAP API; for debugging purposes only.")]
        public bool EnableSoapDiagnostics { get; set; }

        // --- Rendimiento y caché ---

        [DisplayName("Max Concurrent Resolutions")]
        [Description("Maximum number of simultaneous downloads or resolutions (1-10).")]
        public int MaxConcurrentResolutions { get; set; }

        [DisplayName("Enable Cookie Cache")]
        [Description("Maintains an active Chomikuj.pl session to avoid repeated logins.")]
        public bool EnableCookieCache { get; set; }

        [DisplayName("Cookie Cache Duration (Minutes)")]
        [Description("Session expiration time in minutes.")]
        public int CookieCacheDurationMinutes { get; set; }

        // --- Versiones hermanas (Tarea 1) ---

        [DisplayName("Resolve Sibling Versions on Open")]
        [Description("When opening a movie or episode detail, resolves and caches alternative qualities in the background for instant playback.")]
        public bool ResolveSiblingVersions { get; set; }

        [DisplayName("Sibling Versions Limit per Open")]
        [Description("Maximum number of alternative .strm files queued for background resolution on open (1-20).")]
        public int MaxSiblingsPerOpen { get; set; }

        // --- Mejora 3: Vía rápida de resolución ---

        [DisplayName("Fast Resolution Path")]
        [Description("Fast resolution path: uses a single request when the URL already contains the file id; falls back to the classic method if it fails.")]
        public bool UseFastResolvePath { get; set; }

        [DisplayName("Last Sync Status")]
        [Description("Shows the summary and result of the most recent synchronization task.")]
        [ReadOnly(true)]
        public string LastSyncStatus { get; set; }

        public List<string> GetSelectedFoldersList()
        {
            if (string.IsNullOrWhiteSpace(CustomScanPath)) return new List<string>();
            return CustomScanPath.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f) && !string.Equals(f, "SelectedFolders", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<string> GetAllowedVideoExtensionsList()
        {
            if (string.IsNullOrWhiteSpace(AllowedVideoExtensions))
            {
                return new List<string> { ".mkv", ".mp4", ".avi" };
            }

            return AllowedVideoExtensions.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p =>
                {
                    var ext = p.Trim().ToLowerInvariant();
                    return ext.StartsWith(".") ? ext : "." + ext;
                })
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Sanea y valida las opciones antes de persistir, normalizando rangos.
        /// Centraliza la validación para evitar duplicaciones entre las opciones y el plugin.
        /// </summary>
        public bool SanitizeAndValidate(out string error)
        {
            if (MaxConcurrentResolutions < 1) MaxConcurrentResolutions = 1;
            if (MaxConcurrentResolutions > 10) MaxConcurrentResolutions = 10;
            if (CookieCacheDurationMinutes < 1) CookieCacheDurationMinutes = 1;

            if (MaxSiblingsPerOpen < 1) MaxSiblingsPerOpen = 1;
            if (MaxSiblingsPerOpen > 20) MaxSiblingsPerOpen = 20;

            if (!string.IsNullOrWhiteSpace(StrmDestinationFolder))
            {
                StrmDestinationFolder = StrmDestinationFolder.Trim();
            }

            if (string.IsNullOrWhiteSpace(AllowedVideoExtensions))
            {
                AllowedVideoExtensions = "mkv,mp4,avi,m4v,mov,wmv,ts,mpg,mpeg,flv,webm";
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Método de compatibilidad invocado antes de persistir.
        /// </summary>
        public bool IsValid(out string error) => SanitizeAndValidate(out error);
    }
}
