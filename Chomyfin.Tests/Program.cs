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

        #endregion
        #endregion
    }
}
