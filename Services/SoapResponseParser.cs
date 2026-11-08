using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Chomyfin.Models;
using MediaBrowser.Model.Logging;

namespace Chomyfin.Services
{
    public enum FolderFilesOutcome
    {
        Files,
        EmptyFolder,
        Failed
    }

    public class FolderFilesResult
    {
        public FolderFilesOutcome Outcome { get; set; }
        public List<ChomikFileInfo> Files { get; set; } = new List<ChomikFileInfo>();
        public string ErrorMessage { get; set; }
        public string ServerStatus { get; set; }

        public static FolderFilesResult CreateFiles(List<ChomikFileInfo> files) =>
            new FolderFilesResult { Outcome = FolderFilesOutcome.Files, Files = files ?? new List<ChomikFileInfo>() };

        public static FolderFilesResult CreateEmpty(string serverStatus = null, string errorMessage = null) =>
            new FolderFilesResult { Outcome = FolderFilesOutcome.EmptyFolder, ServerStatus = serverStatus, ErrorMessage = errorMessage };

        public static FolderFilesResult CreateFailed(string errorMessage, string serverStatus = null) =>
            new FolderFilesResult { Outcome = FolderFilesOutcome.Failed, ErrorMessage = errorMessage, ServerStatus = serverStatus };
    }

    public enum FoldersOutcome
    {
        Folders,
        Empty,
        Failed
    }

    public class FoldersResult
    {
        public FoldersOutcome Outcome { get; set; }
        public List<ChomikFolderInfo> Folders { get; set; } = new List<ChomikFolderInfo>();
        public string ErrorMessage { get; set; }
        public string ServerStatus { get; set; }

        public static FoldersResult CreateFolders(List<ChomikFolderInfo> folders) =>
            new FoldersResult { Outcome = FoldersOutcome.Folders, Folders = folders ?? new List<ChomikFolderInfo>() };

        public static FoldersResult CreateEmpty(string serverStatus = null) =>
            new FoldersResult { Outcome = FoldersOutcome.Empty, ServerStatus = serverStatus };

        public static FoldersResult CreateFailed(string errorMessage, string serverStatus = null) =>
            new FoldersResult { Outcome = FoldersOutcome.Failed, ErrorMessage = errorMessage, ServerStatus = serverStatus };
    }

    /// <summary>
    /// Parseador puro para respuestas XML SOAP de Chomikuj/ChomikBox.
    /// Sin dependencias externas a .NET BCL.
    /// Navega siempre por LocalName y ruta exacta sin prefijos ni namespaces fijos.
    /// </summary>
    public static class SoapResponseParser
    {
        public const string EmptyFolderErrorMessage = "failed : requested file(s) not available";

        public static ILogger Logger { get; set; }

        /// <summary>
        /// Combina rutas normalizadas con barras inclinadas hacia adelante.
        /// </summary>
        public static string CombineNormalizedPath(string basePath, string child)
        {
            basePath = (basePath ?? string.Empty).Replace('\\', '/').Trim('/');
            child = (child ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(basePath))
            {
                return "/" + child;
            }
            return "/" + basePath + "/" + child;
        }

        /// <summary>
        /// Comprueba si un elemento se considera nulo según la especificación:
        /// - Es null
        /// - Tiene un atributo nil="true" (cualquier namespace)
        /// - Está vacío y sin hijos
        /// </summary>
        public static bool IsElementNilOrEmpty(XElement element)
        {
            if (element == null) return true;

            var hasNilAttr = element.Attributes().Any(a =>
                a.Name.LocalName.Equals("nil", StringComparison.OrdinalIgnoreCase) &&
                a.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));

            if (hasNilAttr) return true;

            if (!element.HasElements && string.IsNullOrWhiteSpace(element.Value))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Obtiene el hijo directo cuyo LocalName coincida (case-insensitive).
        /// </summary>
        public static XElement GetDirectChild(XElement parent, string localName)
        {
            if (parent == null) return null;
            return parent.Elements().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Obtiene todos los hijos directos cuyo LocalName coincida (case-insensitive).
        /// </summary>
        public static IEnumerable<XElement> GetDirectChildren(XElement parent, string localName)
        {
            if (parent == null) return Enumerable.Empty<XElement>();
            return parent.Elements().Where(e =>
                string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Obtiene el valor textual de un hijo directo o null.
        /// </summary>
        public static string GetDirectChildValue(XElement parent, string localName)
        {
            var child = GetDirectChild(parent, localName);
            return child?.Value?.Trim();
        }

        /// <summary>
        /// Extrae el nodo de resultado (ej. DownloadResult o FoldersResult) desde un sobre SOAP o fragmento XML.
        /// </summary>
        public static XElement ExtractResultElement(string xml, string actionName)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;

            try
            {
                var doc = XDocument.Parse(xml);

                // 1. SOAP Fault
                var fault = doc.Descendants().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, "Fault", StringComparison.OrdinalIgnoreCase));
                if (fault != null)
                {
                    return fault;
                }

                // 2. {Action}Result
                string targetResult = $"{actionName}Result";
                var resultEl = doc.Descendants().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, targetResult, StringComparison.OrdinalIgnoreCase));
                if (resultEl != null) return resultEl;

                // 3. {Action}Response
                string targetResp = $"{actionName}Response";
                var respEl = doc.Descendants().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, targetResp, StringComparison.OrdinalIgnoreCase));
                if (respEl != null)
                {
                    var innerResult = respEl.Elements().FirstOrDefault(e =>
                        string.Equals(e.Name.LocalName, targetResult, StringComparison.OrdinalIgnoreCase));
                    return innerResult ?? respEl;
                }

                if (string.Equals(doc.Root?.Name.LocalName, targetResult, StringComparison.OrdinalIgnoreCase))
                {
                    return doc.Root;
                }

                return null;
            }
            catch (XmlException ex)
            {
                var logger = Logger ?? ChomyfinServices.Logger;
                logger?.Warn("[SoapResponseParser] XmlException al parsear XML para {0}: {1}", actionName, ex.Message);
                return null;
            }
            catch (FormatException ex)
            {
                var logger = Logger ?? ChomyfinServices.Logger;
                logger?.Warn("[SoapResponseParser] FormatException al parsear XML para {0}: {1}", actionName, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Parsea la respuesta de Download según la tabla de decisión de la sección 2.3 de plan_fix_raiz.md.
        /// </summary>
        public static FolderFilesResult ParseDownloadResponse(string xml, string currentPath)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return FolderFilesResult.CreateFailed("Cuerpo XML vacío o no recibido de Download");
            }

            XElement downloadResult;
            try
            {
                downloadResult = ExtractResultElement(xml, "Download");
            }
            catch (Exception ex)
            {
                return FolderFilesResult.CreateFailed($"Error al parsear XML de Download: {ex.Message}");
            }

            if (downloadResult == null)
            {
                return FolderFilesResult.CreateFailed("No se pudo interpretar el documento XML o falta DownloadResult");
            }

            // Comprobar si fue Fault
            if (string.Equals(downloadResult.Name.LocalName, "Fault", StringComparison.OrdinalIgnoreCase))
            {
                var faultStr = GetDirectChildValue(downloadResult, "faultstring") ?? downloadResult.Value;
                return FolderFilesResult.CreateFailed($"SOAP Fault: {faultStr}", "Fault");
            }

            var status = GetDirectChildValue(downloadResult, "status");
            var errorMessage = GetDirectChildValue(downloadResult, "errorMessage");
            var listElement = GetDirectChild(downloadResult, "list");

            string normStatus = status?.Trim() ?? string.Empty;
            string normError = errorMessage?.Trim() ?? string.Empty;

            // Regla 1: status = Error y errorMessage normalizado = "failed : requested file(s) not available" -> EmptyFolder
            if (normStatus.Equals("Error", StringComparison.OrdinalIgnoreCase) &&
                normError.Equals(EmptyFolderErrorMessage, StringComparison.OrdinalIgnoreCase))
            {
                return FolderFilesResult.CreateEmpty(normStatus, normError);
            }

            // Regla 2: list ausente o nulo (i:nil="true") y status ausente o "Ok" -> EmptyFolder
            bool isListNilOrMissing = IsElementNilOrEmpty(listElement);
            if (isListNilOrMissing && (string.IsNullOrEmpty(normStatus) || normStatus.Equals("Ok", StringComparison.OrdinalIgnoreCase)))
            {
                return FolderFilesResult.CreateEmpty(normStatus, normError);
            }

            // Regla 3: list contiene uno o varios DownloadFolder
            if (listElement != null && !isListNilOrMissing)
            {
                var downloadFolders = GetDirectChildren(listElement, "DownloadFolder").ToList();
                if (downloadFolders.Count > 0)
                {
                    var fileList = new List<ChomikFileInfo>();

                    foreach (var df in downloadFolders)
                    {
                        var dfStatus = GetDirectChildValue(df, "status") ?? string.Empty;
                        if (dfStatus.Equals(EmptyFolderErrorMessage, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // Carpeta vacía
                        }

                        var filesElem = GetDirectChild(df, "files");
                        if (filesElem != null && !IsElementNilOrEmpty(filesElem))
                        {
                            var fileEntries = GetDirectChildren(filesElem, "FileEntry");
                            foreach (var fe in fileEntries)
                            {
                                var fId = GetDirectChildValue(fe, "id");
                                var fName = GetDirectChildValue(fe, "name");
                                var fSizeStr = GetDirectChildValue(fe, "size");
                                long.TryParse(fSizeStr, out var fSize);

                                if (!string.IsNullOrEmpty(fId) && !string.IsNullOrEmpty(fName))
                                {
                                    var combined = CombineNormalizedPath(currentPath, fName);
                                    fileList.Add(new ChomikFileInfo(fId, fName, combined, fSize));
                                }
                            }
                        }
                    }

                    if (fileList.Count == 0)
                    {
                        return FolderFilesResult.CreateEmpty(normStatus, normError);
                    }

                    return FolderFilesResult.CreateFiles(fileList);
                }
            }

            // Regla 4: status = Error con cualquier otro mensaje (o list nulo con status distinto de Ok) -> Failed
            if (normStatus.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
                (isListNilOrMissing && !string.IsNullOrEmpty(normStatus) && !normStatus.Equals("Ok", StringComparison.OrdinalIgnoreCase)))
            {
                var msg = !string.IsNullOrEmpty(normError) ? normError : $"Servidor devolvió estado '{normStatus}'";
                return FolderFilesResult.CreateFailed(msg, normStatus);
            }

            // Regla 5: Fallback si no encajó en ninguna situación esperada
            if (isListNilOrMissing)
            {
                return FolderFilesResult.CreateEmpty(normStatus, normError);
            }

            return FolderFilesResult.CreateFailed($"Respuesta de Download no interpretable (status='{normStatus}')", normStatus);
        }

        /// <summary>
        /// Parsea la respuesta de Folders de forma tolerante (sección 2.4).
        /// No lee ni expone el nodo password.
        /// Descarta el FolderInfo correspondiente al id padre.
        /// </summary>
        public static FoldersResult ParseFoldersResponse(string xml, long parentFolderId, string currentPath)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return FoldersResult.CreateFailed("Cuerpo XML vacío o no recibido de Folders");
            }

            XElement foldersResult;
            try
            {
                foldersResult = ExtractResultElement(xml, "Folders");
            }
            catch (Exception ex)
            {
                return FoldersResult.CreateFailed($"Error al parsear XML de Folders: {ex.Message}");
            }

            if (foldersResult == null)
            {
                return FoldersResult.CreateFailed("No se pudo interpretar el documento XML o falta FoldersResult");
            }

            if (string.Equals(foldersResult.Name.LocalName, "Fault", StringComparison.OrdinalIgnoreCase))
            {
                var faultStr = GetDirectChildValue(foldersResult, "faultstring") ?? foldersResult.Value;
                return FoldersResult.CreateFailed($"SOAP Fault: {faultStr}", "Fault");
            }

            var status = GetDirectChildValue(foldersResult, "status") ?? string.Empty;
            if (status.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                var errMsg = GetDirectChildValue(foldersResult, "errorMessage") ?? "Error desconocido en Folders";
                return FoldersResult.CreateFailed(errMsg, status);
            }

            // La respuesta puede estructurarse con <folder> o directamente <folders>
            var folderRoot = GetDirectChild(foldersResult, "folder") ?? foldersResult;
            var foldersContainer = GetDirectChild(folderRoot, "folders") ?? GetDirectChild(foldersResult, "folders");

            if (foldersContainer == null || IsElementNilOrEmpty(foldersContainer))
            {
                return FoldersResult.CreateEmpty(status);
            }

            var subfolders = new List<ChomikFolderInfo>();
            var folderInfoNodes = GetDirectChildren(foldersContainer, "FolderInfo");

            foreach (var fi in folderInfoNodes)
            {
                var idStr = GetDirectChildValue(fi, "id");
                var name = GetDirectChildValue(fi, "name");

                if (long.TryParse(idStr, out var id) && id != parentFolderId && !string.IsNullOrEmpty(name))
                {
                    var subPath = CombineNormalizedPath(currentPath, name);
                    // Importante: No se lee ni guarda password
                    subfolders.Add(new ChomikFolderInfo(id, name, subPath));
                }
            }

            if (subfolders.Count == 0)
            {
                return FoldersResult.CreateEmpty(status);
            }

            return FoldersResult.CreateFolders(subfolders);
        }
    }
}
