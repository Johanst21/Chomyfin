using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Chomyfin.Models;
using MediaBrowser.Model.Logging;

namespace Chomyfin.Services
{
    public class ChomikBoxClient : IChomikBoxClient
    {
        private const string ServiceUrl = "https://box.chomikuj.pl/services/ChomikBoxService.svc";
        private const string SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
        private const string ChomikNamespace = "http://chomikuj.pl/";

        private readonly HttpClient _httpClient;
        private readonly bool _disposeHttpClient;
        private readonly ILogger _logger;
        private readonly bool _enableSoapDiagnostics;
        private readonly string _diagnosticsDir;

        private static int _dumpCounter = 0;
        private const int MaxDumpsPerExecution = 20;

        private int _foldersCount = 0;
        private int _downloadCount = 0;

        private string _token;
        private long _hamsterId;
        private bool _disposed;

        public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
        public long HamsterId => _hamsterId;
        public string Token => _token;

        public ChomikBoxClient(ILogger logger = null, HttpClient httpClient = null, bool enableSoapDiagnostics = false, string diagnosticsDir = null)
        {
            _logger = logger;
            _enableSoapDiagnostics = enableSoapDiagnostics;
            _diagnosticsDir = diagnosticsDir;

            if (_enableSoapDiagnostics && !string.IsNullOrWhiteSpace(_diagnosticsDir))
            {
                try
                {
                    if (!Directory.Exists(_diagnosticsDir))
                    {
                        Directory.CreateDirectory(_diagnosticsDir);
                    }
                    else
                    {
                        // Retención: eliminar archivos de diag/ con más de 7 días
                        PurgeOldDiagnosticsFiles(_diagnosticsDir, TimeSpan.FromDays(7));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warn("[ChomikBoxClient] No se pudo crear o purgar el directorio de diagnósticos '{0}': {1}", _diagnosticsDir, ex.Message);
                }
            }

            if (httpClient != null)
            {
                _httpClient = httpClient;
                _disposeHttpClient = false;
            }
            else
            {
                _httpClient = new HttpClient();
                _httpClient.Timeout = TimeSpan.FromSeconds(60);
                _disposeHttpClient = true;
            }
        }

        public async Task<bool> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                _logger?.Warn("[ChomikBoxClient] Credenciales vacías para login SOAP.");
                return false;
            }

            string passHash;
            using (var md5 = MD5.Create())
            {
                var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(password));
                var sb = new StringBuilder(hashBytes.Length * 2);
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                passHash = sb.ToString();
            }

            var requestXml = new XElement(XName.Get("Auth", ChomikNamespace),
                new XElement(XName.Get("name", ChomikNamespace), username),
                new XElement(XName.Get("passHash", ChomikNamespace), passHash),
                new XElement(XName.Get("client", ChomikNamespace),
                    new XElement(XName.Get("name", ChomikNamespace), "chomikbox"),
                    new XElement(XName.Get("version", ChomikNamespace), "2.0.8.2")
                ),
                new XElement(XName.Get("ver", ChomikNamespace), "4")
            );

            var resultElement = await SendActionAsync("Auth", requestXml, cancellationToken).ConfigureAwait(false);
            if (resultElement == null)
            {
                _logger?.Error("[ChomikBoxClient] Sin respuesta del servidor en Auth.");
                return false;
            }

            var status = GetElementValue(resultElement, "status");
            if (string.Equals(status, "Ok", StringComparison.OrdinalIgnoreCase))
            {
                _token = GetElementValue(resultElement, "token");
                var idStr = GetElementValue(resultElement, "hamsterId");
                long.TryParse(idStr, out _hamsterId);
                _logger?.Info("[ChomikBoxClient] Login SOAP correcto para usuario {0}. HamsterId={1}", username, _hamsterId);
                return true;
            }

            var desc = GetElementValue(resultElement, "description");
            _logger?.Warn("[ChomikBoxClient] Fallo de login SOAP. Estado: {0}, Info: {1}", status, desc);
            return false;
        }

        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_token))
            {
                return;
            }

            try
            {
                var requestXml = new XElement(XName.Get("Logout", ChomikNamespace),
                    new XElement(XName.Get("token", ChomikNamespace), _token)
                );
                await SendActionAsync("Logout", requestXml, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Debug("[ChomikBoxClient] Error en Logout: {0}", ex.ToString());
            }
            finally
            {
                _token = null;
                _hamsterId = 0;
            }
        }

        public async Task<List<ChomikFolderInfo>> GetSubfoldersAsync(long parentFolderId, string currentPath, CancellationToken cancellationToken = default)
        {
            var folders = new List<ChomikFolderInfo>();
            if (string.IsNullOrEmpty(_token))
            {
                var err = "[ChomikBoxClient] No autenticado al solicitar carpetas.";
                _logger?.Warn(err);
                if (_enableSoapDiagnostics)
                {
                    throw new InvalidOperationException(err);
                }
                return folders;
            }

            var requestXml = new XElement(XName.Get("Folders", ChomikNamespace),
                new XElement(XName.Get("token", ChomikNamespace), _token),
                new XElement(XName.Get("hamsterId", ChomikNamespace), _hamsterId),
                new XElement(XName.Get("folderId", ChomikNamespace), parentFolderId),
                new XElement(XName.Get("depth", ChomikNamespace), 2)
            );

            var (resultElement, rawResponseXml) = await SendActionRawAsync("Folders", requestXml, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rawResponseXml))
            {
                if (_enableSoapDiagnostics)
                {
                    throw new InvalidOperationException($"[ChomikBoxClient] Respuesta nula o no recibida en Folders (parentFolderId={parentFolderId}, currentPath='{currentPath}')");
                }
                return folders;
            }

            var parsed = SoapResponseParser.ParseFoldersResponse(rawResponseXml, parentFolderId, currentPath);
            if (parsed.Outcome == FoldersOutcome.Failed)
            {
                var err = $"[ChomikBoxClient] Error en Folders para parentFolderId={parentFolderId}: {parsed.ErrorMessage}";
                _logger?.Warn(err);
                if (_enableSoapDiagnostics)
                {
                    throw new InvalidOperationException(err);
                }
                return folders;
            }

            return parsed.Folders;
        }

        public async Task<FolderFilesResult> GetFilesInFolderAsync(string username, long folderId, string currentPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_token))
            {
                var err = "[ChomikBoxClient] No autenticado al solicitar archivos.";
                _logger?.Warn(err);
                if (_enableSoapDiagnostics)
                {
                    throw new InvalidOperationException(err);
                }
                return FolderFilesResult.CreateFailed(err);
            }

            // Construir path_id idéntico al script Python:
            // path_id = urllib.parse.quote(f'/{self.name}{"/" + current_path.strip("/") + "/" if current_path != "/" else "/"}', safe='()/').replace('%', '*')
            var trimmedPath = (currentPath ?? string.Empty).Trim('/');
            var rawPath = string.IsNullOrEmpty(trimmedPath)
                ? $"/{username}/"
                : $"/{username}/{trimmedPath}/";

            var pathId = EscapePathId(rawPath);

            var requestXml = new XElement(XName.Get("Download", ChomikNamespace),
                new XElement(XName.Get("token", ChomikNamespace), _token),
                new XElement(XName.Get("disposition", ChomikNamespace), "download"),
                new XElement(XName.Get("list", ChomikNamespace),
                    new XElement(XName.Get("DownloadReqEntry", ChomikNamespace),
                        new XElement(XName.Get("id", ChomikNamespace), pathId),
                        new XElement(XName.Get("agreementInfo", ChomikNamespace),
                            new XElement(XName.Get("AgreementInfo", ChomikNamespace),
                                new XElement(XName.Get("name", ChomikNamespace), "own")
                            )
                        )
                    )
                )
            );

            var (resultElement, rawResponseXml) = await SendActionRawAsync("Download", requestXml, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rawResponseXml))
            {
                var err = $"[ChomikBoxClient] Respuesta nula o no interpretable en Download (folderId={folderId}, pathId='{pathId}')";
                _logger?.Warn(err);
                if (_enableSoapDiagnostics)
                {
                    throw new InvalidOperationException(err);
                }
                return FolderFilesResult.CreateFailed(err);
            }

            var parsed = SoapResponseParser.ParseDownloadResponse(rawResponseXml, currentPath);

            if (parsed.Outcome == FolderFilesOutcome.Failed)
            {
                _logger?.Warn("[ChomikBoxClient] Download reportó fallo para pathId='{0}': Status='{1}', Mensaje='{2}'",
                    pathId, parsed.ServerStatus, parsed.ErrorMessage);

                if (_enableSoapDiagnostics)
                {
                    throw new InvalidOperationException($"[ChomikBoxClient] Download falló para pathId='{pathId}': {parsed.ErrorMessage} (status='{parsed.ServerStatus}')");
                }
            }

            return parsed;
        }

        private async Task<XElement> SendActionAsync(string action, XElement actionBody, CancellationToken cancellationToken)
        {
            var (element, _) = await SendActionRawAsync(action, actionBody, cancellationToken).ConfigureAwait(false);
            return element;
        }

        private async Task<(XElement Element, string RawXml)> SendActionRawAsync(string action, XElement actionBody, CancellationToken cancellationToken)
        {
            var soapEnvelope = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(XName.Get("Envelope", SoapNamespace),
                    new XAttribute(XNamespace.Xmlns + "s", SoapNamespace),
                    new XElement(XName.Get("Body", SoapNamespace),
                        actionBody
                    )
                )
            );

            var requestXml = soapEnvelope.ToString(SaveOptions.DisableFormatting);
            var stopwatch = Stopwatch.StartNew();

            using (var request = new HttpRequestMessage(HttpMethod.Post, ServiceUrl))
            {
                request.Headers.Add("SOAPAction", $"http://chomikuj.pl/IChomikBoxService/{action}");
                request.Headers.Add("User-Agent", "Mozilla/5.0");
                request.Content = new StringContent(requestXml, Encoding.UTF8, "text/xml");

                HttpResponseMessage response = null;
                string responseXml = null;
                Exception httpException = null;

                try
                {
                    response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    // Leemos el cuerpo ANTES de chequear el status code
                    responseXml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    httpException = ex;
                }
                finally
                {
                    stopwatch.Stop();
                }

                int statusCode = response != null ? (int)response.StatusCode : 0;
                long elapsedMs = stopwatch.ElapsedMilliseconds;

                // Contadores para política de volcado
                if (action == "Folders") _foldersCount++;
                if (action == "Download") _downloadCount++;

                bool shouldDump = false;
                if (_enableSoapDiagnostics)
                {
                    if (action == "Auth")
                    {
                        shouldDump = true;
                    }
                    else if (action == "Folders" && _foldersCount <= 3)
                    {
                        shouldDump = true;
                    }
                    else if (action == "Download" && _downloadCount <= 5)
                    {
                        shouldDump = true;
                    }
                    else if (httpException != null || (response != null && !response.IsSuccessStatusCode))
                    {
                        shouldDump = true;
                    }
                }

                XElement parsedResult = null;
                if (responseXml != null)
                {
                    parsedResult = ParseSoapResponse(action, responseXml);
                    // Si el resultado parseado es nulo o vacío y diagnóstico está activo, hacer dump
                    if (_enableSoapDiagnostics && parsedResult == null)
                    {
                        shouldDump = true;
                    }
                }

                if (_enableSoapDiagnostics)
                {
                    if (shouldDump && _dumpCounter < MaxDumpsPerExecution)
                    {
                        _dumpCounter++;
                        var sanitizedReq = SoapLogSanitizer.Sanitize(requestXml);
                        var sanitizedResp = SoapLogSanitizer.Sanitize(responseXml ?? string.Empty);
                        var truncatedResp = TruncatePayload(sanitizedResp, 30000);

                        _logger?.Info("[ChomikBoxClient Diag #{0}] Acción: {1} | URL: {2} | HTTP {3} | Tiempo: {4} ms\n--- Request XML ---\n{5}\n--- Response XML ({6} chars) ---\n{7}",
                            _dumpCounter, action, ServiceUrl, statusCode, elapsedMs, sanitizedReq, sanitizedResp.Length, truncatedResp);

                        SaveDiagnosticsFile(_dumpCounter, action, sanitizedReq, sanitizedResp);
                    }
                    else
                    {
                        _logger?.Info("[ChomikBoxClient Resumen] Acción: {0} | HTTP {1} | Tiempo: {2} ms | RespSize: {3} chars",
                            action, statusCode, elapsedMs, responseXml?.Length ?? 0);
                    }
                }

                if (httpException != null)
                {
                    _logger?.Error("[ChomikBoxClient] Error HTTP al llamar SOAP {0}: {1}", action, httpException.ToString());
                    if (_enableSoapDiagnostics)
                    {
                        throw new HttpRequestException($"Fallo HTTP en SOAP {action}: {httpException.Message}", httpException);
                    }
                    return (null, responseXml);
                }

                if (response != null && !response.IsSuccessStatusCode)
                {
                    _logger?.Error("[ChomikBoxClient] HTTP {0} en llamada SOAP {1}. Cuerpo respuesta: {2}",
                        statusCode, action, SoapLogSanitizer.Sanitize(responseXml ?? string.Empty));

                    if (_enableSoapDiagnostics)
                    {
                        throw new HttpRequestException($"Respuesta no exitosa HTTP {statusCode} en llamada SOAP {action}: {SoapLogSanitizer.Sanitize(responseXml ?? string.Empty)}");
                    }
                    return (null, responseXml);
                }

                return (parsedResult, responseXml);
            }
        }

        private void SaveDiagnosticsFile(int counter, string action, string sanitizedReqXml, string sanitizedRespXml)
        {
            if (string.IsNullOrWhiteSpace(_diagnosticsDir)) return;

            try
            {
                var reqFile = Path.Combine(_diagnosticsDir, $"{counter:D3}-{action}-request.xml");
                var respFile = Path.Combine(_diagnosticsDir, $"{counter:D3}-{action}-response.xml");
                // Asegurar sanitización estricta por si se invocase con texto crudo
                File.WriteAllText(reqFile, SoapLogSanitizer.Sanitize(sanitizedReqXml), Encoding.UTF8);
                File.WriteAllText(respFile, SoapLogSanitizer.Sanitize(sanitizedRespXml), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger?.Warn("[ChomikBoxClient] No se pudo guardar archivo de diagnóstico para {0}: {1}", action, ex.Message);
            }
        }

        private static void PurgeOldDiagnosticsFiles(string dir, TimeSpan maxAge)
        {
            try
            {
                var cutoff = DateTime.UtcNow - maxAge;
                var files = Directory.GetFiles(dir, "*.xml");
                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.LastWriteTimeUtc < cutoff)
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                        // Continuar con otros archivos
                    }
                }
            }
            catch
            {
                // Silenciar fallos en purga de diagnósticos
            }
        }

        private static string TruncatePayload(string payload, int maxChars)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length <= maxChars)
            {
                return payload;
            }
            return payload.Substring(0, maxChars) + $"\n... [TRUNCADO: total {payload.Length} caracteres] ...";
        }

        private XElement ParseSoapResponse(string action, string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return null;
            }

            try
            {
                var doc = XDocument.Parse(xml);

                // Comprobar si hay un SOAP Fault
                foreach (var el in doc.Descendants())
                {
                    if (string.Equals(el.Name.LocalName, "Fault", StringComparison.OrdinalIgnoreCase))
                    {
                        var faultString = GetElementValue(el, "faultstring") ?? el.Value;
                        var sanitizedFault = SoapLogSanitizer.Sanitize(faultString);
                        _logger?.Error("[ChomikBoxClient] SOAP Fault recibido en {0}: {1}", action, sanitizedFault);
                        if (_enableSoapDiagnostics)
                        {
                            throw new InvalidOperationException($"SOAP Fault en {action}: {faultString}");
                        }
                    }
                }

                // Buscar el nodo {Action}Result o {Action}Response
                var targetName = $"{action}Result";
                foreach (var el in doc.Descendants())
                {
                    if (string.Equals(el.Name.LocalName, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return el;
                    }
                }

                var respName = $"{action}Response";
                foreach (var el in doc.Descendants())
                {
                    if (string.Equals(el.Name.LocalName, respName, StringComparison.OrdinalIgnoreCase))
                    {
                        return el;
                    }
                }

                return doc.Root;
            }
            catch (Exception ex)
            {
                _logger?.Error("[ChomikBoxClient] Error parseando respuesta XML para {0}: {1}", action, ex.ToString());
                if (_enableSoapDiagnostics)
                {
                    throw;
                }
                return null;
            }
        }

        private static string GetElementValue(XElement parent, string localName)
        {
            if (parent == null) return null;
            foreach (var el in parent.Descendants())
            {
                if (string.Equals(el.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                {
                    return el.Value?.Trim();
                }
            }
            return null;
        }

        public static string EscapePathId(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            var sb = new StringBuilder();
            var utf8Bytes = Encoding.UTF8.GetBytes(path);

            foreach (var b in utf8Bytes)
            {
                char c = (char)b;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                    c == '-' || c == '_' || c == '.' || c == '~' || c == '(' || c == ')' || c == '/')
                {
                    sb.Append(c);
                }
                else
                {
                    // En Python: urllib.parse.quote(..., safe='()/').replace('%', '*')
                    sb.Append('*');
                    sb.Append(b.ToString("X2"));
                }
            }

            return sb.ToString();
        }

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

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_disposeHttpClient)
                {
                    _httpClient?.Dispose();
                }
            }
        }
    }
}
