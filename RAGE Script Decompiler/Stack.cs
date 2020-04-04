using System;
using System.Collections.Generic;
using System.Globalization;

namespace RageDecompiler
{
    public class Stack
    {
        private readonly Function _parent;
        private readonly List<StackValue> _stack = new List<StackValue>();

        public Stack(Function parent, bool decodeVar = false, bool isAggregate = false)
        {
            _parent = parent;
            DecodeVarInfo = decodeVar;
            IsAggregate = isAggregate;
        }

        public bool DecodeVarInfo { get; }
        public bool IsAggregate { get; } // Stateless stack information.
        public DataType TopType => _stack.Count == 0 ? DataType.Unk : _stack[_stack.Count - 1].Datatype;

        public void Dispose()
        {
            _stack.Clear();
        }

        public void Push(string value, DataType datatype = DataType.Unk)
        {
            _stack.Add(new StackValue(this, StackValue.Type.Literal, value, datatype));
        }

        public void PushGlobal(string value)
        {
            _stack.Add(StackValue.Global(this, StackValue.Type.Literal, value));
        }

        public void PushPGlobal(string value)
        {
            _stack.Add(StackValue.Global(this, StackValue.Type.Pointer, value));
        }

        public void PushCond(string value)
        {
            _stack.Add(new StackValue(this, StackValue.Type.Literal, value, DataType.Bool));
        }

        private void Push(StackValue item)
        {
            _stack.Add(item);
        }

        public void PushString(string value)
        {
            _stack.Add(new StackValue(this, StackValue.Type.Literal, value, DataType.StringPtr));
        }

        public void Push(params int[] values)
        {
            foreach (var value in values)
                switch (Program.IntStyle)
                {
                    case Program.IntType.Int:
                    case Program.IntType.Hex:
                        _stack.Add(new StackValue(this, StackValue.Type.Literal, Hashes.Inttohex(value), DataType.Int));
                        break;
                    case Program.IntType.Uint:
                        _stack.Add(new StackValue(this, StackValue.Type.Literal, unchecked((uint) value).ToString(),
                            DataType.Int));
                        break;
                }
        }

        public void PushHexInt(uint value)
        {
            _stack.Add(new StackValue(this, StackValue.Type.Literal, Utils.FormatHexHash(value), DataType.Int));
        }

        public void PushVar(VarsInfo.Var variable)
        {
            _stack.Add(new StackValue(this, StackValue.Type.Literal, variable,
                variable.Immediatesize == 3 ? ".x" : ""));
        }

        public void PushPVar(VarsInfo.Var variable, string suffix = "")
        {
            _stack.Add(new StackValue(this, StackValue.Type.Pointer, variable, suffix));
        }

        public void Push(float value)
        {
            _stack.Add(new StackValue(this, StackValue.Type.Literal, value.ToString(CultureInfo.InvariantCulture) + "f",
                DataType.Float));
        }

        public void PushPointer(string value)
        {
            _stack.Add(new StackValue(this, StackValue.Type.Pointer, value));
        }

        private void PushStruct(string value, int size)
        {
            _stack.Add(new StackValue(this, value, size));
        }

        private void PushVector(string value)
        {
            _stack.Add(new StackValue(this, value, 3, true));
        }

        private void PushString(string value, int size)
        {
            _stack.Add(new StackValue(this, size, value));
        }

        public StackValue Pop()
        {
            var index = _stack.Count - 1;
            if (index < 0)
                return new StackValue(this, StackValue.Type.Literal, "StackVal");
            var val = _stack[index];
            _stack.RemoveAt(index);
            return val;
        }

        public object Drop()
        {
            var val = Pop();
            if (val.Value.Contains("(") && val.Value.EndsWith(")"))
                if (val.Value.IndexOf("(") > 4)
                    return val.Value + ";";
            return null;
        }

        private StackValue[] PopList(int size)
        {
            var count = 0;
            var items = new List<StackValue>();
            while (count < size)
            {
                var top = Pop();
                switch (top.ItemType)
                {
                    case StackValue.Type.Literal:
                    {
                        items.Add(top);
                        count++;
                        break;
                    }
                    case StackValue.Type.Pointer:
                    {
                        items.Add(new StackValue(this, StackValue.Type.Literal, top.AsPointer));
                        count++;
                        break;
                    }
                    case StackValue.Type.Struct:
                    {
                        if (count + top.StructSize > size)
                            throw new Exception("Struct size too large");
                        count += top.StructSize;
                        items.Add(new StackValue(this, StackValue.Type.Literal, top.Value));
                        break;
                    }
                    default:
                        throw new Exception("Unexpected Stack Type: " + top.ItemType);
                }
            }

            items.Reverse();
            return items.ToArray();
        }

        private StackValue[] PopTest(int size)
        {
            var count = 0;
            var items = new List<StackValue>();
            while (count < size)
            {
                var top = Pop();
                switch (top.ItemType)
                {
                    case StackValue.Type.Literal:
                    {
                        items.Add(top);
                        count++;
                        break;
                    }
                    case StackValue.Type.Pointer:
                    {
                        items.Add(new StackValue(this, StackValue.Type.Literal, top.AsPointer));
                        count++;
                        break;
                    }
                    case StackValue.Type.Struct:
                    {
                        if (count + top.StructSize > size)
                            throw new Exception("Struct size too large");
                        count += top.StructSize;
                        items.Add(new StackValue(this, top.Value, top.StructSize));
                        break;
                    }
                    default:
                        throw new Exception("Unexpected Stack Type: " + top.ItemType);
                }
            }

            items.Reverse();
            return items.ToArray();
        }

        private string PopVector()
        {
            return StackValue.AsVector(PopList(3));
        }

        private StackValue Peek()
        {
            return _stack[_stack.Count - 1];
        }

        public void Dup()
        {
            var top = Peek();
            if (top.Value.Contains("(") && top.Value.Contains(")"))
                Push("Stack.Peek()");
            else
                Push(top);
        }

        private string PeekLit()
        {
            var val = Peek();
            if (val.ItemType != StackValue.Type.Literal)
            {
                if (val.ItemType == StackValue.Type.Pointer)
                    return "&" + val.Value;
                throw new Exception("Not a literal item recieved");
            }

            return val.Value;
        }

        private string PeekPointerRef()
        {
            var val = Peek();
            if (val.ItemType == StackValue.Type.Pointer)
                return val.Value;
            if (val.ItemType == StackValue.Type.Literal)
                return "*(" + val.Value + ")";
            throw new Exception("Not a pointer item recieved");
        }

        public string PopListForCall(int size)
        {
            if (size == 0) return "";
            var items = StackValue.AsCall(PopList(size));
            return items.Remove(items.Length - 2);
        }

        private string[] EmptyStack()
        {
            var stack = new List<string>();
            foreach (var val in _stack)
                switch (val.ItemType)
                {
                    case StackValue.Type.Literal:
                        stack.Add(val.Value);
                        break;
                    case StackValue.Type.Pointer:
                        stack.Add(val.AsPointer);
                        break;
                    case StackValue.Type.Struct:
                        stack.Add(val.Value);
                        break;
                    default:
                        throw new Exception("Unexpeced Stack Type\n" + val.ItemType);
                }

            _stack.Clear();
            return stack.ToArray();
        }

        public string FunctionCall(string name, int pcount, int rcount)
        {
            var functionline = (IsAggregate ? "func_" : name) + "(" + PopListForCall(pcount) + ")";
            if (rcount == 0)
                return functionline + ";";
            if (rcount == 1)
                Push(functionline);
            else if (rcount > 1)
                PushStruct(functionline, rcount);
            else
                throw new Exception("Error in return items count");
            return "";
        }

        public string FunctionCall(Function function)
        {
            var popList = "";
            if (DecodeVarInfo)
            {
                if (function.PCount != 0)
                {
                    var items = PopList(function.PCount);
                    for (var i = 0; i < items.Length; ++i)
                        if (function.Params.GetTypeAtIndex((uint) i).Precedence() < items[i].Datatype.Precedence())
                        {
                            if (function != _parent)
                                function.UpdateFuncParamType((uint) i, items[i].Datatype);
                        }
                        else if (function.Params.GetTypeAtIndex((uint) i) != items[i].Datatype)
                        {
                            items[i].Datatype = function.Params.GetTypeAtIndex((uint) i);
                        }

                    popList = StackValue.AsCall(items);
                    popList = popList.Remove(popList.Length - 2);
                }
            }
            else
            {
                popList = function.PCount > 0 ? PopListForCall(function.PCount) : "";
            }

            var functionline = function.Name + "(" + popList + ")";
            if (IsAggregate) functionline = "func_()"; // Burn the PopList call.
            if (function.RCount == 0)
                return functionline + ";";
            if (function.RCount == 1)
                Push(new StackValue(this, StackValue.Type.Literal, functionline, function));
            else if (function.RCount > 1)
                PushStruct(functionline, function.RCount);
            else
                throw new Exception("Error in return items count");
            return "";
        }

        public string NativeCallTest(ulong hash, string name, int pcount, int rcount)
        {
            Native native;
            if (!Program.X64Npi.FetchNativeCall(hash, name, pcount, rcount, out native))
                throw new Exception("Unknown Exception for Hash: " + hash.ToString("X"));

            var functionline = name + "(";
            var @params = new List<DataType>();
            var count = 0;
            foreach (var val in PopTest(pcount))
                switch (val.ItemType)
                {
                    case StackValue.Type.Literal:
                    {
                        if (val.Variable != null && DecodeVarInfo)
                        {
                            if (val.Variable.DataType.Precedence() < native.GetParam(count).StackType.Precedence())
                                val.Variable.DataType = native.GetParam(count).StackType;
                            else if (val.Variable.DataType.Precedence() > native.GetParam(count).StackType.Precedence())
                                _parent.UpdateNativeParameter(hash, val.Variable.DataType, count);
                        }

                        if (val.Datatype == DataType.Bool || native.GetParam(count).StackType == DataType.Bool)
                        {
                            bool temp;
                            if (bool.TryParse(val.Value, out temp))
                                functionline += temp ? "true, " : "false, ";
                            else if (val.Value == "0")
                                functionline += "false, ";
                            else if (val.Value == "1")
                                functionline += "true, ";
                            else
                                functionline += val.Value + ", ";
                        }
                        else if (val.Datatype == DataType.Int && native.GetParam(count).StackType == DataType.Float)
                        {
                            switch (Program.IntStyle)
                            {
                                case Program.IntType.Int:
                                {
                                    int temp;
                                    if (int.TryParse(val.Value, out temp))
                                    {
                                        temp = Utils.SwapEndian(temp);
                                        var floatval =
                                            Utils.SwapEndian(BitConverter.ToSingle(BitConverter.GetBytes(temp), 0));
                                        functionline += floatval.ToString(CultureInfo.InvariantCulture) + "f, ";
                                    }
                                    else
                                    {
                                        functionline += val.Value + ", ";
                                    }

                                    break;
                                }
                                case Program.IntType.Uint:
                                {
                                    uint tempu;
                                    if (uint.TryParse(val.Value, out tempu))
                                    {
                                        tempu = Utils.SwapEndian(tempu);
                                        var floatval =
                                            Utils.SwapEndian(BitConverter.ToSingle(BitConverter.GetBytes(tempu), 0));
                                        functionline += floatval.ToString(CultureInfo.InvariantCulture) + "f, ";
                                    }
                                    else
                                    {
                                        functionline += val.Value + ", ";
                                    }

                                    break;
                                }
                                case Program.IntType.Hex:
                                {
                                    int temp;
                                    var temps = val.Value;
                                    if (temps.StartsWith("0x"))
                                        temps = temps.Substring(2);
                                    if (int.TryParse(temps, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                        out temp))
                                    {
                                        temp = Utils.SwapEndian(temp);
                                        var floatval =
                                            Utils.SwapEndian(BitConverter.ToSingle(BitConverter.GetBytes(temp), 0));
                                        functionline += floatval.ToString(CultureInfo.InvariantCulture) + "f, ";
                                    }
                                    else
                                    {
                                        functionline += val.Value + ", ";
                                    }

                                    break;
                                }
                                default:
                                    throw new ArgumentException("Invalid IntType");
                            }
                        }
                        else
                        {
                            functionline += val.Value + ", ";
                        }

                        @params.Add(val.Datatype);
                        count++;
                        break;
                    }
                    case StackValue.Type.Pointer:
                    {
                        functionline += val.AsPointer + " ";
                        if (val.Datatype.PointerType() != DataType.Unk)
                            @params.Add(val.Datatype.PointerType());
                        else
                            @params.Add(val.Datatype);
                        count++;
                        break;
                    }
                    case StackValue.Type.Struct:
                    {
                        functionline += val.Value + ", ";
                        if (val.StructSize == 3 && val.Datatype == DataType.Vector3)
                        {
                            @params.AddRange(new[] {DataType.Float, DataType.Float, DataType.Float});
                            count += 3;
                        }
                        else
                        {
                            for (var i = 0; i < val.StructSize; i++)
                            {
                                @params.Add(DataType.Unk);
                                count++;
                            }
                        }

                        break;
                    }
                    default:
                        throw new Exception("Unexpeced Stack Type\n" + val.ItemType);
                }

            if (pcount > 0)
                functionline = functionline.Remove(functionline.Length - 2) + ")";
            else
                functionline += ")";

            if (rcount == 0)
            {
                Program.X64Npi.UpdateNative(hash, DataType.None, @params.ToArray());
                return functionline + ";";
            }

            if (rcount == 1)
            {
                PushNative(functionline,
                    Program.X64Npi.UpdateNative(hash, Program.X64Npi.GetReturnType(hash), @params.ToArray()));
            }
            else if (rcount > 1)
            {
                Native n = null;
                if (rcount == 2)
                    n = Program.X64Npi.UpdateNative(hash, DataType.Unk, @params.ToArray());
                else if (rcount == 3)
                    n = Program.X64Npi.UpdateNative(hash, DataType.Vector3, @params.ToArray());
                else
                    throw new Exception("Error in return items count");
                PushStructNative(functionline, n, rcount);
            }
            else
            {
                throw new Exception("Error in return items count");
            }

            return "";
        }

        #region Opcodes

        public void Op_Add()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop();
            if (s1.ItemType == StackValue.Type.Literal && s2.ItemType == StackValue.Type.Literal)
                Push("(" + s2.AsType(DataType.Int).Value + " + " + s1.AsType(DataType.Int).Value + ")", DataType.Int);
            else if (s2.ItemType == StackValue.Type.Pointer && s1.ItemType == StackValue.Type.Literal)
                Push("(&" + s2.UnifyType(s1).Value + " + " + s1.UnifyType(s2).Value + ")");
            else if (s1.ItemType == StackValue.Type.Pointer && s2.ItemType == StackValue.Type.Literal)
                Push("(&" + s1.UnifyType(s2).Value + " + " + s2.UnifyType(s1).Value + ")");
            else if (s1.ItemType == StackValue.Type.Pointer && s2.ItemType == StackValue.Type.Pointer)
                Push("(" + s1.UnifyType(s2).Value + " + " + s2.UnifyType(s1).Value + ") /* PointerArith */");
            else
                throw new Exception("Unexpected stack value");
        }

        public void Op_Addf()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop();
            Push("(" + s2.AsType(DataType.Float).AsLiteral + " + " + s1.AsType(DataType.Float).AsLiteral + ")",
                DataType.Float);
        }

        public void Op_Sub()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop();
            if (s1.ItemType == StackValue.Type.Literal && s2.ItemType == StackValue.Type.Literal)
                Push("(" + s2.AsType(DataType.Int).Value + " - " + s1.AsType(DataType.Int).Value + ")", DataType.Int);
            else if (s2.ItemType == StackValue.Type.Pointer && s1.ItemType == StackValue.Type.Literal)
                Push("(&" + s2.UnifyType(s1).Value + " - " + s1.UnifyType(s2).Value + ")");
            else if (s1.ItemType == StackValue.Type.Pointer && s2.ItemType == StackValue.Type.Literal)
                Push("(&" + s1.UnifyType(s2).Value + " - " + s2.UnifyType(s1).Value + ")");
            else if (s1.ItemType == StackValue.Type.Pointer && s2.ItemType == StackValue.Type.Pointer)
                Push("(" + s1.UnifyType(s2).Value + " - " + s2.UnifyType(s1).Value + ") /* PointerArith */");
            else
                throw new Exception("Unexpected stack value");
        }

        public void Op_Subf()
        {
            StackValue s1, s2;
            s1 = Pop().AsType(DataType.Float);
            s2 = Pop().AsType(DataType.Float);
            Push("(" + s2.AsLiteral + " - " + s1.AsLiteral + ")", DataType.Float);
        }

        public void Op_Mult()
        {
            StackValue s1, s2;
            s1 = Pop().AsType(DataType.Int);
            s2 = Pop().AsType(DataType.Int);
            Push("(" + s2.AsLiteral + " * " + s1.AsLiteral + ")", DataType.Int);
        }

        public void Op_Multf()
        {
            StackValue s1, s2;
            s1 = Pop().AsType(DataType.Float);
            s2 = Pop().AsType(DataType.Float);
            Push("(" + s2.AsLiteral + " * " + s1.AsLiteral + ")", DataType.Float);
        }

        public void Op_Div()
        {
            StackValue s1, s2;
            s1 = Pop().AsType(DataType.Int);
            s2 = Pop().AsType(DataType.Int);
            Push("(" + s2.AsLiteral + " / " + s1.AsLiteral + ")", DataType.Int);
        }

        public void Op_Divf()
        {
            StackValue s1, s2;
            s1 = Pop().AsType(DataType.Float);
            s2 = Pop().AsType(DataType.Float);
            Push("(" + s2.AsLiteral + " / " + s1.AsLiteral + ")", DataType.Float);
        }

        public void Op_Mod()
        {
            StackValue s1, s2;
            s1 = Pop().AsType(DataType.Int);
            s2 = Pop().AsType(DataType.Int);
            Push("(" + s2.AsLiteral + " % " + s1.AsLiteral + ")", DataType.Int);
        }

        public void Op_Modf()
        {
            StackValue s1, s2;
            s1 = Pop().AsType(DataType.Float);
            s2 = Pop().AsType(DataType.Float);
            Push("(" + s2.AsLiteral + " % " + s1.AsLiteral + ")", DataType.Float);
        }

        public void Op_Not()
        {
            var s1 = Pop().AsType(DataType.Bool);
            var s1V = s1.AsLiteral;
            if (s1V.StartsWith("!(") && s1V.EndsWith(")"))
            {
                PushCond(s1V.Remove(s1V.Length - 1).Substring(2));
            }
            else if (s1V.StartsWith("(") && s1V.EndsWith(")"))
            {
                PushCond("!" + s1V);
            }
            else if (!(s1V.Contains("&&") && s1V.Contains("||") && s1V.Contains("^")))
            {
                if (s1V.StartsWith("!"))
                    PushCond(s1V.Substring(1));
                else
                    PushCond("!" + s1V);
            }
            else
            {
                PushCond("!(" + s1V + ")");
            }
        }

        public void Op_Neg()
        {
            StackValue s1;
            s1 = Pop().AsType(DataType.Int);
            Push("-" + s1.AsLiteral, DataType.Int);
        }

        public void Op_Negf()
        {
            StackValue s1;
            s1 = Pop().AsType(DataType.Float);
            Push("-" + s1.AsLiteral, DataType.Float);
        }

        public void Op_CmpEQ()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop().UnifyType(s1);
            s1.UnifyType(s2);
            PushCond(s2.AsLiteral + " == " + s1.AsLiteral);
        }

        public void Op_CmpNE()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop().UnifyType(s1);
            s1.UnifyType(s2);
            PushCond(s2.AsLiteral + " != " + s1.AsLiteral);
        }

        public void Op_CmpGE()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop().UnifyType(s1);
            s1.UnifyType(s2);
            PushCond(s2.AsLiteral + " >= " + s1.AsLiteral);
        }

        public void Op_CmpGT()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop().UnifyType(s1);
            s1.UnifyType(s2);
            PushCond(s2.AsLiteral + " > " + s1.AsLiteral);
        }

        public void Op_CmpLE()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop().UnifyType(s1);
            s1.UnifyType(s2);
            PushCond(s2.AsLiteral + " <= " + s1.AsLiteral);
        }

        public void Op_CmpLT()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop().UnifyType(s1);
            s1.UnifyType(s2);
            PushCond(s2.AsLiteral + " < " + s1.AsLiteral);
        }

        public void Op_Vadd()
        {
            string s1, s2;
            s1 = PopVector();
            s2 = PopVector();
            PushVector(s2 + " + " + s1);
        }

        public void Op_VSub()
        {
            string s1, s2;
            s1 = PopVector();
            s2 = PopVector();
            PushVector(s2 + " - " + s1);
        }

        public void Op_VMult()
        {
            string s1, s2;
            s1 = PopVector();
            s2 = PopVector();
            PushVector(s2 + " * " + s1);
        }

        public void Op_VDiv()
        {
            string s1, s2;
            s1 = PopVector();
            s2 = PopVector();
            PushVector(s2 + " / " + s1);
        }

        public void Op_VNeg()
        {
            string s1;
            s1 = PopVector();
            PushVector("-" + s1);
        }

        public void Op_FtoV()
        {
            var top = Pop();
            if (top.Value.Contains("(") && top.Value.Contains(")"))
            {
                PushVector("FtoV(" + top.Value + ")");
            }
            else
            {
                Push(top.Value, DataType.Float);
                Push(top.Value, DataType.Float);
                Push(top.Value, DataType.Float);
            }
        }

        public void Op_Itof()
        {
            Push("IntToFloat(" + Pop().AsType(DataType.Int).AsLiteral + ")", DataType.Float);
        }

        public void Op_FtoI()
        {
            Push("FloatToInt(" + Pop().AsType(DataType.Float).AsLiteral + ")", DataType.Int);
        }

        public void Op_And()
        {
            var s1 = Pop();
            var s2 = Pop();
            int temp;
            if (s1.ItemType == StackValue.Type.Pointer && s1.ItemType == StackValue.Type.Pointer)
                Push("(" + s2.UnifyType(s1).Value + " && " + s1.UnifyType(s1).Value + ") /* PointerArith */");
            else if (s1.ItemType != StackValue.Type.Literal && s2.ItemType != StackValue.Type.Literal)
                throw new Exception("Not a literal item recieved: " + s1.ItemType + " " + s2.ItemType);
            else if (s1.Datatype == DataType.Bool || s2.Datatype == DataType.Bool)
                PushCond("(" + s2.AsType(DataType.Bool).Value + " && " + s1.AsType(DataType.Bool).Value + ")");
            else if (Utils.IntParse(s1.Value, out temp) || Utils.IntParse(s2.Value, out temp))
                Push(s2.AsType(DataType.Int).Value + " & " + s1.AsType(DataType.Int).Value, DataType.Int);
            else
                Push("(" + s2.UnifyType(s1).Value + " && " + s1.UnifyType(s2).Value + ")");
        }

        public void Op_Or()
        {
            var s1 = Pop();
            var s2 = Pop();
            int temp;
            if (s1.ItemType == StackValue.Type.Pointer && s1.ItemType == StackValue.Type.Pointer)
                Push("(" + s2.Value + " || " + s1.Value + ") /* PointerArith */");
            else if (s1.ItemType != StackValue.Type.Literal && s2.ItemType != StackValue.Type.Literal)
                throw new Exception("Not a literal item recieved: " + s1.ItemType + " " + s2.ItemType);
            else if (s1.Datatype == DataType.Bool || s2.Datatype == DataType.Bool)
                PushCond("(" + s2.AsType(DataType.Bool).Value + " || " + s1.AsType(DataType.Bool).Value + ")");
            else if (Utils.IntParse(s1.Value, out temp) || Utils.IntParse(s2.Value, out temp))
                Push(s2.AsType(DataType.Int).Value + " | " + s1.AsType(DataType.Int).Value, DataType.Int);
            else
                Push("(" + s2.UnifyType(s1).Value + " || " + s1.UnifyType(s2).Value + ")");
        }

        public void Op_Xor()
        {
            StackValue s1, s2;
            s1 = Pop();
            s2 = Pop();
            Push(s2.AsType(DataType.Int).AsLiteral + " ^ " + s1.AsType(DataType.Int).AsLiteral, DataType.Int);
        }

        public void Op_GetImm(uint immediate)
        {
            if (PeekVar(0)?.Immediatesize == 3)
                switch (immediate)
                {
                    case 1:
                    {
                        var saccess = Pop().AsStructAccess;
                        if (IsAggregate && Aggregate.Instance.CanAggregateLiteral(saccess))
                            Push(new StackValue(this, StackValue.Type.Literal, saccess));
                        else
                            Push(new StackValue(this, StackValue.Type.Literal, saccess + "y"));
                        return;
                    }
                    case 2:
                    {
                        var saccess = Pop().AsStructAccess;
                        if (IsAggregate && Aggregate.Instance.CanAggregateLiteral(saccess))
                            Push(new StackValue(this, StackValue.Type.Literal, saccess));
                        else
                            Push(new StackValue(this, StackValue.Type.Literal, saccess + "z"));
                        return;
                    }
                }

            var structAss = Pop().AsStructAccess;
            if (IsAggregate)
            {
                if (Aggregate.Instance.CanAggregateLiteral(structAss))
                    Push(new StackValue(this, StackValue.Type.Literal, structAss + "f_"));
                else
                    Push(new StackValue(this, StackValue.Type.Literal,
                        structAss + "f_" + (Program.HexIndex ? immediate.ToString("X") : immediate.ToString())));
            }
            else
            {
                Push(new StackValue(this, StackValue.Type.Literal,
                    structAss + "f_" + (Program.HexIndex ? immediate.ToString("X") : immediate.ToString())));
            }
        }

        public string Op_SetImm(uint immediate)
        {
            StackValue pointer, value;
            pointer = Pop();
            value = Pop();

            var imm = "";
            if (IsAggregate && Aggregate.Instance.CanAggregateLiteral(value.AsLiteral))
            {
                imm = "f_";
            }
            else
            {
                imm = "f_" + (Program.HexIndex ? immediate.ToString("X") : immediate.ToString());
                if (PeekVar(0)?.DataType == DataType.Vector3)
                    switch (immediate)
                    {
                        case 0:
                            imm = "x";
                            break;
                        case 1:
                            imm = "y";
                            break;
                        case 2:
                            imm = "z";
                            break;
                    }
            }

            return Setcheck(pointer.AsStructAccess + imm, value.AsLiteral, value.LiteralComment);
        }

        public void Op_GetImmP(uint immediate)
        {
            var saccess = Pop().AsStructAccess;
            if (IsAggregate && Aggregate.Instance.CanAggregateLiteral(saccess))
                Push(new StackValue(this, StackValue.Type.Pointer, saccess + "f_"));
            else
                Push(new StackValue(this, StackValue.Type.Pointer,
                    saccess + "f_" + (Program.HexIndex ? immediate.ToString("X") : immediate.ToString())));
        }

        public void Op_GetImmP()
        {
            var immediate = Pop().AsLiteral;
            var saccess = Pop().AsStructAccess;

            int temp;
            if (IsAggregate && Aggregate.Instance.CanAggregateLiteral(saccess))
            {
                if (Utils.IntParse(immediate, out temp))
                    Push(new StackValue(this, StackValue.Type.Pointer, saccess + "f_"));
                else
                    Push(new StackValue(this, StackValue.Type.Pointer, saccess + "f_[]"));
            }
            else
            {
                if (Utils.IntParse(immediate, out temp))
                    Push(new StackValue(this, StackValue.Type.Pointer,
                        saccess + "f_" + (Program.HexIndex ? temp.ToString("X") : temp.ToString())));
                else
                    Push(new StackValue(this, StackValue.Type.Pointer, saccess + "f_[" + immediate + "]"));
            }
        }

        /// <summary>
        ///     returns a string saying the size of an array if its > 1
        /// </summary>
        /// <param name="immediate"></param>
        /// <returns></returns>
        private string Getarray(uint immediate)
        {
            if (!Program.ShowArraySize)
                return "";
            if (immediate == 1)
                return "";
            if (IsAggregate)
                return "";
            return " /*" + immediate + "*/";
        }

        public string PopArrayAccess()
        {
            var val = Pop();
            if (val.ItemType == StackValue.Type.Pointer)
                return val.Value;
            if (val.ItemType == StackValue.Type.Literal)
                return $"(*{val.Value})";
            throw new Exception("Not a pointer item recieved");
        }

        public void Op_ArrayGet(uint immediate)
        {
            var arrayloc = PopArrayAccess();
            var index = Pop().AsLiteral;
            Push(new StackValue(this, StackValue.Type.Literal, arrayloc + "[" + index + Getarray(immediate) + "]"));
        }

        public string Op_ArraySet(uint immediate)
        {
            StackValue index, value;
            var arrayloc = PopArrayAccess();
            index = Pop();
            value = Pop();
            return Setcheck(arrayloc + "[" + index.AsLiteral + Getarray(immediate) + "]", value.AsLiteral,
                value.LiteralComment);
        }

        public void Op_ArrayGetP(uint immediate)
        {
            string arrayloc;
            string index;
            if (Peek().ItemType == StackValue.Type.Pointer)
            {
                arrayloc = PopArrayAccess();
                index = Pop().AsLiteral;
                Push(new StackValue(this, StackValue.Type.Pointer, arrayloc + "[" + index + Getarray(immediate) + "]"));
            }
            else if (Peek().ItemType == StackValue.Type.Literal)
            {
                arrayloc = Pop().AsLiteral;
                index = Pop().AsLiteral;
                Push(new StackValue(this, StackValue.Type.Literal, arrayloc + "[" + index + Getarray(immediate) + "]"));
            }
            else
            {
                throw new Exception("Unexpected Stack Value :" + Peek().ItemType);
            }
        }

        public void Op_RefGet()
        {
            Push(new StackValue(this, StackValue.Type.Literal, Pop().AsPointerRef));
        }

        public void Op_ToStack()
        {
            string pointer, count;
            int amount;
            if (TopType == DataType.StringPtr || TopType == DataType.String)
            {
                pointer = Pop().AsPointerRef;
                count = Pop().AsLiteral;

                if (!Utils.IntParse(count, out amount))
                    throw new Exception("Expecting the amount to push");
                PushString(pointer, amount);
            }
            else
            {
                pointer = Pop().AsPointerRef;
                count = Pop().AsLiteral;

                if (!Utils.IntParse(count, out amount))
                    throw new Exception("Expecting the amount to push");
                PushStruct(pointer, amount);
            }
        }

        private int GetIndex(int index)
        {
            var actindex = 0;
            if (_stack.Count == 0) return -1;
            for (var i = 0; i < index; i++)
            {
                var stackIndex = _stack.Count - i - 1;
                if (stackIndex < 0)
                    return -1;

                if (_stack[stackIndex].ItemType == StackValue.Type.Struct &&
                    _stack[stackIndex].Datatype != DataType.Vector3)
                    index -= _stack[stackIndex].StructSize - 1;
                if (i < index)
                    actindex++;
            }

            return actindex < _stack.Count ? actindex : -1;
        }

        public string PeekItem(int index)
        {
            var newIndex = GetIndex(index);
            if (newIndex == -1) return "";
            var val = _stack[_stack.Count - newIndex - 1];
            if (val.ItemType != StackValue.Type.Literal)
            {
                if (val.ItemType == StackValue.Type.Pointer)
                    return "&" + val.Value;
                throw new Exception("Not a literal item recieved");
            }

            return val.Value;
        }

        public VarsInfo.Var PeekVar(int index)
        {
            var newIndex = GetIndex(index);
            if (newIndex == -1) return null;
            return _stack[_stack.Count - newIndex - 1].Variable;
        }

        public Function PeekFunc(int index)
        {
            var newIndex = GetIndex(index);
            if (newIndex == -1) return null;
            return _stack[_stack.Count - newIndex - 1].Function;
        }

        public Native PeekNat64(int index)
        {
            var newIndex = GetIndex(index);
            if (newIndex == -1) return null;
            return _stack[_stack.Count - newIndex - 1].Native;
        }

        public bool Isnat(int index)
        {
            var newIndex = GetIndex(index);
            if (newIndex == -1) return false;
            return _stack[_stack.Count - newIndex - 1].IsNative;
        }

        public bool IsPointer(int index)
        {
            var newIndex = GetIndex(index);
            if (newIndex == -1) return false;
            return _stack[_stack.Count - newIndex - 1].ItemType == StackValue.Type.Pointer;
        }

        public bool IsLiteral(int index)
        {
            var newIndex = GetIndex(index);
            if (newIndex == -1) return false;
            return _stack[_stack.Count - newIndex - 1].ItemType == StackValue.Type.Literal;
        }

        public void PushNative(string value, Native native)
        {
            Push(new StackValue(this, value, native));
        }

        public void PushStructNative(string value, Native native, int structsize)
        {
            Push(new StackValue(this, value, structsize, native));
        }

        public DataType ItemType(int index)
        {
            var newIndex = GetIndex(index);
            if (newIndex == -1) return DataType.Unk;
            return _stack[_stack.Count - newIndex - 1].Datatype;
        }

        public string Op_FromStack()
        {
            string pointer, count;
            pointer = Pop().AsPointerRef;
            count = Pop().AsLiteral;
            int amount;
            if (!Utils.IntParse(count, out amount))
                throw new Exception("Expecting the amount to push");
            return StackValue.AsList(pointer, PopList(amount));
        }

        public void Op_AmmImm(int immediate)
        {
            if (immediate < 0)
                Push(Pop().AsLiteral + " - " + -immediate);
            else if (immediate > 0)
                Push(Pop().AsLiteral + " + " + immediate);
            //else if (immediate == 0) { }
        }

        public void Op_MultImm(int immediate)
        {
            Push(Pop().AsLiteral + " * " + immediate);
        }

        public string Op_RefSet()
        {
            StackValue pointer, value;
            pointer = Pop();
            value = Pop();
            return Setcheck(pointer.AsPointerRef, value.AsLiteral, value.LiteralComment);
        }

        public string Op_PeekSet()
        {
            string pointer, value;
            value = Pop().AsLiteral;
            pointer = Peek().AsPointerRef;
            return Setcheck(pointer, value);
        }

        public string Op_Set(string location)
        {
            var set = Pop();
            return Setcheck(location, set.AsLiteral, set.LiteralComment);
        }

        public string Op_Set(string location, VarsInfo.Var variable)
        {
            return Op_Set(location + (variable.Immediatesize == 3 ? ".x" : ""));
        }

        public void Op_Hash()
        {
            Push("Hash(" + Pop().AsLiteral + ")", DataType.Int);
        }

        public string Op_StrCpy(int size)
        {
            var pointer = Pop().AsType(DataType.StringPtr);
            var pointer2 = Pop().AsType(DataType.StringPtr);
            return "StringCopy(" + pointer.AsPointer + ", " + pointer2.AsPointer + ", " + size + ");";
        }

        public string Op_StrAdd(int size)
        {
            var pointer = Pop().AsType(DataType.StringPtr);
            var pointer2 = Pop().AsType(DataType.StringPtr);
            return "StringConCat(" + pointer.AsPointer + ", " + pointer2.AsPointer + ", " + size + ");";
        }

        public string Op_StrAddI(int size)
        {
            var pointer = Pop().AsType(DataType.StringPtr).AsPointer;
            var inttoadd = Pop().AsType(DataType.Int).AsLiteral;
            return "StringIntConCat(" + pointer + ", " + inttoadd + ", " + size + ");";
        }

        public string Op_ItoS(int size)
        {
            var pointer = Pop().AsPointer;
            var intval = Pop().AsLiteral;
            return "IntToString(" + pointer + ", " + intval + ", " + size + ");";
        }

        public string Op_SnCopy()
        {
            var pointer = Pop().AsPointer;
            var value = Pop().AsLiteral;
            var count = Pop().AsLiteral;
            int amount;
            if (!Utils.IntParse(count, out amount))
                throw new Exception("Int Stack value expected");
            return "MemCopy(" + pointer + ", " + "{" + PopListForCall(amount) + "}, " + value + ");";
        }

        public string[] Pcall()
        {
            var temp = new List<string>();
            var loc = Pop().AsLiteral;
            foreach (var s in EmptyStack())
                temp.Add("Stack.Push(" + s + ");");
            temp.Add("Call_Loc(" + loc + ");");
            return temp.ToArray();
        }

        /// <summary>
        ///     Detects if you can use var++, var *= num etc
        /// </summary>
        /// <param name="loc"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public string Setcheck(string loc, string value, string suffix = "")
        {
            if (!value.StartsWith(loc + " ")) return loc + " = " + value + ";" + suffix;

            var temp = value.Substring(loc.Length + 1);
            var op = temp.Remove(temp.IndexOf(' '));
            var newval = temp.Substring(temp.IndexOf(' ') + 1);
            if (newval == "1" || newval == "1f")
            {
                if (op == "+")
                    return loc + "++;";
                if (op == "-")
                    return loc + "--;";
            }

            return loc + " " + op + "= " + newval + ";" + suffix;
        }

        #endregion

        #region subclasses

        public enum DataType
        {
            Int,
            IntPtr,
            Float,
            FloatPtr,
            String,
            StringPtr,
            Bool,
            BoolPtr,
            Unk,
            UnkPtr,
            Unsure,
            None, //For Empty returns
            Vector3,
            Vector3Ptr
        }

        public class StackValue
        {
            public enum Type
            {
                Literal,
                Pointer,
                Struct
            }

            private DataType _datatype;
            private bool _global;

            private readonly Stack _parent;

            public StackValue(Stack parent, Type type, string value, DataType datatype = DataType.Unk)
            {
                _parent = parent;
                ItemType = type;
                Value = value;
                _datatype = datatype;
            }

            public StackValue(Stack parent, Type type, VarsInfo.Var var, string suffix = "") : this(parent, type,
                var.Name + suffix, var.DataType)
            {
                Variable = var;
            }

            public StackValue(Stack parent, Type type, string name, Function function) : this(parent, type, name,
                function.ReturnType)
            {
                Function = function;
            }

            public StackValue(Stack parent, string value, Native native) : this(parent, Type.Literal, value,
                native.ReturnParam.StackType)
            {
                Native = native;
            }

            public StackValue(Stack parent, string value, int structsize, Native native) : this(parent, Type.Struct,
                value, native.ReturnParam.StackType)
            {
                Native = native;
                StructSize = structsize;
            }

            public StackValue(Stack parent, string value, int structsize, bool vector = false) : this(parent,
                Type.Struct, value, vector && structsize == 3 ? DataType.Vector3 : DataType.Unk)
            {
                StructSize = structsize;
            }

            public StackValue(Stack parent, int stringsize, string value) : this(parent, Type.Struct, value,
                DataType.String)
            {
                StructSize = stringsize;
            }

            public string Value { get; }

            public Type ItemType { get; }

            private bool IsLiteral => ItemType == Type.Literal;
            public int StructSize { get; }

            public VarsInfo.Var Variable { get; }

            public Function Function { get; }

            public Native Native { get; }

            public bool IsNative => Native != null;
            public bool IsNotVar => Variable == null && !_global;

            public DataType Datatype
            {
                get
                {
                    if (_parent.DecodeVarInfo)
                    {
                        if (Native != null && IsLiteral) return Native.ReturnParam.StackType;
                        if (Variable != null && IsLiteral) return Variable.DataType;
                        if (Function != null && IsLiteral) return Function.ReturnType;
                    }

                    return _datatype;
                }

                set
                {
                    if (_parent.DecodeVarInfo)
                    {
                        _datatype = PrecendenceSet(_datatype, value);
                        if (Native != null && IsLiteral)
                            _parent._parent.UpdateNativeReturnType(Native.Hash,
                                PrecendenceSet(Native.ReturnParam.StackType, value));
                        if (Variable != null && IsLiteral) Variable.DataType = PrecendenceSet(Variable.DataType, value);
                        if (Function != null && IsLiteral)
                            Function.ReturnType = PrecendenceSet(Function.ReturnType, value);
                    }
                    else
                    {
                        _datatype = value;
                    }
                }
            }

            public object AsDrop
            {
                get
                {
                    if (Value != null && Value.Contains("(") && Value.EndsWith(")"))
                        if (Value.IndexOf("(") > 4)
                            return Value + ";";
                    return null;
                }
            }

            public string AsLiteralStatement => AsLiteral + LiteralComment;

            public string AsLiteral
            {
                get
                {
                    if (ItemType != Type.Literal)
                    {
                        if (ItemType == Type.Pointer)
                            return "&" + Value;
                        throw new Exception("Not a literal item recieved");
                    }

                    return Value;
                }
            }

            public string LiteralComment
            {
                get
                {
                    int temp;
                    if (ItemType == Type.Literal && Datatype == DataType.Int && int.TryParse(Value, out temp))
                        return Program.Gxtbank.GetEntry(temp, true);
                    return "";
                }
            }

            public string AsPointer
            {
                get
                {
                    if (ItemType == Type.Pointer)
                    {
                        if (IsNotVar)
                            return "&(" + Value + ")";
                        return "&" + Value;
                    }

                    if (ItemType == Type.Literal) return Value;

                    throw new Exception("Not a pointer item recieved");
                }
            }

            public string AsPointerRef
            {
                get
                {
                    if (ItemType == Type.Pointer)
                        return Value;
                    if (ItemType == Type.Literal)
                        return "*" + (Value.Contains(" ") ? "(" + Value + ")" : Value);
                    throw new Exception("Not a pointer item recieved");
                }
            }

            public string AsStructAccess
            {
                get
                {
                    if (ItemType == Type.Pointer)
                        return Value + ".";
                    if (ItemType == Type.Literal)
                        return (Value.Contains(" ") ? "(" + Value + ")" : Value) + "->";
                    throw new Exception("Not a pointer item recieved");
                }
            }

            public static StackValue Global(Stack parent, Type type, string name)
            {
                var g = new StackValue(parent, type, name);
                g._global = true;
                return g;
            }

            private static DataType PrecendenceSet(DataType a, DataType b)
            {
                return a.Precedence() < b.Precedence() ? b : a;
            }

            public StackValue AsType(DataType t)
            {
                if (_parent.DecodeVarInfo) Datatype = t;
                return this;
            }

            public StackValue UnifyType(StackValue other)
            {
                if (_parent.DecodeVarInfo)
                    if (Datatype != DataType.Unk && Datatype != DataType.UnkPtr && Datatype != DataType.Unsure)
                        Datatype = other.Datatype;
                return this;
            }

            public static string AsVector(StackValue[] data)
            {
                switch (data.Length)
                {
                    case 1:
                        data[0]._datatype = DataType.Vector3;
                        return data[0].AsLiteral;
                    case 3:
                        return "Vector(" + data[2].AsType(DataType.Float).AsLiteral + ", " +
                               data[1].AsType(DataType.Float).AsLiteral + ", " +
                               data[0].AsType(DataType.Float).AsLiteral + ")";
                    case 2:
                        return "Vector(" + data[1].AsType(DataType.Float).AsLiteral + ", " +
                               data[0].AsType(DataType.Float).AsLiteral + ")";
                }

                throw new Exception("Unexpected data length");
            }

            public static string AsList(string prefix, StackValue[] data)
            {
                var res = prefix + " = { ";
                foreach (var val in data)
                    res += val.AsLiteralStatement + ", ";
                return res.Remove(res.Length - 2) + " };";
            }

            public static string AsCall(StackValue[] data)
            {
                if (data.Length == 0) return "";

                var items = "";
                foreach (var val in data)
                    switch (val.ItemType)
                    {
                        case Type.Literal:
                            items += val.AsLiteralStatement + ", ";
                            break;
                        case Type.Pointer:
                            items += val.AsPointer + ", ";
                            break;
                        case Type.Struct:
                            items += val.Value + ", ";
                            break;
                        default:
                            throw new Exception("Unexpeced Stack Type\n" + val.ItemType);
                    }

                return items;
            }
        }

        [Serializable]
        private class StackEmptyException : Exception
        {
            public StackEmptyException()
            {
            }

            public StackEmptyException(string message) : base(message)
            {
            }

            public StackEmptyException(string message, Exception innerexception) : base(message, innerexception)
            {
            }
        }

        #endregion
    }

    public static class DataTypeExtensions
    {
        public static bool IsUnknown(this Stack.DataType c)
        {
            return c == Stack.DataType.Unk || c == Stack.DataType.UnkPtr;
        }

        public static string ReturnType(this Stack.DataType c)
        {
            return LongName(c) + " ";
        }

        public static string VarArrayDeclaration(this Stack.DataType c)
        {
            return LongName(c) + "[] " + ShortName(c);
        }

        public static string VarDeclaration(this Stack.DataType c)
        {
            return LongName(c) + " " + ShortName(c);
        }

        /// <summary>
        /// </summary>
        public static Stack.DataType PointerType(this Stack.DataType c)
        {
            switch (c)
            {
                case Stack.DataType.Int: return Stack.DataType.IntPtr;
                case Stack.DataType.Unk: return Stack.DataType.UnkPtr;
                case Stack.DataType.Float: return Stack.DataType.FloatPtr;
                case Stack.DataType.Bool: return Stack.DataType.BoolPtr;
                case Stack.DataType.Vector3: return Stack.DataType.Vector3Ptr;
                default: return Stack.DataType.Unk;
            }
        }

        /// <summary>
        /// </summary>
        public static Stack.DataType BaseType(this Stack.DataType c)
        {
            switch (c)
            {
                case Stack.DataType.IntPtr: return Stack.DataType.Int;
                case Stack.DataType.UnkPtr: return Stack.DataType.Unk;
                case Stack.DataType.FloatPtr: return Stack.DataType.Float;
                case Stack.DataType.BoolPtr: return Stack.DataType.Bool;
                case Stack.DataType.Vector3Ptr: return Stack.DataType.Vector3;
                default: return Stack.DataType.Unk;
            }
        }

        /// <summary>
        ///     Conversion of stack datatypes to precedence integers.
        /// </summary>
        public static int Precedence(this Stack.DataType c)
        {
            switch (c)
            {
                case Stack.DataType.Unsure:
                    return 1;
                case Stack.DataType.Vector3:
                    return 2;
                case Stack.DataType.BoolPtr:
                case Stack.DataType.Int:
                case Stack.DataType.IntPtr:
                case Stack.DataType.String:
                case Stack.DataType.StringPtr:
                case Stack.DataType.Vector3Ptr:
                case Stack.DataType.Float:
                case Stack.DataType.FloatPtr:
                    return 3;
                case Stack.DataType.Bool:
                case Stack.DataType.None:
                    return 4;

                case Stack.DataType.UnkPtr:
                case Stack.DataType.Unk:
                default:
                    return 0;
            }
        }

        /// <summary>
        ///     Conversion of stack datatypes to string/type labels.
        /// </summary>
        public static string LongName(this Stack.DataType c)
        {
            switch (c)
            {
                case Stack.DataType.Bool: return "bool";
                case Stack.DataType.BoolPtr: return "bool*";
                case Stack.DataType.Float: return "float";
                case Stack.DataType.FloatPtr: return "float*";
                case Stack.DataType.Int: return "int";
                case Stack.DataType.IntPtr: return "int*";
                case Stack.DataType.String: return "char[]";
                case Stack.DataType.StringPtr: return "char*";
                case Stack.DataType.Vector3: return "Vector3";
                case Stack.DataType.Vector3Ptr: return "Vector3*";
                case Stack.DataType.None: return "void";
                case Stack.DataType.Unk: return "var";
                case Stack.DataType.UnkPtr: return "var*";
                case Stack.DataType.Unsure:
                default:
                    return "var";
            }
        }

        /// <summary>
        /// </summary>
        public static string ShortName(this Stack.DataType c)
        {
            switch (c)
            {
                case Stack.DataType.Bool: return "b";
                case Stack.DataType.BoolPtr: return "b";
                case Stack.DataType.Float: return "f";
                case Stack.DataType.FloatPtr: return "f";
                case Stack.DataType.Int: return "i";
                case Stack.DataType.IntPtr: return "i";
                case Stack.DataType.String: return "c";
                case Stack.DataType.StringPtr: return "s";
                case Stack.DataType.Vector3: return "v";
                case Stack.DataType.Vector3Ptr: return "v";
                case Stack.DataType.None: return "f";
                case Stack.DataType.Unk: return "u";
                case Stack.DataType.UnkPtr: return "u";
                case Stack.DataType.Unsure: return "u";
                default: return "u";
            }
        }
    }
}
