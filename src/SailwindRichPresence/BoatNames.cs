using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SailwindRichPresence
{
    /// <summary>
    /// The game has no display names for boats, so they come from the boat's save index. ShipyardExpansion
    /// and NANDTweaks key on these same numbers.
    /// </summary>
    internal static class BoatNames
    {
        private static readonly Dictionary<int, string> Stock = new Dictionary<int, string>
        {
            { 10, "Dhow" },
            { 20, "Sanbuq" },
            // Sailwind 0.39 put "BOAT dhow large (30)" on sale in Oasis. Do not let the object name mislead
            // you: "dhow" is the dev's internal family label for the whole Al'Ankh line (small is the dhow,
            // medium is the sanbuq), and this hull is a bigger sanbuq, not a big dhow. Players call it the
            // large or big sanbuq, or bigbuq. The game itself carries no display string for any boat.
            { 30, "Large Sanbuq" },
            { 40, "Cog" },
            { 50, "Brig" },
            { 70, "Jong" },
            { 80, "Junk" },
            { 90, "Kakam" },
        };

        /// <summary>Modded boats whose object name is not what players call them.</summary>
        private static readonly Dictionary<string, string> Modded =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "LEOPARD", "HMS Leopard" },
            };

        private static readonly Regex Bracketed = new Regex(@"\(([^)]*)\)", RegexOptions.CultureInvariant);

        /// <summary>The boat's name, or null when it cannot be told. <paramref name="boat"/> is the boat root.</summary>
        public static string Get(Transform boat)
        {
            var saveable = boat.GetComponent<SaveableObject>();
            if (saveable != null && Stock.TryGetValue(saveable.sceneIndex, out string name)) return name;

            // Boat roots carry the stock index in their name, as in "BOAT medi medium (50)". A copy made by
            // another mod keeps that name under a new index.
            foreach (Match match in Bracketed.Matches(boat.name))
            {
                if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                    && Stock.TryGetValue(index, out name))
                    return name;
            }

            // Other modded boats follow the same pattern: "BOAT LEOPARD (207)(Clone)" is the Leopard.
            string bare = Bracketed.Replace(boat.name, "").Trim();
            if (!bare.StartsWith("BOAT ", StringComparison.OrdinalIgnoreCase)) return null;
            bare = bare.Substring(5).Trim();
            if (Modded.TryGetValue(bare, out name)) return name;
            if (bare.Length < 2) return null;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(bare.ToLowerInvariant());
        }

        /// <summary>"the Brig", but "HMS Leopard".</summary>
        public static string WithArticle(string name)
        {
            return name.StartsWith("HMS ", StringComparison.Ordinal) ? name : "the " + name;
        }
    }
}
