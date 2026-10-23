# Notas del Agente (F1 - P0-3)

- **Paso 6 (<url> de Download)**: [VERIFICAR] No se encontró ninguna muestra de archivo `*-Download-response.xml` ni carpeta `diag/` ni logs con respuestas reales de `Download` en el repositorio (`find . -name "*-Download-response.xml"` / `find . -name "*.xml"` / `find . -name "*.log"` no arrojaron archivos). Siguiendo las instrucciones del plan y de la solicitud del usuario, NO se implementa el enmascaramiento de query string para `<url>` de Download y se documenta aquí.

## Hallazgos fuera de plan (candidatos P2)
- UrlProvider.cs:133-136: el User-Agent ahora se envía siempre que service.UserAgent no esté vacío, antes solo se enviaba junto a las cookies. Cambio de comportamiento observable menor; sin impacto de seguridad. Incluir en "Notas de release" al cerrar la release.
- ChomikBoxClient.cs:478: `throw new InvalidOperationException($"SOAP Fault en {action}: {faultString}")` usa faultString sin sanitizar. Fuera de alcance de P0-3 (el plan solo pedía el _logger). Riesgo: si la excepción se loguea en StrmSyncTask/ChomyfinApiService, el faultString crudo podría aparecer en logs de Emby.

## Bloqueo F3 (P0-4): infraestructura de tests insuficiente
- [VERIFICAR] del plan punto 6: clientFactory inyectable (SÍ existe en StrmGenerator) y FakeChomikBoxClient (SÍ existe en Program.cs:465-490).
- Gap: Chomyfin.Tests.csproj solo compila SoapLogSanitizer, SoapResponseParser, IChomikBoxClient, ChomikFileInfo, ChomikFolderInfo. NO compila StrmGenerator.cs, StrmGeneratorModels.cs ni ChomyfinOptions.cs, ni referencia mediabrowser.server.core / MediaBrowser.Model.
- Consecuencia: los 4 tests de aceptación de P0-4 no se pueden ejecutar hasta ampliar el .csproj.
- Estado: F3 desbloqueado mediante commit 1 (1c3ad15).
- Verificación necesidad ChomikBoxClient.cs en Chomyfin.Tests.csproj: SÍ es estrictamente necesario porque StrmGenerator.cs referencia directamente `ChomikBoxClient` en línea 23 (`new ChomikBoxClient(...)` como fallback en constructor) y en línea 202 (`ChomikBoxClient.EscapePathId(rawPath)`). Si se retira de Compile Include, el compilador falla con CS0019 y CS0103. Se mantiene en Chomyfin.Tests.csproj.


