using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Checks the Live input of the settings editor's Preview (LiveInput.cs) without drawing the window:
        //   1. OscFace on packets made here: a bundle as VRCFaceTracking sends, a plain message, a nested bundle, a bundle
        //      with other messages, a 101-byte blob and broken packets  -> "[FTG] live: parse cases=<n> failed=<n>"
        //   2. -ftKeys <file> ("<index> <name>" per line, the key order of VRChat's docs) against NativeKeys
        //      -> "[FTG] live: keys=<n> mismatched=<n>"
        //   3. -ftPort <port>: receives with LiveFaceReceiver until the values of -ftExpect "Key=value,..." arrive
        //      (-ftSeconds, default 20), and prints the face FaceEval computes for -ftTable from them
        //      -> "[FTG] live: received messages=<n> ... failed=<n>"
        //   4. with -ftPort: the port held by another socket is refused, and it opens again after Stop
        //      -> "[FTG] live: busy-port refused=<bool> reopen=<bool>"
        // Exit 0 when every check passes, 1 when one fails.
        public static void Live() => Cli.Run(() =>
        {
            var failed = 0;

            // 1. parsing
            var cases = 0;
            var bad = new List<string>();
            void Case(string name, byte[] packet, int faces, int ignored, float? jaw = null)
            {
                cases++;
                var got = new List<float[]>();
                var n = OscFace.Read(packet, packet.Length, got.Add);
                var ok = got.Count == faces && n == ignored && (jaw == null || (got.Count > 0 && Mathf.Abs(got[0][NativeKeys.Index("JawOpen")] - jaw.Value) < 1e-6f));
                if (!ok) bad.Add($"{name}: faces={got.Count} ignored={n}");
                Debug.Log($"[FTG] live: parse {name}: faces={got.Count} ignored={n} {(ok ? "ok" : "FAILED")}");
            }
            var blob = new byte[OscFace.Length];
            blob[NativeKeys.Index("JawOpen")] = 255;
            blob[NativeKeys.Index("EyeClosedLeft")] = 128;
            var face = Message(OscFace.Address, ",b", Blob(blob));
            var other = Message("/avatar/parameters/FT/v2/JawOpen", ",f", BigEndian(BitConverter.GetBytes(0.5f)));
            Case("bundle", Bundle(face), 1, 0, 1f);
            Case("plain", face, 1, 0, 1f);
            Case("nested", Bundle(Bundle(face), other), 1, 0, 1f);
            Case("mixed", Bundle(other, face, other), 1, 0, 1f);
            Case("other-only", Bundle(other), 0, 0);
            Case("length-101", Bundle(Message(OscFace.Address, ",b", Blob(new byte[101]))), 0, 1);
            Case("type-f", Message(OscFace.Address, ",f", new byte[4]), 0, 1);
            Case("garbage", Encoding.ASCII.GetBytes("hello world!"), 0, 1);
            Case("bundle-bad-size", Bundle(face).Take(30).ToArray(), 0, 1);
            var parseFailed = bad.Count;
            Debug.Log($"[FTG] live: parse cases={cases} failed={parseFailed}");
            failed += parseFailed;

            // 2. key order
            var keysPath = CliArgs.Get("-ftKeys");
            if (keysPath != null)
            {
                var docs = File.ReadAllLines(keysPath).Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' ')).ToList();
                var ours = NativeKeys.Names.ToList();
                var mismatched = 0;
                for (int i = 0; i < Math.Max(docs.Count, ours.Count); i++)
                {
                    var d = i < docs.Count ? docs[i][1] : "(none)";
                    var o = i < ours.Count ? ours[i] : "(none)";
                    var di = i < docs.Count ? int.Parse(docs[i][0], CultureInfo.InvariantCulture) : -1;
                    if (d != o || di != i) { mismatched++; Debug.Log($"[FTG] live: key {i}: docs={d} ours={o}"); }
                }
                Debug.Log($"[FTG] live: keys={ours.Count} docs={docs.Count} mismatched={mismatched}");
                if (mismatched > 0 || ours.Count != OscFace.Length) failed++;
            }

            // 3. and 4. receiving
            var portText = CliArgs.Get("-ftPort");
            if (portText != null)
            {
                var port = int.Parse(portText, CultureInfo.InvariantCulture);
                var expect = Pairs(CliArgs.Get("-ftExpect") ?? "");
                var seconds = CliArgs.GetFloat("-ftSeconds", 20);
                using (var receiver = new LiveFaceReceiver())
                {
                    if (!receiver.Start(port)) { Debug.Log($"[FTG] live: could not open port {port}: {receiver.Error}"); return 1; }
                    var until = DateTime.UtcNow.AddSeconds(seconds);
                    float[] values = null;
                    while (DateTime.UtcNow < until)
                    {
                        var (v, seq) = receiver.Latest();
                        if (seq > 0 && expect.All(kv => Near(v[NativeKeys.Index(kv.Key)], kv.Value))) { values = v; break; }
                        Thread.Sleep(50);
                    }
                    Thread.Sleep(1100);  // let a second of messages arrive for the rate
                    var (perSecond, since, ignored) = receiver.Status();
                    var (last, count) = receiver.Latest();
                    values = values ?? last;
                    var wrong = expect.Where(kv => values == null || !Near(values[NativeKeys.Index(kv.Key)], kv.Value)).Select(kv => kv.Key).ToList();
                    var got = values == null ? "none" : string.Join(",", expect.Keys.Select(k => $"{k}={values[NativeKeys.Index(k)].ToString("0.###", CultureInfo.InvariantCulture)}"));
                    Debug.Log($"[FTG] live: received messages={count} per-second={perSecond} ignored={ignored} values={got} failed={wrong.Count + (values == null ? 1 : 0)}");
                    if (values == null || wrong.Count > 0) failed++;

                    var tablePath = CliArgs.Get("-ftTable");
                    if (tablePath != null && values != null)
                    {
                        var model = TableLoader.Load(tablePath);
                        var inputs = OscFace.Inputs(values);
                        var states = new SwitchTracker().Update(model, inputs);
                        var weights = FaceEval.Evaluate(model, inputs, states);
                        Debug.Log("[FTG] live: states " + string.Join(",", states.Select(kv => kv.Key + "=" + kv.Value)));
                        Debug.Log("[FTG] live: weights " + string.Join(",", weights.Where(kv => kv.Value > 0).Select(kv => kv.Key + "=" + kv.Value.ToString("0.#", CultureInfo.InvariantCulture))));
                    }
                    receiver.Stop();
                }

                // 4. a port another socket holds, then open again after Stop
                bool refused, reopen;
                using (var holder = new UdpClient(new IPEndPoint(IPAddress.Loopback, port)))
                using (var r = new LiveFaceReceiver())
                {
                    refused = !r.Start(port) && r.Error != null;
                    Debug.Log($"[FTG] live: busy-port error={r.Error}");
                }
                using (var r = new LiveFaceReceiver())
                {
                    r.Start(port);
                    r.Stop();
                    using (var again = new UdpClient(new IPEndPoint(IPAddress.Loopback, port))) reopen = true;
                }
                Debug.Log($"[FTG] live: busy-port refused={refused.ToString().ToLower()} reopen={reopen.ToString().ToLower()}");
                if (!refused || !reopen) failed++;
            }
            Debug.Log($"[FTG] live: failed={failed}");
            return failed == 0 ? 0 : 1;
        });

        // A received value against an expected one: the sender sends round(v x 255) / 255.
        static bool Near(float got, float expected) => Math.Abs(got - (float)Math.Round(expected * 255) / 255) < 1e-4f;

        static Dictionary<string, float> Pairs(string text) => text.Split(',').Where(s => s.Contains("="))
            .Select(s => s.Split('=')).ToDictionary(p => p[0].Trim(), p => float.Parse(p[1], CultureInfo.InvariantCulture));

        static byte[] OscString(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            var n = (b.Length / 4 + 1) * 4;
            var r = new byte[n];
            Array.Copy(b, r, b.Length);
            return r;
        }

        static byte[] BigEndian(byte[] b) { if (BitConverter.IsLittleEndian) Array.Reverse(b); return b; }
        static byte[] Int32(int v) => BigEndian(BitConverter.GetBytes(v));

        static byte[] Blob(byte[] data)
        {
            var padded = new byte[(data.Length + 3) / 4 * 4];
            Array.Copy(data, padded, data.Length);
            return Int32(data.Length).Concat(padded).ToArray();
        }

        static byte[] Message(string address, string tags, byte[] args) => OscString(address).Concat(OscString(tags)).Concat(args).ToArray();

        static byte[] Bundle(params byte[][] elements)
        {
            var r = new List<byte>(OscString("#bundle"));
            r.AddRange(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });  // time tag "immediately"
            foreach (var e in elements) { r.AddRange(Int32(e.Length)); r.AddRange(e); }
            return r.ToArray();
        }
    }
}
