using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chomyfin.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.LiveTv;

namespace Chomyfin
{
    /// <summary>
    /// Provee MediaSourceInfo para archivos .strm que contienen URLs de
    /// páginas de chomikuj.pl: resuelve la URL directa de descarga y la
    /// escribe de vuelta al .strm (cache en disco, estilo Debryfin),
    /// conservando la URL de origen en un archivo .original.
    ///
    /// Cambios 1.3 (Emby 4.10.x) — arreglo de la regresión de 1.2:
    /// - La resolución de la URL de descarga es INDEPENDIENTE de la
    ///   persistencia en disco: GetMediaSources ahora usa la URL que devuelve
    ///   la resolución y ya no vuelve a leer el .strm del disco.
    /// - Las escrituras en disco (.original y reescritura del .strm) son "best-effort".
    /// - Regla de consistencia: el .strm solo se reescribe con la URL directa si
    ///   el .original se pudo escribir.
    ///
    /// Cambios 1.4 (Paso 0 - Refactor):
    /// - Delegación de la lógica de caché y persistencia a <see cref="StrmCacheService"/>.
    /// - Consumo desacoplado de servicios a través de <see cref="ChomyfinServices"/>.
    /// </summary>
    public class UrlProvider : IMediaSourceProvider
    {
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILibraryManager _libraryManager;

        public UrlProvider(ILogManager logManager, IFileSystem fileSystem, ILibraryManager libraryManager = null)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _fileSystem = fileSystem;
            _libraryManager = libraryManager;
            ChomyfinServices.EnsureServices(fileSystem, libraryManager);
        }

        public async Task<List<MediaSourceInfo>> GetMediaSources(BaseItem item, CancellationToken cancellationToken)
        {
            if (item.Path == null || !item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
            {
                return new List<MediaSourceInfo>();
            }

            _logger.Debug("Chomyfin: GetMediaSources invocado para {0} (Id={1}, Type={2})", item.Path, item.Id, item.GetType().Name);

            // 1. Resuelve la URL directa del ítem principal de forma SÍNCRONA
            //    (y, si el disco lo permite, cachea) delegando en StrmCacheService.
            //    Se usa la URL que devuelve la resolución, NO el contenido que
            //    queda en el .strm: así la reproducción no depende de que el
            //    disco sea escribible ni retrasa la ficha de Emby.
            var cacheService = ChomyfinServices.StrmCacheService;
            string directUrl;
            if (cacheService != null)
            {
                directUrl = await cacheService.GetOrResolveDirectUrlAsync(item.Path, false, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _logger.Error("Chomyfin: StrmCacheService no está disponible para resolver {0}", item.Path);
                return new List<MediaSourceInfo>();
            }

            // 2. Encolar resolución de hermanos en segundo plano (fire-and-forget fuera del camino de reproducción)
            _ = Task.Run(() =>
            {
                try
                {
                    var options = ChomyfinServices.OptionsAccessor?.Invoke();
                    if (options != null && options.ResolveSiblingVersions)
                    {
                        var finder = ChomyfinServices.SiblingFinder;
                        var queue = ChomyfinServices.SiblingResolutionQueue;

                        if (finder != null && queue != null)
                        {
                            var siblings = finder.FindSiblings(item, options.MaxSiblingsPerOpen);
                            if (siblings != null && siblings.Count > 0)
                            {
                                queue.EnqueueSiblings(siblings, item.Path);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Chomyfin: error iniciando resolución de hermanos para " + item.Path, ex);
                }
            });

            // 3. Solo servimos una URL de descarga directa (http/https).
            if (string.IsNullOrWhiteSpace(directUrl) || !ChomikujUrlHelper.IsDirectUrl(directUrl))
            {
                return new List<MediaSourceInfo>();
            }

            // 4. Devolvemos la información igual que en Debryfin para evitar el cuadro vacío.
            return BuildMediaSourceInfo(directUrl.Trim(), item.Path);
        }

        private List<MediaSourceInfo> BuildMediaSourceInfo(string directUrl, string strmPath)
        {
            var source = new MediaSourceInfo
            {
                // Nombre dinámico basado en el archivo .strm para que Emby diferencie versiones (1080p, 4K, etc.)
                Name = ResolveMediaSourceName(strmPath),
                Id = ComputeMediaSourceId(strmPath),
                // MediaSourceType.Video no existe en la referencia 4.9.1.90
                // (solo Default/Grouping/Placeholder); dejar null = Default.

                Path = directUrl.Trim(),
                Protocol = MediaProtocol.Http,
                IsRemote = true,
                SupportsDirectPlay = true,
                SupportsDirectStream = true,
                Container = "mkv",
                RequiredHttpHeaders = new Dictionary<string, string>()
            };

            var service = ChomyfinServices.ChomikujService;
            if (service != null && !string.IsNullOrEmpty(service.UserAgent))
            {
                source.RequiredHttpHeaders["User-Agent"] = service.UserAgent;
            }

            return new List<MediaSourceInfo> { source };
        }

        /// <summary>
        /// Obtiene el nombre limpio del archivo .strm sin extensión para identificar versiones en Emby.
        /// Si la ruta es nula o vacía, devuelve null para que Emby use su comportamiento por defecto.
        /// </summary>
        public static string ResolveMediaSourceName(string strmPath)
        {
            if (string.IsNullOrEmpty(strmPath))
            {
                return null;
            }

            var normalized = strmPath.Replace('\\', '/');
            var fileName = Path.GetFileName(normalized);
            return Path.GetFileNameWithoutExtension(fileName);
        }

        /// <summary>
        /// Calcula un identificador determinista para MediaSourceInfo a partir de la ruta del .strm.
        /// Entrada: "chomyfin:" + ruta con separadores normalizados a '/'.
        /// Hash: MD5 (16 bytes) representado como Guid en formato "N" (32 caracteres hexadecimales).
        /// </summary>
        public static string ComputeMediaSourceId(string strmPath)
        {
            if (string.IsNullOrEmpty(strmPath))
            {
                strmPath = string.Empty;
            }

            var normalizedPath = "chomyfin:" + strmPath.Replace('\\', '/');
            using (var md5 = MD5.Create())
            {
                var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
                return new Guid(hashBytes).ToString("N");
            }
        }

        public bool Supports(BaseItem item) => item is Video && item.Path != null && item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase);

        // Firma 4.10: OpenMediaSource ganó `string consumerId` como 2º parámetro
        // (no existe en 4.9.1.90). consumerId no se usa: no hay LiveStream que abrir.
        public Task<ILiveStream> OpenMediaSource(string openToken, string consumerId, List<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
            => Task.FromResult<ILiveStream>(null);
    }
}
