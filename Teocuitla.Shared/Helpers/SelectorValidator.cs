using System;
using System.Xml.XPath;

namespace Teocuitla.Shared.Helpers
{
    public static class SelectorValidator
    {
        /// <summary>
        /// Determina de manera robusta si un selector dado tiene el formato de un XPath.
        /// </summary>
        public static bool EsSelectorXPath(string? selector)
        {
            if (string.IsNullOrWhiteSpace(selector)) return false;
            var trimmed = selector.Trim();
            
            // Prefijos clásicos de XPath
            if (trimmed.StartsWith("/") || 
                trimmed.StartsWith("./") || 
                trimmed.StartsWith("../") || 
                trimmed.StartsWith("(") || 
                trimmed.StartsWith("id(") || 
                trimmed.StartsWith("text("))
            {
                return true;
            }
            
            // Marcadores internos que son exclusivos de XPath y no son CSS válidos
            if (trimmed.Contains("[@") || 
                trimmed.Contains("text()") || 
                trimmed.Contains("contains(") || 
                trimmed.Contains("following-sibling::") || 
                trimmed.Contains("preceding-sibling::") || 
                trimmed.Contains("parent::") || 
                trimmed.Contains("ancestor::"))
            {
                return true;
            }
            
            // Chequeo de slash '/' fuera de comillas (detecta rutas relativas de tipo 'body/main[1]/...')
            if (ContainsCharOutsideQuotes(trimmed, '/'))
            {
                return true;
            }
            
            // Chequeo de índices numéricos de XPath como '[1]' fuera de comillas (ej. 'div[1]')
            if (HasXPathIndexOutsideQuotes(trimmed))
            {
                return true;
            }
            
            return false;
        }

        private static bool ContainsCharOutsideQuotes(string text, char targetChar)
        {
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            bool escaped = false;
            
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (inSingleQuote)
                {
                    if (c == '\'') inSingleQuote = false;
                    continue;
                }
                if (inDoubleQuote)
                {
                    if (c == '"') inDoubleQuote = false;
                    continue;
                }
                if (c == '\'')
                {
                    inSingleQuote = true;
                    continue;
                }
                if (c == '"')
                {
                    inDoubleQuote = true;
                    continue;
                }
                
                if (c == targetChar)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasXPathIndexOutsideQuotes(string text)
        {
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            bool escaped = false;
            
            for (int i = 0; i < text.Length - 1; i++)
            {
                char c = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (inSingleQuote)
                {
                    if (c == '\'') inSingleQuote = false;
                    continue;
                }
                if (inDoubleQuote)
                {
                    if (c == '"') inDoubleQuote = false;
                    continue;
                }
                if (c == '\'')
                {
                    inSingleQuote = true;
                    continue;
                }
                if (c == '"')
                {
                    inDoubleQuote = true;
                    continue;
                }
                
                if (c == '[' && char.IsDigit(text[i + 1]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Valida la sintaxis de un XPath compilándolo mediante la clase del sistema.
        /// </summary>
        public static bool IsValidXPath(string xpath)
        {
            if (string.IsNullOrWhiteSpace(xpath)) return false;
            try
            {
                XPathExpression.Compile(xpath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Valida de forma preventiva la estructura básica de un selector CSS.
        /// Retorna falso si detecta elementos sin cerrar, caracteres ilegales o combinadores huérfanos.
        /// </summary>
        public static bool IsValidCssSelector(string? cssSelector)
        {
            if (string.IsNullOrWhiteSpace(cssSelector)) return false;
            
            var trimmed = cssSelector.Trim();
            if (trimmed.Length == 0) return false;
            
            // Un selector CSS no puede terminar con un combinador (+, ~, >, , o la barra de escape)
            char lastChar = trimmed[trimmed.Length - 1];
            if (lastChar == '+' || lastChar == '~' || lastChar == '>' || lastChar == ',' || lastChar == '\\')
                return false;
                
            int bracketCount = 0;
            int parenCount = 0;
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            bool escaped = false;
            
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                
                if (inSingleQuote)
                {
                    if (c == '\'')
                    {
                        inSingleQuote = false;
                    }
                    continue;
                }
                
                if (inDoubleQuote)
                {
                    if (c == '"')
                    {
                        inDoubleQuote = false;
                    }
                    continue;
                }
                
                if (c == '\'')
                {
                    inSingleQuote = true;
                    continue;
                }
                
                if (c == '"')
                {
                    inDoubleQuote = true;
                    continue;
                }
                
                if (c == '[')
                {
                    bracketCount++;
                }
                else if (c == ']')
                {
                    bracketCount--;
                    if (bracketCount < 0) return false; // Paréntesis rectangular de cierre huérfano
                }
                else if (c == '(')
                {
                    parenCount++;
                }
                else if (c == ')')
                {
                    parenCount--;
                    if (parenCount < 0) return false; // Paréntesis de cierre huérfano
                }
                
                // Validación de caracteres en la estructura externa (fuera de corchetes de atributos y pseudo-clases)
                if (bracketCount == 0 && parenCount == 0)
                {
                    // En selectores CSS estándar, caracteres como @, !, $, %, ^, &, = no deben aparecer fuera de atributos o argumentos
                    if (c == '@' || c == '!' || c == '$' || c == '%' || c == '^' || c == '&' || c == '=')
                    {
                        return false;
                    }
                }
            }
            
            // Si al terminar la lectura quedan elementos abiertos, la sintaxis está incompleta
            if (bracketCount != 0 || parenCount != 0 || inSingleQuote || inDoubleQuote || escaped)
            {
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Valida cualquier tipo de selector (auto-detecta si es XPath o CSS) y retorna si es válido.
        /// </summary>
        public static bool IsValidSelector(string? selector)
        {
            if (string.IsNullOrWhiteSpace(selector)) return false;
            
            if (EsSelectorXPath(selector))
            {
                return IsValidXPath(selector);
            }
            else
            {
                return IsValidCssSelector(selector);
            }
        }
    }
}
