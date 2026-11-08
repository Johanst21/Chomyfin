using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using MediaBrowser.Model.Logging;
using Chomyfin.Configuration;
using Chomyfin.Services;

namespace Chomyfin
{
    /// <summary>
    /// Servicio de acceso a chomikuj.pl: login con la cuenta configurada,
    /// gestión de la sesión (cookies) y resolución de páginas de archivo a
    /// URLs de descarga directa.
    ///
    /// Nota de diseño (autocontención): usa un HttpClient propio con un
    /// CookieContainer aislado porque el flujo de login requiere mantener las
    /// cookies de chomikuj.pl separadas de las del proceso del servidor. No se
    /// empaquetan librerías nativas: System.Net.Http forma parte del runtime
    /// que Emby ya carga.
    ///
    /// Cambios 1.2 (Emby 4.10.x):
    /// - Thread-safety del estado de login (anteriormente ForceReLogin y
    ///   EnsureLoggedIn competían desde varios hilos sin sincronización).
    /// - Reintento de login con backoff fijo cuando falla (antes, un login
    ///   "exitoso pero no confirmado" bloqueaba las resoluciones
    ///   indefinidamente hasta reiniciar el servidor o guardar opciones).
    /// - Timeout explícito de 30 s en el cliente (antes: 100 s por defecto,
    ///   capaz de atascar la cola de resoluciones de Emby).
    /// - Respecto real del CancellationToken en todas las peticiones.
    /// - Se aplican MaxConcurrentResolutions y la caché de cookies (antes
    ///   estaban declarados en las opciones pero no se usaban en absoluto).
    /// </summary>
    public class ChomikujService : IChomikujService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly Func<ChomyfinOptions> _getOptions;
        private readonly HttpClient _httpClient;
        private readonly CookieContainer _cookieContainer;
        private readonly object _stateLock = new object();

        private volatile bool _isLoggedIn;
        private Task<bool> _loginTask;
        private int _loginGeneration;
        private DateTime _lastSuccessfulLoginUtc = DateTime.MinValue;
        private DateTime _lastLoginAttemptUtc = DateTime.MinValue;
        private volatile string _postLoginToken;

        // Mejora 3: Vía Rápida - Cortacircuitos y Caché Negativa
        private const int FastPathMaxConsecutiveFailures = 5;
        private int _fastPathConsecutiveFailures;
        private volatile bool _isCircuitBreakerOpen;

        private const int MaxNegativeCacheEntries = 500;
        private readonly HashSet<string> _slowPathOnlyFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private const int MinFileIdDigits = 6;
        private static readonly Regex StrictUrlFileIdRegex = new Regex(@",\s*(\d{" + MinFileIdDigits + @",})(?:\.[a-zA-Z0-9]+)?(?:\?|$)", RegexOptions.Compiled);

        private SemaphoreSlim _semaphore;
        private int _semaphoreMax = -1;
        internal Action OnEnsureLoggedInForTests = null;

        // Backoff entre reintentos de login (credenciales inválidas / sitio caído):
        // evita martillear chomikuj.pl en cada intento de reproducción.
        private static readonly TimeSpan LoginRetryDelay = TimeSpan.FromMinutes(5);

        private static readonly Regex TokenRegex = new Regex(@"name=""__RequestVerificationToken"".*?value=""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FileIdRegex = new Regex(@"data-file-id=[""'](\d+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex UrlFileIdRegex = new Regex(@",\s*(\d+)(?:\.[a-zA-Z0-9]+)?(?:\?|$)", RegexOptions.Compiled);
        private static readonly Regex FolderIdRegex = new Regex(@"name=""FolderId""\s+type=""hidden""\s+value=""(\d+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RedirectUrlRegex = new Regex(@"""redirectUrl""\s*:\s*""([^""]+)""", RegexOptions.Compiled);

        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";
        string IChomikujService.UserAgent => UserAgent;

        /// <param name="getOptions">
        /// Fábrica de la configuración actual (lee siempre la instancia fresca
        /// cacheada por el ConfigurationManager de Emby; coste casi nulo).
        /// Inyectar así evita el acoplamiento a <c>Plugin.Instance</c> y permite
        /// que los cambios de opciones se apliquen sin reiniciar.
        /// </param>
        public ChomikujService(ILogger logger, Func<ChomyfinOptions> getOptions)
            : this(logger, getOptions, null)
        {
        }

        internal ChomikujService(ILogger logger, Func<ChomyfinOptions> getOptions, HttpMessageHandler messageHandler, CookieContainer cookieContainer = null)
        {
            _logger = logger;
            _getOptions = getOptions;
            _cookieContainer = cookieContainer ?? new CookieContainer();
            var handler = messageHandler ?? new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        /// <summary>Invalida la sesión: se vuelve a loguear en la próxima resolución.</summary>
        public void ForceReLogin()
        {
            lock (_stateLock)
            {
                _loginTask = null;
                _loginGeneration++;
                _isLoggedIn = false;
                _lastSuccessfulLoginUtc = DateTime.MinValue;
                _lastLoginAttemptUtc = DateTime.MinValue;
                _postLoginToken = null;
                _fastPathConsecutiveFailures = 0;
                _isCircuitBreakerOpen = false;
                _slowPathOnlyFolders.Clear();

                foreach (Cookie c in _cookieContainer.GetCookies(new Uri("https://chomikuj.pl/")))
                {
                    c.Expired = true;
                }
            }
        }

        private ChomyfinOptions SafeGetOptions()
        {
            try { return _getOptions(); }
            catch (Exception ex) { _logger.ErrorException("Chomyfin: error leyendo opciones", ex); return null; }
        }

        private Task<bool> EnsureLoggedIn()
        {
            var options = SafeGetOptions();
            bool cacheEnabled = options?.EnableCookieCache ?? true;
            int minutes = options?.CookieCacheDurationMinutes ?? 30;
            if (minutes < 1) minutes = 1;
            var cacheDuration = TimeSpan.FromMinutes(minutes);

            lock (_stateLock)
            {
                bool sessionExpired = _isLoggedIn
                    && DateTime.UtcNow - _lastSuccessfulLoginUtc > cacheDuration;

                bool shouldLogin =
                    !cacheEnabled            // caché desactivada: login por cada resolución
                    || _loginTask == null
                    || sessionExpired
                    // Login fallido (faulted o completado sin sesión): reintento
                    // limitado por LoginRetryDelay (IsFaulted implica IsCompleted).
                    || (_loginTask.IsCompleted && !_isLoggedIn
                        && DateTime.UtcNow - _lastLoginAttemptUtc > LoginRetryDelay);

                if (shouldLogin)
                {
                    if (_loginTask == null || _loginTask.IsCompleted)
                    {
                        _isLoggedIn = false;
                        _lastLoginAttemptUtc = DateTime.UtcNow;
                        int currentGen = ++_loginGeneration;
                        _loginTask = LoginAsync(currentGen);
                    }
                }
                OnEnsureLoggedInForTests?.Invoke();
                return _loginTask;
            }
        }

        private async Task<bool> LoginAsync(int generation)
        {
            await Task.Yield();
            var config = SafeGetOptions();
            if (config == null || string.IsNullOrEmpty(config.Username) || string.IsNullOrEmpty(config.Password))
            {
                _logger.Warn("Chomyfin: no hay credenciales de Chomikuj configuradas; no se podrán resolver URLs de página.");
                return false;
            }

            var mainPage = await _httpClient.GetStringAsync("https://chomikuj.pl/").ConfigureAwait(false);
            var token = TokenRegex.Match(mainPage);
            if (!token.Success)
            {
                // Lanzar (no tragar): la tarea queda faulted y el reintento se
                // gobierna con LoginRetryDelay. El sitio cambió su estructura.
                throw new InvalidOperationException("Chomyfin: no se encontró el token __RequestVerificationToken en la página de Chomikuj (posible cambio de estructura del sitio).");
            }

            var loginData = new Dictionary<string, string>
            {
                { "Login", config.Username },
                { "Password", config.Password },
                { "__RequestVerificationToken", token.Groups[1].Value }
            };
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://chomikuj.pl/action/Login/Login") { Content = new FormUrlEncodedContent(loginData) })
            {
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                request.Headers.Referrer = new Uri("https://chomikuj.pl/");
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/javascript"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.01));

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // Comprobar cookies de sesión devueltas
                    var cookies = _cookieContainer.GetCookies(new Uri("https://chomikuj.pl/")).Cast<Cookie>().ToList();
                    bool hasSessionCookie = cookies.Any(c => c.Name.Equals("rememberMe", StringComparison.OrdinalIgnoreCase)
                                                          || (c.Name.Equals("ChomikSession", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.Value)));

                    // Heurística de éxito: JSON con LoggedIn o toolbar o cookie de sesión válida
                    bool isSuccess = response.IsSuccessStatusCode
                        && (content.Contains("\"LoggedIn\":true")
                            || content.Contains("ChomikToolbar.IsLogged = true")
                            || (content.Contains("\"IsSuccess\":true") && !content.Contains("Niepoprawne dane logowania") && !content.Contains("ContainsCaptcha\":true") && hasSessionCookie));

                    if (isSuccess)
                    {
                        lock (_stateLock)
                        {
                            if (_loginGeneration == generation)
                            {
                                _isLoggedIn = true;
                                _lastSuccessfulLoginUtc = DateTime.UtcNow;
                                _sessionVerificationToken = token.Groups[1].Value;
                                _postLoginToken = null;
                                _fastPathConsecutiveFailures = 0;
                                _isCircuitBreakerOpen = false;
                                _slowPathOnlyFolders.Clear();
                            }
                        }
                        _logger.Info("Chomyfin: Login exitoso para el usuario {0}.", config.Username);

                        // Mejora 3 (Sección 4.1): Token posterior al login con cookies autenticadas
                        bool cacheEnabled = config.EnableCookieCache;
                        if (cacheEnabled)
                        {
                            try
                            {
                                var postLoginHtml = await _httpClient.GetStringAsync("https://chomikuj.pl/").ConfigureAwait(false);
                                var postLoginMatch = TokenRegex.Match(postLoginHtml);
                                if (postLoginMatch.Success)
                                {
                                    lock (_stateLock)
                                    {
                                        if (_loginGeneration == generation)
                                        {
                                            _postLoginToken = postLoginMatch.Groups[1].Value;
                                        }
                                    }
                                    _logger.Debug("Chomyfin: token post-login obtenido correctamente.");
                                }
                                else
                                {
                                    _logger.Debug("Chomyfin: no se encontró __RequestVerificationToken en la página principal post-login.");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug("Chomyfin: error al obtener el token post-login: {0}", ex.Message);
                            }
                        }

                        return true;
                    }
                    else
                    {
                        string reason = "desconocido";
                        if (content.Contains("Niepoprawne dane logowania")) reason = "credenciales incorrectas (usuario o contraseña inválidos)";
                        else if (content.Contains("ContainsCaptcha\":true") || content.Contains("g-recaptcha") || content.Contains("captcha")) reason = "desafío Captcha detectado";
                        else if (content.Contains("Just a moment") || content.Contains("cf-chl")) reason = "Cloudflare anti-bot challenge detectado";

                        string sanitizedContent = SoapLogSanitizer.Sanitize(content);
                        string preview = sanitizedContent.Length > 300 ? sanitizedContent.Substring(0, 300) : sanitizedContent;
                        _logger.Warn("Chomyfin: login no confirmado (HTTP {0}, Motivo: {1}). Respuesta: {2}. Se reintentará en {3} min.",
                            (int)response.StatusCode, reason, preview.Replace("\r", " ").Replace("\n", " "), (int)LoginRetryDelay.TotalMinutes);

                        return false;
                    }
                }
            }
        }

        public IEnumerable<string> GetCookies() =>
            _cookieContainer.GetCookies(new Uri("https://chomikuj.pl/")).Cast<Cookie>().Select(c => $"{c.Name}={c.Value}");

        /// <summary>
        /// Devuelve el semáforo de concurrencia, recreándolo solo cuando el
        /// usuario cambia MaxConcurrentResolutions. El semáforo anterior se
        /// sustituye sin Dispose (los waiters pendientes podrían lanzar al
        /// disponerlo); la fuga es de un objeto trivial y puntual.
        /// </summary>
        private SemaphoreSlim GetCurrentSemaphore()
        {
            var options = SafeGetOptions();
            int max = options?.MaxConcurrentResolutions ?? 3;
            if (max < 1) max = 1;
            if (max > 10) max = 10;

            lock (_stateLock)
            {
                if (max != _semaphoreMax)
                {
                    _semaphore = new SemaphoreSlim(max);
                    _semaphoreMax = max;
                }
                return _semaphore;
            }
        }

        private string _sessionVerificationToken;

        /// <summary>
        /// Resuelve una URL de página de Chomikuj a una URL directa de descarga.
        /// Debe invocarse estrictamente bajo demanda desde <see cref="StrmCacheService"/> para archivos que necesiten resolución.
        /// No debe utilizarse en temporizadores, tareas programadas ni barridos masivos.
        /// </summary>
        public async Task<string> ResolveUrlAsync(string pageUrl, CancellationToken ct)
        {
            bool loggedIn = await EnsureLoggedIn().ConfigureAwait(false);
            if (!loggedIn) return null;

            var semaphore = GetCurrentSemaphore();
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var options = SafeGetOptions();
                bool useFastPath = options?.UseFastResolvePath ?? true;
                string postLoginToken = _postLoginToken;
                string folderPrefix = GetFolderPrefix(pageUrl);

                var strictMatch = !string.IsNullOrEmpty(pageUrl) ? StrictUrlFileIdRegex.Match(pageUrl) : null;
                bool fileIdEligible = strictMatch != null && strictMatch.Success;

                bool shouldAttemptFastPath = useFastPath
                    && !string.IsNullOrEmpty(postLoginToken)
                    && fileIdEligible
                    && !_isCircuitBreakerOpen
                    && !IsFolderInNegativeCache(folderPrefix);

                if (shouldAttemptFastPath)
                {
                    string fileId = strictMatch.Groups[1].Value;
                    string fastResult = await TryResolveFastPathAsync(fileId, postLoginToken, pageUrl, folderPrefix, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(fastResult))
                    {
                        return fastResult;
                    }

                    _logger.Debug("Chomyfin: vía rápida no resolvió para {0}. Cayendo a vía lenta.", pageUrl);
                }

                var slowResult = await ResolveSlowPathWithStatusAsync(pageUrl, ct).ConfigureAwait(false);

                // Sección 4.4: Si la vía rápida fue intentada y falló pero la vía lenta resolvió,
                // o si la vía lenta ejecutó desbloqueo de carpeta protegida, marcar carpeta en caché negativa
                if (shouldAttemptFastPath && !string.IsNullOrEmpty(slowResult.DirectUrl))
                {
                    AddFolderToNegativeCache(folderPrefix);
                }
                else if (slowResult.HadFolderUnlock)
                {
                    AddFolderToNegativeCache(folderPrefix);
                }

                return slowResult.DirectUrl;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.ErrorException("Chomyfin: Error resolviendo URL", ex); }
            finally
            {
                semaphore.Release();
            }
            return null;
        }

        private async Task<string> TryResolveFastPathAsync(string fileId, string token, string pageUrl, string folderPrefix, CancellationToken ct)
        {
            try
            {
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
                {
                    var directUrl = await PostDownloadAsync(fileId, token, pageUrl, isFastPath: true, linkedCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(directUrl))
                    {
                        RecordFastPathSuccess(pageUrl, fileId);
                        return directUrl;
                    }

                    RecordFastPathFailure(pageUrl, "sin redirectUrl en respuesta");
                    return null;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancelación del llamador: debe propagarse sin tragar
                throw;
            }
            catch (OperationCanceledException)
            {
                // Timeout propio de 10s alcanzado
                RecordFastPathFailure(pageUrl, "timeout de 10s alcanzado");
                return null;
            }
            catch (Exception ex)
            {
                RecordFastPathFailure(pageUrl, ex.Message);
                return null;
            }
        }

        private void RecordFastPathSuccess(string pageUrl, string fileId)
        {
            lock (_stateLock)
            {
                _fastPathConsecutiveFailures = 0;
            }
            _logger.Debug("Chomyfin: vía rápida exitosa para {0} (fileId {1})", pageUrl, fileId);
        }

        private void RecordFastPathFailure(string pageUrl, string reason)
        {
            _logger.Debug("Chomyfin: fallo en vía rápida para {0} ({1})", pageUrl, reason);

            lock (_stateLock)
            {
                _fastPathConsecutiveFailures++;
                if (_fastPathConsecutiveFailures >= FastPathMaxConsecutiveFailures && !_isCircuitBreakerOpen)
                {
                    _isCircuitBreakerOpen = true;
                    _logger.Info("Chomyfin: cortacircuitos de vía rápida abierto tras {0} fallos consecutivos. Vía rápida deshabilitada hasta el próximo login.",
                        _fastPathConsecutiveFailures);
                }
            }
        }

        private string GetFolderPrefix(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            int qIndex = url.IndexOf('?');
            string cleanUrl = qIndex >= 0 ? url.Substring(0, qIndex) : url;
            int lastSlash = cleanUrl.LastIndexOf('/');
            if (lastSlash > 0)
            {
                return cleanUrl.Substring(0, lastSlash);
            }
            return cleanUrl;
        }

        private bool IsFolderInNegativeCache(string folderPrefix)
        {
            if (string.IsNullOrEmpty(folderPrefix)) return false;
            lock (_stateLock)
            {
                return _slowPathOnlyFolders.Contains(folderPrefix);
            }
        }

        private void AddFolderToNegativeCache(string folderPrefix)
        {
            if (string.IsNullOrEmpty(folderPrefix)) return;
            lock (_stateLock)
            {
                if (_slowPathOnlyFolders.Count >= MaxNegativeCacheEntries)
                {
                    _slowPathOnlyFolders.Clear();
                }
                _slowPathOnlyFolders.Add(folderPrefix);
                _logger.Debug("Chomyfin: carpeta añadida a caché negativa de vía rápida: {0}", folderPrefix);
            }
        }

        private async Task<string> ResolveSlowPathAsync(string pageUrl, CancellationToken ct) =>
            (await ResolveSlowPathWithStatusAsync(pageUrl, ct).ConfigureAwait(false)).DirectUrl;

        private async Task<(string DirectUrl, bool HadFolderUnlock)> ResolveSlowPathWithStatusAsync(string pageUrl, CancellationToken ct)
        {
            string fileIdValue = null;
            string page = null;
            bool hadFolderUnlock = false;

            // Fallback prioritario/preventivo: extraer fileId de la URL si viene especificado
            var urlFileMatch = UrlFileIdRegex.Match(pageUrl);
            if (urlFileMatch.Success)
            {
                fileIdValue = urlFileMatch.Groups[1].Value;
                _logger.Info("Chomyfin: fileId {0} detectado en la URL.", fileIdValue);
            }

            try
            {
                page = await GetStringAsync(pageUrl, ct).ConfigureAwait(false);

                var fileIdMatch = FileIdRegex.Match(page);
                if (fileIdMatch.Success)
                {
                    fileIdValue = fileIdMatch.Groups[1].Value;
                }

                // Si la carpeta está protegida por contraseña y tenemos contraseña configurada, desbloquearla
                var options = SafeGetOptions();
                if (page.Contains("LoginToFolder") && !string.IsNullOrEmpty(options?.FolderPassword))
                {
                    hadFolderUnlock = true;
                    var folderIdMatch = FolderIdRegex.Match(page);
                    if (folderIdMatch.Success)
                    {
                        var tokenMatch = TokenRegex.Match(page);
                        string tokenVal = tokenMatch.Success ? tokenMatch.Groups[1].Value : _sessionVerificationToken;
                        await UnlockFolderAsync(folderIdMatch.Groups[1].Value, options.FolderPassword, tokenVal, pageUrl, ct).ConfigureAwait(false);
                        // Re-cargar la página una vez desbloqueada la carpeta
                        page = await GetStringAsync(pageUrl, ct).ConfigureAwait(false);
                        var newFileId = FileIdRegex.Match(page);
                        if (newFileId.Success) fileIdValue = newFileId.Groups[1].Value;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("Chomyfin: no se pudo obtener el HTML de {0} ({1}). Intentando descarga con fileId conocido.", pageUrl, ex.Message);
            }

            if (string.IsNullOrEmpty(fileIdValue))
            {
                _logger.Warn("Chomyfin: no se encontró file-id ni en el HTML ni en la URL de {0}.", pageUrl);
                return (null, hadFolderUnlock);
            }

            string verificationToken = null;
            if (!string.IsNullOrEmpty(page))
            {
                var token = TokenRegex.Match(page);
                if (token.Success) verificationToken = token.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(verificationToken))
            {
                verificationToken = _sessionVerificationToken;
            }

            if (string.IsNullOrEmpty(verificationToken))
            {
                _logger.Warn("Chomyfin: no se encontró token de verificación para {0}.", pageUrl);
                return (null, hadFolderUnlock);
            }

            var directUrl = await PostDownloadAsync(fileIdValue, verificationToken, pageUrl, isFastPath: false, ct).ConfigureAwait(false);
            return (directUrl, hadFolderUnlock);
        }

        private async Task<string> PostDownloadAsync(string fileId, string verificationToken, string pageUrl, bool isFastPath, CancellationToken ct)
        {
            var data = new Dictionary<string, string>
            {
                { "fileId", fileId },
                { "__RequestVerificationToken", verificationToken }
            };
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://chomikuj.pl/action/License/Download") { Content = new FormUrlEncodedContent(data) })
            {
                request.Headers.Referrer = new Uri("https://chomikuj.pl/");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                using (var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false))
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var match = RedirectUrlRegex.Match(content);
                    if (match.Success)
                    {
                        return Regex.Unescape(match.Groups[1].Value);
                    }

                    var sanitizedContent = SoapLogSanitizer.Sanitize(content);
                    var preview = sanitizedContent.Length > 250 ? sanitizedContent.Substring(0, 250) : sanitizedContent;

                    if (isFastPath)
                    {
                        _logger.Debug("Chomyfin: intento rápido sin redirectUrl para {0} (HTTP {1}). Contenido: {2}",
                            pageUrl, (int)response.StatusCode, preview);
                    }
                    else
                    {
                        _logger.Warn("Chomyfin: respuesta de descarga sin redirectUrl para {0} (HTTP {1}). Contenido: {2}",
                            pageUrl, (int)response.StatusCode, preview);
                    }
                }
            }

            return null;
        }

        // netstandard2.0 no expone HttpClient.GetStringAsync(Uri, CancellationToken),
        // así que se usa SendAsync con GET.
        private async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private async Task UnlockFolderAsync(string folderId, string password, string token, string refererUrl, CancellationToken ct)
        {
            try
            {
                var data = new Dictionary<string, string>
                {
                    { "FolderId", folderId },
                    { "Password", password },
                    { "Remember", "true" },
                    { "__RequestVerificationToken", token }
                };
                using (var request = new HttpRequestMessage(HttpMethod.Post, "https://chomikuj.pl/action/Files/LoginToFolder") { Content = new FormUrlEncodedContent(data) })
                {
                    request.Headers.Referrer = new Uri(refererUrl);
                    request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                    using (var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false))
                    {
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        _logger.Info("Chomyfin: Desbloqueando carpeta {0} (HTTP {1})", folderId, (int)response.StatusCode);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Chomyfin: Error desbloqueando carpeta con contraseña", ex);
            }
        }

        public void Dispose() => _httpClient?.Dispose();
    }
}
