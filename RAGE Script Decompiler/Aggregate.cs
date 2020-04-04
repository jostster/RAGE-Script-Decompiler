using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RageDecompiler
{
    public struct AggregateData
    {
        public string ScriptName { get; private set; }
        public string FunctionName { get; private set; }
        public string FunctionString { get; private set; }

        public string AggregateString { get; }
        public string AggregateName { get; }
        public string Hash { get; }
        public List<string> Hits;

        public AggregateData(Function function, string aggName, string hash)
        {
            Hits = new List<string>();
            Hash = hash;
            AggregateName = aggName;
            AggregateString = function.ToString();

            ScriptName = function.ScriptFile.Name;
            FunctionName = function.ScriptFile.Name + "." + function.Name;
            FunctionString = function.BaseFunction.ToString();
        }

        public void AddFunction(Function function)
        {
            var addedName = function.ScriptFile.Name + "." + function.Name;
            if (string.Compare(FunctionName, addedName, StringComparison.OrdinalIgnoreCase) > 0)
            {
                Hits.Add("// Hit: " + FunctionName);
                ScriptName = function.ScriptFile.Name;
                FunctionName = addedName;
                FunctionString = function.BaseFunction.ToString();
            }
            else
            {
                Hits.Add("// Hit: " + addedName);
            }
        }
    }

    public sealed class Aggregate
    {
        private static readonly SHA256Managed _crypt = new SHA256Managed();
        private static readonly StringBuilder _sb = new StringBuilder();
        private readonly object _countLock = new object();
        private readonly object _pushLock = new object();

        private readonly Dictionary<string, AggregateData> _functionLoc;
        private readonly Dictionary<string, ulong> _nativeRefCount;

        private Aggregate()
        {
            _functionLoc = new Dictionary<string, AggregateData>();
            _nativeRefCount = new Dictionary<string, ulong>();
        }

        public static Aggregate Instance => Nested.Instance;

        private static int CountLines(string str)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            if (str == string.Empty)
                return 0;

            int index = -1, count = 0;
            while (-1 != (index = str.IndexOf(Environment.NewLine, index + 1, StringComparison.Ordinal)))
                count++;
            return count + 1;
        }

        public static string Sha256(string value)
        {
            _sb.Clear();
            foreach (var b in _crypt.ComputeHash(Encoding.UTF8.GetBytes(value)))
                _sb.Append(b.ToString("x2"));
            return _sb.ToString();
        }

        public bool CanAggregateLiteral(string lit)
        {
            return !lit.StartsWith("Global");
        }

        public bool IsAggregate(string decomp)
        {
            return _functionLoc.ContainsKey(Sha256(decomp));
        }

        public AggregateData FetchAggregate(string decomp)
        {
            return _functionLoc[Sha256(decomp)];
        }

        public void Count(string hash)
        {
            lock (_pushLock)
            {
                ulong value = 0;
                _nativeRefCount[hash] = (_nativeRefCount.TryGetValue(hash, out value) ? value : 0) + 1;
            }
        }

        public void PushAggregate(ScriptFile script, Function function, string decomp)
        {
            lock (_pushLock)
            {
                if (function.NativeCount > 0 && CountLines(decomp) >= Program.AggregateMinLines)
                {
                    var hash = Sha256(decomp);
                    if (_functionLoc.ContainsKey(hash))
                        _functionLoc[hash].AddFunction(function);
                    else
                        _functionLoc.Add(hash, new AggregateData(function, "Aggregate_" + _functionLoc.Count, hash));
                }
            }
        }

        public void SaveAggregate(string saveDirectory)
        {
            var suffix = ".c" + (Program.CompressedOutput ? ".gz" : "");
            using (Stream fileStream = File.Create(Path.Combine(saveDirectory, "_aggregate" + suffix)))
            {
                var streamWriter = new StreamWriter(Program.CompressedOutput
                    ? new GZipStream(fileStream, CompressionMode.Compress)
                    : fileStream);
                streamWriter.AutoFlush = true;

                var list = _functionLoc.ToList();
                list.Sort(delegate(KeyValuePair<string, AggregateData> pair1, KeyValuePair<string, AggregateData> pair2)
                {
                    if (pair2.Value.Hits.Count == pair1.Value.Hits.Count)
                        return string.Compare(pair1.Value.AggregateName, pair2.Value.AggregateName,
                            StringComparison.Ordinal);
                    return pair2.Value.Hits.Count.CompareTo(pair1.Value.Hits.Count);
                });

                foreach (var entry in list)
                    if (entry.Value.Hits.Count >= Program.AggregateMinHits)
                    {
                        streamWriter.WriteLine("// " + entry.Key);
                        streamWriter.WriteLine("// Base: " + entry.Value.FunctionName);

                        entry.Value.Hits.Sort();
                        foreach (var c in entry.Value.Hits)
                            streamWriter.WriteLine(c);
                        streamWriter.WriteLine(entry.Value.FunctionString);
                    }
            }
        }

        public void SaveFrequency(string saveDirectory)
        {
            using (Stream fileStream = File.Create(Path.Combine(saveDirectory, "_funcfreq.csv")))
            {
                using (var streamWriter = new StreamWriter(fileStream))
                {
                    streamWriter.AutoFlush = true;

                    var list = _nativeRefCount.ToList();
                    list.Sort(delegate(KeyValuePair<string, ulong> pair1, KeyValuePair<string, ulong> pair2)
                    {
                        var comp = pair2.Value.CompareTo(pair1.Value);
                        return comp == 0 ? string.CompareOrdinal(pair1.Key, pair2.Key) : comp;
                    });

                    streamWriter.WriteLine("Function, Count");
                    foreach (var entry in list)
                        streamWriter.WriteLine(entry.Key + ", " + entry.Value);
                }
            }
        }

        public void SaveAggregateDefinitions(string saveDirectory)
        {
            using (Stream stream = File.Create(Path.Combine(saveDirectory, "aggregatedefns.c")))
            {
                var streamWriter = new StreamWriter(stream);
                var list = _functionLoc.ToList();
                list.Sort(delegate(KeyValuePair<string, AggregateData> pair1, KeyValuePair<string, AggregateData> pair2)
                {
                    if (pair2.Value.Hits.Count == pair1.Value.Hits.Count)
                        return string.Compare(pair1.Value.AggregateName, pair2.Value.AggregateName,
                            StringComparison.Ordinal);
                    return pair2.Value.Hits.Count.CompareTo(pair1.Value.Hits.Count);
                });

                foreach (var entry in list)
                {
                    if (entry.Value.Hits.Count < Program.AggregateMinHits) continue;
                    streamWriter.WriteLine("// " + entry.Key);
                    streamWriter.WriteLine("// Base: " + entry.Value.FunctionName);
                    streamWriter.WriteLine(entry.Value.AggregateString);
                }
            }
        }

        private class Nested
        {
            internal static readonly Aggregate Instance = new Aggregate();

            static Nested()
            {
            }
        }
    }
}
