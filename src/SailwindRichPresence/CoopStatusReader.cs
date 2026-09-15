using System;
using System.Reflection;

namespace SailwindRichPresence
{
    /// <summary>
    /// Reads SailwindCoop.CoopStatus, the static class Sailwind Co-op 0.4.0 and later keeps for other mods,
    /// by reflection so this mod runs without co-op installed. A missing type, a different ApiVersion or
    /// any exception reads as sailing solo.
    ///
    /// Each read can cost Steam calls on co-op's side, so the runner polls this every few seconds on the
    /// main thread and never from the pipe thread.
    /// </summary>
    internal static class CoopStatusReader
    {
        private const string TypeName = "SailwindCoop.CoopStatus";
        private const string AssemblyName = "SailwindCoop";
        private const int SupportedApi = 1;

        private enum State { Unresolved, Absent, Ready, Broken }

        public struct Crew
        {
            public bool InSession;
            public bool IsCaptain;
            public int Count;
            public int Max;
            public string CaptainName;
        }

        private static State state = State.Unresolved;
        private static PropertyInfo inSession;
        private static PropertyInfo isCaptain;
        private static PropertyInfo crewCount;
        private static PropertyInfo maxCrew;
        private static PropertyInfo captainName;

        public static Crew Read()
        {
            if (state == State.Unresolved) Resolve();
            if (state != State.Ready) return default(Crew);

            try
            {
                var crew = new Crew { InSession = (bool)inSession.GetValue(null, null) };
                if (!crew.InSession) return crew;
                crew.IsCaptain = (bool)isCaptain.GetValue(null, null);
                crew.Count = (int)crewCount.GetValue(null, null);
                crew.Max = (int)maxCrew.GetValue(null, null);
                crew.CaptainName = captainName.GetValue(null, null) as string ?? "";
                return crew;
            }
            catch (Exception e)
            {
                state = State.Broken;
                Plugin.Log.LogWarning("Could not read the Sailwind Co-op session, showing solo from now on: " + e.Message);
                return default(Crew);
            }
        }

        private static void Resolve()
        {
            try
            {
                Type type = Type.GetType(TypeName + ", " + AssemblyName, false);
                if (type == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (assembly.GetName().Name != AssemblyName) continue;
                        type = assembly.GetType(TypeName, false);
                        break;
                    }
                }

                if (type == null)
                {
                    state = State.Absent;
                    Plugin.Log.LogInfo("Sailwind Co-op 0.4.0 or later is not installed. Showing solo.");
                    return;
                }

                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                FieldInfo api = type.GetField("ApiVersion", flags);
                int version = api != null ? Convert.ToInt32(api.GetValue(null)) : 0;
                if (version != SupportedApi)
                {
                    state = State.Broken;
                    Plugin.Log.LogWarning("Sailwind Co-op status API is version " + version + ", this mod reads version " +
                                          SupportedApi + ". Showing solo.");
                    return;
                }

                inSession = type.GetProperty("InSession", flags);
                isCaptain = type.GetProperty("IsCaptain", flags);
                crewCount = type.GetProperty("CrewCount", flags);
                maxCrew = type.GetProperty("MaxCrew", flags);
                captainName = type.GetProperty("CaptainName", flags);

                if (inSession == null || isCaptain == null || crewCount == null || maxCrew == null || captainName == null)
                {
                    state = State.Broken;
                    Plugin.Log.LogWarning("Sailwind Co-op status is missing a member this mod reads. Showing solo.");
                    return;
                }

                state = State.Ready;
                Plugin.Log.LogInfo("Sailwind Co-op detected. Crew info will be shown.");
            }
            catch (Exception e)
            {
                state = State.Broken;
                Plugin.Log.LogWarning("Sailwind Co-op detection failed, showing solo: " + e.Message);
            }
        }
    }
}
