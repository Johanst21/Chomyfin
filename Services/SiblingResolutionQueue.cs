using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace Chomyfin.Services
{
    /// <summary>
    /// Cola en segundo plano para resolución y pre-caché de versiones hermanas.
    /// Garantiza ejecución fire-and-forget segura, deduplicación de tareas en vuelo,
    /// cooldown por archivo y ejecución secuencial con timeouts individuales.
    /// </summary>
    public class SiblingResolutionQueue
    {
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan PerSiblingTimeout = TimeSpan.FromMinutes(2);

        private readonly ILogger _logger;
        private readonly Func<StrmCacheService> _cacheServiceAccessor;
        private readonly Func<IChomikujService> _chomikujServiceAccessor;

        // Deduplicación en vuelo: archivo -> tarea en ejecución
        private readonly ConcurrentDictionary<string, Task> _inFlightTasks =
            new ConcurrentDictionary<string, Task>(StringComparer.OrdinalIgnoreCase);

        // Cooldown por archivo: archivo -> última hora encolado/resuelto
        private readonly ConcurrentDictionary<string, DateTime> _cooldowns =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public SiblingResolutionQueue(
            ILogger logger,
            Func<StrmCacheService> cacheServiceAccessor,
            Func<IChomikujService> chomikujServiceAccessor)
        {
            _logger = logger;
            _cacheServiceAccessor = cacheServiceAccessor;
            _chomikujServiceAccessor = chomikujServiceAccessor;
        }

        /// <summary>
        /// Encola una lista de rutas .strm hermanas para procesar en segundo plano (fire-and-forget).
        /// </summary>
        public void EnqueueSiblings(IReadOnlyList<string> siblingPaths, string triggerItemPath)
        {
            if (siblingPaths == null || siblingPaths.Count == 0) return;

            // Lanzamiento desacoplado (fire-and-forget seguro)
            Task.Run(async () =>
            {
                try
                {
                    await ProcessSiblingsAsync(siblingPaths, triggerItemPath).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Chomyfin: Error no controlado procesando cola de hermanos para " + triggerItemPath, ex);
                }
            });
        }

        private async Task ProcessSiblingsAsync(IReadOnlyList<string> siblingPaths, string triggerItemPath)
        {
            var cacheService = _cacheServiceAccessor();
            var chomikujService = _chomikujServiceAccessor();

            if (cacheService == null || chomikujService == null)
            {
                _logger.Warn("Chomyfin: Servicios no disponibles para procesar hermanos de {0}", triggerItemPath);
                return;
            }

            var now = DateTime.UtcNow;
            var toProcess = new List<string>();

            foreach (var path in siblingPaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                // 1. Cooldown
                if (_cooldowns.TryGetValue(path, out var lastAttempt) && (now - lastAttempt) < CooldownDuration)
                {
                    continue;
                }

                // 2. Comprobación rápida y sin llamadas de red: ¿necesita resolución?
                if (!cacheService.NeedsResolution(path))
                {
                    // Ya está resuelto y vigente en disco; registramos cooldown para no volver a comprobarlo pronto
                    _cooldowns[path] = now;
                    continue;
                }

                toProcess.Add(path);
            }

            if (toProcess.Count == 0)
            {
                return;
            }

            var resolvedCount = 0;
            var skippedCount = 0;
            var failedCount = 0;

            foreach (var path in toProcess)
            {
                // Deduplicar tareas en vuelo si otra petición ya está resolviendo este mismo archivo
                var tcs = new TaskCompletionSource<bool>();
                var existingTask = _inFlightTasks.GetOrAdd(path, tcs.Task);
                if (existingTask != tcs.Task)
                {
                    // Ya se está resolviendo en este instante por otro hilo
                    skippedCount++;
                    continue;
                }

                _cooldowns[path] = DateTime.UtcNow;

                try
                {
                    using (var cts = new CancellationTokenSource(PerSiblingTimeout))
                    {
                        var directUrl = await cacheService.GetOrResolveDirectUrlAsync(path, forceResolve: false, cts.Token).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(directUrl) && ChomikujUrlHelper.IsDirectUrl(directUrl))
                        {
                            resolvedCount++;
                        }
                        else
                        {
                            failedCount++;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    failedCount++;
                    _logger.Warn("Chomyfin: Tiempo de espera agotado resolviendo hermano {0}", Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.Warn("Chomyfin: Error resolviendo hermano {0}: {1}", Path.GetFileName(path), ex.Message);
                }
                finally
                {
                    _inFlightTasks.TryRemove(path, out _);
                    tcs.TrySetResult(true);
                }
            }

            // Único log de resumen por apertura para no inundar el log de Emby
            _logger.Info("Chomyfin: Hermanos procesados para {0} (Resueltos: {1}, Omitidos/Vigentes: {2}, Fallidos: {3})",
                Path.GetFileName(triggerItemPath), resolvedCount, skippedCount, failedCount);
        }
    }
}
