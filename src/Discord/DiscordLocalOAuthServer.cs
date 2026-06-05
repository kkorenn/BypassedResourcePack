using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BypassedResourcePack
{
    internal static class DiscordLocalOAuthServer
    {
        internal const string ClientId = "1512405963490197664";
        private const string RedirectUri = "http://127.0.0.1:5672";
        private const string AuthorizeUrlFixed = "https://discord.com/oauth2/authorize?client_id=1512405963490197664&response_type=token&redirect_uri=http%3A%2F%2F127.0.0.1%3A5672&scope=identify+rpc+rpc.voice.write";
        private const string TokenUrl = "https://discord.com/api/oauth2/token";

        private static readonly object gate = new object();
        private static readonly List<TcpListener> listeners = new List<TcpListener>();
        private static Thread thread;
        private static volatile bool running;
        private static string expectedState = "";
        private static string codeVerifier = "";
        private static Action<string> saveToken;
        private static string status = "oauth off";

        internal static string Status => status;
        internal static bool Running => running;

        internal static bool EnsureStarted(Settings s, Action<string> tokenSaver)
        {
            lock (gate)
            {
                bool sameConfig = running;
                saveToken = tokenSaver;

                if (sameConfig) return true;

                StopLocked();
                return StartLocked();
            }
        }

        internal static void Stop()
        {
            lock (gate)
            {
                StopLocked();
                status = "oauth off";
            }
        }

        internal static void OpenAuthorizeUrl(Settings s)
        {
            string url = AuthorizeUrl(s);
            if (!string.IsNullOrEmpty(url)) OpenUrl(url);
            status = "waiting for discord";
        }

        internal static string AuthorizeUrl(Settings s)
        {
            if (!EnsureStarted(s, DiscordAutoDeafen.SaveAccessToken))
                return "";

            lock (gate)
            {
                expectedState = "";
                codeVerifier = "";
            }

            return AuthorizeUrlFixed;
        }

        private static bool StartLocked()
        {
            try
            {
                Uri uri = new Uri(RedirectUri);
                int port = uri.Port > 0 ? uri.Port : 5672;

                TcpListener v4 = new TcpListener(IPAddress.Loopback, port);
                v4.Start();
                listeners.Add(v4);

                try
                {
                    TcpListener v6 = new TcpListener(IPAddress.IPv6Loopback, port);
                    try { v6.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true); } catch { }
                    v6.Start();
                    listeners.Add(v6);
                }
                catch { }

                running = true;
                thread = new Thread(Run) { IsBackground = true, Name = "BRP-DiscordOAuth" };
                thread.Start();
                status = "oauth listening " + port;
                return true;
            }
            catch (Exception ex)
            {
                StopLocked();
                status = "oauth listen failed: " + ex.Message;
                Log.Info("[discord-oauth] listen failed: " + ex);
                return false;
            }
        }

        private static void StopLocked()
        {
            running = false;
            for (int i = 0; i < listeners.Count; i++)
            {
                try { listeners[i].Stop(); } catch { }
            }
            listeners.Clear();
            thread = null;
        }

        private static void Run()
        {
            while (running)
            {
                try
                {
                    for (int i = 0; i < listeners.Count; i++)
                    {
                        TcpListener listener = listeners[i];
                        if (!listener.Pending()) continue;
                        using (TcpClient client = listener.AcceptTcpClient())
                            Handle(client);
                    }
                }
                catch (Exception ex)
                {
                    if (running)
                    {
                        status = "oauth error: " + ex.Message;
                        Log.Info("[discord-oauth] " + ex);
                    }
                }

                Thread.Sleep(40);
            }
        }

        private static void Handle(TcpClient client)
        {
            IPEndPoint remote = client.Client.RemoteEndPoint as IPEndPoint;
            if (remote != null && !IPAddress.IsLoopback(remote.Address))
            {
                WriteResponse(client, "403 Forbidden", "loopback only");
                return;
            }

            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;

            Request request = ReadRequest(client.GetStream());
            Dictionary<string, string> query = ParseQuery(request.Target);

            string error;
            if (query.TryGetValue("error", out error))
            {
                status = "oauth denied: " + error;
                WriteResponse(client, "200 OK", "Discord authorization was denied: " + WebUtility.HtmlEncode(error));
                return;
            }

            if (request.Method == "POST" && request.Target.StartsWith("/token", StringComparison.Ordinal))
            {
                HandleTokenPost(client, request.Body);
                return;
            }

            string code;
            if (query.TryGetValue("code", out code) && !string.IsNullOrEmpty(code))
            {
                HandleCode(client, code, query);
                return;
            }

            WriteResponse(client, "200 OK", CapturePage());
        }

        private static void HandleCode(TcpClient client, string code, Dictionary<string, string> query)
        {
            string returnedState;
            string stateToCheck;
            string verifier;
            lock (gate)
            {
                stateToCheck = expectedState;
                verifier = codeVerifier;
            }

            if (!string.IsNullOrEmpty(stateToCheck)
                && (!query.TryGetValue("state", out returnedState) || returnedState != stateToCheck))
            {
                status = "oauth state mismatch";
                WriteResponse(client, "400 Bad Request", "OAuth state mismatch. Try authorizing again from the mod settings.");
                return;
            }

            try
            {
                status = "exchanging code";
                string token = ExchangeCode(code, verifier);
                if (string.IsNullOrEmpty(token))
                {
                    status = "token exchange failed";
                    WriteResponse(client, "500 Internal Server Error", "Discord did not return an access token.");
                    return;
                }

                saveToken?.Invoke(token);
                lock (gate)
                {
                    expectedState = "";
                    codeVerifier = "";
                }
                status = "token saved";
                WriteResponse(client, "200 OK", "Discord authorized. You can return to ADOFAI.");
                StopAfterResponse();
            }
            catch (Exception ex)
            {
                status = "token exchange failed";
                Log.Info("[discord-oauth] token exchange failed: " + ex);
                WriteResponse(client, "500 Internal Server Error", "Token exchange failed: " + WebUtility.HtmlEncode(ex.Message));
            }
        }

        private static void HandleTokenPost(TcpClient client, string body)
        {
            try
            {
                JObject jo = JObject.Parse(body ?? "{}");
                string token = jo["access_token"]?.ToString();
                if (string.IsNullOrEmpty(token))
                {
                    status = "missing oauth token";
                    WriteJson(client, "400 Bad Request", "{\"ok\":false,\"error\":\"missing_access_token\"}");
                    return;
                }

                string returnedState = jo["state"]?.ToString();
                string stateToCheck;
                lock (gate) stateToCheck = expectedState;
                if (!string.IsNullOrEmpty(stateToCheck) && returnedState != stateToCheck)
                {
                    status = "oauth state mismatch";
                    WriteJson(client, "400 Bad Request", "{\"ok\":false,\"error\":\"state_mismatch\"}");
                    return;
                }

                saveToken?.Invoke(token);
                lock (gate)
                {
                    expectedState = "";
                    codeVerifier = "";
                }
                status = "token saved";
                WriteJson(client, "200 OK", "{\"ok\":true}");
                StopAfterResponse();
            }
            catch (Exception ex)
            {
                status = "token save failed";
                Log.Info("[discord-oauth] token save failed: " + ex);
                WriteJson(client, "500 Internal Server Error", "{\"ok\":false,\"error\":\"token_save_failed\"}");
            }
        }

        private static string ExchangeCode(string code, string verifier)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(TokenUrl);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";

            string form = "client_id=" + Escape(ClientId) +
                          "&grant_type=authorization_code" +
                          "&code=" + Escape(code) +
                          "&redirect_uri=" + Escape(RedirectUri);
            if (!string.IsNullOrEmpty(verifier))
                form += "&code_verifier=" + Escape(verifier);

            byte[] body = Encoding.UTF8.GetBytes(form);
            req.ContentLength = body.Length;
            using (Stream rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);

            try
            {
                using (WebResponse resp = req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    JObject jo = JObject.Parse(sr.ReadToEnd());
                    return jo["access_token"]?.ToString();
                }
            }
            catch (WebException ex)
            {
                string text = "";
                try
                {
                    using (Stream s = ex.Response?.GetResponseStream())
                    using (StreamReader sr = s != null ? new StreamReader(s) : null)
                        text = sr != null ? sr.ReadToEnd() : "";
                }
                catch { }
                throw new InvalidOperationException(string.IsNullOrEmpty(text) ? ex.Message : text, ex);
            }
        }

        private static Request ReadRequest(Stream stream)
        {
            byte[] buffer = new byte[8192];
            int offset = 0;
            string header = "";
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) break;
                offset += read;
                header = Encoding.UTF8.GetString(buffer, 0, offset);
                int headerEnd = header.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0) continue;

                int contentLength = ContentLength(header);
                int bodyStart = headerEnd + 4;
                int available = offset - bodyStart;
                while (available < contentLength && offset < buffer.Length)
                {
                    read = stream.Read(buffer, offset, Math.Min(buffer.Length - offset, contentLength - available));
                    if (read <= 0) break;
                    offset += read;
                    available += read;
                }

                string body = contentLength > 0 && bodyStart < offset
                    ? Encoding.UTF8.GetString(buffer, bodyStart, Math.Min(contentLength, offset - bodyStart))
                    : "";
                return ParseRequest(header, body);
            }

            return ParseRequest(Encoding.UTF8.GetString(buffer, 0, offset), "");
        }

        private static Request ParseRequest(string header, string body)
        {
            if (string.IsNullOrEmpty(header)) return new Request("GET", "/", body);
            int lineEnd = header.IndexOf("\r\n", StringComparison.Ordinal);
            string line = lineEnd >= 0 ? header.Substring(0, lineEnd) : header;
            string[] parts = line.Split(' ');
            return new Request(parts.Length >= 1 ? parts[0] : "GET", parts.Length >= 2 ? parts[1] : "/", body);
        }

        private static int ContentLength(string header)
        {
            string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                int sep = lines[i].IndexOf(':');
                if (sep < 0) continue;
                string name = lines[i].Substring(0, sep).Trim();
                if (!string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                int value;
                return int.TryParse(lines[i].Substring(sep + 1).Trim(), out value) ? Math.Max(0, value) : 0;
            }
            return 0;
        }

        private static void StopAfterResponse()
        {
            Thread stopper = new Thread(() =>
            {
                Thread.Sleep(250);
                lock (gate)
                {
                    StopLocked();
                    status = "authorized";
                }
            })
            { IsBackground = true, Name = "BRP-DiscordOAuthStop" };
            stopper.Start();
        }

        private static Dictionary<string, string> ParseQuery(string target)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            int q = target.IndexOf('?');
            if (q < 0 || q + 1 >= target.Length) return result;

            string query = target.Substring(q + 1);
            string[] pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                if (string.IsNullOrEmpty(pairs[i])) continue;
                int eq = pairs[i].IndexOf('=');
                string key = eq >= 0 ? pairs[i].Substring(0, eq) : pairs[i];
                string val = eq >= 0 ? pairs[i].Substring(eq + 1) : "";
                result[WebUtility.UrlDecode(key)] = WebUtility.UrlDecode(val);
            }
            return result;
        }

        private static void WriteResponse(TcpClient client, string statusLine, string body)
        {
            string html = body.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase) >= 0
                ? body
                : "<!doctype html><meta charset=\"utf-8\"><title>ADOFAI Discord OAuth</title>" +
                  "<body style=\"font:16px sans-serif;margin:32px\">" + body + "</body>";
            byte[] bytes = Encoding.UTF8.GetBytes(html);
            string header = "HTTP/1.1 " + statusLine + "\r\n" +
                            "Content-Type: text/html; charset=utf-8\r\n" +
                            "Content-Length: " + bytes.Length + "\r\n" +
                            "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            Stream stream = client.GetStream();
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static void WriteJson(TcpClient client, string statusLine, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            string header = "HTTP/1.1 " + statusLine + "\r\n" +
                            "Content-Type: application/json; charset=utf-8\r\n" +
                            "Content-Length: " + bytes.Length + "\r\n" +
                            "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            Stream stream = client.GetStream();
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string CapturePage()
        {
            return @"<!doctype html>
<meta charset=""utf-8"">
<title>ADOFAI Discord OAuth</title>
<body style=""font:16px sans-serif;margin:32px"">
<div id=""msg"">Connecting Discord to ADOFAI...</div>
<script>
(async function () {
  const msg = document.getElementById('msg');
  const params = new URLSearchParams(location.hash.replace(/^#/, ''));
  const accessToken = params.get('access_token');
  const state = params.get('state');
  if (!accessToken) {
    msg.textContent = 'ADOFAI Discord OAuth server is running. Open authorization from the mod settings.';
    return;
  }
  const res = await fetch('/token', {
    method: 'POST',
    headers: {'Content-Type': 'application/json'},
    body: JSON.stringify({access_token: accessToken, state: state})
  });
  msg.textContent = res.ok
    ? 'Discord authorized. You can return to ADOFAI.'
    : 'ADOFAI rejected the Discord token. Try authorizing again from the mod settings.';
  history.replaceState(null, '', location.pathname);
})().catch(function () {
  document.getElementById('msg').textContent = 'Could not pass the token to ADOFAI.';
});
</script>
</body>";
        }

        private static string Escape(string value) => Uri.EscapeDataString(value ?? "");

        private static string CreateCodeVerifier()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        private static string CreateCodeChallenge(string verifier)
        {
            using (SHA256 sha = SHA256.Create())
                return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static void OpenUrl(string url)
        {
            try { Application.OpenURL(url); } catch { }

            try
            {
                if (Environment.OSVersion.Platform == PlatformID.MacOSX)
                    Process.Start("open", url);
                else if (Environment.OSVersion.Platform == PlatformID.Unix || (int)Environment.OSVersion.Platform == 6)
                    Process.Start("xdg-open", url);
                else
                    Process.Start(url);
            }
            catch { }
        }

        private sealed class Request
        {
            internal readonly string Method;
            internal readonly string Target;
            internal readonly string Body;

            internal Request(string method, string target, string body)
            {
                Method = method ?? "GET";
                Target = target ?? "/";
                Body = body ?? "";
            }
        }
    }
}
