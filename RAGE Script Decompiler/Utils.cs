using System;
using System.Globalization;
using System.IO;

namespace RageDecompiler
{
    internal static class Utils
    {
        public static uint GetJoaat(string str)
        {
            uint hash, i;
            var key = str.ToLower().ToCharArray();
            for (hash = i = 0; i < key.Length; i++)
            {
                hash += key[i];
                hash += (hash << 10);
                hash ^= (hash >> 6);
            }

            hash += (hash << 3);
            hash ^= (hash >> 11);
            hash += (hash << 15);
            return hash;
        }

        public static string Represent(long value, Stack.DataType type)
        {
            switch (type)
            {
                case Stack.DataType.Float:
                    return BitConverter.ToSingle(BitConverter.GetBytes(value), 0).ToString() + "f";
                case Stack.DataType.Bool:
                    return value == 0 ? "false" : "true"; // still need to fix bools
                case Stack.DataType.FloatPtr:
                case Stack.DataType.IntPtr:
                case Stack.DataType.StringPtr:
                case Stack.DataType.UnkPtr:
                    return "NULL";
            }

            if (value > int.MaxValue && value <= uint.MaxValue)
                return ((int) ((uint) value)).ToString();
            return value.ToString();
        }

        public static string FormatHexHash(uint hash)
        {
            return $"0x{hash:X8}";
        }

        public static float SwapEndian(float num)
        {
            var data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToSingle(data, 0);
        }

        public static uint SwapEndian(uint num)
        {
            var data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToUInt32(data, 0);
        }

        public static int SwapEndian(int num)
        {
            var data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToInt32(data, 0);
        }

        public static ulong SwapEndian(ulong num)
        {
            var data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToUInt64(data, 0);
        }

        public static long SwapEndian(long num)
        {
            var data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToInt64(data, 0);
        }

        public static ushort SwapEndian(ushort num)
        {
            var data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToUInt16(data, 0);
        }

        public static short SwapEndian(short num)
        {
            var data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToInt16(data, 0);
        }

        public static bool IntParse(string temp, out int value)
        {
            //fixes when a string push also has the same index as a function location and the decompiler adds /*func_loc*/ to the string
            if (temp.Contains("/*") && temp.Contains("*/"))
            {
                var index = temp.IndexOf("/*");
                var index2 = temp.IndexOf("*/", index + 1);
                if (index2 == -1)
                {
                    value = -1;
                    return false;
                }

                temp = temp.Substring(0, index) + temp.Substring(index2 + 2);
            }

            //fixes the rare case when a string push has the same index as a known hash
            if (temp.StartsWith("joaat(\""))
            {
                temp = temp.Remove(temp.Length - 2).Substring(7);
                var val = GetJoaat(temp);
                value = unchecked((int) val);
                return true;
            }

            if (Program.IntStyle == Program.IntType.Hex)
            {
                return int.TryParse(temp.Substring(2), NumberStyles.HexNumber, new CultureInfo("en-gb"), out value);
            }
            else
                return int.TryParse(temp, out value);
        }

        public static ulong RotateRight(ulong x, int n)
        {
            return (((x) >> (n)) | ((x) << (64 - (n))));
        }

        public static ulong RotateLeft(ulong x, int n)
        {
            return (((x) << (n)) | ((x) >> (64 - (n))));
        }

        public static string GetAbsolutePath(string path, string basePath = null)
        {
            if (path == null) return null;
            basePath = (basePath == null) ? Path.GetFullPath(".") : GetAbsolutePath(null, basePath);

            var finalPath = path;
            if (!Path.IsPathRooted(path) || "\\".Equals(Path.GetPathRoot(path)))
            {
                if (path.StartsWith(Path.DirectorySeparatorChar.ToString()))
                    finalPath = Path.Combine(Path.GetPathRoot(basePath), path.TrimStart(Path.DirectorySeparatorChar));
                else
                    finalPath = Path.Combine(basePath, path);
            }

            return Path.GetFullPath(finalPath);
        }
    }
}
