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
        private const string ContentOmitted = "[contenido omitido: no sanitizable]";
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

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
            "secret",
            "cookie",
            "authorization",
            "rememberme",
            "chomiksession"
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
                // Si falla el parseo XML (truncado, HTML, texto no válido), aplicar fallback regex
                string fallback = SanitizeWithRegex(content);
                if (string.Equals(fallback, ContentOmitted, StringComparison.Ordinal))
                {
                    return ContentOmitted;
                }

                if (HasResidualSensitiveData(fallback))
                {
                    return ContentOmitted;
                }

                return fallback;
            }
        }

        private static bool IsPreservedBooleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("has", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("is", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("use", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("enable", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("Required", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasResidualSensitiveData(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            try
            {
                foreach (var keyword in SensitiveKeywords)
                {
                    if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    // Busca palabra sensible seguida de =, : o > y un valor no vacío distinto de ***
                    // 1) clave=valor o clave="valor" o clave:valor o clave:"valor"
                    string patternKv = $@"(?i)\b{Regex.Escape(keyword)}\b\s*[=:]\s*(?!""\*{3}""|'\*{3}'|\*{3}(?:[\s,;""'<>}}]|$)|true\b|false\b)([^\s,;""'<>}}&]+)";
                    var matchKv = Regex.Match(text, patternKv, RegexOptions.Singleline, RegexTimeout);
                    if (matchKv.Success)
                    {
                        return true;
                    }

                    // 2) elemento XML abierto: <tag...keyword...>valor no enmascarado
                    string patternTag = $@"(?i)<(?:\w+:)?[a-zA-Z0-9_\-\.]*{Regex.Escape(keyword)}[a-zA-Z0-9_\-\.]*(?:\s+[^>]*)?>\s*(?!\*{3}|true\b|false\b|\s*<)(?<val>[^\s<]+)";
                    var matchTag = Regex.Match(text, patternTag, RegexOptions.Singleline, RegexTimeout);
                    if (matchTag.Success)
                    {
                        string val = matchTag.Groups["val"].Value;
                        if (!string.IsNullOrEmpty(val) && !val.StartsWith("***"))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return true;
            }

            return false;
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

                    // Excepción para indicadores booleanos: conservar true/false solo si empieza por has, is, use, enable
                    string val = element.Value?.Trim();
                    if ((string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)) &&
                        IsPreservedBooleanName(element.Name.LocalName))
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
                        if ((string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)) &&
                            IsPreservedBooleanName(attr.Name.LocalName))
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

            try
            {
                string result = text;

                // Cabeceras HTTP: Cookie, Set-Cookie, Authorization
                string headerPattern = @"(?m)^([ \t]*(?:Set-Cookie|Cookie|Authorization)[ \t]*:[ \t]*)[^\r\n]+";
                result = Regex.Replace(result, headerPattern, $"$1{Mask}", RegexOptions.IgnoreCase, RegexTimeout);

                foreach (var keyword in SensitiveKeywords)
                {
                    if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    // 1. Elementos XML cerrados: <prefix:name ...>value</prefix:name>
                    string patternElement = $@"(?i)(<(?:\w+:)?[a-zA-Z0-9_\-\.]*{Regex.Escape(keyword)}[a-zA-Z0-9_\-\.]*(?:\s+[^>]*)?>)(.*?)(</(?:\w+:)?[a-zA-Z0-9_\-\.]*{Regex.Escape(keyword)}[a-zA-Z0-9_\-\.]*>)";
                    result = Regex.Replace(result, patternElement, match =>
                    {
                        string openTag = match.Groups[1].Value;
                        string val = match.Groups[2].Value;
                        string closeTag = match.Groups[3].Value;

                        string trimmed = val.Trim();
                        if ((string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) &&
                            IsPreservedBooleanTag(openTag))
                        {
                            return match.Value;
                        }

                        return $"{openTag}{Mask}{closeTag}";
                    }, RegexOptions.Singleline, RegexTimeout);

                    // 2. Elementos XML abiertos sin cierre (p.ej. truncado): <tag...keyword...>valor hasta siguiente < o fin
                    string patternOpenElement = $@"(?i)(<(?:\w+:)?[a-zA-Z0-9_\-\.]*{Regex.Escape(keyword)}[a-zA-Z0-9_\-\.]*(?:\s+[^>]*)?>)([^<]+)";
                    result = Regex.Replace(result, patternOpenElement, match =>
                    {
                        string openTag = match.Groups[1].Value;
                        string val = match.Groups[2].Value;

                        string trimmed = val.Trim();
                        if ((string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) &&
                            IsPreservedBooleanTag(openTag))
                        {
                            return match.Value;
                        }

                        return $"{openTag}{Mask}";
                    }, RegexOptions.Singleline, RegexTimeout);

                    // 3. Atributos XML: name="value" o name='value'
                    string patternAttr = $@"(?i)\b((?:\w+:)?[a-zA-Z0-9_-]*{Regex.Escape(keyword)}[a-zA-Z0-9_-]*\s*=\s*)([""'])(.*?)([""'])";
                    result = Regex.Replace(result, patternAttr, match =>
                    {
                        string prefix = match.Groups[1].Value;
                        string quote1 = match.Groups[2].Value;
                        string val = match.Groups[3].Value;
                        string quote2 = match.Groups[4].Value;

                        string trimmed = val.Trim();
                        if ((string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) &&
                            IsPreservedBooleanAttrName(prefix))
                        {
                            return match.Value;
                        }

                        return $"{prefix}{quote1}{Mask}{quote2}";
                    }, RegexOptions.Singleline, RegexTimeout);

                    // 4. JSON: "clave" : "valor"
                    string patternJsonString = $@"""([^""]*{Regex.Escape(keyword)}[^""]*)""(\s*:\s*)""([^""]*)""";
                    result = Regex.Replace(result, patternJsonString, match =>
                    {
                        string key = match.Groups[1].Value;
                        string colon = match.Groups[2].Value;
                        string val = match.Groups[3].Value;

                        if ((string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)) &&
                            IsPreservedBooleanName(key))
                        {
                            return match.Value;
                        }

                        return $@"""{key}""{colon}""{Mask}""";
                    }, RegexOptions.IgnoreCase, RegexTimeout);

                    // 5. JSON: "clave" : valorNoString (p.ej. números, tokens sin comillas o booleanos)
                    string patternJsonOther = $@"""([^""]*{Regex.Escape(keyword)}[^""]*)""(\s*:\s*)([^"",}}\s]+)";
                    result = Regex.Replace(result, patternJsonOther, match =>
                    {
                        string key = match.Groups[1].Value;
                        string colon = match.Groups[2].Value;
                        string val = match.Groups[3].Value;

                        if (string.Equals(val, $"\"{Mask}\"", StringComparison.Ordinal) ||
                            string.Equals(val, Mask, StringComparison.Ordinal))
                        {
                            return match.Value;
                        }

                        if ((string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)) &&
                            IsPreservedBooleanName(key))
                        {
                            return match.Value;
                        }

                        return $@"""{key}""{colon}""{Mask}""";
                    }, RegexOptions.IgnoreCase, RegexTimeout);

                    // 6. Pares clave=valor sin comillas (URLs query string / form-urlencoded)
                    string patternKv = $@"(?i)(?<=[\?&;\s]|^)([a-zA-Z0-9_\-\.]*{Regex.Escape(keyword)}[a-zA-Z0-9_\-\.]*)=([^&;\s""'<>]+)";
                    result = Regex.Replace(result, patternKv, match =>
                    {
                        string key = match.Groups[1].Value;
                        string val = match.Groups[2].Value;

                        if (string.Equals(val, Mask, StringComparison.Ordinal))
                        {
                            return match.Value;
                        }

                        if ((string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)) &&
                            IsPreservedBooleanName(key))
                        {
                            return match.Value;
                        }

                        return $"{key}={Mask}";
                    }, RegexOptions.IgnoreCase, RegexTimeout);
                }

                return result;
            }
            catch (RegexMatchTimeoutException)
            {
                return ContentOmitted;
            }
        }

        private static bool IsPreservedBooleanTag(string tag)
        {
            var match = Regex.Match(tag, @"<(?:\w+:)?([a-zA-Z0-9_-]+)", RegexOptions.None, RegexTimeout);
            if (match.Success)
            {
                return IsPreservedBooleanName(match.Groups[1].Value);
            }
            return false;
        }

        private static bool IsPreservedBooleanAttrName(string attrPrefix)
        {
            var match = Regex.Match(attrPrefix, @"(?:\w+:)?([a-zA-Z0-9_-]+)\s*=", RegexOptions.None, RegexTimeout);
            if (match.Success)
            {
                return IsPreservedBooleanName(match.Groups[1].Value);
            }
            return false;
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
