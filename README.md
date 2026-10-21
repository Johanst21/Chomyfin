# Chomyfin

Plugin para **Emby Server** (4.10.x) que integra y gestiona contenido directamente desde **Chomikuj.pl**, permitiendo explorar carpetas, sincronizar archivos `.strm` y reproducir medios en streaming.

> **Estado**: **Versión Beta inicial (`v0.8.7.0-beta`)**.  
> Esta versión representa una base funcional y estable antes de la aplicación de los parches de auditoría de seguridad y optimización de rendimiento.

---

## Características

- **Exploración de Chomikuj**: Navegación por cuentas y carpetas de Chomikuj integradas en Emby.
- **Sincronización `.strm`**: Tarea programada para generar y actualizar ficheros `.strm` hacia la biblioteca local de Emby.
- **Reproducción directa**: `IMediaSourceProvider` adaptado a la API y firma de Emby Server 4.10.x.
- **Autocontenido**: Compilado para `netstandard2.0` en una única DLL sin dependencias externas innecesarias.

---

## Requisitos

- **Emby Server**: 4.10.x o superior.
- **.NET SDK**: 8.0 o superior (para compilación).
- **Target Framework**: `netstandard2.0` (estándar requerido por el portal de plugins de Emby).

---

## Compilación

Para compilar el plugin en modo Release:

```bash
dotnet build Chomyfin.csproj -c Release
```

La biblioteca generada se ubicará en:
```
bin/Release/netstandard2.0/Chomyfin.dll
```

---

## Instalación

1. Detén el servidor de Emby.
2. Copia el archivo generado `Chomyfin.dll` dentro del directorio de plugins de Emby:
   - **Linux**: `/var/lib/emby/plugins/` (o la ruta donde esté instalado el perfil de Emby).
   - **Windows**: `%AppData%\Emby-Server\programdata\plugins\`
3. Inicia Emby Server.
4. Accede al panel de administración de Emby en **Configuración > Plugins** para verificar y configurar las credenciales y opciones de Chomyfin.

---

## Licencia

Distribuido bajo licencia de uso de software del proyecto.
