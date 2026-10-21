using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Chomyfin.Services
{
    /// <summary>
    /// Sanitizador centralizado para cuerpos SOAP, peticiones y respuestas XML/texto.
    /// Oculta contraseñas, hashes y tokens para evitar fugas en logs y volcados de diagnóstico.
    /// </summary>
    public static class SoapLogSanitizer
    {
        private const string Mask = "***";

        /// <summary>
        /// Fragmentos sensibles en nombres locales de elementos o atributos a enmascarar.
        /// </summary>
        public static readonly string[] SensitiveKeywords = new[]
        {
            "password",
            "passwd",
            "pwd",
            "pass",
            "passhash",
            "token",
            "secret"
        };

        /// <summary>
        /// Sanitiza el texto XML o texto plano ocultando campos sensibles.
        /// Aplica XDocument como vía principal, y Regex como fallback si el texto no es XML válido.
        /// </summary>
        public static string Sanitize(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            try
            {
                return SanitizeXml(content);
            }
            catch
            {
                // Si falla el parseo XML (truncado, HTML, texto no válido), aplicar regex
                return SanitizeWithRegex(content);
            }
        }

        private static string SanitizeXml(string xml)
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);

            // 1. Revisar elementos
            foreach (var element in doc.Descendants())
            {
                if (IsSensitiveName(element.Name.LocalName))
                {
                    // Si tiene atributos nil="true", no contiene valor que ocultar
                    var isNil = element.Attributes().Any(a => a.Name.LocalName.Equals("nil", StringComparison.OrdinalIgnoreCase) &&
                                                              a.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
                    if (isNil)
                    {
                        continue;
                    }

                    // Excepción para indicadores booleanos: si es "true" o "false", no ocultar
                    string val = element.Value?.Trim();
                    if (string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(val, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(element.Value) || element.Nodes().Any())
                    {
                        element.ReplaceNodes(Mask);
                    }
                }

                // 2. Revisar atributos sensibles
                foreach (var attr in element.Attributes().ToList())
                {
                    if (IsSensitiveName(attr.Name.LocalName))
                    {
                        string val = attr.Value?.Trim();
                        if (string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(val, "false", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        attr.Value = Mask;
                    }
                }
            }

            // Serializar manteniendo formato o declaración original
            var sw = new StringWriter();
            doc.Save(sw, SaveOptions.DisableFormatting);
            return sw.ToString();
        }

        /// <summary>
        /// Sanitización con expresiones regulares para texto plano o XML no parseable/truncado.
        /// </summary>
        public static string SanitizeWithRegex(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string result = text;

            foreach (var keyword in SensitiveKeywords)
            {
                // Elementos XML: <prefix:name ...>value</prefix:name>
                // Coincide con cualquier elemento cuyo nombre local contenga la keyword
                string patternElement = $@"(<(?:\w+:)?[^>]*{Regex.Escape(keyword)}[^>]*>)(.*?)(</(?:\w+:)?[^>]*{Regex.Escape(keyword)}[^>]*>)";
                result = Regex.Replace(result, patternElement, match =>
                {
                    string openTag = match.Groups[1].Value;
                    string val = match.Groups[2].Value;
                    string closeTag = match.Groups[3].Value;

                    string trimmed = val.Trim();
                    if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        return match.Value;
                    }

                    return $"{openTag}{Mask}{closeTag}";
                }, RegexOptions.IgnoreCase | RegexOptions.Singleline);

                // Atributos XML: name="value" o name='value'
                string patternAttr = $@"\b((?:\w+:)?[a-zA-Z0-9_-]*{Regex.Escape(keyword)}[a-zA-Z0-9_-]*\s*=\s*)([""'])(.*?)([""'])";
                result = Regex.Replace(result, patternAttr, match =>
                {
                    string prefix = match.Groups[1].Value;
                    string quote1 = match.Groups[2].Value;
                    string val = match.Groups[3].Value;
                    string quote2 = match.Groups[4].Value;

                    string trimmed = val.Trim();
                    if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        return match.Value;
                    }

                    return $"{prefix}{quote1}{Mask}{quote2}";
                }, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }

            return result;
        }

        public static bool IsSensitiveName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var keyword in SensitiveKeywords)
            {
                if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
