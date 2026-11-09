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
            // 1. Credenciales
            Username = string.Empty;
            Password = string.Empty;
            FolderPassword = string.Empty;
            EnableCookieCache = true;
            CookieCacheDurationMinutes = 30;

            // 2. Almacenamiento
            StrmDestinationFolder = string.Empty;
            SyncMode = StrmSyncMode.FullAccount;
            CustomScanPath = string.Empty;
            AllowedVideoExtensions = "mkv,mp4,avi,m4v,mov,wmv,ts,mpg,mpeg,flv,webm";
            OverwritePolicy = StrmOverwritePolicy.SkipIfExists;
            AutoSyncEnabled = false;
            RefreshLibraryAfterSync = false;
            EnableSoapDiagnostics = false;

            // 3. Reproducción
            FastResolutionPath = false;
            ResolveSiblingVersions = true;
            EnableSiblingLimit = true;
            MaxSiblingsPerOpen = 8;
            ResolveOnlyFirstLink = false;
            EnableMaxConcurrentResolutions = true;
            MaxConcurrentResolutions = 3;

            // 4. Limpieza
            DeleteOrphans = false;
            OrphansDryRun = true;

            // 5. Estado
            LastSyncStatus = ChomyfinServices.SyncStatusStore?.GetSummaryString() ?? "No synchronization has been run yet.";
        }

        public override string EditorTitle => "Chomyfin Options";

        public override string EditorDescription => "Configure Chomikuj.pl access, .strm file synchronization, and advanced settings.";

        // --- 1. Credenciales ---

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

        [DisplayName("Enable Cookie Cache")]
        [Description("Maintains an active Chomikuj.pl session to avoid repeated logins.")]
        public bool EnableCookieCache { get; set; }

        [DisplayName("Cookie Cache Duration (Minutes)")]
        [Description("Session expiration time in minutes.")]
        public int CookieCacheDurationMinutes { get; set; }

        // --- 2. Almacenamiento ---

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

        [DisplayName("Enable Automatic Scheduled Sync")]
        [Description("Enables recurring automatic execution of the sync task. If disabled, you can always run sync manually from Emby Scheduled Tasks.")]
        public bool AutoSyncEnabled { get; set; }

        [DisplayName("Refresh Emby Library After Sync")]
        [Description("Notifies Emby about added or deleted files when synchronization finishes.")]
        public bool RefreshLibraryAfterSync { get; set; }

        [DisplayName("Enable SOAP Diagnostics")]
        [Description("Logs raw XML from the SOAP API; for debugging purposes only.")]
        public bool EnableSoapDiagnostics { get; set; }

        // --- 3. Reproducción ---

        [DisplayName("Fast Resolution Path")]
        [Description("Uses a direct fast-path resolution when the URL already contains the file ID. Falls back to the classic method if it fails.")]
        public bool FastResolutionPath { get; set; } = false;

        [Browsable(false)]
        public bool UseFastResolvePath
        {
            get => FastResolutionPath;
            set => FastResolutionPath = value;
        }

        [DisplayName("Resolve Sibling Versions on Open")]
        [Description("When opening a movie or episode detail, resolves and caches alternative qualities in the background for instant playback.")]
        public bool ResolveSiblingVersions { get; set; }

        [DisplayName("Enable Sibling Limit")]
        [Description("If enabled, limits the number of alternative versions resolved per item based on the value below.")]
        public bool EnableSiblingLimit { get; set; } = true;

        [DisplayName("Sibling Versions Limit per Open")]
        [Description("Maximum number of alternative .strm files queued for background resolution on open (1-20).")]
        public int MaxSiblingsPerOpen { get; set; }

        [DisplayName("Resolve Only First Link")]
        [Description("If enabled, the plugin will only resolve the direct URL of the opened item and will ignore resolving sibling versions or slow paths.")]
        public bool ResolveOnlyFirstLink { get; set; } = false;

        [DisplayName("Enable Concurrency Limit")]
        [Description("Limits the number of simultaneous URL resolutions to prevent rate-limiting from Chomikuj.")]
        public bool EnableMaxConcurrentResolutions { get; set; } = true;

        [DisplayName("Max Concurrent Resolutions (1-20)")]
        [Description("Maximum number of simultaneous resolution requests allowed when the limit is enabled.")]
        [MinValue(1)]
        [MaxValue(20)]
        public int MaxConcurrentResolutions { get; set; }

        // --- 4. Limpieza ---

        [DisplayName("Delete Orphans")]
        [Description("If enabled, .strm files in the destination folder that are no longer present on Chomikuj will be deleted.")]
        public bool DeleteOrphans { get; set; }

        [DisplayName("Orphans: Dry Run Only")]
        [Description("While enabled, only logs which orphan .strm files would be deleted without removing them from disk.")]
        public bool OrphansDryRun { get; set; }

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
            if (MaxConcurrentResolutions > 20) MaxConcurrentResolutions = 20;
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

        [DisplayName("Last Sync Status")]
        [Description("Shows the summary and result of the most recent synchronization task.")]
        public string LastSyncStatus { get; set; }
    }
}
