using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Kmnk.FaceTrackingGenerator
{
    // Reads VRChat's native face tracking input from one UDP packet: the OSC message /tracking/face/v1 with one blob of
    // 102 bytes, one byte per tracking key in the key order of
    //   https://github.com/vrchat-community/creator-docs/blob/16fb16cfb4541fddcf35884e811ce8db16ff6493/Docs/docs/avatars/face-tracking/osc.md
    // and value = byte / 255. VRCFaceTracking sends it inside an OSC bundle (OscSendService.Send, create_osc_bundle):
    //   https://github.com/benaclejames/VRCFaceTracking/blob/4d8ca2245465e8a07c3e7a6f494db1783616cd3c/VRCFaceTracking.Core/Services/OscSendService.cs
    // OSC 1.0: strings end with NUL and are padded to 4 bytes, sizes are big endian int32, a bundle is "#bundle\0", an
    // 8-byte time tag, then (size, element) pairs, and an element may be a bundle.
    public static class OscFace
    {
        // The Preview's inputs ("key:<key>") from the 102 received values, as the settings editor fills them.
        internal static System.Collections.Generic.Dictionary<string, float> Inputs(float[] values)
        {
            var inputs = new System.Collections.Generic.Dictionary<string, float>();
            var names = System.Linq.Enumerable.ToList(NativeKeys.Names);
            for (int i = 0; i < names.Count && i < values.Length; i++) inputs[InputDef.TrackPrefix + names[i]] = values[i];
            return inputs;
        }

        public const string Address = "/tracking/face/v1";
        public const int Length = 102;

        // Calls onFace with the values of each /tracking/face/v1 message in the packet. Returns the number of things
        // ignored: a packet that cannot be read, or a /tracking/face/v1 message of another type or length. Messages
        // with other addresses are skipped without counting.
        public static int Read(byte[] packet, int count, Action<float[]> onFace)
        {
            try { return ReadElement(packet, 0, count, onFace, 0); }
            catch (FormatException) { return 1; }
        }

        static int ReadElement(byte[] b, int start, int end, Action<float[]> onFace, int depth)
        {
            if (end - start < 4 || depth > 8) throw new FormatException("short");
            if (b[start] == (byte)'#')
            {
                if (end - start < 16 || Encoding.ASCII.GetString(b, start, 8) != "#bundle\0") throw new FormatException("not a bundle");
                var ignored = 0;
                var p = start + 16;
                while (p < end)
                {
                    var size = Int(b, p, end);
                    p += 4;
                    if (size < 0 || p + size > end) throw new FormatException("bundle element size");
                    ignored += ReadElement(b, p, p + size, onFace, depth + 1);
                    p += size;
                }
                return ignored;
            }
            if (b[start] != (byte)'/') throw new FormatException("not a message");
            var q = start;
            var address = Str(b, ref q, end);
            if (address != Address) return 0;
            if (q >= end) return 1;
            var tags = Str(b, ref q, end);
            if (tags != ",b") return 1;
            var len = Int(b, q, end);
            q += 4;
            if (len != Length || q + len > end) return 1;
            var values = new float[Length];
            for (int i = 0; i < Length; i++) values[i] = b[q + i] / 255f;
            onFace(values);
            return 0;
        }

        static string Str(byte[] b, ref int p, int end)
        {
            var z = Array.IndexOf(b, (byte)0, p, end - p);
            if (z < 0) throw new FormatException("string without NUL");
            var s = Encoding.ASCII.GetString(b, p, z - p);
            p += ((z - p) / 4 + 1) * 4;  // past the NUL, padded to 4 from the string's start
            if (p > end) throw new FormatException("string padding");
            return s;
        }

        static int Int(byte[] b, int p, int end)
        {
            if (p + 4 > end) throw new FormatException("short int");
            return (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
        }
    }

    // Receives /tracking/face/v1 on 127.0.0.1:<port> on a background thread and keeps the last values. The settings
    // editor reads them on the editor's update (spec "Live input").
    public sealed class LiveFaceReceiver : IDisposable
    {
        readonly object gate = new object();
        UdpClient client;
        Thread thread;
        float[] last;
        long sequence;
        int ignored;
        readonly Queue<DateTime> recent = new Queue<DateTime>();
        DateTime lastAt;

        public int Port { get; private set; }
        public bool Running { get; private set; }
        public string Error { get; private set; }  // why the port could not be opened, or null

        public bool Start(int port)
        {
            Stop();
            Port = port;
            Error = null;
            lock (gate) { last = null; sequence = 0; ignored = 0; recent.Clear(); lastAt = default; }
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException)
            {
                client = null;
                Error = e.Message;
                return false;
            }
            Running = true;
            var c = client;
            thread = new Thread(() => Loop(c)) { IsBackground = true, Name = "FT Live input" };
            thread.Start();
            return true;
        }

        void Loop(UdpClient c)
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                byte[] packet;
                try { packet = c.Receive(ref from); }
                catch (SocketException) { if (!Running || c != client) return; continue; }  // e.g. ICMP port unreachable
                catch (ObjectDisposedException) { return; }
                var n = OscFace.Read(packet, packet.Length, values =>
                {
                    lock (gate)
                    {
                        last = values;
                        sequence++;
                        lastAt = DateTime.UtcNow;
                        recent.Enqueue(lastAt);
                        while (recent.Count > 0 && (lastAt - recent.Peek()).TotalSeconds > 1) recent.Dequeue();
                    }
                });
                if (n > 0) lock (gate) ignored += n;
            }
        }

        public void Stop()
        {
            Running = false;
            var c = client;
            client = null;
            c?.Close();
            thread = null;
        }

        public void Dispose() => Stop();

        // The last values and their sequence number (0: nothing yet).
        public (float[] values, long sequence) Latest()
        {
            lock (gate) return (last, sequence);
        }

        // Messages received in the last second, seconds since the last one (infinity: none yet), and ignored packets.
        public (int perSecond, double sinceLast, int ignored) Status()
        {
            lock (gate)
            {
                var now = DateTime.UtcNow;
                while (recent.Count > 0 && (now - recent.Peek()).TotalSeconds > 1) recent.Dequeue();
                var since = sequence == 0 ? double.PositiveInfinity : (now - lastAt).TotalSeconds;
                return (recent.Count, since, ignored);
            }
        }
    }
}
