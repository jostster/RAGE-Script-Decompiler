using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RageDecompiler
{
    /// <summary>
    ///     Generates a dictionary of indexes and the strings at those given indexed for use with the PushString instruction
    /// </summary>
    public class StringTable
    {
        private static readonly byte[] Nl = {92, 110}, Cr = {92, 114}, Qt = {92, 34};
        private readonly Dictionary<int, string> _dictionary;
        private readonly byte[] _table;

        public StringTable(Stream scriptFile, int[] stringtablelocs, int blockcount, int wholesize)
        {
            _table = new byte[wholesize];
            for (int i = 0, off = 0; i < blockcount; i++, off += 0x4000)
            {
                var tablesize = (i + 1) * 0x4000 >= wholesize ? wholesize % 0x4000 : 0x4000;
                scriptFile.Position = stringtablelocs[i];
                scriptFile.Read(_table, off, tablesize);
            }

            _dictionary = new Dictionary<int, string>();
            var working = new List<byte>(100);
            for (int i = 0, index = 0, max = _table.Length; i < max; i++)
            {
                for (index = i; i < max; i++)
                {
                    var b = _table[i];
                    switch (b)
                    {
                        case 0:
                            goto addString;
                        case 10:
                            working.AddRange(Nl);
                            break;
                        case 13:
                            working.AddRange(Cr);
                            break;
                        case 34:
                            working.AddRange(Qt);
                            break;
                        default:
                            working.Add(b);
                            break;
                    }
                }

                addString:
                _dictionary.Add(index, Encoding.ASCII.GetString(working.ToArray()));
                working.Clear();
            }
        }

        public string this[int index]
        {
            get
            {
                if (_dictionary.ContainsKey(index)) return _dictionary[index]; //keep the fast dictionary access
                //enable support when the string index doesnt fall straight after a null terminator
                if (index < 0 || index >= _table.Length)
                    throw new IndexOutOfRangeException("The index given was outside the range of the String table");
                var working = new List<byte>(100);
                for (int i = index, max = _table.Length; i < max; i++)
                {
                    var b = _table[i];
                    switch (b)
                    {
                        case 0:
                            goto addString;
                        case 10:
                            working.AddRange(Nl);
                            break;
                        case 13:
                            working.AddRange(Cr);
                            break;
                        case 34:
                            working.AddRange(Qt);
                            break;
                        default:
                            working.Add(b);
                            break;
                    }
                }

                addString:
                return Encoding.ASCII.GetString(working.ToArray());
            }
        }

        public int[] Keys => _dictionary.Keys.ToArray();

        public string[] Values => _dictionary.Values.ToArray();

        public bool StringExists(int index)
        {
            return index >= 0 && index < _table.Length;
        }

        public IEnumerator<KeyValuePair<int, string>> GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }
    }
}
