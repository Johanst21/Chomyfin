using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace Chomyfin.Services
{
    /// <summary>
    /// Gestiona la caché en disco (.strm y .original) y la resolución de enlaces directos.
    ///
    /// Máquina de estados:
    /// 1. .strm con URL de página: resuelve y crea .original y .strm resuelto.
    /// 2. .strm con URL directa y .original vigente (< 23 h): sirve la URL directa sin tocar la red.
    /// 3. .strm con URL directa y .original caducado (>= 23 h): re-resuelve desde el .original.
    /// 4. .strm con URL directa sin .original: sirve la URL directa (no hay URL origen para re-resolver).
    ///
    /// Todas las escrituras en disco son best-effort y siguen la regla de consistencia:
    /// primero se escribe .original y solo tras su éxito se reescribe .strm.
    /// </summary>
    public class StrmCacheService
    {
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly Func<IChomikujService> _getChomikujService;
        private static readonly TimeSpan LinkExpirationTime = TimeSpan.FromHours(23);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _perFileLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private static int _lockAcquisitions = 0;

        public StrmCacheService(ILogger logger, IFileSystem fileSystem, Func<IChomikujService> getChomikujService)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _getChomikujService = getChomikujService;
        }

        private SemaphoreSlim GetPerFileLock(string key)
        {
            MaybeCleanupLocks();
            return _perFileLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        }

        /// <summary>
        /// Purga periódicamente bloqueos inactivos para evitar que el diccionario crezca indefinidamente.
        /// </summary>
        private static void MaybeCleanupLocks()
        {
            if (Interlocked.Increment(ref _lockAcquisitions) % 100 == 0 && _perFileLocks.Count > 50)
            {
                foreach (var entry in _perFileLocks)
                {
                    if (entry.Value.CurrentCount == 1)
                    {
                        _perFileLocks.TryRemove(entry.Key, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Comprobación rápida y sin llamadas de red para verificar si un archivo .strm requiere resolución.
        /// </summary>
        public bool NeedsResolution(string strmPath)
        {
            try
            {
                if (string.IsNullOrEmpty(strmPath) || !_fileSystem.FileExists(strmPath))
                {
                    return false;
                }

                var originalPath = Path.ChangeExtension(strmPath, ".original");
                if (_fileSystem.FileExists(originalPath))
                {
                    var lastResolved = _fileSystem.GetLastWriteTimeUtc(originalPath);
                    return (DateTime.UtcNow - lastResolved) >= LinkExpirationTime;
                }

                // Sin archivo .original: comprobamos si el .strm aún contiene la URL de página de Chomikuj
                var content = _fileSystem.ReadAllText(strmPath).Trim();
                return ChomikujUrlHelper.IsChomikujPageUrl(content);
            }
            catch (Exception ex)
            {
                _logger.Warn("Chomyfin: error comprobando necesidad de resolución para {0}: {1}", strmPath, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Obtiene o resuelve la URL directa de descarga de un archivo .strm.
        /// </summary>
        public async Task<string> GetOrResolveDirectUrlAsync(string strmPath, bool forceResolve, CancellationToken cancellationToken)
        {
            var originalStrmPath = Path.ChangeExtension(strmPath, ".original");
            string chomikujUrl;

            var fileLock = GetPerFileLock(strmPath);
            await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var currentContent = (await _fileSystem.ReadAllTextAsync(strmPath).ConfigureAwait(false)).Trim();

                if (forceResolve)
                {
                    if (_fileSystem.FileExists(originalStrmPath))
                    {
                        chomikujUrl = (await _fileSystem.ReadAllTextAsync(originalStrmPath).ConfigureAwait(false)).Trim();
                        if (!ChomikujUrlHelper.IsChomikujPageUrl(chomikujUrl))
                        {
                            chomikujUrl = ChomikujUrlHelper.IsChomikujPageUrl(currentContent) ? currentContent : null;
                        }
                    }
                    else
                    {
                        chomikujUrl = ChomikujUrlHelper.IsChomikujPageUrl(currentContent) ? currentContent : null;
                    }

                    if (string.IsNullOrEmpty(chomikujUrl))
                    {
                        return ChomikujUrlHelper.IsDirectUrl(currentContent) ? currentContent : null;
                    }
                }
                else
                {
                    if (ChomikujUrlHelper.IsChomikujPageUrl(currentContent))
                    {
                        // El .strm aún apunta a la página de chomikuj: hay que resolver.
                        chomikujUrl = currentContent;
                    }
                    else if (_fileSystem.FileExists(originalStrmPath))
                    {
                        var lastResolved = _fileSystem.GetLastWriteTimeUtc(originalStrmPath);
                        if (DateTime.UtcNow - lastResolved < LinkExpirationTime)
                        {
                            // Aún válida: servimos la URL directa ya cacheada en el .strm.
                            return ChomikujUrlHelper.IsDirectUrl(currentContent) ? currentContent : null;
                        }
                        // Expirada: re-resolvemos a partir de la URL de origen.
                        chomikujUrl = (await _fileSystem.ReadAllTextAsync(originalStrmPath).ConfigureAwait(false)).Trim();
                        if (!ChomikujUrlHelper.IsChomikujPageUrl(chomikujUrl))
                        {
                            return ChomikujUrlHelper.IsDirectUrl(currentContent) ? currentContent : null;
                        }
                    }
                    else
                    {
                        // URL directa sin .original de referencia: nada que re-resolver.
                        return ChomikujUrlHelper.IsDirectUrl(currentContent) ? currentContent : null;
                    }
                }

                var service = _getChomikujService();
                if (service == null)
                {
                    _logger.Error("Chomyfin: IChomikujService no está disponible; no se pudo resolver {0}", strmPath);
                    return ChomikujUrlHelper.IsDirectUrl(currentContent) ? currentContent : null;
                }

                _logger.Info("Chomyfin: Resolviendo enlace para {0}", Path.GetFileName(strmPath));
                var directUrl = await service.ResolveUrlAsync(chomikujUrl, cancellationToken).ConfigureAwait(false);
                if (!ChomikujUrlHelper.IsDirectUrl(directUrl))
                {
                    // La resolución falló: servimos lo que ya teníamos (URL directa o null).
                    return ChomikujUrlHelper.IsDirectUrl(currentContent) ? currentContent : null;
                }

                // --- Persistencia (best-effort) -------------------------------------
                // 1) Conservar la URL de origen en .original (crea o actualiza; la
                //    actualización también reinicia el temporizador de expiración).
                var originalOk = true;
                try
                {
                    await _fileSystem.WriteAllTextAsync(originalStrmPath, chomikujUrl).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    originalOk = false;
                    _logger.Warn("Chomyfin: no se pudo escribir {0} ({1}); la caché en disco queda desactivada para este archivo, pero la reproducción sigue funcionando.",
                        originalStrmPath, ex.Message);
                }

                // 2) Reescribir el .strm con la URL directa SOLO si se pudo conservar
                //    la URL de origen (regla de consistencia).
                if (originalOk)
                {
                    try
                    {
                        await _fileSystem.WriteAllTextAsync(strmPath, directUrl).ConfigureAwait(false);
                        _logger.Info("Chomyfin: URL resuelta y cacheada para {0}", Path.GetFileName(strmPath));
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("Chomyfin: no se pudo reescribir {0} ({1}); se servirá la URL resuelta sin caché.",
                            strmPath, ex.Message);
                    }
                }

                return directUrl;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.ErrorException("Chomyfin: Error procesando " + strmPath, ex);
                return null;
            }
            finally
            {
                fileLock.Release();
            }
        }
    }
}
