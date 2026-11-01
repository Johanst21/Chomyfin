using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Chomyfin.Models;
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

            LastSyncStatus = Chomyfin.Services.ChomyfinServices.SyncStatusStore?.GetSummaryString() ?? "No se ha ejecutado ninguna sincronización aún.";
        }

        public override string EditorTitle => Chomyfin.Services.ChomyfinServices.LocalizationManager?.GetLocalizedString("Chomyfin_EditorTitle") ?? "Chomyfin_EditorTitle";

        public override string EditorDescription => Chomyfin.Services.ChomyfinServices.LocalizationManager?.GetLocalizedString("Chomyfin_EditorDescription") ?? "Chomyfin_EditorDescription";

        // --- Credenciales y acceso web ---

        [DisplayName("Chomyfin_Username_Name")]
        [Description("Chomyfin_Username_Description")]
        public string Username { get; set; }

        [DisplayName("Chomyfin_Password_Name")]
        [Description("Chomyfin_Password_Description")]
        [IsPassword]
        public string Password { get; set; }

        [DisplayName("Chomyfin_FolderPassword_Name")]
        [Description("Chomyfin_FolderPassword_Description")]
        [IsPassword]
        public string FolderPassword { get; set; }

        // --- Generación y sincronización de .strm (Tarea 2) ---

        [DisplayName("Chomyfin_StrmDestinationFolder_Name")]
        [Description("Chomyfin_StrmDestinationFolder_Description")]
        [EditFolderPicker]
        public string StrmDestinationFolder { get; set; }

        [DisplayName("Chomyfin_SyncMode_Name")]
        [Description("Chomyfin_SyncMode_Description")]
        public StrmSyncMode SyncMode { get; set; }

        [DisplayName("Chomyfin_CustomScanPath_Name")]
        [Description("Chomyfin_CustomScanPath_Description")]
        public string CustomScanPath { get; set; } = string.Empty;

        [DisplayName("Chomyfin_AllowedVideoExtensions_Name")]
        [Description("Chomyfin_AllowedVideoExtensions_Description")]
        public string AllowedVideoExtensions { get; set; }

        [DisplayName("Chomyfin_OverwritePolicy_Name")]
        [Description("Chomyfin_OverwritePolicy_Description")]
        public StrmOverwritePolicy OverwritePolicy { get; set; }

        [DisplayName("Chomyfin_DeleteOrphans_Name")]
        [Description("Chomyfin_DeleteOrphans_Description")]
        public bool DeleteOrphans { get; set; }

        [DisplayName("Chomyfin_OrphansDryRun_Name")]
        [Description("Chomyfin_OrphansDryRun_Description")]
        public bool OrphansDryRun { get; set; }

        [DisplayName("Chomyfin_AutoSyncEnabled_Name")]
        [Description("Chomyfin_AutoSyncEnabled_Description")]
        public bool AutoSyncEnabled { get; set; }

        [DisplayName("Chomyfin_RefreshLibraryAfterSync_Name")]
        [Description("Chomyfin_RefreshLibraryAfterSync_Description")]
        public bool RefreshLibraryAfterSync { get; set; }

        [DisplayName("Chomyfin_EnableSoapDiagnostics_Name")]
        [Description("Chomyfin_EnableSoapDiagnostics_Description")]
        public bool EnableSoapDiagnostics { get; set; }

        [DisplayName("Chomyfin_LastSyncStatus_Name")]
        [Description("Chomyfin_LastSyncStatus_Description")]
        [ReadOnly(true)]
        public string LastSyncStatus { get; set; }

        // --- Rendimiento y caché ---

        [DisplayName("Chomyfin_MaxConcurrentResolutions_Name")]
        [Description("Chomyfin_MaxConcurrentResolutions_Description")]
        public int MaxConcurrentResolutions { get; set; }

        [DisplayName("Chomyfin_EnableCookieCache_Name")]
        [Description("Chomyfin_EnableCookieCache_Description")]
        public bool EnableCookieCache { get; set; }

        [DisplayName("Chomyfin_CookieCacheDurationMinutes_Name")]
        [Description("Chomyfin_CookieCacheDurationMinutes_Description")]
        public int CookieCacheDurationMinutes { get; set; }

        // --- Versiones hermanas (Tarea 1) ---

        [DisplayName("Chomyfin_ResolveSiblingVersions_Name")]
        [Description("Chomyfin_ResolveSiblingVersions_Description")]
        public bool ResolveSiblingVersions { get; set; }

        [DisplayName("Chomyfin_MaxSiblingsPerOpen_Name")]
        [Description("Chomyfin_MaxSiblingsPerOpen_Description")]
        public int MaxSiblingsPerOpen { get; set; }

        // --- Mejora 3: Vía rápida de resolución ---

        [DisplayName("Chomyfin_UseFastResolvePath_Name")]
        [Description("Chomyfin_UseFastResolvePath_Description")]
        public bool UseFastResolvePath { get; set; }

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
