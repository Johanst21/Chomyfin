using System;
using System.Threading;
using System.Threading.Tasks;
using Chomyfin.Models;
using Chomyfin.Services;
using MediaBrowser.Controller.Api;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Chomyfin.Api
{
    [Route("/Chomyfin/Sync/Run", "POST", Summary = "Inicia la sincronización de archivos .strm en segundo plano")]
    public class RunSyncRequest : IReturn<SyncActionResult>
    {
    }

    [Route("/Chomyfin/Sync/Status", "GET", Summary = "Obtiene el estado de la sincronización de archivos .strm")]
    public class GetSyncStatusRequest : IReturn<SyncStatusResponse>
    {
    }

    [Route("/Chomyfin/TestConnection", "POST", Summary = "Prueba la conexión y autenticación con Chomikuj")]
    public class TestConnectionRequest : IReturn<TestConnectionResult>
    {
    }

    public class SyncActionResult
    {
        public bool Started { get; set; }
        public string Message { get; set; }
    }

    public class SyncStatusResponse
    {
        public bool IsRunning { get; set; }
        public string LastStartTimeUtc { get; set; }
        public string LastEndTimeUtc { get; set; }
        public bool? LastSuccess { get; set; }
        public string LastMessage { get; set; }
        public int FilesDiscovered { get; set; }
        public int FilesCreated { get; set; }
        public int FilesUpdated { get; set; }
        public int FilesSkipped { get; set; }
        public int FilesFailed { get; set; }
        public int OrphansDeleted { get; set; }
        public string Summary { get; set; }
    }

    public class TestConnectionResult
    {
        public bool SoapAuthSuccess { get; set; }
        public string SoapAuthMessage { get; set; }
    }

    public class ChomyfinApiService : BaseApiService
    {
        private readonly ILogger _logger;

        public ChomyfinApiService(ILogManager logManager)
        {
            _logger = logManager.GetLogger("ChomyfinApiService");
        }

        public object Post(RunSyncRequest request)
        {
            var store = ChomyfinServices.SyncStatusStore;
            if (store != null && store.IsRunning)
            {
                return new SyncActionResult
                {
                    Started = false,
                    Message = "Ya hay una sincronización en curso."
                };
            }

            var options = ChomyfinServices.OptionsAccessor?.Invoke();
            if (options == null)
            {
                return new SyncActionResult
                {
                    Started = false,
                    Message = "No se pudo acceder a la configuración del plugin."
                };
            }

            if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
            {
                return new SyncActionResult
                {
                    Started = false,
                    Message = "Credenciales de Chomikuj no configuradas."
                };
            }

            if (string.IsNullOrWhiteSpace(options.StrmDestinationFolder))
            {
                return new SyncActionResult
                {
                    Started = false,
                    Message = "Carpeta de destino no configurada."
                };
            }

            store?.RecordStart();

            // Lanzar en segundo plano para no bloquear la petición HTTP
            Task.Run(async () =>
            {
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
                    result = await generator.GenerateAsync(genOptions, null, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Chomyfin: Error en sincronización manual vía API", ex);
                    result = new StrmGeneratorResult
                    {
                        Success = false,
                        Message = $"Error: {ex.Message}",
                        EndTime = DateTime.UtcNow
                    };
                }
                finally
                {
                    store?.RecordCompletion(result);
                }
            });

            return new SyncActionResult
            {
                Started = true,
                Message = "Sincronización iniciada en segundo plano."
            };
        }

        public object Get(GetSyncStatusRequest request)
        {
            var store = ChomyfinServices.SyncStatusStore;
            if (store == null)
            {
                return new SyncStatusResponse
                {
                    Summary = "Almacén de estado no disponible."
                };
            }

            return new SyncStatusResponse
            {
                IsRunning = store.IsRunning,
                LastStartTimeUtc = store.LastStartTimeUtc?.ToString("o"),
                LastEndTimeUtc = store.LastEndTimeUtc?.ToString("o"),
                LastSuccess = store.LastSuccess,
                LastMessage = store.LastMessage,
                FilesDiscovered = store.FilesDiscovered,
                FilesCreated = store.FilesCreated,
                FilesUpdated = store.FilesUpdated,
                FilesSkipped = store.FilesSkipped,
                FilesFailed = store.FilesFailed,
                OrphansDeleted = store.OrphansDeleted,
                Summary = store.GetSummaryString()
            };
        }

        public async Task<object> Post(TestConnectionRequest request)
        {
            var options = ChomyfinServices.OptionsAccessor?.Invoke();
            var res = new TestConnectionResult();

            if (options == null || string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
            {
                res.SoapAuthMessage = "Credenciales no configuradas.";
                return res;
            }

            // Probar autenticación SOAP (ChomikBox)
            try
            {
                using (var soapClient = new ChomikBoxClient(_logger))
                {
                    var loginSuccess = await soapClient.LoginAsync(options.Username, options.Password, CancellationToken.None).ConfigureAwait(false);
                    res.SoapAuthSuccess = loginSuccess;
                    res.SoapAuthMessage = loginSuccess ? "Autenticación con Chomikuj (ChomikBox) correcta." : "Fallo en autenticación con Chomikuj (comprueba usuario y contraseña).";
                    if (loginSuccess)
                    {
                        await soapClient.LogoutAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                res.SoapAuthSuccess = false;
                res.SoapAuthMessage = $"Error: {ex.Message}";
            }

            return res;
        }
    }
}
