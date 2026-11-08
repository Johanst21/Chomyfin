using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Chomyfin.Models;
using MediaBrowser.Model.Logging;

namespace Chomyfin.Services
{
    public class StrmGenerator
    {
        private static readonly SemaphoreSlim _executionLock = new SemaphoreSlim(1, 1);
        private readonly ILogger _logger;
        private readonly Func<StrmGeneratorOptions, IChomikBoxClient> _clientFactory;

        public StrmGenerator(ILogger logger, Func<StrmGeneratorOptions, IChomikBoxClient> clientFactory = null)
        {
            _logger = logger;
            _clientFactory = clientFactory ?? (opt => new ChomikBoxClient(
                logger,
                null,
                opt?.EnableSoapDiagnostics ?? false,
                opt?.DiagnosticsDirectory));
        }

        public async Task<StrmGeneratorResult> GenerateAsync(
            StrmGeneratorOptions options,
            IProgress<double> progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new StrmGeneratorResult
            {
                StartTime = DateTime.UtcNow
            };

            if (options == null)
            {
                result.Success = false;
                result.Message = "Opciones no especificadas.";
                result.EndTime = DateTime.UtcNow;
                return result;
            }

            if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrEmpty(options.Password))
            {
                result.Success = false;
                result.Message = "Usuario y contraseña requeridos.";
                result.EndTime = DateTime.UtcNow;
                return result;
            }

            if (string.IsNullOrWhiteSpace(options.DestinationPath))
            {
                result.Success = false;
                result.Message = "Ruta de destino no configurada.";
                result.EndTime = DateTime.UtcNow;
                return result;
            }

            if (!await _executionLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                result.Success = false;
                result.Message = "Ya hay una generación de .strm en curso. Espera a que termine.";
                result.EndTime = DateTime.UtcNow;
                return result;
            }

            try
            {
                string fullDestinationPath;
                try
                {
                    fullDestinationPath = Path.GetFullPath(options.DestinationPath);
                    if (!Directory.Exists(fullDestinationPath))
                    {
                        Directory.CreateDirectory(fullDestinationPath);
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"No se pudo preparar la carpeta de destino: {ex.Message}";
                    return result;
                }

                var allowedExtensions = new HashSet<string>(
                    (options.AllowedExtensions ?? new List<string>())
                        .Select(e => e.StartsWith(".") ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()),
                    StringComparer.OrdinalIgnoreCase
                );

                var generatedStrmFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var stopwatchTotal = Stopwatch.StartNew();
                long totalDelayMs = 0;
                int foldersCalls = 0;
                int foldersFailed = 0;
                int downloadCalls = 0;
                int downloadFailed = 0;
                int foldersVisited = 0;
                int foldersEmpty = 0;
                int foldersWithErrors = 0;
                int consecutiveFailures = 0;
                const int MaxConsecutiveFailures = 5;
                int totalApiFilesDiscovered = 0;
                int totalFilesDiscardedByFilter = 0;
                var extensionFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                using (var client = _clientFactory(options))
                {
                    try
                    {
                        _logger?.Info("[StrmGenerator] Iniciando sesión SOAP con Chomikuj ({0})...", options.Username);
                    var loginOk = await client.LoginAsync(options.Username, options.Password, cancellationToken).ConfigureAwait(false);
                    if (!loginOk)
                    {
                        result.Success = false;
                        result.Message = "Fallo de autenticación con la cuenta de Chomikuj.";
                        result.EndTime = DateTime.UtcNow;
                        return result;
                    }

                    progress?.Report(5.0);

                    // 1. Determinar carpetas raíz de inicio
                    var foldersToScan = new Queue<ChomikFolderInfo>();
                    var scannedRoots = new List<string>();

                    if (options.SyncMode == StrmSyncMode.FullAccount)
                    {
                        foldersToScan.Enqueue(new ChomikFolderInfo(0, "Root", "/"));
                        scannedRoots.Add(fullDestinationPath);
                    }
                    else
                    {
                        // Modo Carpetas Seleccionadas
                        var validTargets = options.SelectedFolders?
                            .Where(f => !string.IsNullOrWhiteSpace(f) && !string.Equals(f.Trim(), "SelectedFolders", StringComparison.OrdinalIgnoreCase))
                            .Select(f => f.Trim())
                            .ToList();

                        if (validTargets == null || validTargets.Count == 0)
                        {
                            var errMsg = "Error: El modo Carpetas Seleccionadas está activo pero no se ha especificado ninguna ruta.";
                            _logger?.Error("[StrmGenerator] {0}", errMsg);
                            result.Success = false;
                            result.Message = errMsg;
                            result.AddError(errMsg);
                            result.EndTime = DateTime.UtcNow;
                            return result;
                        }

                        foreach (var targetPath in validTargets)
                        {
                            if (targetPath == "/")
                            {
                                foldersToScan.Enqueue(new ChomikFolderInfo(0, "Root", "/"));
                                scannedRoots.Add(fullDestinationPath);
                                continue;
                            }

                            var resolvedFolder = await ResolveRemotePathAsync(client, targetPath, cancellationToken).ConfigureAwait(false);
                            if (resolvedFolder != null)
                            {
                                foldersToScan.Enqueue(resolvedFolder);
                                var pathSegments = (resolvedFolder.Path ?? string.Empty)
                                    .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(SanitizePathSegment)
                                    .ToArray();
                                var resolvedLocalRoot = pathSegments.Length > 0
                                    ? Path.Combine(fullDestinationPath, Path.Combine(pathSegments))
                                    : fullDestinationPath;
                                scannedRoots.Add(resolvedLocalRoot);
                            }
                            else
                            {
                                var warnMsg = $"Carpeta remota no encontrada: {targetPath}";
                                _logger?.Warn("[StrmGenerator] {0}", warnMsg);
                                result.AddError(warnMsg);
                            }
                        }

                        if (foldersToScan.Count == 0)
                        {
                            var errMsg = "Error: Ninguna de las carpetas seleccionadas especificadas pudo ser encontrada en Chomikuj.";
                            _logger?.Error("[StrmGenerator] {0}", errMsg);
                            result.Success = false;
                            result.Message = errMsg;
                            result.AddError(errMsg);
                            result.EndTime = DateTime.UtcNow;
                            return result;
                        }
                    }

                    // 2. Recorrido iterativo de carpetas
                    var discoveredFiles = new List<ChomikFileInfo>();

                    while (foldersToScan.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var currentFolder = foldersToScan.Dequeue();
                        foldersVisited++;
                        var folderStopwatch = Stopwatch.StartNew();

                        var trimmedPath = (currentFolder.Path ?? string.Empty).Trim('/');
                        var rawPath = string.IsNullOrEmpty(trimmedPath)
                            ? $"/{options.Username}/"
                            : $"/{options.Username}/{trimmedPath}/";
                        var encodedPathId = ChomikBoxClient.EscapePathId(rawPath);

                        bool hadFolderError = false;

                        // 1. Primero obtener y encolar subcarpetas (evita perder el árbol si fallan archivos)
                        foldersCalls++;
                        List<ChomikFolderInfo> subfolders = new List<ChomikFolderInfo>();
                        try
                        {
                            subfolders = await client.GetSubfoldersAsync(currentFolder.Id, currentFolder.Path, cancellationToken).ConfigureAwait(false);
                            foreach (var sub in subfolders)
                            {
                                foldersToScan.Enqueue(sub);
                            }
                            consecutiveFailures = 0; // Éxito en subcarpetas
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            foldersFailed++;
                            hadFolderError = true;
                            consecutiveFailures++;
                            var err = $"Error obteniendo subcarpetas de '{currentFolder.Path}' (Id={currentFolder.Id}): {ex.Message}";
                            _logger?.Error("[StrmGenerator] {0}", err);
                            result.AddError(err);

                            if (consecutiveFailures >= MaxConsecutiveFailures)
                            {
                                throw new StrmGeneratorAbortException($"[StrmGenerator] Abortando sincronización: se alcanzaron {consecutiveFailures} fallos consecutivos en llamadas a la API de Chomikuj.");
                            }
                        }

                        // 2. Después pedir los archivos de la carpeta
                        downloadCalls++;
                        int filesInThisFolderBeforeFilter = 0;
                        try
                        {
                            var filesResult = await client.GetFilesInFolderAsync(options.Username, currentFolder.Id, currentFolder.Path, cancellationToken).ConfigureAwait(false);

                            switch (filesResult.Outcome)
                            {
                                case FolderFilesOutcome.EmptyFolder:
                                    foldersEmpty++;
                                    consecutiveFailures = 0; // Caso legítimo
                                    _logger?.Debug("[StrmGenerator] Carpeta vacía sin archivos: Id={0} | Path='{1}' | RawPath='{2}' | PathId='{3}'",
                                        currentFolder.Id, currentFolder.Path, rawPath, encodedPathId);
                                    break;

                                case FolderFilesOutcome.Files:
                                    consecutiveFailures = 0;
                                    var files = filesResult.Files;
                                    filesInThisFolderBeforeFilter = files.Count;
                                    totalApiFilesDiscovered += filesInThisFolderBeforeFilter;

                                    foreach (var f in files)
                                    {
                                        var ext = Path.GetExtension(f.Name)?.ToLowerInvariant() ?? string.Empty;
                                        if (!string.IsNullOrEmpty(ext))
                                        {
                                            extensionFrequencies.TryGetValue(ext, out var count);
                                            extensionFrequencies[ext] = count + 1;
                                        }

                                        if (allowedExtensions.Contains(ext))
                                        {
                                            discoveredFiles.Add(f);
                                        }
                                        else
                                        {
                                            totalFilesDiscardedByFilter++;
                                        }
                                    }
                                    break;

                                case FolderFilesOutcome.Failed:
                                    downloadFailed++;
                                    hadFolderError = true;
                                    consecutiveFailures++;
                                    var err = $"Fallo obteniendo archivos de '{currentFolder.Path}' (Id={currentFolder.Id}, pathId='{encodedPathId}'): {filesResult.ErrorMessage}";
                                    _logger?.Error("[StrmGenerator] {0}", err);
                                    result.AddError(err);

                                    if (consecutiveFailures >= MaxConsecutiveFailures)
                                    {
                                        throw new StrmGeneratorAbortException($"[StrmGenerator] Abortando sincronización: se alcanzaron {consecutiveFailures} fallos consecutivos en llamadas a la API de Chomikuj.");
                                    }
                                    break;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex) when (!(ex is StrmGeneratorAbortException))
                        {
                            downloadFailed++;
                            hadFolderError = true;
                            consecutiveFailures++;
                            var err = $"Excepción obteniendo archivos de '{currentFolder.Path}' (Id={currentFolder.Id}, pathId='{encodedPathId}'): {ex.Message}";
                            _logger?.Error("[StrmGenerator] {0}", err);
                            result.AddError(err);

                            if (consecutiveFailures >= MaxConsecutiveFailures)
                            {
                                throw new StrmGeneratorAbortException($"[StrmGenerator] Abortando sincronización: se alcanzaron {consecutiveFailures} fallos consecutivos en llamadas a la API de Chomikuj.");
                            }
                        }

                        if (hadFolderError)
                        {
                            foldersWithErrors++;
                        }

                        folderStopwatch.Stop();
                        _logger?.Info("[StrmGenerator Folder] Id: {0} | Ruta: '{1}' | PathIdRaw: '{2}' | PathIdEncoded: '{3}' | Subcarpetas: {4} | FileEntries API: {5} | Tiempo: {6} ms",
                            currentFolder.Id, currentFolder.Path, rawPath, encodedPathId, subfolders.Count, filesInThisFolderBeforeFilter, folderStopwatch.ElapsedMilliseconds);

                        if (options.DelayBetweenRequestsMs > 0)
                        {
                            totalDelayMs += options.DelayBetweenRequestsMs;
                            await Task.Delay(options.DelayBetweenRequestsMs, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    stopwatchTotal.Stop();
                    result.FilesDiscovered = discoveredFiles.Count;

                    // A3: Resumen final detallado
                    var topExtensions = extensionFrequencies.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key} ({kv.Value})");
                    var effectiveExts = string.Join(", ", allowedExtensions);

                    _logger?.Info("[StrmGenerator Resumen]\n" +
                                  "====================================================\n" +
                                  "- Llamadas Folders: {0} (Fallidas: {1})\n" +
                                  "- Llamadas Download: {2} (Fallidas: {3})\n" +
                                  "- Carpetas visitadas: {4} (Sin archivos legítimas: {5}, Con errores: {6})\n" +
                                  "- Total de archivos devueltos por la API (antes de filtro): {7}\n" +
                                  "- Total descartados por filtro de extensiones: {8}\n" +
                                  "- Total finalmente aceptados: {9}\n" +
                                  "- Extensiones efectivas aplicadas: [{10}]\n" +
                                  "- Top 10 extensiones vistas en API: [{11}]\n" +
                                  "- Tiempo total: {12} ms (Tiempo en pausas de limitación: {13} ms)\n" +
                                  "====================================================",
                                  foldersCalls, foldersFailed, downloadCalls, downloadFailed, foldersVisited, foldersEmpty, foldersWithErrors,
                                  totalApiFilesDiscovered, totalFilesDiscardedByFilter, discoveredFiles.Count,
                                  effectiveExts, string.Join(", ", topExtensions), stopwatchTotal.ElapsedMilliseconds, totalDelayMs);

                    // Si no se descubrió ningún archivo Y hubo fallos, marcar ejecución como fallida
                    if (discoveredFiles.Count == 0 && (foldersFailed > 0 || downloadFailed > 0 || foldersWithErrors > 0))
                    {
                        result.Success = false;
                        result.Message = $"Sincronización finalizada con fallos y 0 archivos descubiertos ({foldersWithErrors} carpetas con errores).";
                        return result;
                    }

                    // 3. Generación de archivos .strm
                    var fileAssignments = AssignStrmFileNames(discoveredFiles, fullDestinationPath, _logger);

                    for (int i = 0; i < discoveredFiles.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var file = discoveredFiles[i];
                        var (safeRelativeDir, assignedStrmName) = fileAssignments[file];
                        var targetStrmPath = Path.Combine(fullDestinationPath, safeRelativeDir, assignedStrmName + ".strm");

                        try
                        {
                            FileProcessStatus processStatus;
                            using (await AtomicFile.AcquireLockAsync(targetStrmPath, cancellationToken).ConfigureAwait(false))
                            {
                                processStatus = ProcessFileStrm(
                                    options.Username,
                                    file,
                                    safeRelativeDir,
                                    assignedStrmName,
                                    fullDestinationPath,
                                    options.OverwritePolicy,
                                    generatedStrmFiles);
                            }

                            switch (processStatus)
                            {
                                case FileProcessStatus.Created:
                                    result.FilesCreated++;
                                    break;
                                case FileProcessStatus.Updated:
                                    result.FilesUpdated++;
                                    break;
                                case FileProcessStatus.Skipped:
                                    result.FilesSkipped++;
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.FilesFailed++;
                            var err = $"Error generando .strm para {file.Path}: {ex.ToString()}";
                            _logger?.Error("[StrmGenerator] {0}", err);
                            result.AddError(err);
                        }

                        if ((i + 1) % 10 == 0 || i == discoveredFiles.Count - 1)
                        {
                            double pct = 10.0 + ((double)(i + 1) / discoveredFiles.Count) * 80.0;
                            progress?.Report(pct);
                        }
                    }

                    // 4. Limpieza de huérfanos (si procede)
                    if (options.DeleteOrphans && result.Errors.Count == 0 && discoveredFiles.Count > 0)
                    {
                        try
                        {
                            result.OrphansDeleted = CleanOrphans(
                                fullDestinationPath,
                                generatedStrmFiles,
                                scannedRoots,
                                options.OrphansDryRun,
                                _logger);
                        }
                        catch (Exception ex)
                        {
                            _logger?.Error("[StrmGenerator] Error limpiando huérfanos: {0}", ex.ToString());
                            result.AddError($"Error limpiando huérfanos: {ex.Message}");
                        }
                    }

                    progress?.Report(100.0);
                    result.Success = true;
                    result.Message = $"Sincronización completada. API: {totalApiFilesDiscovered}, Aceptados: {discoveredFiles.Count}, Creados: {result.FilesCreated}, Actualizados: {result.FilesUpdated}, Omitidos: {result.FilesSkipped}, Errores: {result.FilesFailed}.";
                }
                finally
                {
                    try
                    {
                        await client.LogoutAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug("[StrmGenerator] Excepción en Logout: {0}", ex.ToString());
                    }
                }
            }
            }
            finally
            {
                _executionLock.Release();
                result.EndTime = DateTime.UtcNow;
            }

            return result;
        }

        private async Task<ChomikFolderInfo> ResolveRemotePathAsync(IChomikBoxClient client, string path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "/")
            {
                return new ChomikFolderInfo(0, "Root", "/");
            }

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            long currentFolderId = 0;
            string accumulatedPath = string.Empty;
            ChomikFolderInfo lastMatched = null;

            foreach (var segment in segments)
            {
                var subfolders = await client.GetSubfoldersAsync(currentFolderId, accumulatedPath, cancellationToken).ConfigureAwait(false);
                var match = subfolders.FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    return null;
                }

                currentFolderId = match.Id;
                accumulatedPath = match.Path;
                lastMatched = match;
            }

            return lastMatched;
        }

        private static readonly Regex FileIdFromUrlRegex = new Regex(@",(\d+)(?:\.[A-Za-z0-9]+)?(?:\?|$)", RegexOptions.Compiled);

        internal static Dictionary<ChomikFileInfo, (string SafeRelativeDir, string AssignedStrmName)> AssignStrmFileNames(
            List<ChomikFileInfo> discoveredFiles,
            string destinationRoot,
            ILogger logger)
        {
            var result = new Dictionary<ChomikFileInfo, (string SafeRelativeDir, string AssignedStrmName)>();
            if (discoveredFiles == null || discoveredFiles.Count == 0)
            {
                return result;
            }

            // 1. Pre-pasada sobre discoveredFiles: agrupar por ruta final calculada
            // (carpeta saneada + nombre base saneado, comparación insensible a mayúsculas)
            var groups = new Dictionary<string, List<ChomikFileInfo>>(StringComparer.OrdinalIgnoreCase);
            var fileDirMap = new Dictionary<ChomikFileInfo, string>();
            var fileBaseMap = new Dictionary<ChomikFileInfo, string>();

            foreach (var file in discoveredFiles)
            {
                var relativePath = (file.Path ?? string.Empty).TrimStart('/');
                var relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file.Name ?? string.Empty);

                var dirParts = relativeDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(SanitizePathSegment)
                    .ToArray();

                var safeRelativeDir = Path.Combine(dirParts);
                var safeBaseName = SanitizePathSegment(fileNameWithoutExt);

                fileDirMap[file] = safeRelativeDir;
                fileBaseMap[file] = safeBaseName;

                var groupKey = Path.Combine(safeRelativeDir, safeBaseName);
                if (!groups.TryGetValue(groupKey, out var list))
                {
                    list = new List<ChomikFileInfo>();
                    groups[groupKey] = list;
                }
                list.Add(file);
            }

            foreach (var kvp in groups)
            {
                var groupKey = kvp.Key;
                var members = kvp.Value;

                if (members.Count == 1)
                {
                    var file = members[0];
                    result[file] = (fileDirMap[file], fileBaseMap[file]);
                    continue;
                }

                // Grupos con >1 miembro = colisión.
                // 2. Elegir el PROPIETARIO del nombre base dentro de cada grupo:
                var safeDir = fileDirMap[members[0]];
                var safeBase = fileBaseMap[members[0]];
                var targetDirectory = Path.Combine(destinationRoot, safeDir);

                var fullStrmPath = Path.Combine(targetDirectory, safeBase + ".strm");
                var originalPath = Path.Combine(targetDirectory, safeBase + ".original");

                ChomikFileInfo owner = null;
                string existingUrl = null;

                // Bloqueo compartido con AtomicFile para evitar leer archivos .original o .strm a medias si se están actualizando concurrentemente
                using (AtomicFile.AcquireLock(fullStrmPath))
                {
                    if (File.Exists(originalPath))
                    {
                        try
                        {
                            existingUrl = File.ReadAllText(originalPath, Encoding.UTF8)?.Trim();
                        }
                        catch (Exception ex)
                        {
                            logger?.Debug("[StrmGenerator] Error leyendo {0}: {1}", originalPath, ex.ToString());
                        }
                    }
                    else if (File.Exists(fullStrmPath))
                    {
                        try
                        {
                            existingUrl = File.ReadAllText(fullStrmPath, Encoding.UTF8)?.Trim();
                        }
                        catch (Exception ex)
                        {
                            logger?.Debug("[StrmGenerator] Error leyendo {0}: {1}", fullStrmPath, ex.ToString());
                        }
                    }
                }

                if (!string.IsNullOrEmpty(existingUrl))
                {
                    var match = FileIdFromUrlRegex.Match(existingUrl);
                    if (match.Success)
                    {
                        var matchedId = match.Groups[1].Value;
                        owner = members.FirstOrDefault(m => string.Equals(m.Id, matchedId, StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (owner == null)
                {
                    // No se pudo determinar: el propietario es el miembro de menor Id numérico; registrar un Warn
                    owner = members
                        .OrderBy(m =>
                        {
                            if (long.TryParse(m.Id, out var parsedId)) return parsedId;
                            return long.MaxValue;
                        })
                        .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                        .First();

                    logger?.Warn("[StrmGenerator] Colisión de nombre detectada en carpeta '{0}', archivo base '{1}'. Propietario asignado por menor Id: Id={2}",
                        safeDir, safeBase, owner.Id);
                }

                result[owner] = (safeDir, safeBase);

                // 3. Los demás miembros usan el nombre {base} [{ext sin punto}] (p.ej. Peli [mp4]). Si aún colisionan entre sí, añadir -{Id}.
                var nonOwners = members.Where(m => !ReferenceEquals(m, owner)).ToList();
                var subGroups = nonOwners.GroupBy(m =>
                {
                    var ext = (Path.GetExtension(m.Name ?? string.Empty) ?? string.Empty).TrimStart('.');
                    return $"{safeBase} [{ext}]";
                }, StringComparer.OrdinalIgnoreCase);

                foreach (var subGroup in subGroups)
                {
                    var subList = subGroup.ToList();
                    if (subList.Count == 1)
                    {
                        result[subList[0]] = (safeDir, subGroup.Key);
                    }
                    else
                    {
                        foreach (var item in subList)
                        {
                            result[item] = (safeDir, $"{subGroup.Key}-{item.Id}");
                        }
                    }
                }
            }

            return result;
        }

        private FileProcessStatus ProcessFileStrm(
            string username,
            ChomikFileInfo file,
            string safeRelativeDir,
            string assignedStrmFileName,
            string destinationRoot,
            StrmOverwritePolicy policy,
            HashSet<string> generatedStrmFiles)
        {
            var targetDirectory = Path.Combine(destinationRoot, safeRelativeDir);
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var fullStrmPath = Path.Combine(targetDirectory, assignedStrmFileName + ".strm");
            var originalPath = Path.Combine(targetDirectory, assignedStrmFileName + ".original");

            generatedStrmFiles.Add(Path.GetFullPath(fullStrmPath));

            var expectedUrl = BuildChomikUrl(username, file);

            if (!File.Exists(fullStrmPath))
            {
                AtomicFile.WriteAllText(fullStrmPath, expectedUrl, Encoding.UTF8);
                return FileProcessStatus.Created;
            }

            if (policy == StrmOverwritePolicy.SkipIfExists && !File.Exists(originalPath))
            {
                return FileProcessStatus.Skipped;
            }

            string currentEffectiveUrl = null;
            if (File.Exists(originalPath))
            {
                currentEffectiveUrl = File.ReadAllText(originalPath, Encoding.UTF8)?.Trim();
            }
            else
            {
                currentEffectiveUrl = File.ReadAllText(fullStrmPath, Encoding.UTF8)?.Trim();
            }

            if (string.Equals(currentEffectiveUrl, expectedUrl, StringComparison.OrdinalIgnoreCase))
            {
                return FileProcessStatus.Skipped;
            }

            if (policy == StrmOverwritePolicy.UpdateIfUrlChanged)
            {
                AtomicFile.WriteAllText(fullStrmPath, expectedUrl, Encoding.UTF8);
                if (File.Exists(originalPath))
                {
                    try { File.Delete(originalPath); } catch (Exception ex) { _logger?.Debug("[StrmGenerator] Error eliminando {0}: {1}", originalPath, ex.ToString()); }
                }
                return FileProcessStatus.Updated;
            }

            return FileProcessStatus.Skipped;
        }

        public static string BuildChomikUrl(string username, ChomikFileInfo file)
        {
            var cleanPath = file.Path.TrimStart('/');
            var ext = Path.GetExtension(cleanPath) ?? string.Empty;
            var pathPure = !string.IsNullOrEmpty(ext) && cleanPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                ? cleanPath.Substring(0, cleanPath.Length - ext.Length)
                : cleanPath;

            var pId = $"{pathPure},{file.Id}{ext}";
            var quoted = EscapeUrlSegment(pId).Replace("%20", "+");

            return $"https://chomikuj.pl/{username}/{quoted}";
        }

        public static string EscapeUrlSegment(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var sb = new StringBuilder();
            var bytes = Encoding.UTF8.GetBytes(value);

            foreach (var b in bytes)
            {
                char c = (char)b;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                    c == '-' || c == '_' || c == '.' || c == '~' || c == '(' || c == ')' || c == '/' || c == ',')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('%');
                    sb.Append(b.ToString("X2"));
                }
            }

            return sb.ToString();
        }

        private static string SanitizePathSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(segment.Length);
            for (int i = 0; i < segment.Length; i++)
            {
                char c = segment[i];
                if (c == '/' || c == '\\' || invalid.Contains(c))
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim(' ', '.');
        }

        private static int CleanOrphans(
            string destinationPath,
            HashSet<string> validStrmFiles,
            List<string> scannedRoots,
            bool orphansDryRun,
            ILogger logger)
        {
            if (scannedRoots == null || scannedRoots.Count == 0)
            {
                return 0;
            }

            var normalizedRoots = scannedRoots
                .Where(r => !string.IsNullOrEmpty(r))
                .Select(r =>
                {
                    var full = Path.GetFullPath(r);
                    return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                })
                .ToList();

            if (normalizedRoots.Count == 0)
            {
                return 0;
            }

            int deletedCount = 0;
            var filesToDelete = new List<string>();
            var allStrms = Directory.GetFiles(destinationPath, "*.strm", SearchOption.AllDirectories);

            foreach (var strm in allStrms)
            {
                var fullPath = Path.GetFullPath(strm);

                bool withinScannedRoots = false;
                for (int i = 0; i < normalizedRoots.Count; i++)
                {
                    if (fullPath.StartsWith(normalizedRoots[i], StringComparison.OrdinalIgnoreCase))
                    {
                        withinScannedRoots = true;
                        break;
                    }
                }

                if (!withinScannedRoots)
                {
                    continue;
                }

                if (!validStrmFiles.Contains(fullPath))
                {
                    filesToDelete.Add(fullPath);
                }
            }

            // También detectar archivos .original que hayan quedado huérfanos sin su .strm correspondiente
            var originalFilesToDelete = new List<string>();
            var allOriginals = Directory.GetFiles(destinationPath, "*.original", SearchOption.AllDirectories);
            foreach (var orig in allOriginals)
            {
                var fullPath = Path.GetFullPath(orig);

                bool withinScannedRoots = false;
                for (int i = 0; i < normalizedRoots.Count; i++)
                {
                    if (fullPath.StartsWith(normalizedRoots[i], StringComparison.OrdinalIgnoreCase))
                    {
                        withinScannedRoots = true;
                        break;
                    }
                }

                if (!withinScannedRoots)
                {
                    continue;
                }

                var companionStrm = Path.ChangeExtension(fullPath, ".strm");
                if (!File.Exists(companionStrm) || !validStrmFiles.Contains(companionStrm))
                {
                    originalFilesToDelete.Add(fullPath);
                }
            }

            if (orphansDryRun)
            {
                logger?.Info("[ORPHANS-DRYRUN] Se eliminarían {0} .strm", filesToDelete.Count);
                for (int i = 0; i < filesToDelete.Count && i < 50; i++)
                {
                    logger?.Info("[ORPHANS-DRYRUN] {0}", filesToDelete[i]);
                }
                return 0;
            }

            foreach (var fullPath in filesToDelete)
            {
                try
                {
                    File.Delete(fullPath);
                    deletedCount++;
                    var origPath = Path.ChangeExtension(fullPath, ".original");
                    if (File.Exists(origPath))
                    {
                        File.Delete(origPath);
                    }
                }
                catch (Exception ex)
                {
                    // No tragar silenciosamente
                    logger?.Error("[StrmGenerator] Error limpiando huérfano {0}: {1}", fullPath, ex.ToString());
                }
            }

            foreach (var origPath in originalFilesToDelete)
            {
                try
                {
                    if (File.Exists(origPath))
                    {
                        File.Delete(origPath);
                    }
                }
                catch (Exception ex)
                {
                    logger?.Error("[StrmGenerator] Error limpiando .original huérfano {0}: {1}", origPath, ex.ToString());
                }
            }

            var destFullPath = Path.GetFullPath(destinationPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var preservedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { destFullPath };
            foreach (var r in scannedRoots)
            {
                preservedRoots.Add(Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            foreach (var root in scannedRoots)
            {
                if (Directory.Exists(root))
                {
                    CleanEmptyDirectories(root, preservedRoots);
                }
            }

            return deletedCount;
        }

        private static void CleanEmptyDirectories(string path, HashSet<string> preservedRoots)
        {
            foreach (var directory in Directory.GetDirectories(path))
            {
                CleanEmptyDirectories(directory, preservedRoots);
                var fullDir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (preservedRoots.Contains(fullDir))
                {
                    continue;
                }

                if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
                {
                    try { Directory.Delete(directory, false); } catch { }
                }
            }
        }

        private enum FileProcessStatus
        {
            Created,
            Updated,
            Skipped
        }
    }

    /// <summary>
    /// Excepción de control usada para abortar una sincronización de .strm
    /// al superar el umbral de fallos consecutivos en llamadas a la API.
    /// </summary>
    public class StrmGeneratorAbortException : Exception
    {
        public StrmGeneratorAbortException(string message) : base(message) { }
    }
}
