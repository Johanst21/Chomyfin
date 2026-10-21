using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chomyfin.Models;
using Chomyfin.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Chomyfin.ScheduledTasks
{
    /// <summary>
    /// Tarea programada en Emby para sincronizar automáticamente o bajo demanda
    /// la estructura de archivos .strm desde Chomikuj.pl hacia la carpeta local de la biblioteca.
    /// </summary>
    public class StrmSyncTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILogger _logger;

        public StrmSyncTask(ILibraryMonitor libraryMonitor, ILogManager logManager)
        {
            _libraryMonitor = libraryMonitor;
            _logger = logManager.GetLogger(Name);
        }

        public string Name => "Chomyfin: sincronizar .strm de Chomikuj";

        public string Key => "ChomyfinStrmSync";

        public string Description => "Genera y sincroniza archivos .strm desde tu cuenta de Chomikuj.pl según la configuración del plugin.";

        public string Category => "Chomyfin";

        public bool IsHidden => false;

        public bool IsEnabled => true;

        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            var options = ChomyfinServices.OptionsAccessor?.Invoke();
            if (options != null && !options.AutoSyncEnabled)
            {
                return Array.Empty<TaskTriggerInfo>();
            }

            // Disparador por defecto: Diario a las 04:00 AM si la sincronización automática está habilitada
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
                }
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = ChomyfinServices.OptionsAccessor?.Invoke();
            if (options == null)
            {
                _logger.Warn("Chomyfin: No se pudo obtener la configuración para ejecutar la sincronización.");
                return;
            }

            _logger.Info("Chomyfin: Iniciando sincronización de .strm...");
            var statusStore = ChomyfinServices.SyncStatusStore;
            statusStore?.RecordStart();

            var genOptions = new StrmGeneratorOptions
            {
                Username = options.Username,
                Password = options.Password,
                DestinationPath = options.StrmDestinationFolder,
                SyncMode = options.SyncMode,
                SelectedFolders = options.GetSelectedFoldersList(),
                AllowedExtensions = options.GetAllowedVideoExtensionsList(),
                OverwritePolicy = options.OverwritePolicy,
                DeleteOrphans = options.DeleteOrphans,
                EnableSoapDiagnostics = options.EnableSoapDiagnostics,
                DiagnosticsDirectory = ChomyfinServices.PluginDataPath != null ? System.IO.Path.Combine(ChomyfinServices.PluginDataPath, "diag") : null
            };

            var generator = new StrmGenerator(_logger);
            StrmGeneratorResult result = null;

            try
            {
                result = await generator.GenerateAsync(genOptions, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Chomyfin: Error inesperado durante la ejecución de StrmSyncTask", ex);
                result = new StrmGeneratorResult
                {
                    Success = false,
                    Message = $"Error inesperado: {ex.Message}",
                    EndTime = DateTime.UtcNow
                };
            }
            finally
            {
                statusStore?.RecordCompletion(result);
            }

            _logger.Info("Chomyfin: Sincronización finalizada. Resultado: {0}", result?.Message);

            // Refrescar biblioteca en Emby si está configurado y se crearon/eliminaron archivos
            if (options.RefreshLibraryAfterSync && result != null && result.Success && _libraryMonitor != null)
            {
                if (!string.IsNullOrWhiteSpace(options.StrmDestinationFolder))
                {
                    try
                    {
                        _logger.Info("Chomyfin: Notificando cambios a ILibraryMonitor para la carpeta: {0}", options.StrmDestinationFolder);
                        _libraryMonitor.ReportFileSystemChanged(options.StrmDestinationFolder);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("Chomyfin: No se pudo notificar el cambio a ILibraryMonitor: {0}", ex.Message);
                    }
                }
            }
        }
    }
}
