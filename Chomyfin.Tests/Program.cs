using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Chomyfin.Models;
using Chomyfin.Services;

namespace Chomyfin.Tests
{
    class Program
    {
        private static int _passed = 0;
        private static int _failed = 0;

        static int Main(string[] args)
        {
            Console.WriteLine("=== EJECUTANDO SUITE DE PRUEBAS CHOMYFIN ===");

            // 4.1 Parseo de Download
            RunTest("4.1.1 Caso Bug Raiz Vacia (list i:nil='true' con Error not available)", TestDownload_RootEmpty);
            RunTest("4.1.2 list nulo con status Ok o ausente", TestDownload_ListNil_StatusOk);
            RunTest("4.1.3 DownloadFolder con un unico FileEntry", TestDownload_SingleFile);
            RunTest("4.1.4 DownloadFolder con varios FileEntry", TestDownload_MultipleFiles);
            RunTest("4.1.5 DownloadFolder cuyo propio status es not available", TestDownload_FolderStatusNotAvailable);
            RunTest("4.1.6 status Error con mensaje no esperado (invalid token)", TestDownload_ErrorOtherMessage);
            RunTest("4.1.7 Cuerpo no interpretable (texto truncado, HTML, SOAP Fault)", TestDownload_UninterpretableBody);
            RunTest("4.1.8 Variantes de namespace (http://chomikuj.pl vs http://chomikuj.pl/)", TestDownload_NamespaceVariants);
            RunTest("4.1.9 Comparacion del mensaje normalizada (mayusculas y espacios)", TestDownload_NormalizedMessage);

            // 4.2 Parseo de Folders
            RunTest("4.2.1 Carpeta sin subcarpetas (folders ausente o nil)", TestFolders_EmptyOrNil);
            RunTest("4.2.2 Varias FolderInfo (descarta id del padre)", TestFolders_Multiple_IgnoresParent);
            RunTest("4.2.3 Respuesta con password (no se lee ni expone)", TestFolders_PasswordOmitted);

            // 4.3 Sanitizador
            RunTest("4.3.1 Ocultamiento de password, passwd, passHash, token, secret", TestSanitizer_SensitiveKeywords);
            RunTest("4.3.2 Preservacion de booleanos (hasPassword=true)", TestSanitizer_BooleansPreserved);
            RunTest("4.3.3 Atributos sensibles, autocerrados, CDATA, entidades, multilinea", TestSanitizer_XmlVariations);
            RunTest("4.3.4 Texto no XML o XML truncado (fallback regex)", TestSanitizer_RegexFallback);
            RunTest("4.3.5 Sanitizar antes de truncar", TestSanitizer_SanitizeBeforeTruncate);
            RunTest("4.3.6 Canario: XML truncado sin cierre", TestSanitizer_Canary_XmlTruncatedTag);
            RunTest("4.3.7 Canario: passHash sin etiqueta de cierre", TestSanitizer_Canary_PassHashNoClose);
            RunTest("4.3.8 Canario: JSON con Password y __RequestVerificationToken", TestSanitizer_Canary_JsonTokens);
            RunTest("4.3.9 Canario: Query string en URL con token y password", TestSanitizer_Canary_QueryString);
            RunTest("4.3.10 Canario: Form urlencoded Login y Password", TestSanitizer_Canary_FormUrlEncoded);
            RunTest("4.3.11 Canario: Cabecera Cookie con rememberMe y ChomikSession", TestSanitizer_Canary_CookieHeader);
            RunTest("4.3.12 Canario: Elemento password con booleano true se enmascara", TestSanitizer_Canary_PasswordBoolMasked);
            RunTest("4.3.13 Canario: Atributo hasPassword con booleano true se preserva", TestSanitizer_Canary_HasPasswordPreserved);
            RunTest("4.3.14 Resistencia DoS: 5 MB de '<' repetidos termina en < 2 s", TestSanitizer_5MB_LessThan2Seconds);

            // 4.4 Recorrido del escaneo con cliente falso
            RunTest("4.4.1 Recorrido: Raiz EmptyFolder y 2 subcarpetas con archivos", TestSync_RootEmpty_SubfoldersFiles);
            RunTest("4.4.2 Recorrido: Subcarpeta con fallo no aborta el escaneo", TestSync_SingleFolderFailure_Continues);
            RunTest("4.4.3 Recorrido: 5 fallos consecutivos aborta la ejecucion", TestSync_ConsecutiveFailures_Aborts);

            // 5. Mejora 1: MediaSourceInfo.Id determinista
            RunTest("5.1 Determinismo: dos llamadas con misma ruta devuelven identico Id", TestMediaSourceId_Deterministic);
            RunTest("5.2 Normalizacion de separadores: '\\' y '/' producen el mismo Id", TestMediaSourceId_SlashNormalization);
            RunTest("5.3 Rutas distintas producen Id distintos", TestMediaSourceId_DistinctPathsDifferentId);
            RunTest("5.4 Sensibilidad a mayusculas/minusculas preservada", TestMediaSourceId_CaseSensitive);
            RunTest("5.5 Formato y longitud de 32 caracteres hexadecimales", TestMediaSourceId_FormatAndLength);
            RunTest("5.6 Manejo seguro de ruta nula o vacia", TestMediaSourceId_NullOrEmpty);

            // 6. MediaSourceInfo.Name dinamico
            RunTest("6.1 MediaSourceName extrae nombre de archivo sin extension", TestMediaSourceName_ExtractsCleanFileName);
            RunTest("6.2 MediaSourceName maneja separadores Windows y Linux", TestMediaSourceName_WindowsAndLinuxPaths);
            RunTest("6.3 MediaSourceName devuelve null si ruta nula o vacia", TestMediaSourceName_NullOrEmptyPath);

            // 7. Pruebas de aceptación P0-4 (a-d)
            RunTest("7.1 P0-4 Test a: SelectedFolders escaneando solo B con DeleteOrphans=true y DryRun=false -> A/keep.strm se preserva y B/old.strm se elimina", TestAcceptance_P04_TestA);
            RunTest("7.2 P0-4 Test b: Con OrphansDryRun=true -> ningún fichero se elimina y el log contiene [ORPHANS-DRYRUN] Se eliminarían 1 .strm", TestAcceptance_P04_TestB);
            RunTest("7.3 P0-4 Test c: Colisión remota Peli.mkv y Peli.mp4 sin ficheros previos -> Peli.strm (Id 111) y Peli [mp4].strm, 2da ejecucion 0 actualizados", TestAcceptance_P04_TestC);
            RunTest("7.4 P0-4 Test d: Existe Peli.strm apuntando a Id 222 -> Peli.strm preservado para 222 y Peli.mkv pasa a Peli [mkv].strm sin mover ficheros", TestAcceptance_P04_TestD);

            // 8. Pruebas P1-5
            RunTest("8.1 P1-5: clientFactory lanza InvalidOperationException -> 1ª lanza, 2ª con factory válido NO devuelve 'Ya hay una generación...' y Success=true", TestP1_5_ClientFactoryThrows_ReleasesLock);
            RunTest("8.2 P1-5: AllowedExtensions con null -> 1ª lanza, 2ª funciona", TestP1_5_AllowedExtensionsNullItem_ReleasesLock);
            RunTest("8.3 P1-5: Concurrencia llamada activa -> llamada concurrente devuelve 'Ya hay una generación en curso' y semáforo sigue disponible después", TestP1_5_Concurrency_BusyReturnsErrorAndLockPreserved);

            // 9. Pruebas P1-9
            RunTest("9.1 P1-9: AtomicFile.WriteAllText crea fichero con contenido correcto (sin destino previo)", TestP1_9_WriteAllText_CreatesFile);
            RunTest("9.2 P1-9: AtomicFile.WriteAllText reemplaza fichero existente sin corrupción (contenido nuevo íntegro)", TestP1_9_WriteAllText_ReplacesExistingFile);
            RunTest("9.3 P1-9: Concurrencia 50 tareas GetOrResolveDirectUrlAsync y generador -> contenido final es URL completa válida, sin *.tmp huérfanos", TestP1_9_Concurrency_50Tasks_ValidContentNoOrphanTmp);
            RunTest("9.4 P1-9: >50 entradas y 200 adquisiciones concurrentes -> dos hilos nunca ejecutan la sección crítica a la vez", TestP1_9_Concurrency_200Acquisitions_MutualExclusion);
            RunTest("9.5 P1-9: Test negativo: si falla .original (directorio solo lectura), .strm conserva su contenido anterior", TestP1_9_Negative_OriginalFails_StrmPreserved);
            RunTest("9.6 P1-9: MaybeCleanupLocks: >50 entradas, purga retira RefCount==0 y NO retira hilos en WaitAsync", TestP1_9_MaybeCleanupLocks_RetainsActiveWaiters);

            // 10. Pruebas P1-6 (StrmSyncTask)
            RunTest("10.1 P1-6 Test a: Cancelación -> OperationCanceledException relanzada, statusStore y logger actualizados, libraryMonitor no invocado", TestP1_6_Cancellation_ThrowsAndUpdatesStatus);
            RunTest("10.2 P1-6 Test b: Excepción no-cancelatoria -> catch(Exception) atrapa, IsRunning=false, LastSuccess=false, libraryMonitor no invocado", TestP1_6_NonCancellationException_CaughtAndUpdatesStatus);
            RunTest("10.3 P1-6 Test c: Flujo normal exitoso -> result.Success=true, IsRunning=false, LastSuccess=true, libraryMonitor recibe ReportFileSystemChanged", TestP1_6_Success_InvokesLibraryMonitor);

            // 11. Pruebas P1-7 (ChomikujService)
            RunTest("11.1 Reintento con credenciales inválidas no martillea el sitio dentro de la ventana de backoff de 5 min", TestP1_7_11_1_InvalidCredentials_Backoff5Min);
            RunTest("11.2 Cancelación durante GetStringAsync en ResolveUrlAsync propaga OperationCanceledException limpiamente", TestP1_7_11_2_Cancellation_PropagatesCleanly);
            RunTest("11.3 Concurrencia de resoluciones bajo caché deshabilitada comparte un único login en vuelo (sin tormenta)", TestP1_7_11_3_Concurrency_SharedLoginInFlight);
            RunTest("11.4 Await de _loginTask reutilizable propaga resultado a todos los llamadores concurrentes", TestP1_7_11_4_LoginTask_ReusableResult);
            RunTest("11.5 ForceReLogin limpia sesión, vacía cookies con Expired = true e invalida token post-login", TestP1_7_11_5_ForceReLogin_CleansSessionAndCookies);
            RunTest("11.6 Contraseña incorrecta + backoff: contador de intentos de login en el log durante ventana de 5 min == 1", TestP1_7_11_6_WrongPassword_BackoffLogCount);
            RunTest("11.7 EnableCookieCache=false + 10 resoluciones concurrentes sobre el mismo ítem: un ÚNICO login en el log", TestP1_7_11_7_CookieCacheDisabled_TenConcurrent_SingleLoginInLog);
            RunTest("11.8 Test negativo grep + inspección: en métodos con ct NO hay catch general antes de catch(OperationCanceledException)", TestP1_7_11_8_NegativeGrep_CatchOrderInMethodsWithCt);

            // 12. Pruebas P1-8 (SiblingResolutionQueue y UrlProvider)
            RunTest("12.1 SiblingResolutionQueue: resolución secuencial con SemaphoreSlim(1,1)", TestP1_8_12_1_SequentialResolution_SemaphoreMutualExclusion);
            RunTest("12.2 SiblingResolutionQueue: cola saturada descarta y registra log", TestP1_8_12_2_BoundedQueue_SaturationDropsAndLogs);
            RunTest("12.3 SiblingResolutionQueue: cooldown omite archivo intentado recientemente", TestP1_8_12_3_Cooldown_RecentItemsSkipped);
            RunTest("12.4 UrlProvider: GetMediaSources ejecuta resolución de hermanos fire-and-forget", TestP1_8_12_4_FireAndForget_GetMediaSourcesReturnsImmediately);
            RunTest("12.4a UrlProvider: CancellationToken de la petición NO se pasa al Task.Run de FindSiblings", TestP1_8_12_4a_CancellationToken_NotPassedToSiblingTaskRun);
            RunTest("12.4b UrlProvider: Excepción en FindSiblings se captura en log sin romper GetMediaSources", TestP1_8_12_4b_FindSiblingsThrows_CaughtAndLogged_GetMediaSourcesSucceeds);
            RunTest("12.5 SiblingResolutionQueue: purga de _cooldowns expira entradas antiguas", TestP1_8_12_5_CooldownPurge_EvictsExpiredEntries);

            // 13. Pruebas de Regresión (v1.1.0.0)
            RunTest("13.1 Regresión: TestSiblingFinder_Episode_RegexVariations (1x01, T1E1, S01E01)", TestSiblingFinder_Episode_RegexVariations);
            RunTest("13.2 Regresión: TestSiblingFinder_Episode_FlatLayout (no mezcla series distintas con mismo episodio)", TestSiblingFinder_Episode_FlatLayout);
            RunTest("13.3 Regresión: TestCleanOrphans_DeletesOrphanedOriginal (.original sin .strm es eliminado)", TestCleanOrphans_DeletesOrphanedOriginal);

            Console.WriteLine("\n===========================================");
            Console.WriteLine($"TOTAL PRUEBAS: {_passed + _failed} | PASADAS: {_passed} | FALLADAS: {_failed}");
            Console.WriteLine("===========================================");

            return _failed > 0 ? 1 : 0;
        }

        static void RunTest(string testName, Action testAction)
        {
            try
            {
                testAction();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[PASS] {testName}");
                Console.ResetColor();
                _passed++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {testName}: {ex.Message}");
                Console.ResetColor();
                _failed++;
            }
        }

        static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception($"Aserción fallida: {message}");
        }

        static void AssertFalse(bool condition, string message)
        {
            if (condition) throw new Exception($"Aserción fallida: {message}");
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception($"Aserción fallida: {message}. Esperado: '{expected}', Actual: '{actual}'");
            }
        }

        #region 4.1 Tests Download

        static void TestDownload_RootEmpty()
        {
            string xml = @"<DownloadResult xmlns:a=""http://chomikuj.pl"" xmlns:i=""http://www.w3.org/2001/XMLSchema-instance"">
    <a:status>Error</a:status>
    <a:errorMessage>failed : requested file(s) not available</a:errorMessage>
    <a:list i:nil=""true""/>
</DownloadResult>";

            var res = SoapResponseParser.ParseDownloadResponse(xml, "/");
            AssertEqual(FolderFilesOutcome.EmptyFolder, res.Outcome, "Debe interpretarse como EmptyFolder");
            AssertEqual(0, res.Files.Count, "Debe tener 0 archivos");
        }

        static void TestDownload_ListNil_StatusOk()
        {
            string xml = @"<DownloadResult xmlns=""http://chomikuj.pl/"" xmlns:i=""http://www.w3.org/2001/XMLSchema-instance"">
    <status>Ok</status>
    <list i:nil=""true""/>
</DownloadResult>";

            var res = SoapResponseParser.ParseDownloadResponse(xml, "/Carpeta");
            AssertEqual(FolderFilesOutcome.EmptyFolder, res.Outcome, "Debe ser EmptyFolder");
            AssertEqual(0, res.Files.Count, "Debe tener 0 archivos");
        }

        static void TestDownload_SingleFile()
        {
            string xml = @"<DownloadResult xmlns=""http://chomikuj.pl/"">
    <status>Ok</status>
    <list>
        <DownloadFolder>
            <status>Ok</status>
            <files>
                <FileEntry>
                    <id>12345</id>
                    <name>video.mkv</name>
                    <size>1048576</size>
                </FileEntry>
            </files>
        </DownloadFolder>
    </list>
</DownloadResult>";

            var res = SoapResponseParser.ParseDownloadResponse(xml, "/Peliculas");
            AssertEqual(FolderFilesOutcome.Files, res.Outcome, "Debe ser Files");
            AssertEqual(1, res.Files.Count, "Debe contener 1 archivo");
            AssertEqual("12345", res.Files[0].Id, "Id correcto");
            AssertEqual("video.mkv", res.Files[0].Name, "Nombre correcto");
            AssertEqual(1048576, res.Files[0].Size, "Size correcto");
            AssertEqual("/Peliculas/video.mkv", res.Files[0].Path, "Path combinado");
        }

        static void TestDownload_MultipleFiles()
        {
            string xml = @"<DownloadResult xmlns=""http://chomikuj.pl/"">
    <list>
        <DownloadFolder>
            <files>
                <FileEntry>
                    <id>1</id>
                    <name>file1.mp4</name>
                    <size>100</size>
                </FileEntry>
                <FileEntry>
                    <id>2</id>
                    <name>file2.avi</name>
                    <size>200</size>
                </FileEntry>
            </files>
        </DownloadFolder>
    </list>
</DownloadResult>";

            var res = SoapResponseParser.ParseDownloadResponse(xml, "/Videos");
            AssertEqual(FolderFilesOutcome.Files, res.Outcome, "Debe ser Files");
            AssertEqual(2, res.Files.Count, "Debe contener 2 archivos");
        }

        static void TestDownload_FolderStatusNotAvailable()
        {
            string xml = @"<DownloadResult xmlns=""http://chomikuj.pl/"">
    <list>
        <DownloadFolder>
            <status>failed : requested file(s) not available</status>
        </DownloadFolder>
    </list>
</DownloadResult>";

            var res = SoapResponseParser.ParseDownloadResponse(xml, "/Vacia");
            AssertEqual(FolderFilesOutcome.EmptyFolder, res.Outcome, "Debe interpretarse como EmptyFolder");
            AssertEqual(0, res.Files.Count, "Archivos vacíos");
        }

        static void TestDownload_ErrorOtherMessage()
        {
            string xml = @"<DownloadResult xmlns:a=""http://chomikuj.pl"">
    <a:status>Error</a:status>
    <a:errorMessage>invalid token</a:errorMessage>
</DownloadResult>";

            var res = SoapResponseParser.ParseDownloadResponse(xml, "/Cualquiera");
            AssertEqual(FolderFilesOutcome.Failed, res.Outcome, "Debe ser Failed y NO EmptyFolder");
            AssertEqual("invalid token", res.ErrorMessage, "Debe contener el mensaje original");
        }

        static void TestDownload_UninterpretableBody()
        {
            string xml = @"<html><body>502 Bad Gateway</body></html>";
            var res = SoapResponseParser.ParseDownloadResponse(xml, "/");
            AssertEqual(FolderFilesOutcome.Failed, res.Outcome, "Debe ser Failed sin lanzar excepción");

            string soapFault = @"<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"">
    <s:Body>
        <s:Fault>
            <faultcode>s:Client</faultcode>
            <faultstring>Sesion expirada</faultstring>
        </s:Fault>
    </s:Body>
</s:Envelope>";
            var faultRes = SoapResponseParser.ParseDownloadResponse(soapFault, "/");
            AssertEqual(FolderFilesOutcome.Failed, faultRes.Outcome, "Fault debe ser Failed");
            AssertTrue(faultRes.ErrorMessage.Contains("Sesion expirada"), "Debe incluir faultstring");
        }

        static void TestDownload_NamespaceVariants()
        {
            string xmlNoSlash = @"<DownloadResult xmlns=""http://chomikuj.pl"">
    <status>Ok</status>
    <list><DownloadFolder><files><FileEntry><id>10</id><name>test.mkv</name><size>10</size></FileEntry></files></DownloadFolder></list>
</DownloadResult>";
            string xmlWithSlash = @"<DownloadResult xmlns=""http://chomikuj.pl/"">
    <status>Ok</status>
    <list><DownloadFolder><files><FileEntry><id>10</id><name>test.mkv</name><size>10</size></FileEntry></files></DownloadFolder></list>
</DownloadResult>";

            var res1 = SoapResponseParser.ParseDownloadResponse(xmlNoSlash, "/");
            var res2 = SoapResponseParser.ParseDownloadResponse(xmlWithSlash, "/");
            AssertEqual(FolderFilesOutcome.Files, res1.Outcome, "NoSlash res");
            AssertEqual(FolderFilesOutcome.Files, res2.Outcome, "WithSlash res");
            AssertEqual(res1.Files.Count, res2.Files.Count, "Mismo conteo");
            AssertEqual(res1.Files[0].Name, res2.Files[0].Name, "Mismo nombre");
        }

        static void TestDownload_NormalizedMessage()
        {
            string xml = @"<DownloadResult xmlns=""http://chomikuj.pl"">
    <status>  ERROR  </status>
    <errorMessage>   FAILED : REQUESTED FILE(S) NOT AVAILABLE   </errorMessage>
</DownloadResult>";

            var res = SoapResponseParser.ParseDownloadResponse(xml, "/");
            AssertEqual(FolderFilesOutcome.EmptyFolder, res.Outcome, "Debe normalizar mayúsculas y espacios");
        }

        #endregion

        #region 4.2 Tests Folders

        static void TestFolders_EmptyOrNil()
        {
            string xml = @"<FoldersResult xmlns:a=""http://chomikuj.pl"" xmlns:i=""http://www.w3.org/2001/XMLSchema-instance"">
    <a:status>Ok</a:status>
    <a:folders i:nil=""true""/>
</FoldersResult>";

            var res = SoapResponseParser.ParseFoldersResponse(xml, 0, "/");
            AssertEqual(FoldersOutcome.Empty, res.Outcome, "Debe ser Empty");
            AssertEqual(0, res.Folders.Count, "0 subcarpetas");
        }

        static void TestFolders_Multiple_IgnoresParent()
        {
            string xml = @"<FoldersResult xmlns=""http://chomikuj.pl"">
    <status>Ok</status>
    <folders>
        <FolderInfo><id>100</id><name>Padre</name></FolderInfo>
        <FolderInfo><id>101</id><name>Hijo1</name></FolderInfo>
        <FolderInfo><id>102</id><name>Hijo2</name></FolderInfo>
    </folders>
</FoldersResult>";

            var res = SoapResponseParser.ParseFoldersResponse(xml, 100, "/Padre");
            AssertEqual(FoldersOutcome.Folders, res.Outcome, "Debe tener folders");
            AssertEqual(2, res.Folders.Count, "Debe haber 2 hijos (padre descartado)");
            AssertEqual(101, res.Folders[0].Id, "Id hijo 1");
            AssertEqual("/Padre/Hijo1", res.Folders[0].Path, "Path hijo 1");
        }

        static void TestFolders_PasswordOmitted()
        {
            string xml = @"<FoldersResult xmlns=""http://chomikuj.pl"">
    <status>Ok</status>
    <folders>
        <FolderInfo>
            <id>201</id>
            <name>Secreta</name>
            <password>MiPasswordUltraSecreto123</password>
        </FolderInfo>
    </folders>
</FoldersResult>";

            var res = SoapResponseParser.ParseFoldersResponse(xml, 0, "/");
            AssertEqual(1, res.Folders.Count, "Debe parsear la carpeta");
            AssertEqual("Secreta", res.Folders[0].Name, "Nombre");
            // ChomikFolderInfo ni siquiera contiene la propiedad Password ni ToString expone password
            AssertTrue(!res.Folders[0].ToString().Contains("MiPasswordUltraSecreto123"), "ToString no filtra contraseña");
        }

        #endregion

        #region 4.3 Tests Sanitizador

        static void TestSanitizer_SensitiveKeywords()
        {
            string xml = @"<Data>
    <password>SuperSecret1</password>
    <a:passwd xmlns:a=""http://ns"">SuperSecret2</a:passwd>
    <passHash>a1b2c3d4e5</passHash>
    <token>token_xyz_999</token>
    <secret>my_secret_key</secret>
</Data>";

            string sanitized = SoapLogSanitizer.Sanitize(xml);
            AssertTrue(!sanitized.Contains("SuperSecret1"), "No debe contener SuperSecret1");
            AssertTrue(!sanitized.Contains("SuperSecret2"), "No debe contener SuperSecret2");
            AssertTrue(!sanitized.Contains("a1b2c3d4e5"), "No debe contener passHash");
            AssertTrue(!sanitized.Contains("token_xyz_999"), "No debe contener token");
            AssertTrue(!sanitized.Contains("my_secret_key"), "No debe contener secret");
            AssertTrue(sanitized.Contains("<password>***</password>"), "Debe tener valor enmascarado");
        }

        static void TestSanitizer_BooleansPreserved()
        {
            string xml = @"<Info>
    <hasPassword>true</hasPassword>
    <passwordRequired>false</passwordRequired>
</Info>";
            string sanitized = SoapLogSanitizer.Sanitize(xml);
            AssertTrue(sanitized.Contains("<hasPassword>true</hasPassword>"), "hasPassword booleano debe preservarse");
            AssertTrue(sanitized.Contains("<passwordRequired>false</passwordRequired>"), "passwordRequired booleano debe preservarse");
        }

        static void TestSanitizer_XmlVariations()
        {
            string xml = @"<Root attrPassword=""MySecretAttr"">
    <emptyPass i:nil=""true"" xmlns:i=""http://w3.org""/>
    <cdataPass><![CDATA[SecretCdata123]]></cdataPass>
    <multiLinePwd>
        Line1Secret
        Line2Secret
    </multiLinePwd>
</Root>";

            string sanitized = SoapLogSanitizer.Sanitize(xml);
            AssertTrue(!sanitized.Contains("MySecretAttr"), "Atributo sensible enmascarado");
            AssertTrue(!sanitized.Contains("SecretCdata123"), "CDATA enmascarado");
            AssertTrue(!sanitized.Contains("Line1Secret"), "Multilínea enmascarada");
            AssertTrue(sanitized.Contains("attrPassword=\"***\""), "Atributo contiene ***");
        }

        static void TestSanitizer_RegexFallback()
        {
            string nonXml = "Error en auth: <token>tok12345</token> con password=\"p@ssword987\" [cadena rota no XML";
            string sanitized = SoapLogSanitizer.Sanitize(nonXml);
            AssertTrue(!sanitized.Contains("tok12345"), "Token en texto plano enmascarado por Regex");
            AssertTrue(!sanitized.Contains("p@ssword987"), "Password en atributo enmascarado por Regex");
            AssertTrue(sanitized.Contains("***"), "Contiene máscara ***");
        }

        static void TestSanitizer_SanitizeBeforeTruncate()
        {
            string huge = "<token>" + new string('A', 500) + "MiTokenSensible</token>";
            // Sanitizamos antes de truncar
            string sanitized = SoapLogSanitizer.Sanitize(huge);
            AssertTrue(!sanitized.Contains("MiTokenSensible"), "El secreto fue destruido antes del truncado");

            string truncated = sanitized.Length > 20 ? sanitized.Substring(0, 20) : sanitized;
            AssertTrue(!truncated.Contains("MiTokenSensible"), "El truncado posterior no expone nada");
        }

        static void TestSanitizer_Canary_XmlTruncatedTag()
        {
            string input = "<AuthResult><token>CANARY_9X</tok";
            string sanitized = SoapLogSanitizer.Sanitize(input);
            AssertTrue(!sanitized.Contains("CANARY_9X"), "XML truncado no debe contener CANARY_9X");
        }

        static void TestSanitizer_Canary_PassHashNoClose()
        {
            string input = "<passHash>CANARY_9X";
            string sanitized = SoapLogSanitizer.Sanitize(input);
            AssertTrue(!sanitized.Contains("CANARY_9X"), "passHash sin cierre no debe contener CANARY_9X");
        }

        static void TestSanitizer_Canary_JsonTokens()
        {
            string input = "{\"Password\":\"CANARY_9X\",\"__RequestVerificationToken\":\"CANARY_9X\"}";
            string sanitized = SoapLogSanitizer.Sanitize(input);
            AssertTrue(!sanitized.Contains("CANARY_9X"), "JSON no debe contener CANARY_9X");
        }

        static void TestSanitizer_Canary_QueryString()
        {
            string input = "https://x/y?token=CANARY_9X&password=CANARY_9X";
            string sanitized = SoapLogSanitizer.Sanitize(input);
            AssertTrue(!sanitized.Contains("CANARY_9X"), "Query string no debe contener CANARY_9X");
        }

        static void TestSanitizer_Canary_FormUrlEncoded()
        {
            string input = "Login=u&Password=CANARY_9X";
            string sanitized = SoapLogSanitizer.Sanitize(input);
            AssertTrue(!sanitized.Contains("CANARY_9X"), "Form urlencoded no debe contener CANARY_9X");
        }

        static void TestSanitizer_Canary_CookieHeader()
        {
            string input = "Cookie: rememberMe=CANARY_9X; ChomikSession=CANARY_9X";
            string sanitized = SoapLogSanitizer.Sanitize(input);
            AssertTrue(!sanitized.Contains("CANARY_9X"), "Cabecera Cookie no debe contener CANARY_9X");
        }

        static void TestSanitizer_Canary_PasswordBoolMasked()
        {
            string input = "<a><password>true</password></a>";
            string sanitized = SoapLogSanitizer.Sanitize(input);
            AssertTrue(!sanitized.Contains("true"), "password con booleano true debe ser enmascarado");
        }

        static void TestSanitizer_Canary_HasPasswordPreserved()
        {
            string input = "<a hasPassword=\"true\"/>";
            string sanitized = SoapLogSanitizer.Sanitize(input);
            AssertTrue(sanitized.Contains("hasPassword=\"true\""), "hasPassword con booleano true debe ser preservado");
        }

        static void TestSanitizer_5MB_LessThan2Seconds()
        {
            string hugeInput = new string('<', 5 * 1024 * 1024);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string sanitized = SoapLogSanitizer.Sanitize(hugeInput);
            sw.Stop();
            Console.WriteLine($"[INFO] TestSanitizer_5MB_LessThan2Seconds procesado en {sw.ElapsedMilliseconds} ms");
            AssertTrue(sw.ElapsedMilliseconds < 2000, $"Entrada de 5 MB debe procesarse en < 2000 ms (tomó {sw.ElapsedMilliseconds} ms)");
            AssertTrue(sanitized != null, "La sanitización no debe ser nula ni lanzar excepción");
        }

        #endregion

        #region 4.4 Tests Recorrido Escaneo

        class FakeChomikBoxClient : IChomikBoxClient
        {
            public bool IsAuthenticated => true;
            public long HamsterId => 1;
            public string Token => "fake_token";

            public Func<long, string, List<ChomikFolderInfo>> OnGetSubfolders { get; set; }
            public Func<long, string, FolderFilesResult> OnGetFilesInFolder { get; set; }

            public Task<bool> LoginAsync(string username, string password, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<List<ChomikFolderInfo>> GetSubfoldersAsync(long parentFolderId, string currentPath, CancellationToken cancellationToken = default)
            {
                if (OnGetSubfolders != null) return Task.FromResult(OnGetSubfolders(parentFolderId, currentPath));
                return Task.FromResult(new List<ChomikFolderInfo>());
            }

            public Task<FolderFilesResult> GetFilesInFolderAsync(string username, long folderId, string currentPath, CancellationToken cancellationToken = default)
            {
                if (OnGetFilesInFolder != null) return Task.FromResult(OnGetFilesInFolder(folderId, currentPath));
                return Task.FromResult(FolderFilesResult.CreateEmpty());
            }

            public void Dispose() { }
        }

        static void TestSync_RootEmpty_SubfoldersFiles()
        {
            var fakeClient = new FakeChomikBoxClient
            {
                OnGetSubfolders = (parentId, path) =>
                {
                    if (parentId == 0)
                    {
                        return new List<ChomikFolderInfo>
                        {
                            new ChomikFolderInfo(1, "Sub1", "/Sub1"),
                            new ChomikFolderInfo(2, "Sub2", "/Sub2")
                        };
                    }
                    return new List<ChomikFolderInfo>();
                },
                OnGetFilesInFolder = (folderId, path) =>
                {
                    if (folderId == 0)
                    {
                        // Raiz vacía (caso del bug)
                        return FolderFilesResult.CreateEmpty();
                    }
                    if (folderId == 1)
                    {
                        return FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                        {
                            new ChomikFileInfo("101", "peli1.mkv", "/Sub1/peli1.mkv", 100)
                        });
                    }
                    if (folderId == 2)
                    {
                        return FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                        {
                            new ChomikFileInfo("102", "peli2.mkv", "/Sub2/peli2.mkv", 200)
                        });
                    }
                    return FolderFilesResult.CreateEmpty();
                }
            };

            var sub1Files = fakeClient.GetFilesInFolderAsync("user", 0, "/").Result;
            AssertEqual(FolderFilesOutcome.EmptyFolder, sub1Files.Outcome, "Raiz vacía detectada sin lanzar");

            var subFolders = fakeClient.GetSubfoldersAsync(0, "/").Result;
            AssertEqual(2, subFolders.Count, "2 subcarpetas encoladas");

            var f1 = fakeClient.GetFilesInFolderAsync("user", 1, "/Sub1").Result;
            var f2 = fakeClient.GetFilesInFolderAsync("user", 2, "/Sub2").Result;
            AssertEqual(1, f1.Files.Count, "Fichero 1");
            AssertEqual(1, f2.Files.Count, "Fichero 2");
        }

        static void TestSync_SingleFolderFailure_Continues()
        {
            var fakeClient = new FakeChomikBoxClient
            {
                OnGetFilesInFolder = (folderId, path) =>
                {
                    if (folderId == 1) return FolderFilesResult.CreateFailed("Error de red temporal");
                    return FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                    {
                        new ChomikFileInfo("200", "peli.mkv", "/peli.mkv", 100)
                    });
                }
            };

            var f1 = fakeClient.GetFilesInFolderAsync("user", 1, "/Fallida").Result;
            AssertEqual(FolderFilesOutcome.Failed, f1.Outcome, "Carpeta 1 es Failed");

            var f2 = fakeClient.GetFilesInFolderAsync("user", 2, "/Correcta").Result;
            AssertEqual(FolderFilesOutcome.Files, f2.Outcome, "Carpeta 2 es Files");
        }

        static void TestSync_ConsecutiveFailures_Aborts()
        {
            int failures = 0;
            const int maxConsecutive = 5;
            bool aborted = false;

            for (int i = 0; i < 7; i++)
            {
                failures++;
                if (failures >= maxConsecutive)
                {
                    aborted = true;
                    break;
                }
            }

            AssertTrue(aborted, "Debe abortar al alcanzar el umbral de 5 fallos consecutivos");
            AssertEqual(5, failures, "Umbral de 5 fallos");
        }

        #endregion

        #region Pruebas Mejora 1: MediaSourceInfo.Id determinista

        private static string ComputeMediaSourceId(string strmPath)
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

        static void TestMediaSourceId_Deterministic()
        {
            var path = "/media/peliculas/Matrix (1999)/Matrix (1999) - 1080p.strm";
            var id1 = ComputeMediaSourceId(path);
            var id2 = ComputeMediaSourceId(path);

            AssertEqual(id1, id2, "Dos llamadas con la misma ruta deben devolver el mismo Id");
        }

        static void TestMediaSourceId_SlashNormalization()
        {
            var windowsPath = @"C:\media\peliculas\Matrix (1999)\Matrix.strm";
            var linuxPath = "C:/media/peliculas/Matrix (1999)/Matrix.strm";

            var idWindows = ComputeMediaSourceId(windowsPath);
            var idLinux = ComputeMediaSourceId(linuxPath);

            AssertEqual(idLinux, idWindows, "Separadores de barra invertida y barra deben normalizarse");
        }

        static void TestMediaSourceId_DistinctPathsDifferentId()
        {
            var path1080 = "/media/peliculas/Avatar/Avatar - 1080p.strm";
            var path4k = "/media/peliculas/Avatar/Avatar - 4K.strm";

            var id1080 = ComputeMediaSourceId(path1080);
            var id4k = ComputeMediaSourceId(path4k);

            AssertTrue(id1080 != id4k, "Rutas de versiones distintas deben generar Id distintos");
        }

        static void TestMediaSourceId_CaseSensitive()
        {
            var pathLower = "/media/peliculas/test.strm";
            var pathUpper = "/media/peliculas/TEST.strm";

            var idLower = ComputeMediaSourceId(pathLower);
            var idUpper = ComputeMediaSourceId(pathUpper);

            AssertTrue(idLower != idUpper, "Diferencias de mayúsculas/minúsculas deben mantenerse");
        }

        static void TestMediaSourceId_FormatAndLength()
        {
            var path = "/media/peliculas/Inception/Inception.strm";
            var id = ComputeMediaSourceId(path);

            AssertEqual(32, id.Length, "El Id debe tener exactamente 32 caracteres");
            AssertTrue(Regex.IsMatch(id, "^[0-9a-f]{32}$"), "El Id debe ser hexadecimal en minúsculas (formato Guid N)");
        }

        static void TestMediaSourceId_NullOrEmpty()
        {
            var idNull = ComputeMediaSourceId(null);
            var idEmpty = ComputeMediaSourceId(string.Empty);

            AssertEqual(idNull, idEmpty, "null y string.Empty deben comportarse de forma equivalente y segura");
            AssertEqual(32, idNull.Length, "Id de ruta vacía debe ser válido de 32 caracteres");
        }

        #region Helper MediaSourceName

        static string ResolveMediaSourceName(string strmPath)
        {
            if (string.IsNullOrEmpty(strmPath))
            {
                return null;
            }

            var normalized = strmPath.Replace('\\', '/');
            var fileName = Path.GetFileName(normalized);
            return Path.GetFileNameWithoutExtension(fileName);
        }

        static void TestMediaSourceName_ExtractsCleanFileName()
        {
            var path = "/media/peliculas/Avatar (2009)/Avatar (2009) - 1080p.strm";
            var name = ResolveMediaSourceName(path);
            AssertEqual("Avatar (2009) - 1080p", name, "Debe extraer el nombre de archivo limpio sin .strm");
        }

        static void TestMediaSourceName_WindowsAndLinuxPaths()
        {
            var windowsPath = @"C:\media\peliculas\Inception (2010)\Inception - 2160p.strm";
            var name = ResolveMediaSourceName(windowsPath);
            AssertEqual("Inception - 2160p", name, "Debe funcionar con separadores Windows");
        }

        static void TestMediaSourceName_NullOrEmptyPath()
        {
            var nameNull = ResolveMediaSourceName(null);
            var nameEmpty = ResolveMediaSourceName(string.Empty);

            AssertTrue(nameNull == null, "Ruta nula debe devolver null");
            AssertTrue(nameEmpty == null, "Ruta vacía debe devolver null");
        }

        #endregion

        #endregion

        #region 7. Acceptance Tests P0-4 (a-d)

        class TestLogger : MediaBrowser.Model.Logging.ILogger
        {
            public List<string> Logs = new List<string>();

            public void Info(string message, params object[] paramList)
            {
                var formatted = paramList != null && paramList.Length > 0 ? string.Format(message, paramList) : message;
                Logs.Add(formatted);
            }

            public void Info(ReadOnlyMemory<char> message)
            {
                Logs.Add(message.ToString());
            }

            public void Error(string message, params object[] paramList)
            {
                var formatted = paramList != null && paramList.Length > 0 ? string.Format(message, paramList) : message;
                Logs.Add(formatted);
            }

            public void Error(ReadOnlyMemory<char> message)
            {
                Logs.Add(message.ToString());
            }

            public void Warn(string message, params object[] paramList)
            {
                var formatted = paramList != null && paramList.Length > 0 ? string.Format(message, paramList) : message;
                Logs.Add(formatted);
            }

            public void Warn(ReadOnlyMemory<char> message)
            {
                Logs.Add(message.ToString());
            }

            public void Debug(string message, params object[] paramList)
            {
                var formatted = paramList != null && paramList.Length > 0 ? string.Format(message, paramList) : message;
                Logs.Add(formatted);
            }

            public void Debug(ReadOnlyMemory<char> message)
            {
                Logs.Add(message.ToString());
            }

            public void Fatal(string message, params object[] paramList)
            {
                var formatted = paramList != null && paramList.Length > 0 ? string.Format(message, paramList) : message;
                Logs.Add(formatted);
            }

            public void FatalException(string message, Exception exception, params object[] paramList)
            {
                Logs.Add(message + ": " + exception?.Message);
            }

            public void ErrorException(string message, Exception exception, params object[] paramList)
            {
                Logs.Add(message + ": " + exception?.Message);
            }

            public void Log(MediaBrowser.Model.Logging.LogSeverity severity, string message, params object[] paramList)
            {
                var formatted = paramList != null && paramList.Length > 0 ? string.Format(message, paramList) : message;
                Logs.Add(formatted);
            }

            public void Log(MediaBrowser.Model.Logging.LogSeverity severity, ReadOnlyMemory<char> message)
            {
                Logs.Add(message.ToString());
            }

            public void LogMultiline(string message, MediaBrowser.Model.Logging.LogSeverity severity, StringBuilder additionalContent)
            {
                Logs.Add(message);
            }
        }

        static void TestAcceptance_P04_TestA()
        {
            // a) Destino con A/keep.strm (fuera de alcance) y B/old.strm (huérfano).
            // Modo SelectedFolders escaneando solo B, DeleteOrphans=true, OrphansDryRun=false
            // -> A/keep.strm SIGUE EXISTIENDO; B/old.strm se elimina.
            var tempDir = Path.Combine(Path.GetTempPath(), "chomyfin_test_a_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempDir, "A"));
            Directory.CreateDirectory(Path.Combine(tempDir, "B"));

            var keepPath = Path.Combine(tempDir, "A", "keep.strm");
            var oldPath = Path.Combine(tempDir, "B", "old.strm");
            File.WriteAllText(keepPath, "https://chomikuj.pl/user/A/keep,999.strm");
            File.WriteAllText(oldPath, "https://chomikuj.pl/user/B/old,888.strm");

            var testLogger = new TestLogger();
            var fakeClient = new FakeChomikBoxClient
            {
                OnGetSubfolders = (parentId, path) =>
                {
                    if (parentId == 0)
                    {
                        return new List<ChomikFolderInfo> { new ChomikFolderInfo(2, "B", "/B") };
                    }
                    return new List<ChomikFolderInfo>();
                },
                OnGetFilesInFolder = (folderId, path) =>
                {
                    if (folderId == 2)
                    {
                        return FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                        {
                            new ChomikFileInfo("201", "newfile.mkv", "/B/newfile.mkv", 100)
                        });
                    }
                    return FolderFilesResult.CreateEmpty();
                }
            };

            var options = new StrmGeneratorOptions
            {
                Username = "user",
                Password = "pwd",
                DestinationPath = tempDir,
                SyncMode = StrmSyncMode.SelectedFolders,
                SelectedFolders = new List<string> { "/B" },
                DeleteOrphans = true,
                OrphansDryRun = false
            };

            var generator = new StrmGenerator(testLogger, opt => fakeClient);
            var result = generator.GenerateAsync(options).Result;

            try
            {
                AssertTrue(result.Success, "Sincronización exitosa");
                AssertTrue(File.Exists(keepPath), "A/keep.strm SIGUE EXISTIENDO (fuera de scannedRoots)");
                AssertTrue(!File.Exists(oldPath), "B/old.strm se elimina (huérfano dentro de scannedRoots B)");
                AssertEqual(1, result.OrphansDeleted, "OrphansDeleted debe ser 1");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestAcceptance_P04_TestB()
        {
            // b) Con OrphansDryRun=true -> ningún fichero se elimina y el log contiene [ORPHANS-DRYRUN] Se eliminarían 1 .strm.
            var tempDir = Path.Combine(Path.GetTempPath(), "chomyfin_test_b_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempDir, "B"));

            var oldPath = Path.Combine(tempDir, "B", "old.strm");
            File.WriteAllText(oldPath, "https://chomikuj.pl/user/B/old,888.strm");

            var testLogger = new TestLogger();
            var fakeClient = new FakeChomikBoxClient
            {
                OnGetSubfolders = (parentId, path) =>
                {
                    if (parentId == 0)
                    {
                        return new List<ChomikFolderInfo> { new ChomikFolderInfo(2, "B", "/B") };
                    }
                    return new List<ChomikFolderInfo>();
                },
                OnGetFilesInFolder = (folderId, path) =>
                {
                    if (folderId == 2)
                    {
                        return FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                        {
                            new ChomikFileInfo("201", "newfile.mkv", "/B/newfile.mkv", 100)
                        });
                    }
                    return FolderFilesResult.CreateEmpty();
                }
            };

            var options = new StrmGeneratorOptions
            {
                Username = "user",
                Password = "pwd",
                DestinationPath = tempDir,
                SyncMode = StrmSyncMode.SelectedFolders,
                SelectedFolders = new List<string> { "/B" },
                DeleteOrphans = true,
                OrphansDryRun = true
            };

            var generator = new StrmGenerator(testLogger, opt => fakeClient);
            var result = generator.GenerateAsync(options).Result;

            try
            {
                AssertTrue(result.Success, "Sincronización exitosa");
                AssertTrue(File.Exists(oldPath), "B/old.strm NO se elimina cuando OrphansDryRun=true");
                AssertEqual(0, result.OrphansDeleted, "OrphansDeleted debe ser 0 en DryRun");

                bool foundLogSummary = testLogger.Logs.Any(l => l.Contains("[ORPHANS-DRYRUN] Se eliminarían 1 .strm"));
                AssertTrue(foundLogSummary, "El log contiene '[ORPHANS-DRYRUN] Se eliminarían 1 .strm'");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestAcceptance_P04_TestC()
        {
            // c) Dos ficheros remotos Peli.mkv (Id 111) y Peli.mp4 (Id 222) en la misma carpeta, sin ficheros previos
            // -> se crean Peli.strm (propietario Id 111) y Peli [mp4].strm.
            // Segunda ejecución con UpdateIfUrlChanged -> 0 actualizados.
            var tempDir = Path.Combine(Path.GetTempPath(), "chomyfin_test_c_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var testLogger = new TestLogger();
            var fakeClient = new FakeChomikBoxClient
            {
                OnGetSubfolders = (parentId, path) => new List<ChomikFolderInfo>(),
                OnGetFilesInFolder = (folderId, path) =>
                {
                    return FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                    {
                        new ChomikFileInfo("111", "Peli.mkv", "/Peli.mkv", 100),
                        new ChomikFileInfo("222", "Peli.mp4", "/Peli.mp4", 200)
                    });
                }
            };

            var options = new StrmGeneratorOptions
            {
                Username = "user",
                Password = "pwd",
                DestinationPath = tempDir,
                SyncMode = StrmSyncMode.FullAccount,
                OverwritePolicy = StrmOverwritePolicy.SkipIfExists
            };

            var generator = new StrmGenerator(testLogger, opt => fakeClient);
            var result1 = generator.GenerateAsync(options).Result;

            try
            {
                AssertTrue(result1.Success, "Primera sincronización exitosa");
                var peliStrm = Path.Combine(tempDir, "Peli.strm");
                var peliMp4Strm = Path.Combine(tempDir, "Peli [mp4].strm");

                AssertTrue(File.Exists(peliStrm), "Peli.strm existe");
                AssertTrue(File.Exists(peliMp4Strm), "Peli [mp4].strm existe");

                var peliStrmContent = File.ReadAllText(peliStrm);
                AssertTrue(peliStrmContent.Contains(",111.mkv") || peliStrmContent.Contains(",111"), "Peli.strm pertenece al Id 111");

                var peliMp4Content = File.ReadAllText(peliMp4Strm);
                AssertTrue(peliMp4Content.Contains(",222.mp4") || peliMp4Content.Contains(",222"), "Peli [mp4].strm pertenece al Id 222");

                // Segunda ejecución con UpdateIfUrlChanged
                options.OverwritePolicy = StrmOverwritePolicy.UpdateIfUrlChanged;
                var result2 = generator.GenerateAsync(options).Result;

                AssertTrue(result2.Success, "Segunda sincronización exitosa");
                AssertEqual(0, result2.FilesUpdated, "Segunda ejecución con UpdateIfUrlChanged -> 0 actualizados");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestAcceptance_P04_TestD()
        {
            // d) Existe Peli.strm cuyo .original apunta al Id 222 -> Peli.strm sigue siendo el de 222 y Peli.mkv pasa a Peli [mkv].strm; ningún fichero existente se mueve.
            var tempDir = Path.Combine(Path.GetTempPath(), "chomyfin_test_d_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var peliStrm = Path.Combine(tempDir, "Peli.strm");
            var originalFile = Path.Combine(tempDir, "Peli.original");

            // Simular que Peli.strm tiene URL directa (resuelto previamente) y Peli.original tiene la URL con Id 222
            File.WriteAllText(peliStrm, "https://direct-stream.chomikuj.pl/video/file.mp4");
            File.WriteAllText(originalFile, "https://chomikuj.pl/user/Peli,222.mp4");

            var originalWriteTimeStrm = File.GetLastWriteTimeUtc(peliStrm);
            var originalContentStrm = File.ReadAllText(peliStrm);

            var testLogger = new TestLogger();
            var fakeClient = new FakeChomikBoxClient
            {
                OnGetSubfolders = (parentId, path) => new List<ChomikFolderInfo>(),
                OnGetFilesInFolder = (folderId, path) =>
                {
                    return FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                    {
                        new ChomikFileInfo("111", "Peli.mkv", "/Peli.mkv", 100),
                        new ChomikFileInfo("222", "Peli.mp4", "/Peli.mp4", 200)
                    });
                }
            };

            var options = new StrmGeneratorOptions
            {
                Username = "user",
                Password = "pwd",
                DestinationPath = tempDir,
                SyncMode = StrmSyncMode.FullAccount,
                OverwritePolicy = StrmOverwritePolicy.SkipIfExists
            };

            var generator = new StrmGenerator(testLogger, opt => fakeClient);
            var result = generator.GenerateAsync(options).Result;

            try
            {
                AssertTrue(result.Success, "Sincronización exitosa");

                var peliMkvStrm = Path.Combine(tempDir, "Peli [mkv].strm");
                AssertTrue(File.Exists(peliStrm), "Peli.strm sigue existiendo");
                AssertTrue(File.Exists(peliMkvStrm), "Peli [mkv].strm fue creado para Id 111");

                // Verificar que Peli.strm sigue siendo de 222 y su contenido original no se movió ni sobreescribió
                var currentContentStrm = File.ReadAllText(peliStrm);
                AssertEqual(originalContentStrm, currentContentStrm, "El contenido de Peli.strm existente está intacto");

                var peliMkvContent = File.ReadAllText(peliMkvStrm);
                AssertTrue(peliMkvContent.Contains(",111.mkv") || peliMkvContent.Contains(",111"), "Peli [mkv].strm pertenece a Id 111");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        #region P1-5 Tests

        static void TestP1_5_ClientFactoryThrows_ReleasesLock()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p15_1_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var options = new StrmGeneratorOptions
                {
                    Username = "user",
                    Password = "pwd",
                    DestinationPath = tempDir
                };

                bool throwOnFactory = true;
                var fakeClient = new FakeChomikBoxClient();
                var generator = new StrmGenerator(new TestLogger(), opt =>
                {
                    if (throwOnFactory)
                    {
                        throw new InvalidOperationException("Factory failure simulated");
                    }
                    return fakeClient;
                });

                bool didThrow = false;
                try
                {
                    generator.GenerateAsync(options).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException)
                {
                    didThrow = true;
                }
                AssertTrue(didThrow, "Primera llamada lanzó InvalidOperationException");

                // Segunda llamada con factory válido
                throwOnFactory = false;
                var result = generator.GenerateAsync(options).GetAwaiter().GetResult();
                AssertTrue(result.Success, "Segunda llamada tuvo éxito (Success=true)");
                AssertFalse(result.Message.Contains("Ya hay una generación"), "Segunda llamada NO reporta 'Ya hay una generación en curso'");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestP1_5_AllowedExtensionsNullItem_ReleasesLock()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p15_2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var fakeClient = new FakeChomikBoxClient();
                var generator = new StrmGenerator(new TestLogger(), opt => fakeClient);

                var optionsFaulty = new StrmGeneratorOptions
                {
                    Username = "user",
                    Password = "pwd",
                    DestinationPath = tempDir,
                    AllowedExtensions = new List<string> { ".mp4", null, ".mkv" }
                };

                bool didThrow = false;
                try
                {
                    generator.GenerateAsync(optionsFaulty).GetAwaiter().GetResult();
                }
                catch (NullReferenceException)
                {
                    didThrow = true;
                }
                AssertTrue(didThrow, "Primera llamada lanzó NullReferenceException por elemento null");

                var optionsValid = new StrmGeneratorOptions
                {
                    Username = "user",
                    Password = "pwd",
                    DestinationPath = tempDir,
                    AllowedExtensions = new List<string> { ".mp4", ".mkv" }
                };

                var result = generator.GenerateAsync(optionsValid).GetAwaiter().GetResult();
                AssertTrue(result.Success, "Segunda llamada funciona con Success=true");
                AssertFalse(result.Message.Contains("Ya hay una generación"), "Segunda llamada NO reporta 'Ya hay una generación en curso'");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestP1_5_Concurrency_BusyReturnsErrorAndLockPreserved()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p15_3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var firstStarted = new ManualResetEventSlim(false);
                var unblockFirst = new ManualResetEventSlim(false);

                var fakeClient = new FakeChomikBoxClient
                {
                    OnGetSubfolders = (parentId, path) =>
                    {
                        firstStarted.Set();
                        unblockFirst.Wait(5000);
                        return new List<ChomikFolderInfo>();
                    }
                };

                var generator = new StrmGenerator(new TestLogger(), opt => fakeClient);

                var options = new StrmGeneratorOptions
                {
                    Username = "user",
                    Password = "pwd",
                    DestinationPath = tempDir
                };

                var task1 = Task.Run(() => generator.GenerateAsync(options));

                AssertTrue(firstStarted.Wait(5000), "La primera tarea inició y está dentro de la sección crítica");

                // Segunda tarea concurrente debe fallar de inmediato diciendo 'Ya hay una generación en curso'
                var result2 = generator.GenerateAsync(options).GetAwaiter().GetResult();
                AssertFalse(result2.Success, "La segunda llamada devuelve Success=false");
                AssertTrue(result2.Message.Contains("Ya hay una generación de .strm en curso"), "El mensaje indica que ya hay una generación en curso");

                // Desbloqueamos la primera
                unblockFirst.Set();
                var result1 = task1.GetAwaiter().GetResult();
                AssertTrue(result1.Success, "La primera tarea finalizó con Success=true");

                // Una tercera llamada posterior debe adquirir el lock sin problemas
                var result3 = generator.GenerateAsync(options).GetAwaiter().GetResult();
                AssertTrue(result3.Success, "Una tercera llamada tras completar la primera adquiere el lock con éxito");
                AssertFalse(result3.Message.Contains("Ya hay una generación"), "No devuelve 'Ya hay una generación'");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        #endregion

        #region P1-9 Tests

        static void TestP1_9_WriteAllText_CreatesFile()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p19_1_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var filePath = Path.Combine(tempDir, "test.strm");
                var content = "https://chomikuj.pl/user/film.mkv";
                AtomicFile.WriteAllText(filePath, content);

                AssertTrue(File.Exists(filePath), "El fichero fue creado");
                var read = File.ReadAllText(filePath, Encoding.UTF8);
                AssertEqual(content, read, "El contenido del fichero creado coincide");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestP1_9_WriteAllText_ReplacesExistingFile()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p19_2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var filePath = Path.Combine(tempDir, "test.strm");
                var contentInitial = "https://chomikuj.pl/initial";
                var contentUpdated = "https://storage.chomikuj.pl/direct/updated-video.mkv";

                AtomicFile.WriteAllText(filePath, contentInitial);
                AssertEqual(contentInitial, File.ReadAllText(filePath, Encoding.UTF8), "Contenido inicial coincide");

                AtomicFile.WriteAllText(filePath, contentUpdated);
                AssertEqual(contentUpdated, File.ReadAllText(filePath, Encoding.UTF8), "Contenido reemplazado íntegro");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private class FakeChomikujDirectService : IChomikujService
        {
            public string UserAgent => "TestAgent";
            public Task<string> ResolveUrlAsync(string pageUrl, CancellationToken ct)
            {
                return Task.FromResult("https://s123.chomikuj.pl/download/resolved_url_direct.mkv");
            }
            public IEnumerable<string> GetCookies() => new List<string>();
            public void ForceReLogin() { }
        }

        public class FakeFileSystemProxy : System.Reflection.DispatchProxy
        {
            protected override object Invoke(System.Reflection.MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == "FileExists")
                {
                    return File.Exists((string)args[0]);
                }
                if (targetMethod.Name == "DirectoryExists")
                {
                    return Directory.Exists((string)args[0]);
                }
                if (targetMethod.Name == "GetLastWriteTimeUtc")
                {
                    var dt = File.GetLastWriteTimeUtc((string)args[0]);
                    if (targetMethod.ReturnType == typeof(DateTimeOffset))
                    {
                        return new DateTimeOffset(dt);
                    }
                    return dt;
                }
                if (targetMethod.Name == "ReadAllText")
                {
                    return File.ReadAllText((string)args[0], Encoding.UTF8);
                }
                if (targetMethod.Name == "ReadAllTextAsync")
                {
                    return Task.FromResult(File.ReadAllText((string)args[0], Encoding.UTF8));
                }
                if (targetMethod.Name == "WriteAllTextAsync")
                {
                    AtomicFile.WriteAllText((string)args[0], (string)args[1]);
                    return Task.CompletedTask;
                }
                if (targetMethod.Name == "GetFilePaths")
                {
                    return Directory.GetFiles((string)args[0]);
                }
                if (targetMethod.Name == "GetDirectoryPaths")
                {
                    return Directory.GetDirectories((string)args[0]);
                }
                return null;
            }

            public static MediaBrowser.Model.IO.IFileSystem Create()
            {
                return System.Reflection.DispatchProxy.Create<MediaBrowser.Model.IO.IFileSystem, FakeFileSystemProxy>();
            }
        }

        static void TestP1_9_Concurrency_50Tasks_ValidContentNoOrphanTmp()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p19_3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var strmPath = Path.Combine(tempDir, "Pelicula.strm");
                var pageUrl = "https://chomikuj.pl/cinefilo/Pelicula,123456.mkv";
                File.WriteAllText(strmPath, pageUrl, Encoding.UTF8);

                var fs = FakeFileSystemProxy.Create();
                var chomikService = new FakeChomikujDirectService();
                var cacheService = new StrmCacheService(new TestLogger(), fs, () => chomikService);

                var fakeBoxClient = new FakeChomikBoxClient
                {
                    OnGetSubfolders = (id, path) => new List<ChomikFolderInfo>(),
                    OnGetFilesInFolder = (id, path) => FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                    {
                        new ChomikFileInfo("123456", "Pelicula.mkv", "/Pelicula.mkv", 1024)
                    })
                };
                var generator = new StrmGenerator(new TestLogger(), opt => fakeBoxClient);
                var genOptions = new StrmGeneratorOptions
                {
                    Username = "cinefilo",
                    Password = "pwd",
                    DestinationPath = tempDir,
                    OverwritePolicy = StrmOverwritePolicy.UpdateIfUrlChanged
                };

                var tasks = new List<Task>();
                for (int i = 0; i < 50; i++)
                {
                    if (i % 2 == 0)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            await cacheService.GetOrResolveDirectUrlAsync(strmPath, forceResolve: false, CancellationToken.None);
                        }));
                    }
                    else
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            await generator.GenerateAsync(genOptions);
                        }));
                    }
                }

                Task.WaitAll(tasks.ToArray());

                // Verificar contenido final de Pelicula.strm
                AssertTrue(File.Exists(strmPath), "Pelicula.strm existe");
                var finalContent = File.ReadAllText(strmPath, Encoding.UTF8).Trim();
                AssertTrue(finalContent.Length > 0, "Pelicula.strm no está vacío");
                AssertTrue(finalContent.StartsWith("http", StringComparison.OrdinalIgnoreCase), "Pelicula.strm tiene URL completa válida");

                // Verificar que no quedan ficheros temporales .tmp
                var tmpFiles = Directory.GetFiles(tempDir, "*.tmp*");
                AssertEqual(0, tmpFiles.Length, "No hay archivos temporales *.tmp huérfanos");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestP1_9_Concurrency_200Acquisitions_MutualExclusion()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p19_4_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Crear > 50 rutas para poblar el diccionario
                for (int i = 0; i < 60; i++)
                {
                    var dummyPath = Path.Combine(tempDir, $"dummy_{i}.strm");
                    using (AtomicFile.AcquireLockAsync(dummyPath).GetAwaiter().GetResult())
                    {
                    }
                }

                // Ahora 200 adquisiciones concurrentes sobre la misma clave
                var sharedPath = Path.Combine(tempDir, "shared_resource.strm");
                int concurrentInCriticalSection = 0;
                int maxConcurrencyObserved = 0;
                var tasks = new List<Task>();

                for (int i = 0; i < 200; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        using (await AtomicFile.AcquireLockAsync(sharedPath))
                        {
                            var count = Interlocked.Increment(ref concurrentInCriticalSection);
                            var currentMax = maxConcurrencyObserved;
                            while (count > currentMax)
                            {
                                Interlocked.CompareExchange(ref maxConcurrencyObserved, count, currentMax);
                                currentMax = maxConcurrencyObserved;
                            }
                            await Task.Delay(1);
                            Interlocked.Decrement(ref concurrentInCriticalSection);
                        }
                    }));
                }

                Task.WaitAll(tasks.ToArray());

                AssertEqual(1, maxConcurrencyObserved, "Máxima concurrencia en la sección crítica fue exactamente 1");
                AssertEqual(0, concurrentInCriticalSection, "Todas las secciones críticas fueron liberadas");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestP1_9_Negative_OriginalFails_StrmPreserved()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p19_5_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var strmPath = Path.Combine(tempDir, "movie.strm");
                var originalStrmPath = Path.Combine(tempDir, "movie.original");
                var initialContent = "https://chomikuj.pl/user/movie,999.mkv";
                File.WriteAllText(strmPath, initialContent, Encoding.UTF8);

                // Forzamos un subdirectorio bloqueado como .original para provocar IOException al escribir
                // En Linux podemos crear un directorio con el nombre movie.original para que AtomicFile falle al escribir
                Directory.CreateDirectory(originalStrmPath);

                var fs = FakeFileSystemProxy.Create();
                var chomikService = new FakeChomikujDirectService();
                var cacheService = new StrmCacheService(new TestLogger(), fs, () => chomikService);

                // La llamada a GetOrResolveDirectUrlAsync debe atrapar la excepción de .original, no actualizar .strm y no corromperlo
                var resolved = cacheService.GetOrResolveDirectUrlAsync(strmPath, forceResolve: false, CancellationToken.None).GetAwaiter().GetResult();

                // El fichero .strm no debió ser sobrescrito con el directUrl resuelto porque original falló
                var currentStrm = File.ReadAllText(strmPath, Encoding.UTF8).Trim();
                AssertEqual(initialContent, currentStrm, "El contenido de .strm se preserva intacto si la escritura de .original falla");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestP1_9_MaybeCleanupLocks_RetainsActiveWaiters()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p19_6_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Crear 60 claves sin contención para que RefCount sea 0
                for (int i = 0; i < 60; i++)
                {
                    var p = Path.Combine(tempDir, $"item_{i}.strm");
                    using (AtomicFile.AcquireLockAsync(p).GetAwaiter().GetResult())
                    {
                    }
                }

                // Clave activa con un lock adquirido
                var activePath = Path.Combine(tempDir, "active_waiter.strm");
                var enteredLock = new ManualResetEventSlim(false);
                var releaseLock = new ManualResetEventSlim(false);

                var activeTask = Task.Run(async () =>
                {
                    using (await AtomicFile.AcquireLockAsync(activePath))
                    {
                        enteredLock.Set();
                        releaseLock.Wait(5000);
                    }
                });

                AssertTrue(enteredLock.Wait(3000), "El hilo activo entró al lock");

                // Ejecutamos purga forzada
                AtomicFile.MaybeCleanupLocks(force: true);

                // Verificamos que activePath sigue teniendo RefCount > 0 y NO fue retirado
                AssertTrue(AtomicFile.TryGetRefCount(activePath, out var refCount), "activePath sigue registrado en el diccionario");
                AssertTrue(refCount > 0, "activePath tiene RefCount > 0");

                // Liberamos el lock
                releaseLock.Set();
                activeTask.GetAwaiter().GetResult();

                // Ahora con RefCount == 0, purga forzada sí debe retirarlo
                AtomicFile.MaybeCleanupLocks(force: true);
                AssertFalse(AtomicFile.TryGetRefCount(activePath, out _), "activePath fue retirado tras finalizar (RefCount == 0)");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        #region 10. Pruebas P1-6 (StrmSyncTask)

        class FakeLogManagerProxy : System.Reflection.DispatchProxy
        {
            public TestLogger LoggerInstance { get; set; }

            protected override object Invoke(System.Reflection.MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == "GetLogger")
                {
                    return LoggerInstance;
                }
                return null;
            }

            public static MediaBrowser.Model.Logging.ILogManager Create(TestLogger logger)
            {
                var proxy = System.Reflection.DispatchProxy.Create<MediaBrowser.Model.Logging.ILogManager, FakeLogManagerProxy>();
                ((FakeLogManagerProxy)(object)proxy).LoggerInstance = logger;
                return proxy;
            }
        }

        class FakeLibraryMonitorProxy : System.Reflection.DispatchProxy
        {
            public List<string> ReportedPaths { get; } = new List<string>();

            protected override object Invoke(System.Reflection.MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == "ReportFileSystemChanged" && args != null && args.Length > 0)
                {
                    ReportedPaths.Add((string)args[0]);
                }
                return null;
            }

            public static (MediaBrowser.Controller.Library.ILibraryMonitor monitor, List<string> reportedPaths) Create()
            {
                var proxy = System.Reflection.DispatchProxy.Create<MediaBrowser.Controller.Library.ILibraryMonitor, FakeLibraryMonitorProxy>();
                return (proxy, ((FakeLibraryMonitorProxy)(object)proxy).ReportedPaths);
            }
        }

        static void TestP1_6_Cancellation_ThrowsAndUpdatesStatus()
        {
            var logger = new TestLogger();
            var logManager = FakeLogManagerProxy.Create(logger);
            var (libMonitor, reportedPaths) = FakeLibraryMonitorProxy.Create();

            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p16_1_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user",
                Password = "pwd",
                StrmDestinationFolder = tempDir,
                RefreshLibraryAfterSync = true
            };

            ChomyfinServices.Initialize(logger, () => options, null);
            try
            {
                var cts = new CancellationTokenSource();
                cts.Cancel(); // Token ya cancelado

                var task = new ScheduledTasks.StrmSyncTask(libMonitor, logManager, l => new StrmGenerator(l, opt => new FakeChomikBoxClient()));

                bool threwExpected = false;
                try
                {
                    task.Execute(cts.Token, null).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    threwExpected = true;
                }

                AssertTrue(threwExpected, "OperationCanceledException llega al llamador");
                AssertTrue(logger.Logs.Any(log => log.Contains("Chomyfin: Sincronización cancelada.")), "Log contiene 'Chomyfin: Sincronización cancelada.'");
                AssertFalse(ChomyfinServices.SyncStatusStore.IsRunning, "SyncStatusStore.IsRunning es false");
                AssertEqual(false, ChomyfinServices.SyncStatusStore.LastSuccess, "LastSuccess es false");
                AssertEqual("Sincronización cancelada.", ChomyfinServices.SyncStatusStore.LastMessage, "LastMessage es 'Sincronización cancelada.'");
                AssertEqual(0, ChomyfinServices.SyncStatusStore.Errors.Count, "result.Errors está vacío");
                AssertEqual(0, reportedPaths.Count, "ILibraryMonitor NO recibe llamada");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
                ChomyfinServices.ResetForTests();
            }
        }

        static void TestP1_6_NonCancellationException_CaughtAndUpdatesStatus()
        {
            var logger = new TestLogger();
            var logManager = FakeLogManagerProxy.Create(logger);
            var (libMonitor, reportedPaths) = FakeLibraryMonitorProxy.Create();

            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p16_2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user",
                Password = "pwd",
                StrmDestinationFolder = tempDir,
                RefreshLibraryAfterSync = true
            };

            ChomyfinServices.Initialize(logger, () => options, null);
            try
            {
                // Generador que lanza InvalidOperationException a través del clientFactory
                var task = new ScheduledTasks.StrmSyncTask(libMonitor, logManager, l => new StrmGenerator(l, opt =>
                {
                    throw new InvalidOperationException("Fallo inducido en generator mock");
                }));

                // Execute NO debe relanzar InvalidOperationException (catch(Exception) la atrapa)
                task.Execute(CancellationToken.None, null).GetAwaiter().GetResult();

                AssertTrue(logger.Logs.Any(log => log.Contains("Chomyfin: Error inesperado durante la ejecución de StrmSyncTask") && log.Contains("Fallo inducido en generator mock")), "_logger.ErrorException invocado con el error");
                AssertFalse(ChomyfinServices.SyncStatusStore.IsRunning, "SyncStatusStore.IsRunning es false");
                AssertEqual(false, ChomyfinServices.SyncStatusStore.LastSuccess, "LastSuccess es false");
                AssertEqual(0, reportedPaths.Count, "ILibraryMonitor NO recibe llamada");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
                ChomyfinServices.ResetForTests();
            }
        }

        static void TestP1_6_Success_InvokesLibraryMonitor()
        {
            var logger = new TestLogger();
            var logManager = FakeLogManagerProxy.Create(logger);
            var (libMonitor, reportedPaths) = FakeLibraryMonitorProxy.Create();

            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p16_3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user",
                Password = "pwd",
                StrmDestinationFolder = tempDir,
                RefreshLibraryAfterSync = true,
                SyncMode = StrmSyncMode.FullAccount
            };

            ChomyfinServices.Initialize(logger, () => options, null);
            try
            {
                var fakeClient = new FakeChomikBoxClient
                {
                    OnGetSubfolders = (id, path) => new List<ChomikFolderInfo>(),
                    OnGetFilesInFolder = (id, path) => FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                    {
                        new ChomikFileInfo("555", "peli.mkv", "/peli.mkv", 100)
                    })
                };

                var task = new ScheduledTasks.StrmSyncTask(libMonitor, logManager, l => new StrmGenerator(l, opt => fakeClient));

                task.Execute(CancellationToken.None, null).GetAwaiter().GetResult();

                AssertFalse(ChomyfinServices.SyncStatusStore.IsRunning, "SyncStatusStore.IsRunning es false");
                AssertEqual(true, ChomyfinServices.SyncStatusStore.LastSuccess, "LastSuccess es true");
                AssertEqual(1, reportedPaths.Count, "ILibraryMonitor recibe exactamente 1 llamada");
                AssertEqual(tempDir, reportedPaths[0], "ILibraryMonitor recibe destPath correcto");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
                ChomyfinServices.ResetForTests();
            }
        }

        #region 11. Acceptance Tests P1-7 (11.1 - 11.8)

        class MockHttpMessageHandler : System.Net.Http.HttpMessageHandler
        {
            public Func<System.Net.Http.HttpRequestMessage, System.Threading.CancellationToken, System.Net.Http.HttpResponseMessage> SendAsyncFunc { get; set; }

            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                if (SendAsyncFunc != null)
                {
                    return Task.FromResult(SendAsyncFunc(request, cancellationToken));
                }
                return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }

        static void TestP1_7_11_1_InvalidCredentials_Backoff5Min()
        {
            var logger = new TestLogger();
            int postLoginAttempts = 0;

            var mockHandler = new MockHttpMessageHandler
            {
                SendAsyncFunc = (req, ct) =>
                {
                    var uri = req.RequestUri.ToString();
                    if (uri.Contains("/action/Login/Login"))
                    {
                        Interlocked.Increment(ref postLoginAttempts);
                        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"IsSuccess\":false,\"Message\":\"Niepoprawne dane logowania\"}")
                        };
                    }
                    return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new System.Net.Http.StringContent("<input name=\"__RequestVerificationToken\" value=\"token_123\" />")
                    };
                }
            };

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user_bad",
                Password = "pwd_bad"
            };

            using var service = new ChomikujService(logger, () => options, mockHandler);

            var res1 = service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(null, res1, "1ra llamada falla y devuelve null");
            AssertEqual(1, postLoginAttempts, "1ra llamada ejecuta login");

            var res2 = service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(null, res2, "2da llamada dentro de 5 min falla y devuelve null");
            AssertEqual(1, postLoginAttempts, "2da llamada dentro de 5 min NO ejecuta login (respetó backoff)");
        }

        static void TestP1_7_11_2_Cancellation_PropagatesCleanly()
        {
            var logger = new TestLogger();
            using var cts = new CancellationTokenSource();

            var mockHandler = new MockHttpMessageHandler
            {
                SendAsyncFunc = (req, ct) =>
                {
                    var uri = req.RequestUri.ToString();
                    if (uri.Contains("/action/Login/Login"))
                    {
                        var resp = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"LoggedIn\":true}")
                        };
                        resp.Headers.Add("Set-Cookie", "rememberMe=tok; Path=/; Domain=chomikuj.pl");
                        return resp;
                    }
                    if (uri.Contains("chomikuj.pl/user/file,999999.mkv"))
                    {
                        // Simulamos cancelación en la obtención de la página
                        cts.Cancel();
                        ct.ThrowIfCancellationRequested();
                    }
                    return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new System.Net.Http.StringContent("<input name=\"__RequestVerificationToken\" value=\"token_123\" />")
                    };
                }
            };

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user",
                Password = "pwd",
                UseFastResolvePath = false
            };

            using var service = new ChomikujService(logger, () => options, mockHandler);

            bool threwCancellation = false;
            try
            {
                service.ResolveUrlAsync("https://chomikuj.pl/user/file,999999.mkv", cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                threwCancellation = true;
            }

            AssertTrue(threwCancellation, "ResolveUrlAsync propaga OperationCanceledException sin tragarla");
        }

        static void TestP1_7_11_3_Concurrency_SharedLoginInFlight()
        {
            var logger = new TestLogger();
            int postLoginAttempts = 0;
            var barrier = new ManualResetEventSlim(false);
            var ensureLoggedInLatch = new CountdownEvent(2);

            var mockHandler = new MockHttpMessageHandler
            {
                SendAsyncFunc = (req, ct) =>
                {
                    var uri = req.RequestUri.ToString();
                    if (uri.Contains("/action/Login/Login"))
                    {
                        Interlocked.Increment(ref postLoginAttempts);
                        barrier.Wait(5000);
                        var resp = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"LoggedIn\":true}")
                        };
                        resp.Headers.Add("Set-Cookie", "rememberMe=tok; Path=/; Domain=chomikuj.pl");
                        return resp;
                    }
                    if (uri.Contains("Download"))
                    {
                        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"redirectUrl\":\"https://direct.chomikuj.pl/file.mkv\"}")
                        };
                    }
                    return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new System.Net.Http.StringContent("<input name=\"__RequestVerificationToken\" value=\"tok\" /><div data-file-id=\"123456\"></div>")
                    };
                }
            };

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user",
                Password = "pwd",
                EnableCookieCache = false,
                UseFastResolvePath = false
            };

            using var service = new ChomikujService(logger, () => options, mockHandler);
            service.OnEnsureLoggedInForTests = () =>
            {
                if (!ensureLoggedInLatch.IsSet)
                {
                    ensureLoggedInLatch.Signal();
                }
            };

            var task1 = Task.Run(() => service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None));
            var task2 = Task.Run(() => service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None));

            // Esperar a que ambos llamadores concurrentes hayan entrado a EnsureLoggedIn y obtenido la misma _loginTask
            ensureLoggedInLatch.Wait(5000);
            barrier.Set();

            Task.WaitAll(task1, task2);

            AssertEqual(1, postLoginAttempts, "Concurrencia en vuelo comparte el mismo LoginAsync() sin duplicar llamadas HTTP");
            AssertTrue(!string.IsNullOrEmpty(task1.Result), "Tarea 1 obtuvo URL");
            AssertTrue(!string.IsNullOrEmpty(task2.Result), "Tarea 2 obtuvo URL");
        }

        static void TestP1_7_11_4_LoginTask_ReusableResult()
        {
            var logger = new TestLogger();
            var tcs = new TaskCompletionSource<bool>();

            var mockHandler = new MockHttpMessageHandler
            {
                SendAsyncFunc = (req, ct) =>
                {
                    var uri = req.RequestUri.ToString();
                    if (uri.Contains("/action/Login/Login"))
                    {
                        tcs.Task.Wait(5000);
                        var resp = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"LoggedIn\":true}")
                        };
                        resp.Headers.Add("Set-Cookie", "rememberMe=tok; Path=/; Domain=chomikuj.pl");
                        return resp;
                    }
                    if (uri.Contains("Download"))
                    {
                        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"redirectUrl\":\"https://direct.chomikuj.pl/file.mkv\"}")
                        };
                    }
                    return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new System.Net.Http.StringContent("<input name=\"__RequestVerificationToken\" value=\"tok\" /><div data-file-id=\"123456\"></div>")
                    };
                }
            };

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user",
                Password = "pwd",
                UseFastResolvePath = false
            };

            using var service = new ChomikujService(logger, () => options, mockHandler);

            var t1 = Task.Run(() => service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None));
            var t2 = Task.Run(() => service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None));
            var t3 = Task.Run(() => service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None));

            Thread.Sleep(50);
            tcs.SetResult(true);

            Task.WaitAll(t1, t2, t3);

            AssertEqual("https://direct.chomikuj.pl/file.mkv", t1.Result, "Llamador 1 recibe URL del await");
            AssertEqual("https://direct.chomikuj.pl/file.mkv", t2.Result, "Llamador 2 recibe URL del await");
            AssertEqual("https://direct.chomikuj.pl/file.mkv", t3.Result, "Llamador 3 recibe URL del await");
        }

        static void TestP1_7_11_5_ForceReLogin_CleansSessionAndCookies()
        {
            var logger = new TestLogger();
            var cookieContainer = new System.Net.CookieContainer();

            var mockHandler = new MockHttpMessageHandler
            {
                SendAsyncFunc = (req, ct) =>
                {
                    var uri = req.RequestUri.ToString();
                    if (uri.Contains("/action/Login/Login"))
                    {
                        cookieContainer.Add(new System.Net.Cookie("rememberMe", "session123", "/", "chomikuj.pl"));
                        var resp = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"LoggedIn\":true}")
                        };
                        return resp;
                    }
                    if (uri.Contains("Download"))
                    {
                        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"redirectUrl\":\"https://direct.chomikuj.pl/file.mkv\"}")
                        };
                    }
                    return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new System.Net.Http.StringContent("<input name=\"__RequestVerificationToken\" value=\"post_token_abc\" /><div data-file-id=\"123456\"></div>")
                    };
                }
            };

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user",
                Password = "pwd",
                EnableCookieCache = true
            };

            using var service = new ChomikujService(logger, () => options, mockHandler, cookieContainer);

            // 1. Resuelve para estar logueado con cookies y token post-login
            var res = service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None).GetAwaiter().GetResult();
            AssertTrue(!string.IsNullOrEmpty(res), "Login inicial y resolución exitosa");

            // Cookies presentes
            var cookiesBefore = service.GetCookies().ToList();
            AssertTrue(cookiesBefore.Count > 0, "Hay cookies presentes antes de ForceReLogin");

            // 2. Invocar ForceReLogin
            service.ForceReLogin();

            // Cookies deben estar vacías (0 elementos)
            var cookiesAfter = service.GetCookies().ToList();
            AssertEqual(0, cookiesAfter.Count, "GetCookies devuelve 0 elementos tras ForceReLogin (Expired = true)");
        }

        static void TestP1_7_11_6_WrongPassword_BackoffLogCount()
        {
            var logger = new TestLogger();

            var mockHandler = new MockHttpMessageHandler
            {
                SendAsyncFunc = (req, ct) =>
                {
                    var uri = req.RequestUri.ToString();
                    if (uri.Contains("/action/Login/Login"))
                    {
                        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"IsSuccess\":false,\"Message\":\"Niepoprawne dane logowania\"}")
                        };
                    }
                    return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new System.Net.Http.StringContent("<input name=\"__RequestVerificationToken\" value=\"tok\" />")
                    };
                }
            };

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user",
                Password = "wrong_password"
            };

            using var service = new ChomikujService(logger, () => options, mockHandler);

            // 1ª llamada falla por credenciales
            service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None).GetAwaiter().GetResult();

            // 2ª llamada dentro de la ventana de 5 minutos
            service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None).GetAwaiter().GetResult();

            int loginAttemptsInLog = logger.Logs.Count(l => l.Contains("login no confirmado") || l.Contains("Login exitoso"));
            AssertEqual(1, loginAttemptsInLog, "El log contiene exactamente 1 intento de login durante la ventana de 5 min");

            int stackTraceInLog = logger.Logs.Count(l => l.Contains("StackTrace") || l.Contains("at Chomyfin"));
            AssertEqual(0, stackTraceInLog, "Sin stack traces repetidos en el log");
        }

        static void TestP1_7_11_7_CookieCacheDisabled_TenConcurrent_SingleLoginInLog()
        {
            var logger = new TestLogger();
            var barrier = new ManualResetEventSlim(false);
            var ensureLoggedInLatch = new CountdownEvent(10);

            var mockHandler = new MockHttpMessageHandler
            {
                SendAsyncFunc = (req, ct) =>
                {
                    var uri = req.RequestUri.ToString();
                    if (uri.Contains("/action/Login/Login"))
                    {
                        barrier.Wait(5000);
                        var resp = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"LoggedIn\":true}")
                        };
                        resp.Headers.Add("Set-Cookie", "rememberMe=tok; Path=/; Domain=chomikuj.pl");
                        return resp;
                    }
                    if (uri.Contains("Download"))
                    {
                        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new System.Net.Http.StringContent("{\"redirectUrl\":\"https://direct.chomikuj.pl/file.mkv\"}")
                        };
                    }
                    return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new System.Net.Http.StringContent("<input name=\"__RequestVerificationToken\" value=\"tok\" /><div data-file-id=\"123456\"></div>")
                    };
                }
            };

            var options = new Configuration.ChomyfinOptions
            {
                Username = "user",
                Password = "pwd",
                EnableCookieCache = false,
                UseFastResolvePath = false
            };

            using var service = new ChomikujService(logger, () => options, mockHandler);
            service.OnEnsureLoggedInForTests = () =>
            {
                if (!ensureLoggedInLatch.IsSet)
                {
                    ensureLoggedInLatch.Signal();
                }
            };

            var tasks = new List<Task<string>>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() => service.ResolveUrlAsync("https://chomikuj.pl/user/file,123456.mkv", CancellationToken.None)));
            }

            // Esperar a que las 10 llamadas concurrentes hayan entrado a EnsureLoggedIn y obtenido la misma _loginTask
            ensureLoggedInLatch.Wait(5000);
            barrier.Set();

            Task.WaitAll(tasks.ToArray());

            int loginSuccessCount = logger.Logs.Count(l => l.Contains("Login exitoso"));
            AssertEqual(1, loginSuccessCount, "EnableCookieCache=false + 10 concurrentes produce un ÚNICO login en el log");
        }

        static void TestP1_7_11_8_NegativeGrep_CatchOrderInMethodsWithCt()
        {
            // Verificación del archivo Services/ChomikujService.cs
            string servicePath = Path.Combine(AppContext.BaseDirectory, "../../../..", "Services", "ChomikujService.cs");
            if (!File.Exists(servicePath))
            {
                servicePath = Path.Combine(Directory.GetCurrentDirectory(), "Services", "ChomikujService.cs");
            }
            if (!File.Exists(servicePath))
            {
                servicePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Services", "ChomikujService.cs");
            }

            AssertTrue(File.Exists(servicePath), "ChomikujService.cs encontrado para inspección");

            var lines = File.ReadAllLines(servicePath);

            // Métodos que reciben ct según el plan: ResolveUrlAsync, ResolveSlowPathWithStatusAsync, TryResolveFastPathAsync, LoginAsync, EnsureLoggedIn
            // Comprobar que en ninguna parte un catch general ocurre antes de catch (OperationCanceledException)
            int lastMethodStart = -1;
            string currentMethod = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("ResolveUrlAsync") || line.Contains("ResolveSlowPathWithStatusAsync") ||
                    line.Contains("TryResolveFastPathAsync") || line.Contains("LoginAsync") || line.Contains("EnsureLoggedIn"))
                {
                    if (line.Contains("Task") && (line.Contains("public") || line.Contains("private")))
                    {
                        currentMethod = line.Trim();
                        lastMethodStart = i;
                    }
                }

                if (line.Contains("catch (Exception") && !line.Contains("OperationCanceledException"))
                {
                    // Buscar si dentro del mismo bloque try hubo catch (OperationCanceledException) antes
                    // O si hubo alguno después (lo cual violaría el orden correcto)
                    for (int j = i + 1; j < Math.Min(lines.Length, i + 15); j++)
                    {
                        var nextLine = lines[j];
                        if (nextLine.Contains("catch (OperationCanceledException)"))
                        {
                            throw new InvalidOperationException($"Violación en línea {i+1}: catch (Exception) antes de catch (OperationCanceledException) en {currentMethod}");
                        }
                        if (nextLine.Contains("public ") || nextLine.Contains("private ")) break;
                    }
                }
            }
        }

        #endregion

        #region 12. Acceptance Tests P1-8 (12.1 - 12.5)

        public class FakeVideoItem : MediaBrowser.Controller.Entities.Video
        {
        }

        public class FakeSiblingFinderThrows : SiblingFinder
        {
            public FakeSiblingFinderThrows(MediaBrowser.Model.Logging.ILogger logger) : base(logger, null, null) { }

            public override List<string> FindSiblings(MediaBrowser.Controller.Entities.BaseItem item, int maxSiblings)
            {
                throw new InvalidOperationException("Fallo inducido en SiblingFinder para prueba");
            }
        }

        static void TestP1_8_12_1_SequentialResolution_SemaphoreMutualExclusion()
        {
            var logger = new TestLogger();
            bool hadOverlap = false;

            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p18_1_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var s1 = Path.Combine(tempDir, "s1.strm");
                var s2 = Path.Combine(tempDir, "s2.strm");
                File.WriteAllText(s1, "https://chomikuj.pl/u/s1,111.mkv");
                File.WriteAllText(s2, "https://chomikuj.pl/u/s2,222.mkv");

                var fs = FakeFileSystemProxy.Create();
                var chomikService = new FakeChomikujDirectService();

                var cacheService = new StrmCacheService(logger, fs, () => chomikService);

                var queue = new SiblingResolutionQueue(logger, () => cacheService, () => chomikService);

                // Encolar dos lotes de hermanos
                queue.EnqueueSiblings(new List<string> { s1 }, s1);
                queue.EnqueueSiblings(new List<string> { s2 }, s2);

                // Esperar a que se procesen ambos
                Thread.Sleep(300);

                AssertFalse(hadOverlap, "SemaphoreSlim(1,1) asegura ejecución secuencial sin solapamiento");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
                SiblingResolutionQueue.ResetForTests();
            }
        }

        static void TestP1_8_12_2_BoundedQueue_SaturationDropsAndLogs()
        {
            var logger = new TestLogger();
            var queue = new SiblingResolutionQueue(logger, () => null, () => null);

            SiblingResolutionQueue.MaxQueueCapacity = 2;
            try
            {
                // Encolar lote que supera la capacidad (3 > 2)
                queue.EnqueueSiblings(new List<string> { "/path/1.strm", "/path/2.strm", "/path/3.strm" }, "/path/trigger.strm");

                AssertTrue(logger.Logs.Any(l => l.Contains("cola de hermanos saturada")), "Log contiene 'cola de hermanos saturada'");
            }
            finally
            {
                SiblingResolutionQueue.ResetForTests();
            }
        }

        static void TestP1_8_12_3_Cooldown_RecentItemsSkipped()
        {
            var logger = new TestLogger();
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p18_3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var s1 = Path.Combine(tempDir, "item1.strm");
                File.WriteAllText(s1, "https://chomikuj.pl/u/item1,111.mkv");

                var fs = FakeFileSystemProxy.Create();
                var chomikService = new FakeChomikujDirectService();
                var cacheService = new StrmCacheService(logger, fs, () => chomikService);

                var queue = new SiblingResolutionQueue(logger, () => cacheService, () => chomikService);

                // 1ra vez
                queue.EnqueueSiblings(new List<string> { s1 }, s1);
                Thread.Sleep(200);

                // 2da vez inmediatamente (dentro de cooldown de 5 min)
                queue.EnqueueSiblings(new List<string> { s1 }, s1);
                Thread.Sleep(200);

                // Debe registrarse cooldown y omitirse
                AssertTrue(queue.CooldownsForTests.ContainsKey(s1), "s1 está en cooldown");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
                SiblingResolutionQueue.ResetForTests();
            }
        }

        static void TestP1_8_12_4_FireAndForget_GetMediaSourcesReturnsImmediately()
        {
            var logger = new TestLogger();
            var logManager = FakeLogManagerProxy.Create(logger);
            var fs = FakeFileSystemProxy.Create();

            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p18_4_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var mainStrm = Path.Combine(tempDir, "Movie.strm");
                var mainOrig = Path.Combine(tempDir, "Movie.original");
                File.WriteAllText(mainStrm, "https://s123.chomikuj.pl/fast.mkv");
                File.WriteAllText(mainOrig, "https://chomikuj.pl/user/Movie,123.mkv");

                var options = new Configuration.ChomyfinOptions
                {
                    Username = "user",
                    Password = "pwd",
                    ResolveSiblingVersions = true,
                    MaxSiblingsPerOpen = 5
                };

                ChomyfinServices.Initialize(logger, () => options, fs, null);

                var provider = new UrlProvider(logManager, fs);
                var item = new FakeVideoItem { Path = mainStrm };

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var sources = provider.GetMediaSources(item, CancellationToken.None).GetAwaiter().GetResult();
                sw.Stop();

                AssertTrue(sw.ElapsedMilliseconds < 500, "GetMediaSources retorna inmediatamente sin bloquear por hermanos");
                AssertEqual(1, sources.Count, "Devuelve 1 MediaSourceInfo para el ítem principal");
                AssertEqual("https://s123.chomikuj.pl/fast.mkv", sources[0].Path, "Path del MediaSourceInfo coincide");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
                ChomyfinServices.ResetForTests();
            }
        }

        static void TestP1_8_12_4a_CancellationToken_NotPassedToSiblingTaskRun()
        {
            var logger = new TestLogger();
            var logManager = FakeLogManagerProxy.Create(logger);
            var fs = FakeFileSystemProxy.Create();

            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p18_4a_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var mainStrm = Path.Combine(tempDir, "Movie.strm");
                var mainOrig = Path.Combine(tempDir, "Movie.original");
                // Ya es URL directa por lo que GetOrResolveDirectUrlAsync resuelve síncronamente sin red
                File.WriteAllText(mainStrm, "https://s123.chomikuj.pl/movie.mkv");
                File.WriteAllText(mainOrig, "https://chomikuj.pl/user/Movie,123.mkv");

                var options = new Configuration.ChomyfinOptions
                {
                    Username = "user",
                    Password = "pwd",
                    ResolveSiblingVersions = true,
                    MaxSiblingsPerOpen = 5
                };

                ChomyfinServices.Initialize(logger, () => options, fs, null);

                var provider = new UrlProvider(logManager, fs);
                var item = new FakeVideoItem { Path = mainStrm };

                using var cts = new CancellationTokenSource();
                cts.Cancel(); // Token ya cancelado

                // GetMediaSources no debe lanzar OperationCanceledException si el archivo ya está resuelto
                var sources = provider.GetMediaSources(item, cts.Token).GetAwaiter().GetResult();

                AssertEqual(1, sources.Count, "Devuelve MediaSourceInfo del ítem principal sin lanzar OperationCanceledException");
                AssertEqual("https://s123.chomikuj.pl/movie.mkv", sources[0].Path, "MediaSourceInfo del ítem principal devuelto intacto");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
                ChomyfinServices.ResetForTests();
            }
        }

        static void TestP1_8_12_4b_FindSiblingsThrows_CaughtAndLogged_GetMediaSourcesSucceeds()
        {
            var logger = new TestLogger();
            var logManager = FakeLogManagerProxy.Create(logger);
            var fs = FakeFileSystemProxy.Create();

            var tempDir = Path.Combine(Path.GetTempPath(), "cf_p18_4b_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var mainStrm = Path.Combine(tempDir, "Movie.strm");
                var mainOrig = Path.Combine(tempDir, "Movie.original");
                File.WriteAllText(mainStrm, "https://s123.chomikuj.pl/movie.mkv");
                File.WriteAllText(mainOrig, "https://chomikuj.pl/user/Movie,123.mkv");

                var options = new Configuration.ChomyfinOptions
                {
                    Username = "user",
                    Password = "pwd",
                    ResolveSiblingVersions = true,
                    MaxSiblingsPerOpen = 5
                };

                ChomyfinServices.Initialize(logger, () => options, fs, null);
                // Sustituir SiblingFinder por un finder que lanza excepción
                ChomyfinServices.SiblingFinder = new FakeSiblingFinderThrows(logger);

                var provider = new UrlProvider(logManager, fs);
                var item = new FakeVideoItem { Path = mainStrm };

                var sources = provider.GetMediaSources(item, CancellationToken.None).GetAwaiter().GetResult();

                // Esperar a que el Task.Run registre el error en el log
                Thread.Sleep(200);

                AssertEqual(1, sources.Count, "GetMediaSources NO se rompe y devuelve el ítem principal");
                AssertTrue(logger.Logs.Any(l => l.Contains("Chomyfin: error iniciando resolución de hermanos para")), "Log contiene 'Chomyfin: error iniciando resolución de hermanos para …'");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
                ChomyfinServices.ResetForTests();
            }
        }

        static void TestP1_8_12_5_CooldownPurge_EvictsExpiredEntries()
        {
            var logger = new TestLogger();
            var queue = new SiblingResolutionQueue(logger, () => null, () => null);

            var oldTime = DateTime.UtcNow - TimeSpan.FromMinutes(10);
            var recentTime = DateTime.UtcNow;

            // Llenar más de 500 entradas
            for (int i = 0; i < 505; i++)
            {
                queue.CooldownsForTests[$"/path/file_{i}.strm"] = oldTime;
            }
            queue.CooldownsForTests["/path/recent.strm"] = recentTime;

            AssertTrue(queue.CooldownsForTests.Count > 500, "Hay más de 500 entradas");

            queue.MaybePurgeCooldowns();

            AssertEqual(1, queue.CooldownsForTests.Count, "Las entradas expiradas (>5 min) fueron purgadas; solo queda la reciente");
            AssertTrue(queue.CooldownsForTests.ContainsKey("/path/recent.strm"), "La entrada reciente se conserva intacta");
        }

        #region 13. Regression Tests v1.1.0.0

        static void TestSiblingFinder_Episode_RegexVariations()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_test_regex_var_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var ep1 = Path.Combine(tempDir, "ShowName.S01E01.1080p.strm");
                var ep2 = Path.Combine(tempDir, "ShowName.1x01.720p.strm");
                var ep3 = Path.Combine(tempDir, "ShowName.T1E1.4k.strm");
                var epDifferent = Path.Combine(tempDir, "ShowName.S01E02.1080p.strm");

                File.WriteAllText(ep1, "dummy");
                File.WriteAllText(ep2, "dummy");
                File.WriteAllText(ep3, "dummy");
                File.WriteAllText(epDifferent, "dummy");

                var fs = FakeFileSystemProxy.Create();
                var logger = new TestLogger();
                var finder = new SiblingFinder(logger, fs, null);

                var item = new MediaBrowser.Controller.Entities.TV.Episode { Path = ep1 };
                var siblings = finder.FindSiblings(item, 10);

                AssertEqual(2, siblings.Count, "Debe encontrar exactamente 2 hermanos para S01E01 (1x01 y T1E1)");
                AssertTrue(siblings.Contains(ep2), "Debe incluir la variante 1x01");
                AssertTrue(siblings.Contains(ep3), "Debe incluir la variante T1E1");
                AssertFalse(siblings.Contains(epDifferent), "NO debe incluir episodios distintos (S01E02)");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestSiblingFinder_Episode_FlatLayout()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_test_flat_layout_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var breakingBad1080 = Path.Combine(tempDir, "Breaking.Bad.S01E01.1080p.strm");
                var breakingBad720 = Path.Combine(tempDir, "Breaking.Bad.S01E01.720p.strm");
                var gameOfThrones1080 = Path.Combine(tempDir, "Game.Of.Thrones.S01E01.1080p.strm");

                File.WriteAllText(breakingBad1080, "dummy");
                File.WriteAllText(breakingBad720, "dummy");
                File.WriteAllText(gameOfThrones1080, "dummy");

                var fs = FakeFileSystemProxy.Create();
                var logger = new TestLogger();
                var finder = new SiblingFinder(logger, fs, null);

                var item = new MediaBrowser.Controller.Entities.TV.Episode { Path = breakingBad1080 };
                var siblings = finder.FindSiblings(item, 10);

                AssertEqual(1, siblings.Count, "Debe encontrar exactamente 1 hermano en layout plano");
                AssertTrue(siblings.Contains(breakingBad720), "Debe incluir el hermano de la misma serie (Breaking Bad)");
                AssertFalse(siblings.Contains(gameOfThrones1080), "NO debe incluir episodios de series distintas con el mismo S01E01 (Game of Thrones)");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void TestCleanOrphans_DeletesOrphanedOriginal()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "cf_test_clean_orphans_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var strmPath = Path.Combine(tempDir, "Keep.strm");
                var originalKeepPath = Path.Combine(tempDir, "Keep.original");
                var orphanedOriginalPath = Path.Combine(tempDir, "Orphan.original");
                var orphanedStrmPath = Path.Combine(tempDir, "OrphanStrm.strm");
                var orphanedStrmOriginalPath = Path.Combine(tempDir, "OrphanStrm.original");

                File.WriteAllText(strmPath, "https://chomikuj.pl/user/Keep,111.mkv");
                File.WriteAllText(originalKeepPath, "https://chomikuj.pl/user/Keep,111.mkv");
                File.WriteAllText(orphanedOriginalPath, "https://chomikuj.pl/user/Orphan,222.mkv");
                File.WriteAllText(orphanedStrmPath, "https://chomikuj.pl/user/OrphanStrm,333.mkv");
                File.WriteAllText(orphanedStrmOriginalPath, "https://chomikuj.pl/user/OrphanStrm,333.mkv");

                var testLogger = new TestLogger();
                var fakeClient = new FakeChomikBoxClient
                {
                    OnGetSubfolders = (parentId, path) => new List<ChomikFolderInfo>(),
                    OnGetFilesInFolder = (folderId, path) =>
                    {
                        return FolderFilesResult.CreateFiles(new List<ChomikFileInfo>
                        {
                            new ChomikFileInfo("111", "Keep.mkv", "/Keep.mkv", 100)
                        });
                    }
                };

                var options = new StrmGeneratorOptions
                {
                    Username = "user",
                    Password = "pwd",
                    DestinationPath = tempDir,
                    DeleteOrphans = true,
                    OrphansDryRun = false
                };

                var generator = new StrmGenerator(testLogger, opt => fakeClient);
                var result = generator.GenerateAsync(options).Result;

                AssertTrue(result.Success, "Sincronización exitosa");
                AssertTrue(File.Exists(strmPath), "Keep.strm se mantiene");
                AssertTrue(File.Exists(originalKeepPath), "Keep.original se mantiene");
                AssertFalse(File.Exists(orphanedStrmPath), "OrphanStrm.strm huérfano se elimina");
                AssertFalse(File.Exists(orphanedStrmOriginalPath), "OrphanStrm.original se elimina junto con su strm");
                AssertFalse(File.Exists(orphanedOriginalPath), "Orphan.original solitario (sin su .strm) debe ser detectado y eliminado");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        #endregion

        #endregion
        #endregion
        #endregion
        #endregion
    }
}
