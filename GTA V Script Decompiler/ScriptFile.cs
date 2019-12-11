using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace Decompiler
{
    public class ScriptFile
    {
        List<byte> CodeTable;
        public StringTable StringTable;
        public X64NativeTable X64NativeTable;
        private int offset = 0;

        public List<Function> Functions;
        public List<Function> AggFunctions;
        public Dictionary<int, FunctionName> FunctionLoc;
        public Dictionary<string, Tuple<int, int>> Function_loc = new Dictionary<string, Tuple<int, int>>();

        public static Hashes hashbank = new Hashes();
        private Stream file;
        public ScriptHeader Header;
        public string name;
        internal Vars_Info Statics;
        internal bool CheckNative = true;

        public ScriptFile(Stream scriptStream)
        {
            file = scriptStream;
            CodeTable = new List<byte>();
            Functions = new List<Function>();
            AggFunctions = new List<Function>();
            FunctionLoc = new Dictionary<int, FunctionName>();

            Header = ScriptHeader.Generate(scriptStream);
            StringTable = new StringTable(scriptStream, Header.StringTableOffsets, Header.StringBlocks, Header.StringsSize);
            X64NativeTable = new X64NativeTable(scriptStream, Header.NativesOffset + Header.RSC7Offset, Header.NativesCount, Header.CodeLength);
            name = Header.ScriptName;

            for (int i = 0; i < Header.CodeBlocks; i++)
            {
                int tablesize = ((i + 1) * 0x4000 >= Header.CodeLength) ? Header.CodeLength % 0x4000 : 0x4000;
                byte[] working = new byte[tablesize];
                scriptStream.Position = Header.CodeTableOffsets[i];
                scriptStream.Read(working, 0, tablesize);
                CodeTable.AddRange(working);
            }

            GetStaticInfo();
            GetFunctions();
            foreach (Function func in Functions) func.PreDecode();
            if (Program.AggregateFunctions) foreach (Function func in AggFunctions) func.PreDecode();
            Statics.checkvars();
            foreach (Function func in Functions) func.Decode();
            if (Program.AggregateFunctions) foreach (Function func in AggFunctions) func.Decode();
        }

        public void Save(string filename)
        {
            Stream savefile = File.Create(filename);
            Save(savefile, true);
        }

        public void Save(Stream stream, bool close = false)
        {
            StreamWriter savestream = new StreamWriter(stream);
            try
            {
                int i = 1;
                if (Program.Declare_Variables)
                {
                    if (Header.StaticsCount > 0)
                    {
                        savestream.WriteLine("#region Local Var");
                        i++;
                        foreach (string s in Statics.GetDeclaration())
                        {
                            savestream.WriteLine("\t" + s);
                            i++;
                        }
                        savestream.WriteLine("#endregion");
                        savestream.WriteLine("");
                        i += 2;
                    }
                }
                foreach (Function f in Functions)
                {
                    savestream.WriteLine(f.ToString());
                    Function_loc.Add(f.Name, new Tuple<int, int>(i, f.Location));
                    i += f.LineCount;
                }
            }
            finally
            {
                savestream.Flush();
                if (close)
                    savestream.Close();
            }
        }

        public void Close()
        {
            if (!Program.AggregateFunctions)
                foreach (Function func in Functions) func.Invalidate();
            file.Close();
        }

        public string[] GetStringTable()
        {
            List<string> table = new List<string>();
            foreach (KeyValuePair<int, string> item in StringTable)
                table.Add(item.Key.ToString() + ": " + item.Value);
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
            for (int i = 0; i < Functions.Count - 1; i++)
            {
                int start = Functions[i].MaxLocation, end = Functions[i + 1].Location;
                Functions[i].CodeBlock = CodeTable.GetRange(start, end - start);
                if (Program.AggregateFunctions) AggFunctions[i].CodeBlock = Functions[i].CodeBlock;
            }

            Functions[Functions.Count - 1].CodeBlock = CodeTable.GetRange(Functions[Functions.Count - 1].MaxLocation, CodeTable.Count - Functions[Functions.Count - 1].MaxLocation);
            if (Program.AggregateFunctions) AggFunctions[Functions.Count - 1].CodeBlock = Functions[Functions.Count - 1].CodeBlock;
            foreach (Function func in Functions)
            {
                if (func.CodeBlock[0] != (int) Instruction.Enter && func.CodeBlock[func.CodeBlock.Count - 3] != (int) Instruction.Return)
                    throw new Exception("Function has incorrect start/ends");
            }
        }

        void advpos(int pos)
        {
            offset += pos;
        }

        void AddFunction(int start1, int start2)
        {
            byte namelen = CodeTable[start1 + 4];
            string name = "";
            if (namelen > 0)
            {
                for (int i = 0; i < namelen; i++)
                {
                    name += (char)CodeTable[start1 + 5 + i];
                }
            }
            else if (start1 == 0)
            {
                name = "__EntryFunction__";
            }
            else name = "func_" + Functions.Count.ToString();
            int pcount = CodeTable[offset + 1];
            int tmp1 = CodeTable[offset + 2], tmp2 = CodeTable[offset + 3];
            int vcount = ((Program.Bit32) ? (tmp1 << 0x8) | tmp2 : (tmp2 << 0x8) | tmp1);
            if (vcount < 0)
            {
                throw new Exception("Well this shouldnt have happened");
            }
            int temp = start1 + 5 + namelen;
            while (CodeTable[temp] != (int) Instruction.Return)
            {
                switch (CodeTable[temp])
                {
                    case (int) Instruction.iPushByte1: temp += 1; break;
                    case (int) Instruction.iPushByte2: temp += 2; break;
                    case (int) Instruction.iPushByte3: temp += 3; break;
                    case (int) Instruction.iPushInt:
                    case (int) Instruction.fPush: temp += 4; break;
                    case (int) Instruction.Native: temp += 3; break;
                    case (int) Instruction.Enter: throw new Exception("Return Expected");
                    case (int) Instruction.Return: throw new Exception("Return Expected");
                    case (int) Instruction.pArray1:
                    case (int) Instruction.ArrayGet1:
                    case (int) Instruction.ArraySet1:
                    case (int) Instruction.pFrame1:
                    case (int) Instruction.GetFrame1:
                    case (int) Instruction.SetFrame1:
                    case (int) Instruction.pStatic1:
                    case (int) Instruction.StaticGet1:
                    case (int) Instruction.StaticSet1:
                    case (int) Instruction.Add1:
                    case (int) Instruction.Mult1:
                    case (int) Instruction.pStruct1:
                    case (int) Instruction.GetStruct1:
                    case (int) Instruction.SetStruct1: temp += 1; break;
                    case (int) Instruction.iPushShort:
                    case (int) Instruction.Add2:
                    case (int) Instruction.Mult2:
                    case (int) Instruction.pStruct2:
                    case (int) Instruction.GetStruct2:
                    case (int) Instruction.SetStruct2:
                    case (int) Instruction.pArray2:
                    case (int) Instruction.ArrayGet2:
                    case (int) Instruction.ArraySet2:
                    case (int) Instruction.pFrame2:
                    case (int) Instruction.GetFrame2:
                    case (int) Instruction.SetFrame2:
                    case (int) Instruction.pStatic2:
                    case (int) Instruction.StaticGet2:
                    case (int) Instruction.StaticSet2:
                    case (int) Instruction.pGlobal2:
                    case (int) Instruction.GlobalGet2:
                    case (int) Instruction.GlobalSet2:
                    case (int) Instruction.Jump:
                    case (int) Instruction.JumpFalse:
                    case (int) Instruction.JumpNe:
                    case (int) Instruction.JumpEq:
                    case (int) Instruction.JumpLe:
                    case (int) Instruction.JumpLt:
                    case (int) Instruction.JumpGe:
                    case (int) Instruction.JumpGt: temp += 2; break;
                    case (int) Instruction.Call:
                    case (int) Instruction.pGlobal3:
                    case (int) Instruction.GlobalGet3:
                    case (int) Instruction.GlobalSet3:
                    case (int) Instruction.iPushI24: temp += 3; break;
                    case (int) Instruction.Switch: temp += 1 + CodeTable[temp + 1] * 6; break;
                    case (int) Instruction.StrCopy:
                    case (int) Instruction.ItoS:
                    case (int) Instruction.StrConCat:
                    case (int) Instruction.StrConCatInt: temp += 1; break;
                }
                temp += 1;
            }
            int rcount = CodeTable[temp + 2];
            int Location = start2;
            if (start1 == start2)
            {
                Function baseFunction = new Function(this, name, pcount, vcount, rcount, Location, -1, false);
                Functions.Add(baseFunction);
                if (Program.AggregateFunctions) {
                    Function aggregateFunction = new Function(this, name, pcount, vcount, rcount, Location, -1, true);
                    aggregateFunction.BaseFunction = baseFunction;
                    AggFunctions.Add(aggregateFunction);
                }
            }
            else
            {
                Function baseFunction = new Function(this, name, pcount, vcount, rcount, Location, start1, false);
                Functions.Add(baseFunction);
                if (Program.AggregateFunctions) {
                    Function aggregateFunction = new Function(this, name, pcount, vcount, rcount, Location, start1, true);
                    aggregateFunction.BaseFunction = baseFunction;
                    AggFunctions.Add(aggregateFunction);
                }
            }
        }

        void GetFunctions()
        {
            int returnpos = -3;
            while (offset < CodeTable.Count)
            {
                switch (CodeTable[offset])
                {
                    case (int) Instruction.iPushByte1: advpos(1); break;
                    case (int) Instruction.iPushByte2: advpos(2); break;
                    case (int) Instruction.iPushByte3: advpos(3); break;
                    case (int) Instruction.iPushInt:
                    case (int) Instruction.fPush: advpos(4); break;
                    case (int) Instruction.Native: advpos(3); break;
                    case (int) Instruction.Enter: AddFunction(offset, returnpos + 3); ; advpos(CodeTable[offset + 4] + 4); break;
                    case (int) Instruction.Return: returnpos = offset; advpos(2); break;
                    case (int) Instruction.pArray1:
                    case (int) Instruction.ArrayGet1:
                    case (int) Instruction.ArraySet1:
                    case (int) Instruction.pFrame1:
                    case (int) Instruction.GetFrame1:
                    case (int) Instruction.SetFrame1:
                    case (int) Instruction.pStatic1:
                    case (int) Instruction.StaticGet1:
                    case (int) Instruction.StaticSet1:
                    case (int) Instruction.Add1:
                    case (int) Instruction.Mult1:
                    case (int) Instruction.pStruct1:
                    case (int) Instruction.GetStruct1:
                    case (int) Instruction.SetStruct1: advpos(1); break;
                    case (int) Instruction.iPushShort:
                    case (int) Instruction.Add2:
                    case (int) Instruction.Mult2:
                    case (int) Instruction.pStruct2:
                    case (int) Instruction.GetStruct2:
                    case (int) Instruction.SetStruct2:
                    case (int) Instruction.pArray2:
                    case (int) Instruction.ArrayGet2:
                    case (int) Instruction.ArraySet2:
                    case (int) Instruction.pFrame2:
                    case (int) Instruction.GetFrame2:
                    case (int) Instruction.SetFrame2:
                    case (int) Instruction.pStatic2:
                    case (int) Instruction.StaticGet2:
                    case (int) Instruction.StaticSet2:
                    case (int) Instruction.pGlobal2:
                    case (int) Instruction.GlobalGet2:
                    case (int) Instruction.GlobalSet2:
                    case (int) Instruction.Jump:
                    case (int) Instruction.JumpFalse:
                    case (int) Instruction.JumpNe:
                    case (int) Instruction.JumpEq:
                    case (int) Instruction.JumpLe:
                    case (int) Instruction.JumpLt:
                    case (int) Instruction.JumpGe:
                    case (int) Instruction.JumpGt: advpos(2); break;
                    case (int) Instruction.Call:
                    case (int) Instruction.pGlobal3:
                    case (int) Instruction.GlobalGet3:
                    case (int) Instruction.GlobalSet3:
                    case (int) Instruction.iPushI24: advpos(3); break;
                    case (int) Instruction.Switch: advpos(1 + CodeTable[offset + 1] * 6); break;
                    case (int) Instruction.StrCopy:
                    case (int) Instruction.ItoS:
                    case (int) Instruction.StrConCat:
                    case (int) Instruction.StrConCatInt: advpos(1); break;
                }
                advpos(1);
            }
            offset = 0;
            GetFunctionCode();
        }

        private void GetStaticInfo()
        {
            Statics = new Vars_Info(Vars_Info.ListType.Statics);
            Statics.SetScriptParamCount(Header.ParameterCount);
            IO.Reader reader = new IO.Reader(file);
            reader.BaseStream.Position = Header.StaticsOffset + Header.RSC7Offset;
            for (int count = 0; count < Header.StaticsCount; count++)
            {
                Statics.AddVar(Program.Bit32 ? reader.SReadInt32() : reader.ReadInt64());
            }
        }

        /* Aggregate Function */
        public void TryAgg()
        {
            foreach (Function f in AggFunctions)
            {
                Agg.Instance.PushAggregate(this, f, f.ToString());
                f.Invalidate(); f.BaseFunction.Invalidate();
            }
        }
    }
}
