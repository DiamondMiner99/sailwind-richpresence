using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SailwindRichPresence
{
    /// <summary>
    /// Just enough JSON for the Discord pipe. Everything sent is built by hand, and the only things read
    /// back are the event name and an error message, so a parser would be dead weight to ship.
    /// </summary>
    internal static class Json
    {
        private static readonly Regex EvtPattern =
            new Regex("\"evt\"\\s*:\\s*\"([A-Z_]+)\"", RegexOptions.CultureInvariant);

        private static readonly Regex MessagePattern =
            new Regex("\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);

        public static void String(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>The "evt" of an incoming frame, such as READY or ERROR, or null when it has none.</summary>
        public static string Event(string json)
        {
            var match = EvtPattern.Match(json);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>The first "message" in an incoming frame, still escaped, or the whole frame if there is none.</summary>
        public static string Message(string json)
        {
            var match = MessagePattern.Match(json);
            return match.Success ? match.Groups[1].Value : json;
        }
    }
}
