using UnityEngine;

namespace SailwindRichPresence
{
    /// <summary>
    /// Reads the game every couple of seconds on the main thread and hands the result to the pipe thread.
    /// All timing here is unscaled, so the 16x sleep warp does not speed it up.
    /// </summary>
    internal sealed class PresenceRunner : MonoBehaviour
    {
        private const float PollSeconds = 2f;
        private const float CrewPollSeconds = 5f;

        private DiscordClient client;
        private GameReader reader;
        private CoopStatusReader.Crew crew;
        private float nextPoll;
        private float nextCrewPoll;

        private Activity handed;
        private bool handedAny;
        private Activity candidate;
        private bool haveCandidate;

        private void Start()
        {
            reader = new GameReader();
            client = new DiscordClient(Plugin.DiscordApplicationId);
            client.Start();
        }

        private void Update()
        {
            client.FlushLog();

            float now = Time.unscaledTime;
            if (now < nextPoll) return;
            nextPoll = now + PollSeconds;

            Activity next = null;
            if (Plugin.Enabled.Value)
            {
                if (now >= nextCrewPoll)
                {
                    crew = CoopStatusReader.Read();
                    nextCrewPoll = now + CrewPollSeconds;
                }
                if (!reader.TryBuild(crew, out next)) return;
            }

            if (handedAny && Activity.Same(next, handed))
            {
                haveCandidate = false;
                return;
            }

            // A new status has to read the same on two polls in a row before it goes out, so stepping
            // over the gunwale does not flash "Swimming" on the profile.
            if (handedAny && !(haveCandidate && Activity.Same(next, candidate)))
            {
                candidate = next;
                haveCandidate = true;
                return;
            }

            handed = next;
            handedAny = true;
            haveCandidate = false;
            client.SetActivity(next);
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            if (client == null) return;
            client.Stop();
            client.FlushLog();
        }
    }
}
