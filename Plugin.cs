using System;
using System.IO;
using Chomyfin.Configuration;
using Chomyfin.Services;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;

namespace Chomyfin
{
    /// <summary>
    /// Plugin Chomyfin (Emby 4.10.x).
    ///
    /// Cambios 1.2:
    /// - ChomikujService ya no depende de <see cref="Instance"/>: recibe una
    ///   fábrica de opciones, lo que evita el acoplamiento a la instancia
    ///   estática y aplica los cambios de configuración sin reiniciar.
    ///
    /// Cambios 1.4:
    /// - Registro central de servicios en <see cref="ChomyfinServices"/>.
    /// - Tarea 2: Sincronización de .strm (SyncStatusStore, StrmSyncTask).
    /// - Actualización dinámica del estado de sincronización al abrir la interfaz de configuración.
    /// </summary>
    public class Plugin : BasePluginSimpleUI<ChomyfinOptions>, IHasThumbImage, IHasTranslations
    {
        public static Plugin Instance { get; private set; }
        public ILocalizationManager LocalizationManager { get; private set; }
        private readonly ILogger _logger;

        public Plugin(IApplicationHost applicationHost, ILogManager logManager) : base(applicationHost)
        {
            Instance = this;
            _logger = logManager.GetLogger(this.Name);
            try
            {
                try
                {
                    LocalizationManager = applicationHost.Resolve<ILocalizationManager>();
                }
                catch
                {
                    // Fallback si no está disponible directamente desde el host
                }

                IFileSystem fileSystem = null;
                try
                {
                    fileSystem = applicationHost.Resolve<IFileSystem>();
                }
                catch
                {
                    // Fallback si no está disponible directamente desde el host
                }

                MediaBrowser.Controller.Library.ILibraryManager libraryManager = null;
                try
                {
                    libraryManager = applicationHost.Resolve<MediaBrowser.Controller.Library.ILibraryManager>();
                }
                catch
                {
                    // Fallback si no está disponible directamente desde el host
                }

                string dataFolderPath = null;
                try
                {
                    dataFolderPath = this.DataFolderPath;
                }
                catch { }

                ChomyfinServices.Initialize(_logger, () => GetOptions(), fileSystem, libraryManager, dataFolderPath, LocalizationManager);
                _logger.Info("Chomyfin ({0}) is getting loaded", this.Name);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Chomyfin: Failed to initialize ChomyfinServices", ex);
            }
        }

        public override Guid Id => new Guid("3448308b-4818-471a-b3d2-a40107e8583a");
        public override string Name => "Chomyfin";
        public override string Description => "Resuelve URLs de Chomikuj.pl para reproducción directa y sincroniza archivos .strm.";

        // La configuración se gestiona internamente por BasePluginSimpleUI<T>
        public ChomyfinOptions Configuration => this.GetOptions();
        public ImageFormat ThumbImageFormat => ImageFormat.Jpg;

        public Stream GetThumbImage()
        {
            var type = this.GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".ThumbImage.jpg");
        }

        public TranslationInfo[] GetTranslations()
        {
            var type = this.GetType();
            return new[]
            {
                new TranslationInfo
                {
                    Locale = "en-US",
                    EmbeddedResourcePath = type.Namespace + ".Localization.en-US.json"
                },
                new TranslationInfo
                {
                    Locale = "es-ES",
                    EmbeddedResourcePath = type.Namespace + ".Localization.es-ES.json"
                }
            };
        }

        protected override ChomyfinOptions OnBeforeShowUI(ChomyfinOptions options)
        {
            return base.OnBeforeShowUI(options);
        }

        protected override bool OnOptionsSaving(ChomyfinOptions options)
        {
            // Saneado y validación de rangos centralizada en ChomyfinOptions.
            if (!options.SanitizeAndValidate(out string error))
            {
                _logger.Warn("Chomyfin: valor de opción inválido al guardar: {0}", error);
            }
            return base.OnOptionsSaving(options);
        }

        protected override void OnOptionsSaved(ChomyfinOptions options)
        {
            _logger.Info("Chomyfin ({0}) options have been updated. CustomScanPath='{1}'", this.Name, options?.CustomScanPath);
            try
            {
                ChomyfinServices.ChomikujService?.ForceReLogin();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Chomyfin: error forcing re-login", ex);
            }
        }
    }
}
