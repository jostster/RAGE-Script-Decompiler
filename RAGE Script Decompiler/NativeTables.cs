using System;
using System.Collections.Generic;
using System.IO;

namespace RageDecompiler
{
    public class X64NativeTable
    {
        private readonly List<ulong> _nativehash = new List<ulong>();
        private readonly List<string> _natives = new List<string>();

        public X64NativeTable(Stream scriptFile, int position, int length, int codeSize)
        {
            scriptFile.Position = position;

            Stream stream;
            if (Program.RdrNativeCipher)
            {
                stream = new MemoryStream();
                var carry = (byte) codeSize;
                for (var i = 0; i < length * 8; ++i)
                {
                    int b;
                    if ((b = scriptFile.ReadByte()) == -1)
                        throw new EndOfStreamException("Invalid ScriptFile!");

                    var xordeciphed = (byte) (carry ^ (byte) b);
                    carry = (byte) b;
                    stream.WriteByte(xordeciphed);
                }

                stream.Position = 0;
            }
            else
            {
                stream = scriptFile;
            }

            var reader = new Reader(stream);
            var count = 0;
            ulong nat;
            while (count < length)
            {
                //GTA V PC natives arent stored sequentially in the table. Each native needs a bitwise rotate depending on its position and codetable size
                //Then the natives needs to go back through translation tables to get to their hash as defined in the vanilla game version
                //or the earliest game version that native was introduced in.
                //Just some of the steps Rockstar take to make reverse engineering harder
                nat = Program.IsBit32
                    ? reader.CReadUInt32()
                    : Utils.RotateLeft(reader.ReadUInt64(), (codeSize + count) & 0x3F);

                _nativehash.Add(nat);
                if (Program.X64Npi.ContainsKey(nat))
                    _natives.Add(Program.X64Npi[nat].Display);
                else
                    _natives.Add(Program.NativeName(Native.UnkPrefix) + Native.CreateNativeHash(nat));
                count++;
            }
        }

        public string[] GetNativeTable()
        {
            var table = new List<string>();
            var i = 0;
            foreach (var native in _natives) table.Add(i++.ToString("X2") + ": " + native);
            return table.ToArray();
        }

        public string[] GetNativeHeader()
        {
            var nativesHeader = new List<string>();
            foreach (var hash in _nativehash) nativesHeader.Add(Program.X64Npi.GetNativeInfo(hash));

            return nativesHeader.ToArray();
        }

        public string GetNativeFromIndex(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException("Index must be a positive integer");
            if (index >= _natives.Count)
                throw new ArgumentOutOfRangeException("Index is greater than native table size");
            return _natives[index];
        }

        public ulong GetNativeHashFromIndex(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException("Index must be a positive integer");
            if (index >= _nativehash.Count)
                throw new ArgumentOutOfRangeException("Index is greater than native table size");
            return _nativehash[index];
        }

        public void Dispose()
        {
            _natives.Clear();
        }
    }
}
