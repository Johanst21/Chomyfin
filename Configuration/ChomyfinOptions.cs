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
            LastSyncStatus = "No se ha ejecutado ninguna sincronización aún.";
            EnableSoapDiagnostics = false;

            // Opciones Tarea 1: Versiones hermanas
            ResolveSiblingVersions = true;
            MaxSiblingsPerOpen = 8;

            // Mejora 3: Vía rápida de resolución
            UseFastResolvePath = true;
        }

        public override string EditorTitle => "Chomyfin Opciones";

        public override string EditorDescription =>
            "Configura el acceso a Chomikuj.pl, la sincronización de archivos .strm y parámetros avanzados.";

        // --- Credenciales y acceso web ---

        [DisplayName("Usuario de Chomikuj")]
        [Description("Nombre de usuario de tu cuenta de Chomikuj.pl")]
        public string Username { get; set; }

        [DisplayName("Contraseña de Chomikuj")]
        [Description("Contraseña de tu cuenta de Chomikuj.pl (el campo se oculta en la interfaz).")]
        [IsPassword]
        public string Password { get; set; }

        [DisplayName("Contraseña de carpetas protegidas (opcional)")]
        [Description("Contraseña para acceder a carpetas privadas protegidas como 'Prywatne'")]
        [IsPassword]
        public string FolderPassword { get; set; }

        // --- Generación y sincronización de .strm (Tarea 2) ---

        [DisplayName("Carpeta de destino local de los .strm")]
        [Description("Directorio local en el servidor donde se generará la biblioteca de archivos .strm.")]
        [EditFolderPicker]
        public string StrmDestinationFolder { get; set; }

        [DisplayName("Modo de sincronización")]
        [Description("Elige si sincronizar toda la cuenta de Chomikuj o únicamente las carpetas indicadas.")]
        public StrmSyncMode SyncMode { get; set; }

        [DisplayName("Carpetas seleccionadas de Chomikuj")]
        [Description("Rutas separadas por comas o punto y coma (ej: /Prywatne/Servidores/Chomyfin/, /Otra/Ruta)")]
        public string CustomScanPath { get; set; } = string.Empty;

        [DisplayName("Extensiones de vídeo permitidas")]
        [Description("Lista de extensiones separadas por coma (sin punto). Solo los archivos con estas extensiones generarán un .strm.")]
        public string AllowedVideoExtensions { get; set; }

        [DisplayName("Política de sobrescritura de .strm")]
        [Description("Omitir si el .strm ya existe (recomendado) o actualizar si la URL del archivo en Chomikuj ha cambiado.")]
        public StrmOverwritePolicy OverwritePolicy { get; set; }

        [DisplayName("Eliminar archivos huérfanos")]
        [Description("⚠️ ATENCIÓN: Si se activa, se eliminarán los archivos .strm locales que ya no existan en tu cuenta de Chomikuj.")]
        public bool DeleteOrphans { get; set; }

        [DisplayName("Orphans: solo simular (dry-run)")]
        [Description("Mientras esté activo, solo se registrará qué archivos .strm huérfanos se borrarían sin eliminarlos del disco.")]
        public bool OrphansDryRun { get; set; }

        [DisplayName("Habilitar sincronización programada automática")]
        [Description("Activa la ejecución automática periódica de la tarea de sincronización. Si está desactivada, siempre podrás iniciar la sincronización manualmente desde el panel de Tareas Programadas de Emby.")]
        public bool AutoSyncEnabled { get; set; }

        [DisplayName("Actualizar biblioteca de Emby tras sincronizar")]
        [Description("Notifica a Emby de los archivos añadidos o eliminados al terminar la sincronización.")]
        public bool RefreshLibraryAfterSync { get; set; }

        [DisplayName("Habilitar diagnóstico SOAP")]
        [Description("Registra el XML crudo de la API SOAP; solo para depurar")]
        public bool EnableSoapDiagnostics { get; set; }

                [DisplayName("Estado de la última sincronización")]
        [Description("Información sobre la última sincronización ejecutada.")]
        public string LastSyncStatus { get; set; }

        // --- Rendimiento y caché ---

        [DisplayName("Resoluciones concurrentes")]
        [Description("Número máximo de descargas simultáneas (1-10)")]
        public int MaxConcurrentResolutions { get; set; }

        [DisplayName("Habilitar caché de cookies")]
        [Description("Mantiene sesión activa de Chomikuj.pl")]
        public bool EnableCookieCache { get; set; }

        [DisplayName("Duración caché cookies (minutos)")]
        public int CookieCacheDurationMinutes { get; set; }

        // --- Versiones hermanas (Tarea 1) ---

        [DisplayName("Resolver versiones hermanas al abrir")]
        [Description("Al abrir la ficha de una película o capítulo, resuelve y cachea en segundo plano las otras versiones o calidades hermanas para reproducción inmediata.")]
        public bool ResolveSiblingVersions { get; set; }

        [DisplayName("Límite de versiones hermanas por apertura")]
        [Description("Número máximo de archivos .strm alternativos que se encolarán para resolver en segundo plano al abrir una ficha (1-20).")]
        public int MaxSiblingsPerOpen { get; set; }

        // --- Mejora 3: Vía rápida de resolución ---

        [DisplayName("Vía rápida de resolución")]
        [Description("Vía rápida de resolución: usa una sola petición cuando la URL ya contiene el id del archivo; si falla, se usa el método clásico.")]
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
