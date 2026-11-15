using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MoonlighterTogether.Testing
{
    /// Test control port ("--mt-bridge PORT"), bound to 127.0.0.1 only and off unless asked for.
    /// One JSON object per line in, one per line out:
    ///   {"id":7,"cmd":"state"}            ->  {"id":7,"ok":true,"result":{...}}
    ///   {"id":8,"cmd":"nope"}             ->  {"id":8,"ok":false,"error":"unknown command nope"}
    /// Commands run on Unity's main thread. A command may return an IEnumerator: it then runs as a
    /// coroutine and its last yielded non-instruction value (or Result.Value) is the answer.
    public static class Bridge
    {
        public delegate object Command(JObject args);

        private static readonly Dictionary<string, Command> Commands = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentQueue<Request> Queue = new ConcurrentQueue<Request>();
        private static TcpListener _listener;
        public static bool Running => _listener != null;

        private class Request
        {
            public JObject Body;
            public Action<string> Reply;
        }

        /// A coroutine command sets this to hand back its answer.
        public class Result { public object Value; }

        public static void Register(string name, Command c) => Commands[name] = c;

        public static void Start(int port)
        {
            if (_listener != null) return;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            new Thread(AcceptLoop) { IsBackground = true, Name = "mt-bridge" }.Start();
            BridgeCommands.RegisterAll();
            Plugin.Log.LogInfo($"[bridge] test control on 127.0.0.1:{port}");
        }

        private static void AcceptLoop()
        {
            while (true)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); } catch { return; }
                new Thread(() => Serve(c)) { IsBackground = true, Name = "mt-bridge-conn" }.Start();
            }
        }

        private static void Serve(TcpClient c)
        {
            try
            {
                using (c)
                {
                    c.NoDelay = true;
                    var s = c.GetStream();
                    var rd = new StreamReader(s, new UTF8Encoding(false));
                    var wr = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    var wlock = new object();
                    string line;
                    while ((line = rd.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        // A web page can reach 127.0.0.1 too: anything that looks like HTTP closes the connection.
                        if (line.StartsWith("GET ") || line.StartsWith("POST ") || line.StartsWith("OPTIONS ") || line.StartsWith("PUT ") || line.Contains("HTTP/1.")) return;
                        JObject body;
                        try { body = JObject.Parse(line); }
                        catch (Exception e) { lock (wlock) wr.WriteLine("{\"ok\":false,\"error\":" + JsonConvert.ToString("bad json: " + e.Message) + "}"); continue; }
                        Queue.Enqueue(new Request { Body = body, Reply = text => { try { lock (wlock) wr.WriteLine(text); } catch { } } });
                    }
                }
            }
            catch { }
        }

        /// Main thread, once per frame.
        public static void Pump()
        {
            if (_listener == null) return;
            int budget = 64;
            while (budget-- > 0 && Queue.TryDequeue(out var req)) Run(req);
        }

        private static void Run(Request req)
        {
            JToken id = req.Body["id"];
            string cmd = (string)req.Body["cmd"] ?? "";
            if (!Commands.TryGetValue(cmd, out var handler)) { Fail(req, id, "unknown command " + cmd); return; }
            object result;
            try { result = handler(req.Body); }
            catch (Exception e) { Fail(req, id, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); return; }
            if (result is IEnumerator routine) Plugin.Instance.StartCoroutine(RunRoutine(req, id, routine));
            else Ok(req, id, result);
        }

        private static IEnumerator RunRoutine(Request req, JToken id, IEnumerator routine)
        {
            object last = null;
            while (true)
            {
                bool more;
                try { more = routine.MoveNext(); }
                catch (Exception e) { Fail(req, id, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); yield break; }
                if (!more) break;
                object cur = routine.Current;
                if (cur is Result r) { last = r.Value; continue; }
                yield return cur;
            }
            Ok(req, id, last);
        }

        private static void Ok(Request req, JToken id, object result)
        {
            var o = new JObject { ["id"] = id, ["ok"] = true, ["result"] = result == null ? JValue.CreateNull() : JToken.FromObject(result) };
            req.Reply(o.ToString(Formatting.None));
        }

        private static void Fail(Request req, JToken id, string error)
        {
            var o = new JObject { ["id"] = id, ["ok"] = false, ["error"] = error };
            req.Reply(o.ToString(Formatting.None));
        }
    }
}
