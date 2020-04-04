using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using RageDecompiler.Properties;

namespace RageDecompiler
{
    public class Hashes
    {
        private readonly Dictionary<int, string> _hashes;

        public Hashes()
        {
            _hashes = new Dictionary<int, string>();
            var file = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Entities.dat");

            StreamReader reader;
            if (File.Exists(file))
                reader = new StreamReader(File.OpenRead(file));
            else
                reader = new StreamReader(new MemoryStream(Resources.Entities));
            Populate(reader);
        }

        private void Populate(StreamReader reader)
        {
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                var hash = (int) Utils.GetJoaat(line.ToLower());

                if (hash != 0 && !_hashes.ContainsKey(hash))
                    _hashes.Add(hash, line.ToUpper());
            }
        }

        public string GetHash(int value, string temp = "")
        {
            if (!Program.ReverseHashes)
                return Inttohex(value);
            if (_hashes.ContainsKey(value))
                return "joaat(\"" + _hashes[value] + "\")";
            return Inttohex(value) + temp;
        }

        public string GetHash(uint value, string temp = "")
        {
            if (!Program.ReverseHashes)
                return value.ToString();
            var intvalue = (int) value;
            if (_hashes.ContainsKey(intvalue))
                return "joaat(\"" + _hashes[intvalue] + "\")";
            return value + temp;
        }

        public bool IsKnownHash(int value)
        {
            return _hashes.ContainsKey(value);
        }

        public static string Inttohex(int value)
        {
            if (Program.IntStyle == Program.IntType.Hex)
            {
                var s = value.ToString("X");
                while (s.Length < 8) s = "0" + s;
                return "0x" + s;
            }

            return value.ToString();
        }
    }

    public class GxtEntries
    {
        private readonly Dictionary<int, string> _entries;

        public GxtEntries()
        {
            _entries = new Dictionary<int, string>();
            var file = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Program.RdrOpcodes ? "rdrgxr.dat" : "vgxt.dat");

            StreamReader reader;
            if (File.Exists(file))
                reader = new StreamReader(File.OpenRead(file));
            else
                reader = new StreamReader(new MemoryStream(Program.RdrOpcodes ? Resources.rdrgxt : Resources.vgxt));
            Populate(reader);
        }

        private static string ToLiteral(string input)
        {
            using (var writer = new StringWriter())
            {
                using (var provider = CodeDomProvider.CreateProvider("CSharp"))
                {
                    provider.GenerateCodeFromExpression(new CodePrimitiveExpression(input), writer, null);
                    return writer.ToString();
                }
            }
        }

        private void Populate(StreamReader reader)
        {
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line.Contains(" // "))
                {
                    var split = line.Split(new[] {" // "}, StringSplitOptions.RemoveEmptyEntries);
                    if (split.Length != 2)
                        continue;

                    var hash = split[0].StartsWith("0x")
                        ? Convert.ToInt32(split[0], 16)
                        : (int) Utils.GetJoaat(split[0]);
                    if (hash != 0 && !_entries.ContainsKey(hash))
                        _entries.Add(hash, ToLiteral(split[1]));
                }
            }
        }


        public string GetEntry(int value, bool floatTranslate)
        {
            if (!Program.ShowEntryComments) return "";
            if (_entries.ContainsKey(value)) return " /* GXTEntry: " + _entries[value] + " */";

            /* This is a hack. There are many like it. But this one is mine. */
            if (floatTranslate && value != 1 && value != 0)
            {
                var f = BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
                if (float.IsNaN(f) || float.IsInfinity(f) || f == 0f)
                    return "";

                var fs = f.ToString(CultureInfo.InvariantCulture);
                if (!fs.Contains("E") && ((int) f == f && Math.Abs(f) < 10000f || fs.Length < 6))
                    return " /* Float: " + fs + "f */";
            }

            return "";
        }

        public string GetEntry(string value, bool floatTranslate)
        {
            int tmp;
            return int.TryParse(value, out tmp) ? GetEntry(tmp, floatTranslate) : "";
        }
    }
}
