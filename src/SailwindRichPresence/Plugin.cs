using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace SailwindRichPresence
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(CoopGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.diamondminer99.richpresence";
        public const string PluginName = "Sailwind Rich Presence";
        // BepInEx 5 parses this as a strict System.Version. No SemVer suffixes, or the plugin
        // silently fails to load with no error.
        public const string PluginVersion = "0.1.1";

        /// <summary>Only used to load after Sailwind Co-op when it is installed. Nothing references it.</summary>
        public const string CoopGuid = "com.sailwindcoop.mod";

        /// <summary>
        /// The Discord application this presence belongs to. Its name is what Discord shows after "Playing",
        /// and its Art Assets are the images the keys in <see cref="Art"/> refer to. Not a secret.
        /// </summary>
        public const string DiscordApplicationId = "1549283131201290321";

        public static ManualLogSource Log;

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> ShowBoatAndPort;
        public static ConfigEntry<bool> ShowCrew;
        public static ConfigEntry<bool> ShowElapsedTime;

        private void Awake()
        {
            Log = Logger;

            const string section = "General";
            Enabled = Config.Bind(section, "Enabled", true,
                "Show what you are doing in Sailwind on your Discord profile. Off clears it.");
            ShowBoatAndPort = Config.Bind(section, "ShowBoatAndPort", true,
                "Show the boat you are on, the port you are in, the port you left and the region. Off " +
                "shows only moored, anchored, at sea or ashore.");
            ShowCrew = Config.Bind(section, "ShowCrew", true,
                "Show whether you sail solo or with a Sailwind Co-op crew, the captain's name and the crew " +
                "size. Off hides the second line.");
            ShowElapsedTime = Config.Bind(section, "ShowElapsedTime", true,
                "Show how long the game has been open, in real time.");

            var runner = new GameObject("SailwindRichPresenceRunner");
            runner.AddComponent<PresenceRunner>();
            DontDestroyOnLoad(runner);

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }
    }
}
