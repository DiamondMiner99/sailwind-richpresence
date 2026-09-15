using System.Globalization;
using System.Text;

namespace SailwindRichPresence
{
    /// <summary>
    /// Keys of the images uploaded under Rich Presence, Art Assets in the Discord application. The files
    /// are in the repo's art folder, named after these keys. Discord also takes a full https:// image URL
    /// in place of a key.
    /// </summary>
    internal static class Art
    {
        public const string Large = "sailwind";
        public const string Moored = "moored";
        public const string Anchored = "anchored";
        public const string AtSea = "atsea";
        public const string Ashore = "ashore";
        public const string Asleep = "asleep";
    }

    /// <summary>
    /// One Discord status, in plain values. Built on the main thread from game state and handed to the
    /// pipe thread, which only ever reads it, so nothing here touches Unity.
    /// </summary>
    internal sealed class Activity
    {
        private const string ModUrl = "https://github.com/DiamondMiner99/sailwind-richpresence";
        private const string SteamUrl = "https://store.steampowered.com/app/1764530/Sailwind/";

        public string Details;
        public string State;
        public string LargeImage;
        public string LargeText;
        public string SmallImage;
        public string SmallText;

        /// <summary>Unix seconds the elapsed timer counts from, or 0 for no timer.</summary>
        public long StartTimestamp;

        /// <summary>People in the co-op crew including this player, or 0 when not in a crew.</summary>
        public int PartySize;
        public int PartyMax;

        public static bool Same(Activity a, Activity b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Details == b.Details && a.State == b.State
                && a.LargeImage == b.LargeImage && a.LargeText == b.LargeText
                && a.SmallImage == b.SmallImage && a.SmallText == b.SmallText
                && a.StartTimestamp == b.StartTimestamp
                && a.PartySize == b.PartySize && a.PartyMax == b.PartyMax;
        }

        /// <summary>
        /// The SET_ACTIVITY command. A null activity clears the status. <paramref name="partyId"/> is a
        /// random id made when the game starts. It exists so Discord shows the party size, and it is not
        /// tied to the co-op lobby, so nobody can use it to find or join the crew.
        /// </summary>
        public static string Command(int pid, Activity activity, string partyId, string nonce)
        {
            var sb = new StringBuilder(640);
            sb.Append("{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":")
              .Append(pid.ToString(CultureInfo.InvariantCulture))
              .Append(",\"activity\":");
            if (activity == null) sb.Append("null");
            else activity.Write(sb, partyId);
            sb.Append("},\"nonce\":");
            Json.String(sb, nonce);
            sb.Append('}');
            return sb.ToString();
        }

        private void Write(StringBuilder sb, string partyId)
        {
            sb.Append('{');
            bool first = true;

            TextField(sb, ref first, "details", Details);
            TextField(sb, ref first, "state", State);

            if (StartTimestamp > 0)
            {
                Comma(sb, ref first);
                sb.Append("\"timestamps\":{\"start\":")
                  .Append(StartTimestamp.ToString(CultureInfo.InvariantCulture))
                  .Append('}');
            }

            Comma(sb, ref first);
            sb.Append("\"assets\":{");
            bool firstAsset = true;
            TextField(sb, ref firstAsset, "large_image", LargeImage);
            TextField(sb, ref firstAsset, "large_text", LargeText);
            TextField(sb, ref firstAsset, "small_image", SmallImage);
            TextField(sb, ref firstAsset, "small_text", SmallText);
            sb.Append('}');

            if (PartySize > 0 && PartyMax >= PartySize)
            {
                Comma(sb, ref first);
                sb.Append("\"party\":{\"id\":");
                Json.String(sb, partyId);
                sb.Append(",\"size\":[")
                  .Append(PartySize.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(PartyMax.ToString(CultureInfo.InvariantCulture)).Append("]}");
            }

            // Labels are capped at 32 characters. Discord hides these buttons from the player whose
            // profile it is, so only friends see them.
            Comma(sb, ref first);
            sb.Append("\"buttons\":[");
            Button(sb, "Sailwind Rich Presence", ModUrl);
            sb.Append(',');
            Button(sb, "Sailwind on Steam", SteamUrl);
            sb.Append(']');

            sb.Append('}');
        }

        /// <summary>
        /// Discord rejects the whole update if any text is under 2 or over 128 characters, so short text is
        /// left out and long text is cut.
        /// </summary>
        private static void TextField(StringBuilder sb, ref bool first, string name, string value)
        {
            if (value == null || value.Length < 2) return;
            if (value.Length > 128) value = value.Substring(0, 128);
            Comma(sb, ref first);
            sb.Append('"').Append(name).Append("\":");
            Json.String(sb, value);
        }

        private static void Button(StringBuilder sb, string label, string url)
        {
            sb.Append("{\"label\":");
            Json.String(sb, label);
            sb.Append(",\"url\":");
            Json.String(sb, url);
            sb.Append('}');
        }

        private static void Comma(StringBuilder sb, ref bool first)
        {
            if (!first) sb.Append(',');
            first = false;
        }
    }
}
