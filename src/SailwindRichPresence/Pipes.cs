using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace SailwindRichPresence
{
    /// <summary>
    /// A connected pipe to the Discord client, used only from the pipe thread. This game's Mono implements
    /// NamedPipeClientStream on Windows (CreateFile with the handle wrapped in a FileStream), but its
    /// ConnectAsync throws and reads cannot time out, so reads only happen once PeekNamedPipe says the
    /// bytes are there.
    /// </summary>
    internal sealed class DiscordPipe : IDisposable
    {
        private readonly NamedPipeClientStream stream;

        private DiscordPipe(NamedPipeClientStream stream)
        {
            this.stream = stream;
        }

        public static DiscordPipe Open(string name)
        {
            var stream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
            try
            {
                stream.Connect(500);
                return new DiscordPipe(stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>Bytes waiting to be read. Never blocks. Throws once the pipe is broken.</summary>
        public int Available()
        {
            return Kernel32.Available(stream.SafePipeHandle.DangerousGetHandle());
        }

        /// <summary>Fills the buffer completely, blocking until the bytes arrive.</summary>
        public void ReadExactly(byte[] buffer)
        {
            int done = 0;
            while (done < buffer.Length)
            {
                int read = stream.Read(buffer, done, buffer.Length - done);
                if (read <= 0) throw new IOException("The pipe returned no data.");
                done += read;
            }
        }

        public void Write(byte[] buffer)
        {
            stream.Write(buffer, 0, buffer.Length);
            stream.Flush();
        }

        public void Dispose()
        {
            try { stream.Dispose(); } catch { }
        }
    }

    internal static class Kernel32
    {
        private const int ErrorFileNotFound = 2;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PeekNamedPipe(IntPtr pipe, IntPtr buffer, uint size, IntPtr read,
            out uint available, IntPtr leftThisMessage);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WaitNamedPipeW(string name, uint timeout);

        public static int Available(IntPtr pipe)
        {
            if (!PeekNamedPipe(pipe, IntPtr.Zero, 0, IntPtr.Zero, out uint available, IntPtr.Zero))
                throw new IOException("The pipe is closed (error " + Marshal.GetLastWin32Error() + ").");
            return (int)Math.Min(available, int.MaxValue);
        }

        /// <summary>
        /// Whether Discord has this pipe open. Checked before opening it, so a missing pipe is skipped
        /// quietly instead of being logged as a failure.
        /// </summary>
        public static bool PipeExists(string name)
        {
            if (WaitNamedPipeW(@"\\.\pipe\" + name, 1)) return true;
            // Any other error, such as a timeout because every instance is busy, still means it exists.
            return Marshal.GetLastWin32Error() != ErrorFileNotFound;
        }
    }
}
