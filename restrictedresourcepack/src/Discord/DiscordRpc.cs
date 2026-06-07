using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RestrictedResourcePack
{
    // Minimal Discord desktop IPC client for auto-deafen.
    //
    // Flow: local OAuth callback server stores a user access token, then this IPC client
    // connects to Discord desktop -> handshakes with client_id -> AUTHENTICATEs
    // -> SET_VOICE_SETTINGS { deaf }.
    //
    // All socket/HTTP work runs on a background thread; the game only flips a desired-deaf flag.
    internal sealed class DiscordRpc
    {
        private readonly string clientId;
        private string accessToken;

        private Thread thread;
        private volatile bool running;
        private volatile bool desiredDeaf;
        private volatile bool ready;
        private volatile string status = "idle";
        private Stream stream;
        private readonly object ioLock = new object();

        internal string Status => status;
        internal bool Ready => ready;

        internal DiscordRpc(string clientId, string accessToken)
        {
            this.clientId = clientId;
            this.accessToken = accessToken;
        }

        internal void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "BRP-DiscordRpc" };
            thread.Start();
        }

        internal void SetDeaf(bool deaf) => desiredDeaf = deaf;

        internal void Stop()
        {
            running = false;
            try { if (ready) ApplyDeaf(false); } catch { }   // never leave the user deafened
            try { stream?.Dispose(); } catch { }
            stream = null;
            ready = false;
        }

        private void Run()
        {
            try
            {
                status = "connecting";
                stream = Connect();
                if (stream == null) { status = "discord not found"; running = false; return; }

                Handshake();

                if (!TryAuthenticate())
                {
                    status = "authenticate failed";
                    running = false;
                    return;
                }

                ready = true;
                status = "ready";

                bool current = false;
                while (running)
                {
                    if (desiredDeaf != current)
                    {
                        ApplyDeaf(desiredDeaf);
                        current = desiredDeaf;
                    }
                    Thread.Sleep(120);
                }
            }
            catch (Exception ex)
            {
                status = "error: " + ex.Message;
                Log.Info("[discord] " + ex);
            }
            finally
            {
                try { stream?.Dispose(); } catch { }
                stream = null;
                ready = false;
            }
        }

        // =====================================================================
        // Connection (Windows named pipe / Unix domain socket)
        // =====================================================================

        private Stream Connect()
        {
            bool unix = Environment.OSVersion.Platform == PlatformID.Unix
                     || Environment.OSVersion.Platform == PlatformID.MacOSX
                     || (int)Environment.OSVersion.Platform == 6;

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (unix)
                    {
                        string dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
                                  ?? Environment.GetEnvironmentVariable("TMPDIR")
                                  ?? Environment.GetEnvironmentVariable("TMP")
                                  ?? "/tmp";
                        string path = Path.Combine(dir.TrimEnd('/'), "discord-ipc-" + i);
                        if (!File.Exists(path)) continue;
                        Socket sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                        sock.Connect(new UnixDomainSocketEndPoint(path));
                        return new NetworkStream(sock, true);
                    }

                    NamedPipeClientStream pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut);
                    pipe.Connect(2000);
                    return pipe;
                }
                catch { }
            }
            return null;
        }

        // =====================================================================
        // Framing: [int32 op][int32 length][utf8 json]
        // =====================================================================

        private void WriteFrame(int op, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] header = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(op), 0, header, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, header, 4, 4);
            lock (ioLock)
            {
                stream.Write(header, 0, 8);
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }
        }

        private JObject ReadFrame(out int op)
        {
            byte[] header = ReadExact(8);
            op = BitConverter.ToInt32(header, 0);
            int len = BitConverter.ToInt32(header, 4);
            byte[] payload = len > 0 ? ReadExact(len) : new byte[0];
            string json = Encoding.UTF8.GetString(payload);
            return string.IsNullOrEmpty(json) ? new JObject() : JObject.Parse(json);
        }

        private byte[] ReadExact(int n)
        {
            byte[] buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r = stream.Read(buf, off, n - off);
                if (r <= 0) throw new IOException("ipc closed");
                off += r;
            }
            return buf;
        }

        // =====================================================================
        // Protocol
        // =====================================================================

        private void Handshake()
        {
            WriteFrame(0, JsonConvert.SerializeObject(new { v = 1, client_id = clientId }));
            int op;
            ReadFrame(out op); // READY dispatch
        }

        private JObject Command(string cmd, object args)
        {
            string nonce = Guid.NewGuid().ToString();
            WriteFrame(1, JsonConvert.SerializeObject(new { cmd = cmd, args = args, nonce = nonce }));
            while (true)
            {
                int op;
                JObject msg = ReadFrame(out op);
                if (op == 3) { WriteFrame(4, msg.ToString(Formatting.None)); continue; } // ping -> pong
                if (msg.Value<string>("nonce") == nonce) return msg;
            }
        }

        private bool TryAuthenticate()
        {
            if (string.IsNullOrEmpty(accessToken)) return false;
            try
            {
                JObject r = Command("AUTHENTICATE", new { access_token = accessToken });
                JToken data = r["data"];
                if (data == null || data["user"] == null) return false;
                return true;
            }
            catch { return false; }
        }

        private void ApplyDeaf(bool deaf)
        {
            Command("SET_VOICE_SETTINGS", new { deaf = deaf });
        }
    }
}
