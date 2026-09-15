using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SailwindRichPresence
{
    /// <summary>
    /// Talks to the Discord desktop client over its local IPC pipe on a background thread. The game hands
    /// it the status it wants through <see cref="SetActivity"/> and never waits on it.
    ///
    /// Frames are an int32 opcode and an int32 payload length, both little-endian, then UTF-8 JSON. The
    /// client sends a handshake, waits for READY, then sends SET_ACTIVITY whenever the wanted status
    /// changes, at most once every 15 seconds. Discord allows about 5 updates per 20 seconds.
    /// </summary>
    internal sealed class DiscordClient
    {
        private const int OpHandshake = 0;
        private const int OpFrame = 1;
        private const int OpClose = 2;
        private const int OpPing = 3;
        private const int OpPong = 4;

        private const double RetrySeconds = 20;
        private const double MinSendGapSeconds = 15;
        private const double ReadyTimeoutSeconds = 10;
        private const int MaxFrameBytes = 1 << 20;

        private readonly string applicationId;
        private readonly int pid;
        private readonly string partyId = Guid.NewGuid().ToString("N");

        // Shared with the main thread.
        private readonly object gate = new object();
        private Activity wanted;
        private bool wantedSet;
        private volatile bool stopping;
        private readonly ManualResetEvent wake = new ManualResetEvent(false);
        private readonly ConcurrentQueue<KeyValuePair<bool, string>> log =
            new ConcurrentQueue<KeyValuePair<bool, string>>();

        // Pipe thread only.
        private Thread thread;
        private DiscordPipe pipe;
        private string lastProblem;
        private Activity sent;
        private bool sentAny;
        private double lastSendAt = double.NegativeInfinity;
        private readonly Stopwatch clock = new Stopwatch();

        public DiscordClient(string applicationId)
        {
            this.applicationId = applicationId ?? "";
            pid = Process.GetCurrentProcess().Id;
        }

        public void Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "SailwindRichPresence" };
            thread.Start();
        }

        /// <summary>
        /// Main thread. The pipe thread only reads the activity, so the caller must not change it after
        /// handing it over. Null clears the status.
        /// </summary>
        public void SetActivity(Activity activity)
        {
            lock (gate)
            {
                wanted = activity;
                wantedSet = true;
            }
        }

        /// <summary>Main thread. Clears the status and closes the pipe, waiting briefly for the thread.</summary>
        public void Stop()
        {
            if (thread == null || stopping) return;
            stopping = true;
            wake.Set();
            thread.Join(1500);
        }

        /// <summary>
        /// Main thread. The pipe thread queues its log lines rather than writing them itself, so every
        /// write to the BepInEx log happens on the thread Unity expects.
        /// </summary>
        public void FlushLog()
        {
            while (log.TryDequeue(out var entry))
            {
                if (entry.Key) Plugin.Log.LogWarning(entry.Value);
                else Plugin.Log.LogInfo(entry.Value);
            }
        }

        private void Run()
        {
            clock.Start();
            double nextAttempt = 0;

            while (!stopping)
            {
                if (pipe == null)
                {
                    if (clock.Elapsed.TotalSeconds >= nextAttempt)
                    {
                        Connect();
                        if (pipe == null) nextAttempt = clock.Elapsed.TotalSeconds + RetrySeconds;
                    }
                }
                else
                {
                    try
                    {
                        Pump();
                        SendIfChanged();
                    }
                    catch (Exception e)
                    {
                        Close();
                        Problem("Lost the connection to Discord (" + e.Message + "). Trying again every 20 seconds.", true);
                        nextAttempt = clock.Elapsed.TotalSeconds + RetrySeconds;
                    }
                }

                wake.WaitOne(250);
            }

            if (pipe != null)
            {
                try { WriteFrame(pipe, OpFrame, Activity.Command(pid, null, partyId, Nonce())); }
                catch { }
                Close();
            }
        }

        private void Connect()
        {
            bool anyPipe = false;

            for (int i = 0; i < 10 && !stopping; i++)
            {
                string name = "discord-ipc-" + i;
                if (!Kernel32.PipeExists(name)) continue;
                anyPipe = true;

                DiscordPipe candidate = null;
                try
                {
                    candidate = DiscordPipe.Open(name);
                    Handshake(candidate);
                    pipe = candidate;
                    sentAny = false;
                    lastSendAt = double.NegativeInfinity;
                    lastProblem = null;
                    Info("Connected to Discord on " + name + ".");
                    return;
                }
                catch (DiscordRefused e)
                {
                    // Discord answered, so the pipe works. The reason is on Discord's side, usually the
                    // application id.
                    candidate.Dispose();
                    Problem("Discord refused the connection: " + e.Message + ". Trying again every 20 seconds.", true);
                    return;
                }
                catch (Exception e)
                {
                    if (candidate != null) candidate.Dispose();
                    Problem("Could not talk to Discord on " + name + " (" + e.Message + ").", true);
                }
            }

            if (!anyPipe) Problem("Discord is not running. Checking again every 20 seconds.", false);
        }

        private void Handshake(DiscordPipe p)
        {
            var sb = new StringBuilder("{\"v\":1,\"client_id\":");
            Json.String(sb, applicationId);
            sb.Append('}');
            WriteFrame(p, OpHandshake, sb.ToString());

            double deadline = clock.Elapsed.TotalSeconds + ReadyTimeoutSeconds;
            while (clock.Elapsed.TotalSeconds < deadline)
            {
                if (stopping) throw new IOException("The game is closing.");

                if (p.Available() < 8)
                {
                    Thread.Sleep(50);
                    continue;
                }

                ReadFrame(p, out int op, out string json);
                if (op == OpClose) throw new DiscordRefused(Json.Message(json));
                if (op == OpPing)
                {
                    WriteFrame(p, OpPong, json);
                    continue;
                }
                if (op != OpFrame) continue;

                string evt = Json.Event(json);
                if (evt == "READY") return;
                if (evt == "ERROR") throw new DiscordRefused(Json.Message(json));
            }

            throw new TimeoutException("Discord did not answer the handshake within 10 seconds");
        }

        private void Pump()
        {
            while (pipe.Available() >= 8)
            {
                ReadFrame(pipe, out int op, out string json);
                switch (op)
                {
                    case OpPing:
                        WriteFrame(pipe, OpPong, json);
                        break;
                    case OpClose:
                        throw new IOException("Discord closed the pipe: " + Json.Message(json));
                    case OpFrame:
                        if (Json.Event(json) == "ERROR")
                            Problem("Discord rejected the status: " + Json.Message(json), true);
                        break;
                }
            }
        }

        private void SendIfChanged()
        {
            Activity want;
            bool have;
            lock (gate)
            {
                want = wanted;
                have = wantedSet;
            }

            if (!have) return;
            if (sentAny && Activity.Same(want, sent)) return;

            double now = clock.Elapsed.TotalSeconds;
            if (now - lastSendAt < MinSendGapSeconds) return;

            WriteFrame(pipe, OpFrame, Activity.Command(pid, want, partyId, Nonce()));
            sent = want;
            sentAny = true;
            lastSendAt = now;
        }

        private void Close()
        {
            if (pipe == null) return;
            pipe.Dispose();
            pipe = null;
        }

        // BitConverter is little-endian on every platform Sailwind runs on, which is what Discord expects.
        private static void ReadFrame(DiscordPipe p, out int op, out string json)
        {
            var header = new byte[8];
            p.ReadExactly(header);
            op = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);
            if (length < 0 || length > MaxFrameBytes)
                throw new IOException("Discord sent a frame of " + length + " bytes");

            var body = new byte[length];
            p.ReadExactly(body);
            json = Encoding.UTF8.GetString(body);
        }

        // One write per frame, so a frame never goes out in pieces.
        private static void WriteFrame(DiscordPipe p, int op, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            var frame = new byte[8 + body.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(op), 0, frame, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, frame, 4, 4);
            Buffer.BlockCopy(body, 0, frame, 8, body.Length);
            p.Write(frame);
        }

        private static string Nonce()
        {
            return Guid.NewGuid().ToString();
        }

        /// <summary>Logs a failure once, not on every retry. Cleared when a connection succeeds.</summary>
        private void Problem(string message, bool warning)
        {
            if (message == lastProblem) return;
            lastProblem = message;
            log.Enqueue(new KeyValuePair<bool, string>(warning, message));
        }

        private void Info(string message)
        {
            log.Enqueue(new KeyValuePair<bool, string>(false, message));
        }

        private sealed class DiscordRefused : Exception
        {
            public DiscordRefused(string message) : base(message) { }
        }
    }
}
