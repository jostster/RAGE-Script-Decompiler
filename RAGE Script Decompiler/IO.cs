using System.IO;
using System.Text;

namespace RageDecompiler
{
    public class Reader : BinaryReader
    {
        public Reader(Stream stream) : base(stream)
        {
        }

        public void Advance(int size = 4)
        {
            base.BaseStream.Position += size;
        }

        public uint CReadUInt32()
        {
            return Program.SwapEndian ? SReadUInt32() : ReadUInt32();
        }

        public int CReadInt32()
        {
            return Program.SwapEndian ? SReadInt32() : ReadInt32();
        }

        public uint SReadUInt32()
        {
            return Utils.SwapEndian(ReadUInt32());
        }

        public int SReadInt32()
        {
            return Utils.SwapEndian(ReadInt32());
        }

        public ulong CReadUInt64()
        {
            return Program.SwapEndian ? SReadUInt64() : ReadUInt64();
        }

        public long CReadInt64()
        {
            return Program.SwapEndian ? SReadInt64() : ReadInt64();
        }

        public ulong SReadUInt64()
        {
            return Utils.SwapEndian(ReadUInt64());
        }

        public long SReadInt64()
        {
            return Utils.SwapEndian(ReadInt64());
        }

        public ushort CReadUInt16()
        {
            return Program.SwapEndian ? SReadUInt16() : ReadUInt16();
        }

        public short CReadInt16()
        {
            return Program.SwapEndian ? SReadInt16() : ReadInt16();
        }

        public ushort SReadUInt16()
        {
            return Utils.SwapEndian(ReadUInt16());
        }

        public short SReadInt16()
        {
            return Utils.SwapEndian(ReadInt16());
        }

        public int CReadPointer()
        {
            return Program.SwapEndian ? SReadPointer() : ReadPointer();
        }

        public int ReadPointer()
        {
            return ReadInt32() & 0xFFFFFF;
        }

        public int SReadPointer()
        {
            return SReadInt32() & 0xFFFFFF;
        }

        public override string ReadString()
        {
            var temp = "";
            var next = ReadByte();
            while (next != 0)
            {
                temp += (char) next;
                next = ReadByte();
            }

            return temp;
        }
    }

    public class Writer : BinaryWriter
    {
        public Writer(Stream stream) : base(stream)
        {
        }

        public void SWrite(ushort num)
        {
            Write(Utils.SwapEndian(num));
        }

        public void SWrite(uint num)
        {
            Write(Utils.SwapEndian(num));
        }

        public void SWrite(ulong num)
        {
            Write(Utils.SwapEndian(num));
        }

        public void SWrite(short num)
        {
            Write(Utils.SwapEndian(num));
        }

        public void SWrite(int num)
        {
            Write(Utils.SwapEndian(num));
        }

        public void SWrite(long num)
        {
            Write(Utils.SwapEndian(num));
        }

        public void WritePointer(int pointer)
        {
            if (pointer == 0)
            {
                Write(0);
                return;
            }

            Write((pointer & 0xFFFFFF) | 0x50000000);
        }

        public void SWritePointer(int pointer)
        {
            if (pointer == 0)
            {
                Write(0);
                return;
            }

            Write(Utils.SwapEndian((pointer & 0xFFFFFF) | 0x50000000));
        }

        public override void Write(string str)
        {
            Write(Encoding.ASCII.GetBytes(str + "\0"));
        }
    }
}
