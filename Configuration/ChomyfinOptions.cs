using System;
using System.Collections.Generic;
using System.Linq;
using Chomyfin.Models;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
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
        }

        public override string EditorTitle => Chomyfin.Services.ChomyfinServices.LocalizationManager?.GetLocalizedString("Chomyfin_EditorTitle") ?? "Chomyfin_EditorTitle";

        public override string EditorDescription
        {
            get
            {
                var baseDesc = Chomyfin.Services.ChomyfinServices.LocalizationManager?.GetLocalizedString("Chomyfin_EditorDescription") ?? "Chomyfin: Configuración del plugin";
                var summary = Chomyfin.Services.ChomyfinServices.SyncStatusStore?.GetSummaryString() ?? (Chomyfin.Services.ChomyfinServices.LocalizationManager?.GetLocalizedString("Chomyfin_Status_NoSyncYet") ?? "No se ha ejecutado ninguna sincronización aún.");
                return $"{baseDesc}\n\nÚltima sincronización: {summary}";
            }
        }

        // --- Credenciales y acceso web ---

        [LocalizedDisplayName("Chomyfin_Username_Name")]
        [LocalizedDescription("Chomyfin_Username_Description")]
        public string Username { get; set; }

        [LocalizedDisplayName("Chomyfin_Password_Name")]
        [LocalizedDescription("Chomyfin_Password_Description")]
        [IsPassword]
        public string Password { get; set; }

        [LocalizedDisplayName("Chomyfin_FolderPassword_Name")]
        [LocalizedDescription("Chomyfin_FolderPassword_Description")]
        [IsPassword]
        public string FolderPassword { get; set; }

        // --- Generación y sincronización de .strm (Tarea 2) ---

        [LocalizedDisplayName("Chomyfin_StrmDestinationFolder_Name")]
        [LocalizedDescription("Chomyfin_StrmDestinationFolder_Description")]
        [EditFolderPicker]
        public string StrmDestinationFolder { get; set; }

        [LocalizedDisplayName("Chomyfin_SyncMode_Name")]
        [LocalizedDescription("Chomyfin_SyncMode_Description")]
        public StrmSyncMode SyncMode { get; set; }

        [LocalizedDisplayName("Chomyfin_CustomScanPath_Name")]
        [LocalizedDescription("Chomyfin_CustomScanPath_Description")]
        public string CustomScanPath { get; set; } = string.Empty;

        [LocalizedDisplayName("Chomyfin_AllowedVideoExtensions_Name")]
        [LocalizedDescription("Chomyfin_AllowedVideoExtensions_Description")]
        public string AllowedVideoExtensions { get; set; }

        [LocalizedDisplayName("Chomyfin_OverwritePolicy_Name")]
        [LocalizedDescription("Chomyfin_OverwritePolicy_Description")]
        public StrmOverwritePolicy OverwritePolicy { get; set; }

        [LocalizedDisplayName("Chomyfin_DeleteOrphans_Name")]
        [LocalizedDescription("Chomyfin_DeleteOrphans_Description")]
        public bool DeleteOrphans { get; set; }

        [LocalizedDisplayName("Chomyfin_OrphansDryRun_Name")]
        [LocalizedDescription("Chomyfin_OrphansDryRun_Description")]
        public bool OrphansDryRun { get; set; }

        [LocalizedDisplayName("Chomyfin_AutoSyncEnabled_Name")]
        [LocalizedDescription("Chomyfin_AutoSyncEnabled_Description")]
        public bool AutoSyncEnabled { get; set; }

        [LocalizedDisplayName("Chomyfin_RefreshLibraryAfterSync_Name")]
        [LocalizedDescription("Chomyfin_RefreshLibraryAfterSync_Description")]
        public bool RefreshLibraryAfterSync { get; set; }

        [LocalizedDisplayName("Chomyfin_EnableSoapDiagnostics_Name")]
        [LocalizedDescription("Chomyfin_EnableSoapDiagnostics_Description")]
        public bool EnableSoapDiagnostics { get; set; }

        [LocalizedDisplayName("Chomyfin_LastSyncStatus_Name")]
        [LocalizedDescription("Chomyfin_LastSyncStatus_Description")]
        public string LastSyncStatus
        {
            get => Chomyfin.Services.ChomyfinServices.SyncStatusStore?.GetSummaryString() ?? (Chomyfin.Services.ChomyfinServices.LocalizationManager?.GetLocalizedString("Chomyfin_Status_NotAvailable") ?? "No disponible.");
            set { }
        }

        // --- Rendimiento y caché ---

        [LocalizedDisplayName("Chomyfin_MaxConcurrentResolutions_Name")]
        [LocalizedDescription("Chomyfin_MaxConcurrentResolutions_Description")]
        public int MaxConcurrentResolutions { get; set; }

        [LocalizedDisplayName("Chomyfin_EnableCookieCache_Name")]
        [LocalizedDescription("Chomyfin_EnableCookieCache_Description")]
        public bool EnableCookieCache { get; set; }

        [LocalizedDisplayName("Chomyfin_CookieCacheDurationMinutes_Name")]
        [LocalizedDescription("Chomyfin_CookieCacheDurationMinutes_Description")]
        public int CookieCacheDurationMinutes { get; set; }

        // --- Versiones hermanas (Tarea 1) ---

        [LocalizedDisplayName("Chomyfin_ResolveSiblingVersions_Name")]
        [LocalizedDescription("Chomyfin_ResolveSiblingVersions_Description")]
        public bool ResolveSiblingVersions { get; set; }

        [LocalizedDisplayName("Chomyfin_MaxSiblingsPerOpen_Name")]
        [LocalizedDescription("Chomyfin_MaxSiblingsPerOpen_Description")]
        public int MaxSiblingsPerOpen { get; set; }

        // --- Mejora 3: Vía rápida de resolución ---

        [LocalizedDisplayName("Chomyfin_UseFastResolvePath_Name")]
        [LocalizedDescription("Chomyfin_UseFastResolvePath_Description")]
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
