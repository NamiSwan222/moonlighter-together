using System;
using System.Text;
using UnityEngine;

namespace MoonlighterTogether.Net
{
    /// Growable byte buffer for writing messages. Little-endian, no allocations per value.
    public sealed class W
    {
        public byte[] Data = new byte[256];
        public int Len;

        public W Reset() { Len = 0; return this; }

        private void Need(int n)
        {
            if (Len + n <= Data.Length) return;
            int size = Data.Length * 2;
            while (size < Len + n) size *= 2;
            Array.Resize(ref Data, size);
        }

        public W U8(byte v) { Need(1); Data[Len++] = v; return this; }
        public W Bool(bool v) => U8(v ? (byte)1 : (byte)0);
        public W U16(ushort v) { Need(2); Data[Len++] = (byte)v; Data[Len++] = (byte)(v >> 8); return this; }
        public W I16(short v) => U16((ushort)v);
        public W I32(int v) { Need(4); Data[Len++] = (byte)v; Data[Len++] = (byte)(v >> 8); Data[Len++] = (byte)(v >> 16); Data[Len++] = (byte)(v >> 24); return this; }
        public W U32(uint v) => I32((int)v);
        public W I64(long v) { I32((int)v); return I32((int)(v >> 32)); }
        public unsafe W F32(float v) => I32(*(int*)&v);
        public unsafe W F64(double v) => I64(*(long*)&v);
        public W V2(Vector2 v) { F32(v.x); return F32(v.y); }
        public W V2(Vector3 v) { F32(v.x); return F32(v.y); }

        public W Str(string s)
        {
            if (s == null) return U16(0xFFFF);
            int n = Encoding.UTF8.GetByteCount(s);
            if (n >= 0xFFFF) throw new ArgumentException("string too long for the wire");
            U16((ushort)n); Need(n);
            Encoding.UTF8.GetBytes(s, 0, s.Length, Data, Len); Len += n;
            return this;
        }

        public W Bytes(byte[] b) => Bytes(b, 0, b?.Length ?? 0);
        public W Bytes(byte[] b, int off, int n)
        {
            I32(n); Need(n);
            if (n > 0) Buffer.BlockCopy(b, off, Data, Len, n);
            Len += n;
            return this;
        }

        public byte[] ToArray() { var a = new byte[Len]; Buffer.BlockCopy(Data, 0, a, 0, Len); return a; }
    }

    /// Reader over a received message. Any read past the end throws WireException, which the receive loop
    /// turns into "drop this message" (a peer can never crash us with a short or garbled packet).
    public sealed class R
    {
        public byte[] Data;
        public int Pos, End;

        public R() { }
        public R(byte[] data) { Set(data, 0, data.Length); }
        public R Set(byte[] data, int off, int len) { Data = data; Pos = off; End = off + len; return this; }
        public int Left => End - Pos;

        private void Need(int n) { if (n < 0 || Pos + n > End) throw new WireException("message too short"); }

        public byte U8() { Need(1); return Data[Pos++]; }
        public bool Bool() => U8() != 0;
        public ushort U16() { Need(2); ushort v = (ushort)(Data[Pos] | Data[Pos + 1] << 8); Pos += 2; return v; }
        public short I16() => (short)U16();
        public int I32() { Need(4); int v = Data[Pos] | Data[Pos + 1] << 8 | Data[Pos + 2] << 16 | Data[Pos + 3] << 24; Pos += 4; return v; }
        public uint U32() => (uint)I32();
        public long I64() { uint lo = U32(); uint hi = U32(); return (long)((ulong)hi << 32 | lo); }
        public unsafe float F32() { int v = I32(); float f = *(float*)&v; if (float.IsNaN(f) || float.IsInfinity(f)) throw new WireException("bad float"); return f; }
        public unsafe double F64() { long v = I64(); double d = *(double*)&v; if (double.IsNaN(d) || double.IsInfinity(d)) throw new WireException("bad double"); return d; }
        public Vector2 V2() { float x = F32(); float y = F32(); return new Vector2(x, y); }

        public string Str()
        {
            int n = U16();
            if (n == 0xFFFF) return null;
            Need(n);
            string s = Encoding.UTF8.GetString(Data, Pos, n); Pos += n;
            return s;
        }

        public byte[] Bytes(int max = 16 * 1024 * 1024)
        {
            int n = I32();
            if (n < 0 || n > max) throw new WireException("byte block too large: " + n);
            Need(n);
            var b = new byte[n];
            Buffer.BlockCopy(Data, Pos, b, 0, n); Pos += n;
            return b;
        }

        /// A count that is about to drive a loop: refuse nonsense before allocating anything.
        public int Count(int max) { int n = I32(); if (n < 0 || n > max) throw new WireException("count out of range: " + n); return n; }
    }

    public sealed class WireException : Exception
    {
        public WireException(string m) : base(m) { }
    }
}
