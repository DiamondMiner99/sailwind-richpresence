using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace SailwindRichPresence
{
    /// <summary>
    /// Turns game state into a status, on the main thread. Every check here is one the game itself makes:
    ///
    /// - Aboard: GameState.currentBoat, set by the embark code and by the shipyard. Its parent is the boat root.
    /// - Moored and anchored: the two halves of BoatMooringRopes.AnyRopeMoored, which Sleep uses to decide
    ///   whether a boat is tied up. Split so an anchored boat does not read as moored.
    /// - In a port: RecoveryPort.Update's test for GameState.lastVisitedPort, within 1000 m of the port's
    ///   recovery point. The game has no other idea of being in a port. See FindPort for the few without one.
    /// - Region: RegionBlender's current region, which drives the weather.
    ///
    /// No position, distance, speed, heading or game time is ever put into the text. At sea the line is
    /// picked at random from what is true, and only re-picked every few minutes, so it cannot be watched
    /// to find land.
    /// </summary>
    internal sealed class GameReader
    {
        private const float PortRadius = 1000f;

        // "Out of <port>" is offered while the departure is recent by either measure.
        private const float DepartureWindowSeconds = 20f * 60f;
        private const float DepartureRange = 6000f;

        private const float MinRollSeconds = 4f * 60f;
        private const float MaxRollSeconds = 8f * 60f;

        private static readonly FieldInfo RecoveryPorts =
            typeof(Recovery).GetField("ports", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo CurrentRegion =
            typeof(RegionBlender).GetField("currentTargetRegion", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly long launchUnix = LaunchTime();
        private readonly System.Random random = new System.Random();
        private readonly Dictionary<IslandHorizon, float> islandTops = new Dictionary<IslandHorizon, float>();
        private readonly HashSet<Port> portsWithRecoveryPoint = new HashSet<Port>();

        // The port the player was last moored, anchored or ashore in, and when they were last seen there.
        private Port departurePort;
        private Transform departurePoint;
        private float departureTime;

        // What the at-sea line says until the next roll.
        private bool atSea;
        private Transform seaBoat;
        private string seaLine;
        private float nextRoll;

        private bool loggedFailure;
        private bool loggedPorts;

        /// <summary>
        /// False while the game is mid-load or carrying a passed-out player to port. The state is in flux
        /// then, and Discord keeps showing what it had.
        /// </summary>
        public bool TryBuild(CoopStatusReader.Crew crew, out Activity activity)
        {
            activity = null;
            try
            {
                return Build(crew, out activity);
            }
            catch (Exception e)
            {
                if (!loggedFailure)
                {
                    loggedFailure = true;
                    Plugin.Log.LogWarning("Could not read the game state: " + e);
                }
                return false;
            }
        }

        private bool Build(CoopStatusReader.Crew crew, out Activity a)
        {
            a = new Activity { LargeImage = Art.Large, LargeText = "Sailwind" };
            if (Plugin.ShowElapsedTime.Value) a.StartTimestamp = launchUnix;

            if (!GameState.playing)
            {
                a.Details = GameState.currentlyLoading ? "Loading a save" : "In the main menu";
                if (crew.InSession) AddCrew(a, crew);
                atSea = false;
                return true;
            }

            if (GameState.currentlyLoading || GameState.recovering || Refs.observerMirror == null) return false;

            AddCrew(a, crew);

            bool showWhere = Plugin.ShowBoatAndPort.Value;
            Vector3 player = Refs.observerMirror.transform.position;
            Transform boat = GameState.currentBoat != null ? GameState.currentBoat.parent : null;
            string boatName = showWhere && boat != null ? BoatNames.Get(boat) : null;
            if (boatName != null) a.LargeText = boatName;

            bool moored = false, anchored = false;
            if (boat != null) ReadMooring(boat, out moored, out anchored);
            bool swimming = boat == null && PlayerSwimming.observerSwimming;
            bool shipyard = GameState.currentShipyard != null;

            // The port lookup only runs where a port name may be shown, never out at sea.
            Port port = null;
            if (shipyard || moored || anchored || (boat == null && !swimming))
            {
                port = FindPort(player, out Transform point);
                if (port != null)
                {
                    departurePort = port;
                    departurePoint = point;
                    departureTime = Time.realtimeSinceStartup;
                }
            }
            string portName = showWhere && port != null ? port.GetPortName() : null;

            bool wasAtSea = atSea;
            atSea = false;

            if (shipyard)
            {
                a.Details = portName != null ? "At the shipyard in " + portName : "At the shipyard";
            }
            else if (GameState.sleeping)
            {
                SetBadge(a, Art.Asleep, "Asleep");
                if (GameState.sleepingInTavern)
                    a.Details = portName != null ? "Asleep at the tavern in " + portName : "Asleep at a tavern";
                else if (boat != null)
                    a.Details = boatName != null ? "Asleep aboard " + BoatNames.WithArticle(boatName) : "Asleep on board";
                else
                    a.Details = "Asleep";
            }
            else if (boat != null)
            {
                if (moored)
                {
                    SetBadge(a, Art.Moored, "Moored");
                    a.Details = Aboard(boatName, portName != null ? "moored at " + portName : "moored");
                }
                else if (anchored)
                {
                    SetBadge(a, Art.Anchored, "Anchored");
                    a.Details = Aboard(boatName, "anchored");
                }
                else
                {
                    SetBadge(a, Art.AtSea, "At sea");
                    atSea = true;
                    a.Details = Aboard(boatName, showWhere ? SeaLine(boat, wasAtSea, player) : "at sea");
                }
            }
            else if (swimming)
            {
                a.Details = "Swimming";
            }
            else
            {
                SetBadge(a, Art.Ashore, "Ashore");
                a.Details = portName != null ? "Ashore in " + portName : "Ashore";
            }

            return true;
        }

        private static void AddCrew(Activity a, CoopStatusReader.Crew crew)
        {
            if (!Plugin.ShowCrew.Value) return;

            if (!crew.InSession)
            {
                a.State = "Sailing solo";
                return;
            }

            if (crew.IsCaptain)
                a.State = "Sailing as captain";
            else
                a.State = string.IsNullOrEmpty(crew.CaptainName) ? "Sailing with a crew" : "Sailing with " + crew.CaptainName;

            if (crew.Count > 0 && crew.Max >= crew.Count)
            {
                a.PartySize = crew.Count;
                a.PartyMax = crew.Max;
            }
        }

        private static void SetBadge(Activity a, string image, string text)
        {
            a.SmallImage = image;
            a.SmallText = text;
        }

        /// <summary>"Brig, moored at Fort Aestrin", or "Moored at Fort Aestrin" when the boat is hidden or unknown.</summary>
        private static string Aboard(string boatName, string situation)
        {
            if (boatName != null) return boatName + ", " + situation;
            return char.ToUpperInvariant(situation[0]) + situation.Substring(1);
        }

        private static void ReadMooring(Transform boat, out bool moored, out bool anchored)
        {
            moored = false;
            anchored = false;

            var mooring = boat.GetComponent<BoatMooringRopes>();
            if (mooring == null) return;

            if (mooring.ropes != null)
            {
                foreach (var rope in mooring.ropes)
                {
                    if (rope != null && rope.IsMoored())
                    {
                        moored = true;
                        break;
                    }
                }
            }

            // Anchor.IsSet reads a rigidbody the anchor only picks up in Start.
            try { anchored = mooring.anchor != null && mooring.anchor.IsSet(); }
            catch (NullReferenceException) { anchored = false; }
        }

        private Port FindPort(Vector3 player, out Transform point)
        {
            point = null;
            Port best = null;
            float bestDistance = PortRadius;

            portsWithRecoveryPoint.Clear();
            var recoveryPoints = RecoveryPorts != null ? RecoveryPorts.GetValue(null) as List<RecoveryPort> : null;
            if (recoveryPoints != null)
            {
                foreach (var recoveryPoint in recoveryPoints)
                {
                    if (recoveryPoint == null || recoveryPoint.parentPort == null) continue;
                    portsWithRecoveryPoint.Add(recoveryPoint.parentPort);
                    float distance = Vector3.Distance(recoveryPoint.transform.position, player);
                    if (distance > bestDistance) continue;
                    bestDistance = distance;
                    best = recoveryPoint.parentPort;
                    point = recoveryPoint.transform;
                }
            }

            // A few ports have no recovery point (On'na and Saffron Island in 0.38.1). For those the port
            // object itself stands in, which is where the game's debug teleport sends the player. Only
            // when it sits under the shifting world, so the floating origin keeps it in the player's frame.
            if (Port.ports != null && Refs.shiftingWorld != null)
            {
                foreach (var port in Port.ports)
                {
                    if (port == null || portsWithRecoveryPoint.Contains(port)) continue;
                    if (!port.transform.IsChildOf(Refs.shiftingWorld)) continue;
                    float distance = Vector3.Distance(port.transform.position, player);
                    if (distance > bestDistance) continue;
                    bestDistance = distance;
                    best = port;
                    point = port.transform;
                }
            }

            LogPortsOnce();
            return best;
        }

        /// <summary>
        /// Lists the ports that have no recovery point and whether they can be named from the port object
        /// instead. Names only.
        /// </summary>
        private void LogPortsOnce()
        {
            if (loggedPorts || portsWithRecoveryPoint.Count == 0 || Port.ports == null) return;
            loggedPorts = true;

            var missing = new List<string>();
            int total = 0;
            foreach (var port in Port.ports)
            {
                if (port == null) continue;
                total++;
                if (portsWithRecoveryPoint.Contains(port)) continue;
                bool usable = Refs.shiftingWorld != null && port.transform.IsChildOf(Refs.shiftingWorld);
                missing.Add(port.GetPortName() + (usable ? " (named from the port object)" : " (cannot be named)"));
            }

            Plugin.Log.LogInfo(portsWithRecoveryPoint.Count + " of " + total + " ports have a recovery point. " +
                               (missing.Count == 0 ? "None missing." : "Without one: " + string.Join(", ", missing.ToArray()) + "."));
        }

        private string SeaLine(Transform boat, bool wasAtSea, Vector3 player)
        {
            float now = Time.realtimeSinceStartup;
            if (wasAtSea && boat == seaBoat && seaLine != null && now < nextRoll) return seaLine;

            seaLine = RollSeaLine(player);
            seaBoat = boat;
            nextRoll = now + Mathf.Lerp(MinRollSeconds, MaxRollSeconds, (float)random.NextDouble());
            return seaLine;
        }

        /// <summary>
        /// Plain "at sea" is always possible and is the only choice far from land. The region joins in once
        /// land is in sight, and the port just left joins in while the departure is recent.
        /// </summary>
        private string RollSeaLine(Vector3 player)
        {
            bool land = LandInSight();
            string region = land ? RegionName() : null;
            string left = RecentDeparture(player) ? departurePort.GetPortName() : null;

            int plainWeight = land ? 1 : 3;
            int regionWeight = region != null ? 2 : 0;
            int leftWeight = left != null ? 3 : 0;
            int pick = random.Next(plainWeight + regionWeight + leftWeight);

            string line;
            if (pick < plainWeight) line = "at sea";
            else if (pick < plainWeight + regionWeight) line = "at sea in " + region;
            else line = "out of " + left;
            return line;
        }

        private bool RecentDeparture(Vector3 player)
        {
            if (departurePort == null) return false;
            if (Time.realtimeSinceStartup - departureTime < DepartureWindowSeconds) return true;
            return departurePoint != null && Vector3.Distance(departurePoint.position, player) < DepartureRange;
        }

        /// <summary>
        /// IslandHorizon lowers every island by the game's curvature formula as the player sails away from it,
        /// until it drops under the sea. An island whose highest mesh still stands above sea level is one the
        /// player could see in clear weather. Fog, rain and night are not taken into account.
        /// </summary>
        private bool LandInSight()
        {
            var tracker = IslandDistanceTracker.instance;
            if (tracker == null || tracker.islands == null) return false;

            foreach (var island in tracker.islands)
            {
                if (island == null) continue;
                float top = IslandTopAbovePivot(island);
                if (top > 0f && island.transform.position.y + top > 1f) return true;
            }
            return false;
        }

        /// <summary>
        /// Height of the island's highest mesh above its own pivot. The sinking moves the island as one
        /// piece, so this is measured once per island and added to the pivot's current height.
        /// </summary>
        private float IslandTopAbovePivot(IslandHorizon island)
        {
            if (islandTops.TryGetValue(island, out float cached)) return cached;

            float pivot = island.transform.position.y;
            float top = float.NegativeInfinity;
            foreach (var renderer in island.GetComponentsInChildren<MeshRenderer>(false))
            {
                Bounds bounds = renderer.bounds;
                if (bounds.size.sqrMagnitude <= 0f) continue;
                top = Mathf.Max(top, bounds.max.y);
            }

            float offset = float.IsNegativeInfinity(top) ? 0f : top - pivot;
            if (offset > 0f) islandTops[island] = offset;
            return offset;
        }

        private static string RegionName()
        {
            var blender = RegionBlender.instance;
            if (blender == null || CurrentRegion == null) return null;

            var region = CurrentRegion.GetValue(blender) as Region;
            if (region == null) return null;

            // The start menu's names for the three regions.
            switch (region.portRegion)
            {
                case PortRegion.alankh: return "Al'Ankh";
                case PortRegion.emerald: return "the Emerald Archipelago";
                case PortRegion.medi: return "Aestrin";
                default: return null;
            }
        }

        private static long LaunchTime()
        {
            try { return new DateTimeOffset(Process.GetCurrentProcess().StartTime).ToUnixTimeSeconds(); }
            catch { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
        }
    }
}
