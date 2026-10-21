using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Chomyfin.Services
{
    /// <summary>
    /// Interfaz del cliente web de Chomikuj.pl para resolución de enlaces
    /// directos y gestión de sesión web.
    /// </summary>
    public interface IChomikujService
    {
        /// <summary>User-Agent utilizado para las peticiones HTTP.</summary>
        string UserAgent { get; }

        /// <summary>
        /// Resuelve una URL de página de Chomikuj a una URL directa de descarga.
        /// Debe invocarse estrictamente bajo demanda desde StrmCacheService para archivos que necesiten resolución.
        /// No debe utilizarse en temporizadores, tareas programadas ni barridos masivos.
        /// </summary>
        Task<string> ResolveUrlAsync(string pageUrl, CancellationToken ct);

        /// <summary>Devuelve las cookies vigentes de sesión.</summary>
        IEnumerable<string> GetCookies();

        /// <summary>Invalida la sesión actual forzando un nuevo login en la siguiente resolución.</summary>
        void ForceReLogin();
    }
}
