using System;
using System.Collections.Generic;
using System.IO;

namespace RageDecompiler
{
    public class ScriptFile
    {
        private readonly List<byte> _codeTable;

        private readonly Stream _file;
        private readonly Dictionary<ulong, HashSet<Function>> _nativeXRef = new Dictionary<ulong, HashSet<Function>>();
        private int _offset;
        public List<Function> AggFunctions;
        internal bool CheckNative = true;
        public Dictionary<string, Tuple<int, int>> Function_loc = new Dictionary<string, Tuple<int, int>>();
        public Dictionary<int, FunctionName> FunctionLoc;

        public List<Function> Functions;
        public ScriptHeader Header;
        public string Name;
        internal VarsInfo Statics;

        public StringTable StringTable;
        public X64NativeTable X64NativeTable;

        public ScriptFile(Stream scriptStream, OpcodeSet opCodeSet)
        {
            _file = scriptStream;
            CodeSet = opCodeSet;

            _codeTable = new List<byte>();
            Functions = new List<Function>();
            AggFunctions = new List<Function>();
            FunctionLoc = new Dictionary<int, FunctionName>();

            Header = ScriptHeader.Generate(scriptStream);
            StringTable = new StringTable(scriptStream, Header.StringTableOffsets, Header.StringBlocks,
                Header.StringsSize);
            X64NativeTable = new X64NativeTable(scriptStream, Header.NativesOffset + Header.Rsc7Offset,
                Header.NativesCount, Header.CodeLength);
            Name = Header.ScriptName;

            for (var i = 0; i < Header.CodeBlocks; i++)
            {
                var tablesize = (i + 1) * 0x4000 >= Header.CodeLength ? Header.CodeLength % 0x4000 : 0x4000;
                var working = new byte[tablesize];
                scriptStream.Position = Header.CodeTableOffsets[i];
                scriptStream.Read(working, 0, tablesize);
                _codeTable.AddRange(working);
            }

            GetStaticInfo();
            GetFunctions();
            foreach (var func in Functions) func.PreDecode();
            Statics.Checkvars();

            var dirty = true;
            while (dirty)
            {
                dirty = false;
                foreach (var func in Functions)
                    if (func.Dirty)
                    {
                        dirty = true;
                        func.Dirty = false;
                        func.DecodeInstructionsForVarInfo();
                    }
            }

            if (Program.AggregateFunctions)
                foreach (var func in AggFunctions)
                    func.PreDecode();
            foreach (var func in Functions) func.Decode();
            if (Program.AggregateFunctions)
                foreach (var func in AggFunctions)
                    func.Decode();
        }

        public OpcodeSet CodeSet { get; }

        public void CrossReferenceNative(ulong hash, Function f)
        {
            if (!_nativeXRef.ContainsKey(hash))
                _nativeXRef.Add(hash, new HashSet<Function>(new[] {f}));
            else
                _nativeXRef[hash].Add(f);
        }

        public void UpdateStaticType(uint index, Stack.DataType dataType)
        {
            var prev = Statics.GetTypeAtIndex(index);
            if (Statics.SetTypeAtIndex(index, dataType))
                foreach (var f in Functions)
                    f.Dirty = true;
        }

        public void UpdateNativeReturnType(ulong hash, Stack.DataType dataType)
        {
            if (Program.X64Npi.UpdateRetType(hash, dataType) && _nativeXRef.ContainsKey(hash))
                foreach (var f in _nativeXRef[hash])
                    f.Dirty = true;
        }

        public void UpdateNativeParameter(ulong hash, Stack.DataType type, int index)
        {
            if (Program.X64Npi.UpdateParam(hash, type, index))
                foreach (var f in _nativeXRef[hash])
                    f.Dirty = true;
        }

        public void Save(string filename)
        {
            Stream fileStream = File.Create(filename);
            Save(fileStream, true);
        }

        public void Save(Stream stream, bool close = false)
        {
            var streamWriter = new StreamWriter(stream);
            try
            {
                var i = 1;
                if (Program.DeclareVariables)
                    if (Header.StaticsCount > 0)
                    {
                        streamWriter.WriteLine("#region Local Var");
                        i++;
                        foreach (var s in Statics.GetDeclaration())
                        {
                            streamWriter.WriteLine("\t" + s);
                            i++;
                        }

                        streamWriter.WriteLine("#endregion");
                        streamWriter.WriteLine("");
                        i += 2;
                    }

                foreach (var f in Functions)
                {
                    streamWriter.WriteLine(f.ToString());
                    Function_loc.Add(f.Name, new Tuple<int, int>(i, f.Location));
                    i += f.LineCount;
                }
            }
            finally
            {
                streamWriter.Flush();
                if (close)
                    streamWriter.Close();
            }
        }

        public void Close()
        {
            if (!Program.AggregateFunctions)
                foreach (var func in Functions)
                    func.Invalidate();
            _file.Close();
        }

        public string[] GetStringTable()
        {
            var table = new List<string>();
            foreach (var item in StringTable)
                table.Add(item.Key + ": " + item.Value);
            return table.ToArray();
        }

        public string[] GetNativeTable()
        {
            return X64NativeTable.GetNativeTable();
        }

        public string[] GetNativeHeader()
        {
            return X64NativeTable.GetNativeHeader();
        }

        public void GetFunctionCode()
        {
            for (var i = 0; i < Functions.Count - 1; i++)
            {
                int start = Functions[i].MaxLocation, end = Functions[i + 1].Location;
                Functions[i].CodeBlock = _codeTable.GetRange(start, end - start);
                if (Program.AggregateFunctions) AggFunctions[i].CodeBlock = Functions[i].CodeBlock;
            }

            Functions[Functions.Count - 1].CodeBlock = _codeTable.GetRange(Functions[Functions.Count - 1].MaxLocation,
                _codeTable.Count - Functions[Functions.Count - 1].MaxLocation);
            if (Program.AggregateFunctions)
                AggFunctions[Functions.Count - 1].CodeBlock = Functions[Functions.Count - 1].CodeBlock;
            foreach (var func in Functions)
                if (CodeSet.Map(func.CodeBlock[0]) != Instruction.RAGE_ENTER &&
                    CodeSet.Map(func.CodeBlock[func.CodeBlock.Count - 3]) != Instruction.RAGE_LEAVE)
                    throw new Exception("Function has incorrect start/ends");
        }

        private void Advpos(int pos)
        {
            _offset += pos;
        }

        private void AddFunction(int start1, int start2)
        {
            var namelen = _codeTable[start1 + 4];
            var name = "";
            if (namelen > 0)
                for (var i = 0; i < namelen; i++)
                    name += (char) _codeTable[start1 + 5 + i];
            else if (start1 == 0)
                name = "__EntryFunction__";
            else name = "func_" + Functions.Count;
            int pcount = _codeTable[_offset + 1];
            int tmp1 = _codeTable[_offset + 2], tmp2 = _codeTable[_offset + 3];
            var vcount = Program.SwapEndian ? (tmp1 << 0x8) | tmp2 : (tmp2 << 0x8) | tmp1;
            if (vcount < 0) throw new Exception("Well this shouldnt have happened");
            var temp = start1 + 5 + namelen;
            while (CodeSet.Map(_codeTable[temp]) != Instruction.RAGE_LEAVE)
            {
                switch (CodeSet.Map(_codeTable[temp]))
                {
                    case Instruction.RAGE_PUSH_CONST_U8:
                        temp += 1;
                        break;
                    case Instruction.RAGE_PUSH_CONST_U8_U8:
                        temp += 2;
                        break;
                    case Instruction.RAGE_PUSH_CONST_U8_U8_U8:
                        temp += 3;
                        break;
                    case Instruction.RAGE_PUSH_CONST_U32:
                    case Instruction.RAGE_PUSH_CONST_F:
                        temp += 4;
                        break;
                    case Instruction.RAGE_NATIVE:
                        temp += 3;
                        break;
                    case Instruction.RAGE_ENTER: throw new Exception("Return Expected");
                    case Instruction.RAGE_LEAVE: throw new Exception("Return Expected");
                    case Instruction.RAGE_ARRAY_U8:
                    case Instruction.RAGE_ARRAY_U8_LOAD:
                    case Instruction.RAGE_ARRAY_U8_STORE:
                    case Instruction.RAGE_LOCAL_U8:
                    case Instruction.RAGE_LOCAL_U8_LOAD:
                    case Instruction.RAGE_LOCAL_U8_STORE:
                    case Instruction.RAGE_STATIC_U8:
                    case Instruction.RAGE_STATIC_U8_LOAD:
                    case Instruction.RAGE_STATIC_U8_STORE:
                    case Instruction.RAGE_IADD_U8:
                    case Instruction.RAGE_IMUL_U8:
                    case Instruction.RAGE_IOFFSET_U8:
                    case Instruction.RAGE_IOFFSET_U8_LOAD:
                    case Instruction.RAGE_IOFFSET_U8_STORE:
                        temp += 1;
                        break;
                    case Instruction.RAGE_PUSH_CONST_S16:
                    case Instruction.RAGE_IADD_S16:
                    case Instruction.RAGE_IMUL_S16:
                    case Instruction.RAGE_IOFFSET_S16:
                    case Instruction.RAGE_IOFFSET_S16_LOAD:
                    case Instruction.RAGE_IOFFSET_S16_STORE:
                    case Instruction.RAGE_ARRAY_U16:
                    case Instruction.RAGE_ARRAY_U16_LOAD:
                    case Instruction.RAGE_ARRAY_U16_STORE:
                    case Instruction.RAGE_LOCAL_U16:
                    case Instruction.RAGE_LOCAL_U16_LOAD:
                    case Instruction.RAGE_LOCAL_U16_STORE:
                    case Instruction.RAGE_STATIC_U16:
                    case Instruction.RAGE_STATIC_U16_LOAD:
                    case Instruction.RAGE_STATIC_U16_STORE:
                    case Instruction.RAGE_GLOBAL_U16:
                    case Instruction.RAGE_GLOBAL_U16_LOAD:
                    case Instruction.RAGE_GLOBAL_U16_STORE:
                    case Instruction.RAGE_J:
                    case Instruction.RAGE_JZ:
                    case Instruction.RAGE_IEQ_JZ:
                    case Instruction.RAGE_INE_JZ:
                    case Instruction.RAGE_IGT_JZ:
                    case Instruction.RAGE_IGE_JZ:
                    case Instruction.RAGE_ILT_JZ:
                    case Instruction.RAGE_ILE_JZ:
                        temp += 2;
                        break;
                    case Instruction.RAGE_CALL:
                    case Instruction.RAGE_GLOBAL_U24:
                    case Instruction.RAGE_GLOBAL_U24_LOAD:
                    case Instruction.RAGE_GLOBAL_U24_STORE:
                    case Instruction.RAGE_PUSH_CONST_U24:
                        temp += 3;
                        break;
                    case Instruction.RAGE_SWITCH:
                    {
                        if (Program.RdrOpcodes)
                        {
                            var length = (_codeTable[temp + 2] << 8) | _codeTable[temp + 1];
                            temp += 2 + 6 * (Program.SwapEndian ? Utils.SwapEndian(length) : length);
                        }
                        else
                        {
                            temp += 1 + 6 * _codeTable[temp + 1];
                        }

                        break;
                    }
                    case Instruction.RAGE_TEXT_LABEL_ASSIGN_STRING:
                    case Instruction.RAGE_TEXT_LABEL_ASSIGN_INT:
                    case Instruction.RAGE_TEXT_LABEL_APPEND_STRING:
                    case Instruction.RAGE_TEXT_LABEL_APPEND_INT:
                        temp += 1;
                        break;
                }

                temp += 1;
            }

            int rcount = _codeTable[temp + 2];
            var location = start2;
            if (start1 == start2)
            {
                var baseFunction = new Function(this, name, pcount, vcount, rcount, location);
                Functions.Add(baseFunction);
                if (Program.AggregateFunctions)
                {
                    var aggregateFunction = new Function(this, name, pcount, vcount, rcount, location, -1, true);
                    aggregateFunction.BaseFunction = baseFunction;
                    AggFunctions.Add(aggregateFunction);
                }
            }
            else
            {
                var baseFunction = new Function(this, name, pcount, vcount, rcount, location, start1);
                Functions.Add(baseFunction);
                if (Program.AggregateFunctions)
                {
                    var aggregateFunction = new Function(this, name, pcount, vcount, rcount, location, start1, true);
                    aggregateFunction.BaseFunction = baseFunction;
                    AggFunctions.Add(aggregateFunction);
                }
            }
        }

        private void GetFunctions()
        {
            var returnPos = -3;
            while (_offset < _codeTable.Count)
            {
                switch (CodeSet.Map(_codeTable[_offset]))
                {
                    case Instruction.RAGE_PUSH_CONST_U8:
                        Advpos(1);
                        break;
                    case Instruction.RAGE_PUSH_CONST_U8_U8:
                        Advpos(2);
                        break;
                    case Instruction.RAGE_PUSH_CONST_U8_U8_U8:
                        Advpos(3);
                        break;
                    case Instruction.RAGE_PUSH_CONST_U32:
                    case Instruction.RAGE_PUSH_CONST_F:
                        Advpos(4);
                        break;
                    case Instruction.RAGE_NATIVE:
                        Advpos(3);
                        break;
                    case Instruction.RAGE_ENTER:
                        AddFunction(_offset, returnPos + 3);
                        ;
                        Advpos(_codeTable[_offset + 4] + 4);
                        break;
                    case Instruction.RAGE_LEAVE:
                        returnPos = _offset;
                        Advpos(2);
                        break;
                    case Instruction.RAGE_ARRAY_U8:
                    case Instruction.RAGE_ARRAY_U8_LOAD:
                    case Instruction.RAGE_ARRAY_U8_STORE:
                    case Instruction.RAGE_LOCAL_U8:
                    case Instruction.RAGE_LOCAL_U8_LOAD:
                    case Instruction.RAGE_LOCAL_U8_STORE:
                    case Instruction.RAGE_STATIC_U8:
                    case Instruction.RAGE_STATIC_U8_LOAD:
                    case Instruction.RAGE_STATIC_U8_STORE:
                    case Instruction.RAGE_IADD_U8:
                    case Instruction.RAGE_IMUL_U8:
                    case Instruction.RAGE_IOFFSET_U8:
                    case Instruction.RAGE_IOFFSET_U8_LOAD:
                    case Instruction.RAGE_IOFFSET_U8_STORE:
                        Advpos(1);
                        break;
                    case Instruction.RAGE_PUSH_CONST_S16:
                    case Instruction.RAGE_IADD_S16:
                    case Instruction.RAGE_IMUL_S16:
                    case Instruction.RAGE_IOFFSET_S16:
                    case Instruction.RAGE_IOFFSET_S16_LOAD:
                    case Instruction.RAGE_IOFFSET_S16_STORE:
                    case Instruction.RAGE_ARRAY_U16:
                    case Instruction.RAGE_ARRAY_U16_LOAD:
                    case Instruction.RAGE_ARRAY_U16_STORE:
                    case Instruction.RAGE_LOCAL_U16:
                    case Instruction.RAGE_LOCAL_U16_LOAD:
                    case Instruction.RAGE_LOCAL_U16_STORE:
                    case Instruction.RAGE_STATIC_U16:
                    case Instruction.RAGE_STATIC_U16_LOAD:
                    case Instruction.RAGE_STATIC_U16_STORE:
                    case Instruction.RAGE_GLOBAL_U16:
                    case Instruction.RAGE_GLOBAL_U16_LOAD:
                    case Instruction.RAGE_GLOBAL_U16_STORE:
                    case Instruction.RAGE_J:
                    case Instruction.RAGE_JZ:
                    case Instruction.RAGE_IEQ_JZ:
                    case Instruction.RAGE_INE_JZ:
                    case Instruction.RAGE_IGT_JZ:
                    case Instruction.RAGE_IGE_JZ:
                    case Instruction.RAGE_ILT_JZ:
                    case Instruction.RAGE_ILE_JZ:
                        Advpos(2);
                        break;
                    case Instruction.RAGE_CALL:
                    case Instruction.RAGE_GLOBAL_U24:
                    case Instruction.RAGE_GLOBAL_U24_LOAD:
                    case Instruction.RAGE_GLOBAL_U24_STORE:
                    case Instruction.RAGE_PUSH_CONST_U24:
                        Advpos(3);
                        break;
                    case Instruction.RAGE_SWITCH:
                    {
                        if (Program.RdrOpcodes)
                        {
                            var length = (_codeTable[_offset + 2] << 8) | _codeTable[_offset + 1];
                            Advpos(2 + 6 * (Program.SwapEndian ? Utils.SwapEndian(length) : length));
                        }
                        else
                        {
                            Advpos(1 + 6 * _codeTable[_offset + 1]);
                        }

                        break;
                    }
                    case Instruction.RAGE_TEXT_LABEL_ASSIGN_STRING:
                    case Instruction.RAGE_TEXT_LABEL_ASSIGN_INT:
                    case Instruction.RAGE_TEXT_LABEL_APPEND_STRING:
                    case Instruction.RAGE_TEXT_LABEL_APPEND_INT:
                        Advpos(1);
                        break;
                }

                Advpos(1);
            }

            _offset = 0;
            GetFunctionCode();
        }

        private void GetStaticInfo()
        {
            Statics = new VarsInfo(VarsInfo.ListType.Statics);
            Statics.SetScriptParamCount(Header.ParameterCount);
            var reader = new Reader(_file);
            reader.BaseStream.Position = Header.StaticsOffset + Header.Rsc7Offset;
            for (var count = 0; count < Header.StaticsCount; count++)
                Statics.AddVar(Program.IsBit32 ? reader.CReadInt32() : reader.ReadInt64());
        }

        /* Aggregate Function */
        public void CompileAggregate()
        {
            foreach (var f in AggFunctions)
            {
                Aggregate.Instance.PushAggregate(this, f, f.ToString());
                f.Invalidate();
                f.BaseFunction.Invalidate();
            }
        }
    }
}
