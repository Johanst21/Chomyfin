# Notas del Agente (F1 - P0-3)

- **Paso 6 (<url> de Download)**: [VERIFICAR] No se encontró ninguna muestra de archivo `*-Download-response.xml` ni carpeta `diag/` ni logs con respuestas reales de `Download` en el repositorio (`find . -name "*-Download-response.xml"` / `find . -name "*.xml"` / `find . -name "*.log"` no arrojaron archivos). Siguiendo las instrucciones del plan y de la solicitud del usuario, NO se implementa el enmascaramiento de query string para `<url>` de Download y se documenta aquí.

## Hallazgos fuera de plan (candidatos P2)
- ChomikBoxClient.cs:478: `throw new InvalidOperationException($"SOAP Fault en {action}: {faultString}")` usa faultString sin sanitizar. Fuera de alcance de P0-3 (el plan solo pedía el _logger). Riesgo: si la excepción se loguea en StrmSyncTask/ChomyfinApiService, el faultString crudo podría aparecer en logs de Emby.

