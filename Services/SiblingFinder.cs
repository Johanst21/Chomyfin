using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace Chomyfin.Services
{
    /// <summary>
    /// Localiza archivos .strm que representan versiones o calidades alternativas
    /// de la misma película o capítulo.
    /// </summary>
    public class SiblingFinder
    {
        private static readonly Regex BracketRegex = new Regex(@"\[.*?\]|\(.*?\)", RegexOptions.Compiled);
        private static readonly Regex YearRegex = new Regex(@"\b(19\d\d|20\d\d)\b", RegexOptions.Compiled);
        private static readonly Regex SxxExxRegex = new Regex(@"[sS](?<season>\d{1,2})[eE](?<ep1>\d{2,3})(?:[eE](?<ep2>\d{2,3}))?", RegexOptions.Compiled);
        private static readonly Regex SeparatorsRegex = new Regex(@"[._\-\+]+", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly HashSet<string> QualityTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1080p", "720p", "2160p", "4k", "uhd", "hdr", "hdr10", "hdr10plus", "dv", "dovi", "dolby", "vision",
            "remux", "bluray", "bdrip", "brrip", "web-dl", "webdl", "webrip", "hdtv", "dvdrip", "x264", "h264",
            "x265", "h265", "hevc", "avc", "xvid", "divx", "10bit", "8bit", "dual", "latino", "castellano",
            "spanish", "english", "subtitulado", "sub", "multi", "dts", "ac3", "aac", "atmos", "truehd", "ddp", "dd5.1"
        };

        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILibraryManager _libraryManager;

        public SiblingFinder(ILogger logger, IFileSystem fileSystem, ILibraryManager libraryManager)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _libraryManager = libraryManager;
        }

        /// <summary>
        /// Obtiene la lista de rutas .strm hermanas al ítem actual (excluyendo el propio ítem).
        /// </summary>
        public virtual List<string> FindSiblings(BaseItem item, int maxSiblings)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (item == null || string.IsNullOrWhiteSpace(item.Path) || maxSiblings <= 0)
            {
                return new List<string>();
            }

            var currentPath = item.Path;

            try
            {
                if (item is Episode episode)
                {
                    FindEpisodeSiblings(episode, currentPath, results, maxSiblings);
                }
                else if (item is Video video)
                {
                    FindVideoSiblings(video, currentPath, results, maxSiblings);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Chomyfin: Error buscando versiones hermanas para " + currentPath, ex);
            }

            results.Remove(currentPath);
            return results.Take(maxSiblings).ToList();
        }

        private void FindVideoSiblings(Video video, string currentPath, HashSet<string> results, int maxSiblings)
        {
            // 1. Versiones alternativas reconocidas por Emby
            try
            {
                var altIds = video.GetAlternateVersionIds();
                if (altIds != null && _libraryManager != null)
                {
                    foreach (var id in altIds)
                    {
                        if (results.Count >= maxSiblings) break;
                        var altItem = _libraryManager.GetItemById(id);
                        if (altItem != null && !string.IsNullOrWhiteSpace(altItem.Path) &&
                            altItem.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(altItem.Path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Chomyfin: error obteniendo AlternateVersionIds para {0}: {1}", currentPath, ex.Message);
            }

            if (results.Count >= maxSiblings) return;

            // 2. Coincidencia en el mismo directorio basada en normalización
            var dir = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(dir) || !_fileSystem.DirectoryExists(dir)) return;

            var currentFileName = Path.GetFileNameWithoutExtension(currentPath);
            var normalizedCurrent = NormalizeMediaName(currentFileName, preserveYear: true);

            try
            {
                var files = _fileSystem.GetFilePaths(dir);
                foreach (var file in files)
                {
                    if (results.Count >= maxSiblings) break;
                    if (!file.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(file, currentPath, StringComparison.OrdinalIgnoreCase)) continue;

                    var candidateName = Path.GetFileNameWithoutExtension(file);
                    var normalizedCandidate = NormalizeMediaName(candidateName, preserveYear: true);

                    if (!string.IsNullOrEmpty(normalizedCurrent) &&
                        string.Equals(normalizedCurrent, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Chomyfin: error explorando directorio para {0}: {1}", dir, ex.Message);
            }
        }

        private void FindEpisodeSiblings(Episode episode, string currentPath, HashSet<string> results, int maxSiblings)
        {
            var currentFileName = Path.GetFileNameWithoutExtension(currentPath);

            // 1. Vía ILibraryManager si tiene metadatos enlazados
            if (_libraryManager != null && episode.SeriesId != 0 && episode.ParentIndexNumber.HasValue && episode.IndexNumber.HasValue)
            {
                try
                {
                    var query = new InternalItemsQuery
                    {
                        SeriesIds = new[] { episode.SeriesId },
                        ParentIndexNumber = episode.ParentIndexNumber,
                        IndexNumber = episode.IndexNumber,
                        IncludeItemTypes = new[] { "Episode" }
                    };

                    var items = _libraryManager.GetItemList(query);
                    if (items != null)
                    {
                        foreach (var match in items)
                        {
                            if (results.Count >= maxSiblings) break;
                            if (match != null && !string.IsNullOrWhiteSpace(match.Path) &&
                                match.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                            {
                                results.Add(match.Path);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("Chomyfin: error consultando episodios en ILibraryManager para {0}: {1}", currentPath, ex.Message);
                }
            }

            if (results.Count >= maxSiblings) return;

            // 2. Respaldo por regex (SxxExx + nombre de serie)
            var sxxMatch = SxxExxRegex.Match(currentFileName);
            if (!sxxMatch.Success) return;

            int season, ep1;
            if (!int.TryParse(sxxMatch.Groups["season"].Value, out season) ||
                !int.TryParse(sxxMatch.Groups["ep1"].Value, out ep1))
            {
                _logger.Warn("Chomyfin: patrón SxxExx corrupto en '{0}'; omitiendo búsqueda por regex", currentPath);
                return;
            }

            int? ep2 = null;
            if (sxxMatch.Groups["ep2"].Success)
            {
                int ep2Parsed;
                if (!int.TryParse(sxxMatch.Groups["ep2"].Value, out ep2Parsed))
                {
                    _logger.Warn("Chomyfin: patrón SxxExx corrupto (ep2) en '{0}'; omitiendo búsqueda por regex", currentPath);
                    return;
                }
                ep2 = ep2Parsed;
            }

            var dir = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(dir) || !_fileSystem.DirectoryExists(dir)) return;

            // Buscar en la misma carpeta del episodio
            ScanFolderForEpisodeSiblings(dir, season, ep1, ep2, currentPath, results, maxSiblings);

            // Si todavía hay espacio, buscar en carpetas hermanas del mismo nivel (ej: otras temporadas o calidades de la misma serie)
            if (results.Count < maxSiblings)
            {
                try
                {
                    var parentDir = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(parentDir) && _fileSystem.DirectoryExists(parentDir))
                    {
                        var siblingDirs = _fileSystem.GetDirectoryPaths(parentDir);
                        foreach (var subDir in siblingDirs)
                        {
                            if (results.Count >= maxSiblings) break;
                            if (string.Equals(subDir, dir, StringComparison.OrdinalIgnoreCase)) continue;
                            ScanFolderForEpisodeSiblings(subDir, season, ep1, ep2, currentPath, results, maxSiblings);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("Chomyfin: error buscando en carpetas hermanas para {0}: {1}", dir, ex.Message);
                }
            }
        }

        private void ScanFolderForEpisodeSiblings(string folder, int targetSeason, int targetEp1, int? targetEp2, string currentPath, HashSet<string> results, int maxSiblings)
        {
            try
            {
                var files = _fileSystem.GetFilePaths(folder);
                foreach (var file in files)
                {
                    if (results.Count >= maxSiblings) break;
                    if (!file.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(file, currentPath, StringComparison.OrdinalIgnoreCase)) continue;

                    var candidateName = Path.GetFileNameWithoutExtension(file);
                    var match = SxxExxRegex.Match(candidateName);
                    if (match.Success)
                    {
                        int s, e1;
                        if (!int.TryParse(match.Groups["season"].Value, out s) ||
                            !int.TryParse(match.Groups["ep1"].Value, out e1))
                        {
                            continue; // Candidado corrupto: se omite
                        }

                        int? e2 = null;
                        if (match.Groups["ep2"].Success)
                        {
                            int e2Parsed;
                            if (!int.TryParse(match.Groups["ep2"].Value, out e2Parsed))
                            {
                                continue; // Candidado corrupto: se omite
                            }
                            e2 = e2Parsed;
                        }

                        if (s == targetSeason && e1 == targetEp1 && e2 == targetEp2)
                        {
                            results.Add(file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Chomyfin: error escaneando carpeta {0}: {1}", folder, ex.Message);
            }
        }

        /// <summary>
        /// Normaliza el nombre de un archivo multimedia para comparación de versiones.
        /// Función pura y determinista.
        /// </summary>
        public static string NormalizeMediaName(string name, bool preserveYear = true)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            string year = null;
            if (preserveYear)
            {
                var ym = YearRegex.Match(name);
                if (ym.Success)
                {
                    year = ym.Groups[1].Value;
                }
            }

            // Eliminar texto entre corchetes o paréntesis
            var cleaned = BracketRegex.Replace(name, " ");

            // Eliminar diacríticos
            cleaned = RemoveDiacritics(cleaned);

            // Colapsar separadores comunes
            cleaned = SeparatorsRegex.Replace(cleaned, " ");

            // Procesar tokens
            var parts = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();

            foreach (var part in parts)
            {
                var p = part.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(p)) continue;

                // Omitir tokens de calidad / codec / audio
                if (QualityTokens.Contains(p)) continue;

                // Si no se preserva año, omitir tokens de 4 dígitos tipo 19xx/20xx
                if (!preserveYear && YearRegex.IsMatch(p)) continue;

                kept.Add(p);
            }

            var result = string.Join(" ", kept).Trim();
            result = MultiSpaceRegex.Replace(result, " ");

            if (preserveYear && !string.IsNullOrEmpty(year) && !result.Contains(year))
            {
                result = $"{result} {year}".Trim();
            }

            return result;
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(normalizedString.Length);

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
