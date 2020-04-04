using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using CommandLine;
using RageDecompiler.Properties;

namespace RageDecompiler
{
    internal static class Program
    {
        public enum IntType
        {
            Int,
            Uint,
            Hex
        }

        public static OpcodeSet Codeset;
        public static X64NativeFile X64Npi;
        public static Hashes Hashbank;
        public static GxtEntries Gxtbank;

        public static object ThreadLock;
        public static int ThreadCount;

        private static string _saveDirectory = "";
        private static readonly Queue<string> _compileList = new Queue<string>();
        public static ThreadLocal<int> GcCount = new ThreadLocal<int>(() => { return 0; });

        public static bool UseMultiThreading { get; private set; }

        public static bool UppercaseNatives { get; private set; } = true;
        public static bool ShowNamespace { get; private set; } = true;

        public static bool DeclareVariables { get; private set; } = true;
        public static bool ShiftVariables { get; private set; }
        public static bool ReverseHashes { get; private set; } = true;
        public static IntType IntStyle { get; private set; } = IntType.Int;
        public static bool ShowArraySize { get; private set; } = true;
        public static bool ShowEntryComments { get; private set; } = true;

        public static bool HexIndex { get; private set; }
        public static bool ShowFuncPosition { get; private set; }

        public static bool AggregateFunctions { get; private set; }
        public static int AggregateMinLines { get; private set; } = 7;
        public static int AggregateMinHits { get; private set; } = 3;

        public static bool CompressedInput { get; private set; }
        public static bool CompressedOutput { get; private set; }
        public static bool IsBit32 { get; private set; }
        public static bool SwapEndian { get; private set; }
        public static bool RdrOpcodes { get; private set; }
        public static bool RdrNativeCipher { get; private set; }

        private static void InitializeFields(Options o)
        {
            IsBit32 = SwapEndian = RdrOpcodes = RdrNativeCipher = false;
            switch (o.Opcode.ToLower())
            {
                case "v":
                    Codeset = new OpcodeSet();
                    break;
                case "vconsole":
                    IsBit32 = SwapEndian = true;
                    Codeset = new OpcodeSet();
                    break;
                case "rdr":
                    RdrOpcodes = RdrNativeCipher = true;
                    Codeset = new RdrOpcodeSet();
                    break;
                case "rdrconsole":
                    RdrOpcodes = true;
                    Codeset = new RdrConsoleOpcodeSet();
                    break;
                default:
                    throw new ArgumentException("Invalid Opcode Set: " + o.Opcode);
            }

            Console.WriteLine("Loading hashes...");
            Hashbank = new Hashes();

            Console.WriteLine("Loading GXT entries...");
            Gxtbank = new GxtEntries();

            CompressedInput = o.CompressedInput;
            CompressedOutput = o.CompressOutput;
            AggregateFunctions = o.Aggregate;
            if (o.AggregateMinLines > 0) AggregateMinLines = o.AggregateMinLines;
            if (o.AggregateMinHits > 0) AggregateMinHits = o.AggregateMinHits;

            if (!o.Default)
            {
                UseMultiThreading = o.UseMultiThreading;
                UppercaseNatives = o.UppercaseNatives;
                ShowNamespace = o.ShowNamespace;
                DeclareVariables = o.DeclareVariables;
                ShiftVariables = o.ShiftVariables;
                ReverseHashes = o.ReverseHashes;
                ShowArraySize = o.ShowArraySize;
                ShowEntryComments = o.ShowEntryComments;
                HexIndex = o.HexIndex;
                ShowFuncPosition = o.ShowFuncPosition;
                switch (o.IntStyle.ToLower())
                {
                    case "uint":
                        IntStyle = IntType.Uint;
                        break;
                    case "hex":
                        IntStyle = IntType.Hex;
                        break;
                    case "int":
                    default:
                        IntStyle = IntType.Int;
                        break;
                }
            }
        }

        private static void InitializeNativeTable(string nativeFile)
        {
            if (nativeFile != null && !File.Exists(nativeFile))
                throw new Exception("Could not find provided native file: " + nativeFile);

            Stream nativeJson;
            if (nativeFile != null && File.Exists(nativeFile))
                nativeJson = File.OpenRead(nativeFile);
            else if (RdrOpcodes)
                nativeJson = new MemoryStream(Resources.RDNatives);
            else
                nativeJson = new MemoryStream(Resources.Natives);
            X64Npi = new X64NativeFile(nativeJson);
        }

        /// <summary>
        /// </summary>
        /// <param name="inputPath"></param>
        /// <param name="outputPath"></param>
        /// <returns></returns>
        private static ScriptFile ProcessScriptfile(string inputPath, string outputPath)
        {
            /* A ScriptFile tends to skip around the offset table */
            var buffer = new MemoryStream();
            using (Stream fs = File.OpenRead(inputPath))
            {
                (CompressedInput ? new GZipStream(fs, CompressionMode.Decompress) : fs).CopyTo(buffer);
            }

            var scriptFile = new ScriptFile(buffer, Codeset);
            if (outputPath != null)
                using (Stream stream = File.Create(outputPath))
                {
                    scriptFile.Save(CompressedOutput ? new GZipStream(stream, CompressionMode.Compress) : stream, true);
                }
            else
                scriptFile.Save(Console.OpenStandardOutput());

            buffer.Close();
            return scriptFile;
        }

        /// <summary>
        ///     The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            ThreadLock = new object();
            Parser.Default.ParseArguments<Options>(args).WithParsed(o =>
            {
                Console.WriteLine("Heya!");

                var inputPath = Utils.GetAbsolutePath(o.InputPath);
                var outputPath = o.OutputPath != null ? Utils.GetAbsolutePath(o.OutputPath) : null;
                var nativeFile = o.NativeFile != null ? Utils.GetAbsolutePath(o.NativeFile) : null;
                if (File.Exists(inputPath)) // Decompile a single file if given the option.
                {
                    if (outputPath != null && File.Exists(outputPath) && !o.Force)
                    {
                        Console.WriteLine("Cannot overwrite file, use -f to force.");
                        return;
                    }

                    AggregateFunctions = false;

                    InitializeFields(o);
                    InitializeNativeTable(nativeFile);
                    ProcessScriptfile(inputPath, outputPath).Close();
                }
                else if (Directory.Exists(inputPath)) // Decompile directory
                {
                    if (outputPath != null && Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
                    if (outputPath != null && !Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

                    InitializeFields(o);
                    InitializeNativeTable(nativeFile);

                    foreach (var file in Directory.GetFiles(inputPath, "*.ysc*"))
                        _compileList.Enqueue(file);

                    _saveDirectory = outputPath;
                    if (UseMultiThreading)
                    {
                        for (var i = 0; i < Environment.ProcessorCount - 1; i++)
                        {
                            ThreadCount++;
                            new Thread(Decompile).Start();
                        }

                        ThreadCount++;
                        Decompile();
                        while (ThreadCount > 0)
                            Thread.Sleep(10);
                    }
                    else
                    {
                        ThreadCount++;
                        Decompile();
                    }

                    if (AggregateFunctions)
                    {
                        Console.WriteLine("Saving aggregate file...");
                        Aggregate.Instance.SaveAggregate(outputPath);
                        Console.WriteLine("Saving frequency file...");
                        Aggregate.Instance.SaveFrequency(outputPath);
                    }

                    Console.WriteLine("Done!");
                }
                else
                {
                    Console.WriteLine("Invalid YSC Path");
                }
            });
        }

        private static void Decompile()
        {
            while (_compileList.Count > 0)
            {
                string scriptToDecode;
                lock (ThreadLock)
                {
                    scriptToDecode = _compileList.Dequeue();
                }

                try
                {
                    var suffix = ".c" + (CompressedOutput ? ".gz" : "");
                    var outname = Path.GetFileNameWithoutExtension(scriptToDecode);
                    outname = outname.Replace(".ysc", "");
                    if (Path.GetExtension(scriptToDecode) == ".gz"
                    ) // Ensure the extension without compression is removed.
                        outname = Path.GetFileNameWithoutExtension(outname);

                    var output = Path.Combine(_saveDirectory, outname + suffix);
                    Console.WriteLine($"Decompiling: {Path.GetFileName(scriptToDecode)} -> {Path.GetFileName(output)}");

                    var scriptFile = ProcessScriptfile(scriptToDecode, output);

                    if (AggregateFunctions) /* Compile aggregation statistics for each function. */
                        scriptFile.CompileAggregate();

                    scriptFile.Close();
                    if (GcCount.Value++ % 25 == 0)
                        GC.Collect();
                }
                catch (Exception ex)
                {
                    throw new SystemException("Error decompiling script " +
                                              Path.GetFileNameWithoutExtension(scriptToDecode) + " - " + ex.Message);
                }
            }

            ThreadCount--;
        }

        public static string NativeName(string s)
        {
            return UppercaseNatives ? s.ToUpper() : s.ToLower();
        }

        private class Options
        {
            [Option('n', "natives", Required = false, HelpText = "native json file")]
            public string NativeFile { get; set; }

            [Option('i', "in", Default = null, Required = true, HelpText = "Input Directory/File Path.")]
            public string InputPath { get; set; }

            [Option('o', "out", Default = null, Required = false, HelpText = "Output Directory/File Path")]
            public string OutputPath { get; set; }

            [Option("gzin", Default = false, Required = false, HelpText = "Compressed Input (GZIP)")]
            public bool CompressedInput { get; set; }

            [Option("gzout", Default = false, Required = false, HelpText = "Compress Output (GZIP)")]
            public bool CompressOutput { get; set; }

            [Option('c', "opcode", Default = "v", Required = true, HelpText = "Opcode Set (v|vconsole|rdr|rdrconsole)")]
            public string Opcode { get; set; }

            [Option('f', "force", Default = false, Required = false, HelpText = "Allow output file overriding")]
            public bool Force { get; set; }

            [Option('a', "aggregate", Default = true, Required = false,
                HelpText = "Compute aggregation statistics of bulk data set")]
            public bool Aggregate { get; set; }

            [Option("minlines", Default = -1, Required = false,
                HelpText = "Minimum function line count for aggregation")]
            public int AggregateMinHits { get; set; }

            [Option("minhits", Default = -1, Required = false,
                HelpText = "Minimum number of occurrences for aggregation")]
            public int AggregateMinLines { get; set; }

            /* Previous INI Configuration */

            [Option("default", Default = false, Required = false, HelpText = "Use default configuration")]
            public bool Default { get; set; }

            [Option("uppercase", Default = false, Required = false, HelpText = "Use uppercase native names")]
            public bool UppercaseNatives { get; set; }

            [Option("namespace", Default = false, Required = false,
                HelpText = "Concatenate Namespace to Native definition")]
            public bool ShowNamespace { get; set; }

            [Option("int", Default = "int", Required = false, HelpText = "Integer Formatting Method (int, uint, hex)")]
            public string IntStyle { get; set; }

            [Option("hash", Default = false, Required = false,
                HelpText = "Use hash (Entity.dat) lookup table when formatting integers")]
            public bool ReverseHashes { get; set; }

            [Option("arraysize", Default = false, Required = false, HelpText = "Show array sizes in definitions")]
            public bool ShowArraySize { get; set; }

            [Option("declare", Default = false, Required = false,
                HelpText = "Declare all variables at the beginning of function/script definitions")]
            public bool DeclareVariables { get; set; }

            [Option("shift", Default = false, Required = false,
                HelpText = "Shift variable names, i.e., take into consideration the immediate size of stack values")]
            public bool ShiftVariables { get; set; }

            [Option("mt", Default = true, Required = false, HelpText = "Multi-thread bulk decompilation")]
            public bool UseMultiThreading { get; set; }

            [Option("position", Default = false, Required = false, HelpText = "Show function location in definition")]
            public bool ShowFuncPosition { get; set; }

            [Option("comment", Default = false, Required = false,
                HelpText = "Show inlined GXT entries and other comments.")]
            public bool ShowEntryComments { get; set; }

            [Option("HexIndex", Default = false, Required = false, HelpText = "")]
            public bool HexIndex { get; set; }
        }
    }
}
