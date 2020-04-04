using System;
using System.Collections.Generic;
using System.Text;

namespace RageDecompiler
{
    /// <summary>
    ///     This is what i use for detecting if a variable is a int/float/bool/struct/array etc
    ///     ToDo: work on proper detection of Vector3 and Custom script types(Entities, Ped, Vehicles etc)
    /// </summary>
    public class VarsInfo
    {
        public enum ListType
        {
            Statics,
            Params,
            Vars
        }

        private int _count;
        private readonly ListType _listtype; //static/function_var/parameter
        private int _scriptParamCount;

        private Dictionary<int, int>
            _varRemapper; //not necessary, just shifts variables up if variables before are bigger than 1 DWORD

        private readonly List<Var> _vars;

        public VarsInfo(ListType type, int varcount, bool isAggregate = false)
        {
            _listtype = type;
            _vars = new List<Var>();
            for (var i = 0; i < varcount; i++)
                _vars.Add(new Var(this, i));
            _count = varcount;

            IsAggregate = isAggregate;
        }

        public VarsInfo(ListType type, bool isAggregate = false)
        {
            _listtype = type;
            _vars = new List<Var>();

            IsAggregate = isAggregate;
        }

        private int ScriptParamStart => _vars.Count - _scriptParamCount;

        public bool IsAggregate { get; } // Stateless variable information.

        public void AddVar(int value)
        {
            _vars.Add(new Var(this, _vars.Count, value)); //only used for static variables that are pre assigned
        }

        public void AddVar(long value)
        {
            _vars.Add(new Var(this, _vars.Count, value));
        }

        public void Checkvars()
        {
            _varRemapper = new Dictionary<int, int>();
            for (int i = 0, k = 0; i < _vars.Count; i++)
            {
                if (!_vars[i].IsUsed)
                    continue;
                if (_listtype == ListType.Vars && !_vars[i].IsCalled)
                    continue;
                if (_vars[i].IsArray)
                    for (var j = i + 1; j < i + 1 + _vars[i].Value * _vars[i].Immediatesize; j++)
                        _vars[j].Dontuse();
                else if (_vars[i].Immediatesize > 1)
                    for (var j = i + 1; j < i + _vars[i].Immediatesize; j++)
                    {
                        broken_check((uint) j);
                        _vars[j].Dontuse();
                    }

                _varRemapper.Add(i, k);
                k++;
            }
        }

        //This shouldnt be needed but in gamever 1.0.757.2
        //It seems a few of the scripts are accessing items from the
        //Stack frame that they havent reserver
        private void broken_check(uint index)
        {
            if (index >= _vars.Count)
                for (var i = _vars.Count; i <= index; i++)
                    _vars.Add(new Var(this, i));
        }

        public string GetVarName(uint index)
        {
            if (IsAggregate)
                switch (_listtype)
                {
                    case ListType.Statics: return index >= ScriptParamStart ? "ScriptParam_" : "Local_";
                    case ListType.Params: return "Param";
                    case ListType.Vars:
                    default:
                        return "Var";
                }

            var name = "";
            var var = _vars[(int) index];
            if (var.DataType == Stack.DataType.String)
                name = "c";
            else if (var.Immediatesize == 1)
                name = var.DataType.ShortName();
            else if (var.Immediatesize == 3)
                name = "v";

            switch (_listtype)
            {
                case ListType.Statics:
                    name += index >= ScriptParamStart ? "ScriptParam_" : "Local_";
                    break;
                case ListType.Vars:
                    name += "Var";
                    break;
                case ListType.Params:
                    name += "Param";
                    break;
            }

            if (Program.ShiftVariables)
            {
                if (_varRemapper.ContainsKey((int) index))
                    return name + _varRemapper[(int) index];
                return name + "unknownVar";
            }

            return name + (_listtype == ListType.Statics && index >= ScriptParamStart
                ? index - ScriptParamStart
                : index);
        }

        public void SetScriptParamCount(int count)
        {
            if (_listtype == ListType.Statics) _scriptParamCount = count;
        }

        public string[] GetDeclaration()
        {
            var working = new List<string>();
            var varlocation = "";
            var datatype = "";

            var i = 0;
            var j = -1;
            foreach (var var in _vars)
            {
                switch (_listtype)
                {
                    case ListType.Statics:
                        varlocation = i >= ScriptParamStart ? "ScriptParam_" : "Local_";
                        break;
                    case ListType.Vars:
                        varlocation = "Var";
                        break;
                    case ListType.Params:
                        throw new DecompilingException("Parameters have different declaration");
                }

                j++;
                if (!var.IsUsed)
                {
                    if (!Program.ShiftVariables)
                        i++;
                    continue;
                }

                if (_listtype == ListType.Vars && !var.IsCalled)
                {
                    if (!Program.ShiftVariables)
                        i++;
                    continue;
                }

                if (var.Immediatesize == 1)
                    datatype = var.DataType.VarDeclaration();
                else if (var.Immediatesize == 3)
                    datatype = "vector3 v";
                else if (var.DataType == Stack.DataType.String)
                    datatype = "char c";
                else
                    datatype = "struct<" + var.Immediatesize + "> ";
                var value = "";
                if (!var.IsArray)
                {
                    if (_listtype == ListType.Statics)
                    {
                        if (var.Immediatesize == 1)
                        {
                            value = " = " + Utils.Represent(_vars[j].Value, var.DataType);
                        }
                        else if (var.DataType == Stack.DataType.String)
                        {
                            var data = new List<byte>();
                            for (var l = 0; l < var.Immediatesize; l++)
                                data.AddRange(BitConverter.GetBytes(_vars[j + l].Value));
                            var len = data.IndexOf(0);
                            data.RemoveRange(len, data.Count - len);
                            value = " = \"" + Encoding.ASCII.GetString(data.ToArray()) + "\"";
                        }
                        else if (var.Immediatesize == 3)
                        {
                            value += " = { " + Utils.Represent(_vars[j].Value, Stack.DataType.Float) + ", ";
                            value += Utils.Represent(_vars[j + 1].Value, Stack.DataType.Float) + ", ";
                            value += Utils.Represent(_vars[j + 2].Value, Stack.DataType.Float) + " }";
                        }
                        else if (var.Immediatesize > 1)
                        {
                            value += " = { " + Utils.Represent(_vars[j].Value, Stack.DataType.Int);
                            for (var l = 1; l < var.Immediatesize; l++)
                                value += ", " + Utils.Represent(_vars[j + l].Value, Stack.DataType.Int);
                            value += " } ";
                        }
                    }
                }
                else
                {
                    if (_listtype == ListType.Statics)
                    {
                        if (var.Immediatesize == 1)
                        {
                            value = " = { ";
                            for (var k = 0; k < var.Value; k++)
                                value += Utils.Represent(_vars[j + 1 + k].Value, var.DataType) + ", ";
                            if (value.Length > 2) value = value.Remove(value.Length - 2);
                            value += " }";
                        }
                        else if (var.DataType == Stack.DataType.String)
                        {
                            value = " = { ";
                            for (var k = 0; k < var.Value; k++)
                            {
                                var data = new List<byte>();
                                for (var l = 0; l < var.Immediatesize; l++)
                                    if (Program.IsBit32)
                                        data.AddRange(
                                            BitConverter.GetBytes((int) _vars[j + 1 + var.Immediatesize * k + l]
                                                .Value));
                                    else
                                        data.AddRange(
                                            BitConverter.GetBytes(_vars[j + 1 + var.Immediatesize * k + l].Value));
                                value += "\"" + Encoding.ASCII.GetString(data.ToArray()) + "\", ";
                            }

                            if (value.Length > 2) value = value.Remove(value.Length - 2);
                            value += " }";
                        }
                        else if (var.Immediatesize == 3)
                        {
                            value = " = {";
                            for (var k = 0; k < var.Value; k++)
                            {
                                value += "{ " + Utils.Represent(_vars[j + 1 + 3 * k].Value, Stack.DataType.Float) +
                                         ", ";
                                value += Utils.Represent(_vars[j + 2 + 3 * k].Value, Stack.DataType.Float) + ", ";
                                value += Utils.Represent(_vars[j + 3 + 3 * k].Value, Stack.DataType.Float) + " }, ";
                            }

                            if (value.Length > 2) value = value.Remove(value.Length - 2);
                            value += " }";
                        }
                    }
                }

                string decl;
                if (IsAggregate)
                {
                    decl = datatype;
                }
                else
                {
                    decl = datatype + varlocation +
                           (_listtype == ListType.Statics && i >= ScriptParamStart ? i - ScriptParamStart : i);
                    if (var.IsArray)
                        decl += "[" + var.Value + "]";
                    if (var.DataType == Stack.DataType.String)
                        decl += "[" + (var.Immediatesize * (Program.IsBit32 ? 4 : 8)) + "]";
                }

                working.Add(decl + value + ";");
                i++;
            }

            return working.ToArray();
        }

        public string GetPDec()
        {
            if (_listtype != ListType.Params)
                throw new DecompilingException("Only params use this declaration");
            var decl = "";
            var i = 0;
            foreach (var var in _vars)
            {
                if (!var.IsUsed)
                {
                    if (!Program.ShiftVariables) i++;
                    continue;
                }

                var datatype = "";
                if (!var.IsArray)
                {
                    if (var.DataType == Stack.DataType.String)
                        datatype = "char[" + var.Immediatesize * 4 + "] c";
                    else if (var.Immediatesize == 1)
                        datatype = var.DataType.VarDeclaration();
                    else if (var.Immediatesize == 3)
                        datatype = "vector3 v";
                    else
                        datatype = "struct<" + var.Immediatesize + "> ";
                }
                else
                {
                    if (var.DataType == Stack.DataType.String)
                        datatype = "char[" + var.Immediatesize * 4 + "][] c";
                    else if (var.Immediatesize == 1)
                        datatype = var.DataType.VarArrayDeclaration();
                    else if (var.Immediatesize == 3)
                        datatype = "vector3[] v";
                    else
                        datatype = "struct<" + var.Immediatesize + ">[] ";
                }

                if (IsAggregate)
                    decl += "Param, ";
                else
                    decl += datatype + "Param" + i + ", ";
                i++;
            }

            if (decl.Length > 2)
                decl = decl.Remove(decl.Length - 2);
            return decl;
        }

        public Stack.DataType GetTypeAtIndex(uint index)
        {
            return _vars[(int) index].DataType;
        }

        public bool SetTypeAtIndex(uint index, Stack.DataType type)
        {
            var prev = _vars[(int) index].DataType;
            if (!type.IsUnknown() && (prev.IsUnknown() || prev.Precedence() < type.Precedence()))
            {
                _vars[(int) index].DataType = type;
                return true;
            }

            return false;
        }

        public Var GetVarAtIndex(uint index)
        {
            broken_check(index);
            return _vars[(int) index];
        }

        public class Var
        {
            private Stack.DataType _datatype = Stack.DataType.Unk;
            private bool _fixed;
            private readonly VarsInfo _parent;

            public Var(VarsInfo parent, int index)
            {
                _parent = parent;
                Index = index;
                Value = 0;
            }

            public Var(VarsInfo parent, int index, long value)
            {
                _parent = parent;
                Index = index;
                Value = value;
            }

            public int Index { get; }
            public long Value { get; set; }
            public int Immediatesize { get; set; } = 1;

            public Stack.DataType DataType
            {
                get => _datatype;
                set
                {
                    if (_fixed && value.Precedence() <= _datatype.Precedence()) return;
                    _datatype = value;
                }
            }

            public bool IsStruct { get; private set; }
            public bool IsArray { get; private set; }
            public bool IsUsed { get; private set; } = true;
            public bool IsCalled { get; private set; }

            public string Name
            {
                get
                {
                    var listtype = _parent._listtype;
                    var scriptParamStart = _parent.ScriptParamStart;

                    if (_parent.IsAggregate)
                        switch (listtype)
                        {
                            case ListType.Statics: return Index >= scriptParamStart ? "ScriptParam_" : "Local_";
                            case ListType.Params: return "Param";
                            case ListType.Vars:
                            default:
                                return "Var";
                        }

                    var name = "";
                    if (DataType == Stack.DataType.String)
                        name = "c";
                    else if (Immediatesize == 1)
                        name = DataType.ShortName();
                    else if (Immediatesize == 3)
                        name = "v";

                    switch (listtype)
                    {
                        case ListType.Statics:
                            name += Index >= scriptParamStart ? "ScriptParam_" : "Local_";
                            break;
                        case ListType.Vars:
                            name += "Var";
                            break;
                        case ListType.Params:
                            name += "Param";
                            break;
                    }

                    if (Program.ShiftVariables && _parent._varRemapper != null)
                    {
                        if (_parent._varRemapper.ContainsKey(Index))
                            return name + _parent._varRemapper[Index];
                        return name + "unknownVar";
                    }

                    return name + (listtype == ListType.Statics && Index >= scriptParamStart
                        ? Index - scriptParamStart
                        : Index);
                }
            }

            public Var Fixed()
            {
                if (_parent._varRemapper == null) return this;
                _fixed = true;
                return this;
            }

            public void Makearray()
            {
                if (!IsStruct)
                    IsArray = true;
            }

            public void Call()
            {
                IsCalled = true;
            }

            public void Dontuse()
            {
                IsUsed = false;
            }

            public void Makestruct()
            {
                DataType = Stack.DataType.Unk;
                IsArray = false;
                IsStruct = true;
            }
        }
    }
}
