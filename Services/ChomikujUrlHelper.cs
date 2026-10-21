using System;
using System.Text.RegularExpressions;

namespace Chomyfin.Services
{
    /// <summary>
    /// Métodos auxiliares para la clasificación y validación de URLs de Chomikuj.pl.
    /// </summary>
    public static class ChomikujUrlHelper
    {
        private static readonly Regex SubdomainPattern = new Regex(@"s\d+\.chomikuj\.pl", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Determina si una URL corresponde a una página HTML de Chomikuj que requiere resolución.
        /// </summary>
        public static bool IsChomikujPageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            // netstandard2.0 no tiene string.Contains(string, StringComparison);
            // se usa IndexOf con ordinal ignore case.
            return url.IndexOf("chomikuj.pl", StringComparison.OrdinalIgnoreCase) >= 0
                && url.IndexOf("getfile.pl", StringComparison.OrdinalIgnoreCase) < 0
                && !SubdomainPattern.IsMatch(url);
        }

        /// <summary>
        /// Determina si una URL es una dirección de descarga directa (http/https).
        /// </summary>
        public static bool IsDirectUrl(string url)
        {
            return !string.IsNullOrWhiteSpace(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        }
    }
}
