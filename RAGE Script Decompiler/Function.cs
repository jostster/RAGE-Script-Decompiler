using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace RageDecompiler
{
    [Serializable]
    public class DecompilingException : Exception
    {
        public DecompilingException(string message) : base(message)
        {
        }
    }

    public class Function
    {
        private readonly HashSet<Function> _associated = new HashSet<Function>();

        private bool _dirty;
        private readonly FunctionName _fnName;
        private Dictionary<int, int> _instructionMap;
        private List<HlInstruction> _instructions;

        private int _offset;
        private CodePath _outerPath;

        private StringBuilder _sb;
        private Stack _stack;

        /// <summary>
        ///     Disposes of the function and returns the function text
        /// </summary>
        /// <returns>The whole function high level code</returns>
        private string _strCache;

        /// <summary>
        ///     Gets the first line of the function Declaration
        ///     return type + name + params
        /// </summary>
        private string _strFirstLineCache;

        private string _tabs = "";
        private bool _writeElse;
        internal bool DecodeStarted;
        public int LineCount;
        internal bool PreDecoded;
        internal bool PreDecodeStarted;

        public Function(ScriptFile owner, string name, int pCount, int vCount, int rCount, int location,
            int locmax = -1, bool isAggregate = false)
        {
            ScriptFile = owner;
            Name = name;
            PCount = pCount;
            VCount = vCount;
            RCount = rCount;
            Location = location;
            MaxLocation = locmax != -1 ? locmax : Location;
            Decoded = false;

            NativeCount = 0;
            IsAggregate = isAggregate;
            BaseFunction = null;

            _fnName = new FunctionName(Name, PCount, RCount, Location, MaxLocation);
            Vars = new VarsInfo(VarsInfo.ListType.Vars, vCount - 2, IsAggregate);
            Params = new VarsInfo(VarsInfo.ListType.Params, pCount, IsAggregate);
            if (!IsAggregate)
                ScriptFile.FunctionLoc.Add(location, _fnName);
        }

        public string Name { get; }
        public int PCount { get; }
        public int VCount { get; }
        public int RCount { get; }
        public int Location { get; }
        public int MaxLocation { get; }

        public ScriptFile ScriptFile { get; }
        public int NativeCount { get; private set; } // Number of decoded native calls.
        public bool IsAggregate { get; } // Stateless function.
        public Function BaseFunction { get; set; } // For aggregate functions.
        public Stack.DataType ReturnType { get; set; } = Stack.DataType.Unk;
        internal bool Decoded { get; private set; }
        public VarsInfo Vars { get; }
        public VarsInfo Params { get; }

        public bool Dirty
        {
            get => _dirty;
            set
            {
                var forward = !_dirty && value;
                _dirty = value;
                if (forward) // Dirty all associate functions.
                    foreach (var f in _associated)
                        f.Dirty = true;
            }
        }

        /// <summary>
        ///     The block of code that the function takes up
        /// </summary>
        public List<byte> CodeBlock { get; set; }

        /// <summary>
        ///     Compute the hash of the current string buffer (function signature for aggregate functions).
        /// </summary>
        /// <returns></returns>
        public string ToHash()
        {
            return Aggregate.Sha256(_sb.ToString());
        }

        public void UpdateNativeReturnType(ulong hash, Stack.DataType dataType)
        {
            ScriptFile.UpdateNativeReturnType(hash, dataType);
        }

        public void UpdateNativeParameter(ulong hash, Stack.DataType dataType, int index)
        {
            ScriptFile.UpdateNativeParameter(hash, dataType, index);
        }

        public void UpdateFuncParamType(uint index, Stack.DataType dataType)
        {
            if (Params.SetTypeAtIndex(index, dataType))
                Dirty = true;
        }

        public void Associate(Function f)
        {
            if (f != this) _associated.Add(f); // f.Associated.Add(this);
        }

        /// <summary>
        ///     Invalidate function aggregate cache
        /// </summary>
        public void Invalidate()
        {
            _strCache = null;
            _strFirstLineCache = null;
            if (_instructionMap != null)
            {
                _instructionMap.Clear();
                _instructionMap = null;
                _instructions.Clear();
                _instructions = null;
                CodeBlock.Clear();
                CodeBlock = null;
                _stack.Dispose();
                _stack = null;
                _sb.Clear();
                _sb = null;
            }
        }

        public override string ToString()
        {
            if (_strCache == null)
            {
                _instructionMap.Clear();
                _instructionMap = null;
                _instructions.Clear();
                _instructions = null;
                CodeBlock.Clear();
                CodeBlock = null;
                _stack.Dispose();
                _stack = null;

                try
                {
                    if (ReturnType == Stack.DataType.Bool)
                        _strCache = FirstLine() + "\r\n" + _sb.ToString().Replace("return 0;", "return false;")
                            .Replace("return 1;", "return true;");
                    else
                        _strCache = FirstLine() + "\r\n" + _sb;
                }
                finally
                {
                    _sb.Clear();
                    _sb = null;
                    LineCount += 2;
                }
            }

            return _strCache;
        }

        public virtual string FirstLine()
        {
            if (_strFirstLineCache == null)
            {
                string working;
                if (RCount == 0) // extract return type of function
                {
                    working = "void ";
                }
                else if (RCount == 1)
                {
                    working = ReturnType.ReturnType();
                }
                else if (RCount == 3)
                {
                    working = "Vector3 ";
                }
                else if (RCount > 1)
                {
                    if (ReturnType == Stack.DataType.String)
                        working = "char[" + RCount * 4 + "] ";
                    else
                        working = "struct<" + RCount + "> ";
                }
                else
                {
                    throw new DecompilingException("Unexpected return count");
                }

                var name = IsAggregate ? working + "func_" : working + Name;
                working = "(" + Params.GetPDec() + ")";
                _strFirstLineCache = name + working +
                                     (Program.ShowFuncPosition ? "//Position - 0x" + Location.ToString("X") : "");
            }

            return _strFirstLineCache;
        }

        /// <summary>
        ///     Determines if a frame variable is a parameter or a variable and returns its index
        /// </summary>
        /// <param name="index">the frame variable index</param>
        /// <returns>The variable</returns>
        public VarsInfo.Var GetFrameVar(uint index)
        {
            if (index < PCount)
                return Params.GetVarAtIndex(index);
            if (index < PCount + 2)
                throw new Exception("Unexpected frame var");
            return Vars.GetVarAtIndex((uint) (index - 2 - PCount));
        }

        private Instruction Map(byte b)
        {
            return ScriptFile.CodeSet.Map(b);
        }

        private Instruction MapOffset(int offset)
        {
            return Map(CodeBlock[offset]);
        }

        /// <summary>
        ///     Gets the function info given the offset where its called from
        /// </summary>
        /// <param name="offset">the offset that is being called</param>
        /// <returns>basic information about the function at that offset</returns>
        public FunctionName GetFunctionNameFromOffset(int offset)
        {
            if (ScriptFile.FunctionLoc.ContainsKey(offset))
                return ScriptFile.FunctionLoc[offset];
            throw new Exception("Function Not Found");
        }

        /// <summary>
        ///     Gets the function info given the offset where its called from
        /// </summary>
        /// <param name="offset">the offset that is being called</param>
        /// <returns>basic information about the function at that offset</returns>
        public Function GetFunctionFromOffset(int offset)
        {
            foreach (var f in ScriptFile.Functions)
                if (f.Location <= offset && offset <= f.MaxLocation)
                    return f;
            throw new Exception("Function Not Found");
        }

        public void ScruffDissasemble()
        {
            //getinstructions(false);
        }

        /// <summary>
        ///     Indents everything below by 1 tab space
        /// </summary>
        /// <param name="write">if true(or default) it will write the open curly bracket, {</param>
        private void OpenTab(bool write = true)
        {
            if (write)
                WriteLine("{");
            _tabs += "\t";
        }

        /// <summary>
        ///     Removes 1 tab space from indentation of everything below it
        /// </summary>
        /// <param name="write">if true(or default) it will write the close curly bracket, }</param>
        private void CloseTab(bool write = true)
        {
            if (_tabs.Length > 0) _tabs = _tabs.Remove(_tabs.Length - 1);
            if (write)
                WriteLine("}");
        }

        /// <summary>
        ///     Step done before decoding, getting the variables types
        ///     Aswell as getting the list of instructions
        ///     Needs to PreDecode all functions before decoding any as this step
        ///     Builds The Static Variable types aswell
        /// </summary>
        public void PreDecode()
        {
            if (PreDecoded || PreDecodeStarted) return;
            Dirty = false;
            PreDecodeStarted = true;
            GetInstructions();
            DecodeInstructionsForVarInfo();
            PreDecoded = true;
        }

        /// <summary>
        ///     The method that actually decodes the function into high level
        /// </summary>
        public void Decode()
        {
            lock (Program.ThreadLock)
            {
                DecodeStarted = true;
                if (Decoded) return;
            }

            //Set up a stack
            _stack = new Stack(this, false, IsAggregate);

            //Get The Instructions in the function along with their operands
            //getinstructions();

            //Set up the codepaths to a null item
            _outerPath = new CodePath(CodePathType.Main, CodeBlock.Count, -1);

            _sb = new StringBuilder();
            OpenTab();
            _offset = 0;

            //write all the function variables declared by the function
            if (Program.DeclareVariables)
            {
                var temp = false;
                foreach (var s in Vars.GetDeclaration())
                {
                    WriteLine(s);
                    temp = true;
                }

                if (temp) WriteLine("");
            }

            while (_offset < _instructions.Count)
                DecodeInstruction();
            //Fix for switches that end at the end of a function
            while (_outerPath.Parent != null && _outerPath.Parent.Type != CodePathType.Main)
            {
                if (_outerPath.IsSwitch) CloseTab(false);
                CloseTab();
                _outerPath = _outerPath.Parent;
            }

            CloseTab();
            //fnName.RetType = RetType;
            _fnName.RetType = ReturnType;
            Decoded = true;
        }

        /// <summary>
        ///     Writes a line to the function text as well as any tab chars needed before it
        /// </summary>
        /// <param name="line">the line to write</param>
        private void WriteLine(string line)
        {
            if (_writeElse)
            {
                _writeElse = false;
                WriteLine("else");
                OpenTab();
            }

            AppendLine(_tabs + line);
        }

        public void AppendLine(string line)
        {
            _sb.AppendLine(line.TrimEnd());
            LineCount++;
        }

        /// <summary>
        ///     Check if a jump is jumping out of the function
        ///     if not, then add it to the list of instructions
        /// </summary>
        private void CheckJumpCodePath()
        {
            var cur = _offset;
            var temp = new HlInstruction(MapOffset(_offset), GetArray(2), cur);
            if (temp.GetJumpOffset > 0)
                if (temp.GetJumpOffset < CodeBlock.Count)
                {
                    AddInstruction(cur, temp);
                    return;
                }

            //if the jump is out the function then its useless
            //So nop this jump
            AddInstruction(cur, new HlInstruction(Instruction.RAGE_NOP, cur));
            AddInstruction(cur + 1, new HlInstruction(Instruction.RAGE_NOP, cur + 1));
            AddInstruction(cur + 2, new HlInstruction(Instruction.RAGE_NOP, cur + 2));
        }

        /// <summary>
        ///     See if a dup is being used for an AND or OR
        ///     if it is, dont add it (Rockstars way of doing and/or conditionals)
        /// </summary>
        private void CheckDupForInstruction()
        {
            //May need refining, but works fine for rockstars code
            var off = 0;
            Start:
            off += 1;
            if (MapOffset(_offset + off) == Instruction.RAGE_NOP)
                goto Start;
            if (MapOffset(_offset + off) == Instruction.RAGE_JZ)
            {
                _offset = _offset + off + 2;
                return;
            }

            if (MapOffset(_offset + off) == Instruction.RAGE_INOT) goto Start;
            _instructions.Add(new HlInstruction(MapOffset(_offset), _offset));
        }

        /// <summary>
        ///     Gets the given amount of bytes from the codeblock at its offset
        ///     while advancing its position by how ever many items it uses
        /// </summary>
        /// <param name="items">how many bytes to grab</param>
        /// <returns>the operands for the instruction</returns>
        //IEnumerable<byte> GetArray(int items)
        //{
        //    int temp = Offset + 1;
        //    Offset += items;
        //    return CodeBlock.GetRange(temp, items);
        //}
        private IEnumerable<byte> GetArray(int items)
        {
            var temp = _offset + 1;
            _offset += items;
            return CodeBlock.GetRange(temp, items);
        }

        /// <summary>
        ///     When we hit a jump, decide how to handle it
        /// </summary>
        private void HandleJumpCheck()
        {
            //Check the jump location against each switch statement, to see if it is recognised as a break
            startsw:
            if (_outerPath.IsSwitch && _instructions[_offset].GetJumpOffset == _outerPath.BreakOffset)
            {
                var outerSwitch = (SwitchPath) _outerPath;
                var switchOffset = outerSwitch.ActiveOffset;
                if (!outerSwitch.EscapedCases[switchOffset])
                {
                    WriteLine("break;");
                    outerSwitch.HasDefaulted = false;
                }

                return;
            }

            if (_outerPath.IsSwitch)
                if (_outerPath.Parent != null)
                {
                    _outerPath = _outerPath.Parent;
                    goto startsw;
                }

            var tempoff = 0;
            if (_instructions[_offset + 1].Offset == _outerPath.EndOffset)
            {
                if (_instructions[_offset].GetJumpOffset != _instructions[_offset + 1].Offset)
                {
                    if (!_instructions[_offset].IsWhileJump)
                    {
                        //The jump is detected as being an else statement
                        //finish the current if code path and add an else code path
                        var temp = _outerPath;
                        _outerPath = _outerPath.Parent;
                        _outerPath.ChildPaths.Remove(temp);
                        _outerPath = _outerPath.CreateCodePath(CodePathType.Else, _instructions[_offset].GetJumpOffset,
                            -1);
                        CloseTab();
                        _writeElse = true;
                        return;
                    }

                    throw new Exception("Shouldnt find a while loop here");
                }

                return;
            }

            start:
            //Check to see if the jump is just jumping past nops(end of code table)
            //should be the only case for finding another jump now
            if (_instructions[_offset].GetJumpOffset != _instructions[_offset + 1 + tempoff].Offset)
            {
                if (_instructions[_offset + 1 + tempoff].Instruction == Instruction.RAGE_NOP)
                {
                    tempoff++;
                    goto start;
                }

                if (_instructions[_offset + 1 + tempoff].Instruction == Instruction.RAGE_J)
                    if (_instructions[_offset + 1 + tempoff].GetOperandsAsInt == 0)
                    {
                        tempoff++;
                        goto start;
                    }

                //These seem to be cause from continue statements in for loops
                //But given the current implementation of codepaths, it is not really faesible
                //to add in support for for loops. And to save rewriting the entire codepath handling
                //I'll just ignore this case, only occurs in 2 scripts in the whole script_rel.rpf
                //If I was to fix this, it would involve rewriting the codepath(probably as a tree
                //structure like it really should've been done in the first place
                if (_instructions[_offset].GetOperandsAsInt != 0)
                    WriteLine("Jump @" + _instructions[_offset].GetJumpOffset +
                              $"; //curOff = {_instructions[_offset].Offset}");
                //int JustOffset = InstructionMap[Instructions[Offset].GetJumpOffset];
                //HLInstruction instruction = Instructions[JustOffset];
                //System.Diagnostics.Debug.WriteLine(this.ScriptFile.name);
            }
        }

        //Needs Merging with method below
        private bool IsNewCodePath()
        {
            if (!_outerPath.IsSwitch && _outerPath.Parent != null)
                if (_instructionMap[_outerPath.EndOffset] == _offset)
                    return true;
            if (_outerPath.IsSwitch && ((SwitchPath) _outerPath).Offsets.Count > 0)
                if (_instructions[_offset].Offset == ((SwitchPath) _outerPath).Offsets[0])
                    return true;
            return false;
        }

        /// <summary>
        ///     Checks if the current offset is a new code path, then decides how to handle it
        /// </summary>
        private void HandleNewPath()
        {
            start:
            if (!_outerPath.IsSwitch && _instructions[_offset].Offset == _outerPath.EndOffset)
            {
                //Offset recognised as the exit instruction of the outermost code path
                //remove outermost code path
                var temp = _outerPath;
                _outerPath = _outerPath.Parent;
                _outerPath.ChildPaths.Remove(temp);
                CloseTab();
                //check next codepath to see if it belongs there aswell
                goto start;
            }

            if (_outerPath.IsSwitch && ((SwitchPath) _outerPath).Offsets.Count > 0)
            {
                var outerSwitch = (SwitchPath) _outerPath;
                if (_instructions[_offset].Offset == outerSwitch.Offsets[0])
                {
                    if (outerSwitch.Offsets.Count == 1)
                    {
                        if (outerSwitch.HasDefaulted && !outerSwitch.EscapedCases[outerSwitch.ActiveOffset])
                        {
                            WriteLine("break;");
                            outerSwitch.HasDefaulted = false;
                        }

                        CloseTab(false);
                        outerSwitch.ActiveOffset = -1;

                        //end of switch statement detected
                        //remove child class
                        CodePath temp = outerSwitch;
                        _outerPath = _outerPath.Parent;
                        _outerPath.ChildPaths.Remove(temp);
                        CloseTab();
                        //go check if its the next switch exit instruction
                        //probably isnt and the goto can probably be removed
                        goto start;
                    }

                    CloseTab(false);
                    outerSwitch.ActiveOffset = outerSwitch.Offsets[0];

                    //more cases left in switch
                    //so write the next switch case
                    for (var i = 0; i < outerSwitch.Cases[outerSwitch.Offsets[0]].Count; i++)
                    {
                        var temp = outerSwitch.Cases[outerSwitch.Offsets[0]][i];
                        if (temp == "default")
                        {
                            outerSwitch.HasDefaulted = true;
                            WriteLine("default:");
                        }
                        else
                        {
                            WriteLine("case " + temp + ":" + Program.Gxtbank.GetEntry(temp, false));
                        }
                    }

                    OpenTab(false);

                    //remove last switch case from class, so it wont attemp to jump there again
                    outerSwitch.Offsets.RemoveAt(0);

                    //as before, probably not needed, so should always skip past here
                    goto start;
                }
            }
        }

        /// <summary>
        ///     Create a switch statement, then set up the rest of the decompiler to handle the rest of the switch statement
        /// </summary>
        private void HandleSwitch()
        {
            var cases = new Dictionary<int, List<string>>();
            int defaultloc;
            int breakloc;
            bool usedefault;
            HlInstruction temp;

            //Hanldle(skip past) any Nops immediately after switch statement
            var tempoff = 0;
            while (_instructions[_offset + 1 + tempoff].Instruction == Instruction.RAGE_NOP)
                tempoff++;

            //Extract the location to jump to if no cases match
            defaultloc = _instructions[_offset + 1 + tempoff].GetJumpOffset;

            var switchCount = Program.RdrOpcodes
                ? _instructions[_offset].GetOperandsAsUInt16
                : _instructions[_offset].GetOperand(0);
            for (var i = 0; i < switchCount; i++)
            {
                var caseVal = _instructions[_offset].GetSwitchStringCase(i);
                var offset = _instructions[_offset].GetSwitchOffset(i); // Get the offset to jump to
                if (!cases.ContainsKey(offset)) // Check if the case is a known hash
                    cases.Add(offset, new List<string>(new[] {caseVal}));
                else // This offset is known, multiple cases are jumping to this path
                    cases[offset].Add(caseVal);
            }

            //Not sure how necessary this step is, but just incase R* compiler doesnt order jump offsets, do it anyway
            var sorted = cases.Keys.ToList();
            sorted.Sort();

            //We have found the jump location, so that instruction is no longer needed and can be nopped
            _instructions[_offset + 1 + tempoff].NopInstruction();

            //Temporary stage
            breakloc = defaultloc;
            usedefault = true;

            //check if case last instruction is a jump to default location, if so default location is a break;
            //if not break location is where last instrcution jumps to
            for (var i = 0; i <= sorted.Count; i++)
            {
                var index = 0;
                if (i == sorted.Count)
                    index = _instructionMap[defaultloc] - 1;
                else
                    index = _instructionMap[sorted[i]] - 1;
                if (index - 1 == _offset) continue;
                temp = _instructions[index];
                if (temp.Instruction != Instruction.RAGE_J) continue;
                if (temp.GetJumpOffset == defaultloc)
                {
                    usedefault = false;
                    breakloc = defaultloc;
                    break;
                }

                breakloc = temp.GetJumpOffset;
            }

            if (usedefault)
            {
                //Default location found, best add it in
                if (cases.ContainsKey(defaultloc))
                {
                    //Default location shares code path with other known case
                    cases[defaultloc].Add("default");
                }
                else
                {
                    //Default location is a new code path
                    sorted = cases.Keys.ToList();
                    sorted.Sort();
                    sorted.Add(defaultloc); // Ensure default is last offset
                    cases.Add(defaultloc, new List<string>(new[] {"default"}));
                }
            }

            // Create the class the rest of the decompiler needs to handle the rest of the switch
            var sortedOffset = sorted[0];
            _outerPath = new SwitchPath(_outerPath, cases, -1, breakloc);

            // Found all information about switch, write the first case, the rest will be handled when we get to them
            WriteLine("switch (" + _stack.Pop().AsLiteral + ")");
            OpenTab();
            for (var i = 0; i < cases[sortedOffset].Count; i++)
            {
                var caseStr = cases[sortedOffset][i];
                WriteLine("case " + caseStr + ":" + Program.Gxtbank.GetEntry(caseStr, false));
            }

            OpenTab(false);

            // Need to build the escape paths prior to removing the offsets.
            cases.Remove(sortedOffset);
            ((SwitchPath) _outerPath).ActiveOffset = sortedOffset;
            ((SwitchPath) _outerPath).Cases.Remove(sortedOffset);
            ((SwitchPath) _outerPath).Offsets.Remove(sortedOffset);
        }

        /// <summary>
        ///     If we have a conditional statement determine whether its for an if/while statement
        ///     Then handle it accordingly
        /// </summary>
        private void CheckConditional()
        {
            var tempstring = _stack.Pop().AsLiteral;
            if (!(tempstring.StartsWith("(") && tempstring.EndsWith(")")))
                tempstring = "(" + tempstring + ")";

            var offset = _instructions[_offset].GetJumpOffset;
            var tempcp = _outerPath;
            start:

            if (tempcp.Type == CodePathType.While)
                if (offset == tempcp.EndOffset)
                {
                    WriteLine("if " + tempstring);
                    OpenTab(false);
                    WriteLine("break;");
                    CloseTab(false);
                    return;
                }

            if (tempcp.Parent != null)
            {
                tempcp = tempcp.Parent;
                goto start;
            }

            var jumploc = _instructions[_instructionMap[offset] - 1];

            if (jumploc.IsWhileJump && jumploc.GetJumpOffset < _instructions[_offset].Offset)
            {
                jumploc.NopInstruction();
                if (tempstring == "(1)")
                    tempstring = "(true)";
                WriteLine("while " + tempstring);
                _outerPath = _outerPath.CreateCodePath(CodePathType.While, _instructions[_offset].GetJumpOffset, -1);
                OpenTab();
            }
            else
            {
                var written = false;
                if (_writeElse)
                {
                    if (_outerPath.EndOffset == _instructions[_offset].GetJumpOffset)
                    {
                        _writeElse = false;
                        var temp = _outerPath;
                        _outerPath = _outerPath.Parent;
                        _outerPath.ChildPaths.Remove(temp);
                        _outerPath =
                            _outerPath.CreateCodePath(CodePathType.If, _instructions[_offset].GetJumpOffset, -1);
                        WriteLine("else if " + tempstring);
                        OpenTab();
                        written = true;
                    }
                    else if (_instructions[_instructionMap[_instructions[_offset].GetJumpOffset] - 1].Instruction ==
                             Instruction.RAGE_J)
                    {
                        if (_outerPath.EndOffset ==
                            _instructions[_instructionMap[_instructions[_offset].GetJumpOffset] - 1].GetJumpOffset)
                        {
                            _writeElse = false;
                            var temp = _outerPath;
                            _outerPath = _outerPath.Parent;
                            _outerPath.ChildPaths.Remove(temp);
                            _outerPath = _outerPath.CreateCodePath(CodePathType.If,
                                _instructions[_offset].GetJumpOffset, -1);
                            WriteLine("else if " + tempstring);
                            OpenTab();
                            written = true;
                        }
                    }
                }

                if (!written)
                {
                    WriteLine("if " + tempstring);
                    _outerPath = _outerPath.CreateCodePath(CodePathType.If, _instructions[_offset].GetJumpOffset, -1);
                    OpenTab();
                }
            }
        }

        /// <summary>
        ///     Turns the raw code into a list of instructions
        /// </summary>
        public void GetInstructions()
        {
            _offset = CodeBlock[4] + 5;
            _instructions = new List<HlInstruction>();
            _instructionMap = new Dictionary<int, int>();
            int curoff;
            while (_offset < CodeBlock.Count)
            while (_offset < CodeBlock.Count)
            {
                curoff = _offset;
                var instruct = MapOffset(_offset);
                switch (MapOffset(_offset))
                {
                    //case Instruction.RAGE_NOP: if (addnop) AddInstruction(curoff, new HLInstruction(instruct, curoff)); break;
                    case Instruction.RAGE_PUSH_CONST_U8:
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(1), curoff));
                        break;
                    case Instruction.RAGE_PUSH_CONST_U8_U8:
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(2), curoff));
                        break;
                    case Instruction.RAGE_PUSH_CONST_U8_U8_U8:
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(3), curoff));
                        break;
                    case Instruction.RAGE_PUSH_CONST_U32:
                    case Instruction.RAGE_PUSH_CONST_F:
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(4), curoff));
                        break;
                    case Instruction.RAGE_DUP:
                        // Because of how rockstar codes and/or conditionals, its neater to detect dups
                        // and only add them if they are not used for conditionals
                        CheckDupForInstruction();
                        break;
                    case Instruction.RAGE_NATIVE:
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(3), curoff));
                        break;
                    case Instruction.RAGE_ENTER:
                        throw new Exception("Function not exptected");
                    case Instruction.RAGE_LEAVE:
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(2), curoff));
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
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(1), curoff));
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
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(2), curoff));
                        break;
                    case Instruction.RAGE_J:
                        CheckJumpCodePath();
                        break;
                    case Instruction.RAGE_JZ:
                    case Instruction.RAGE_IEQ_JZ:
                    case Instruction.RAGE_INE_JZ:
                    case Instruction.RAGE_IGT_JZ:
                    case Instruction.RAGE_IGE_JZ:
                    case Instruction.RAGE_ILT_JZ:
                    case Instruction.RAGE_ILE_JZ:
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(2), curoff));
                        break;
                    case Instruction.RAGE_CALL:
                    case Instruction.RAGE_GLOBAL_U24:
                    case Instruction.RAGE_GLOBAL_U24_LOAD:
                    case Instruction.RAGE_GLOBAL_U24_STORE:
                    case Instruction.RAGE_PUSH_CONST_U24:
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(3), curoff));
                        break;
                    case Instruction.RAGE_SWITCH:
                    {
                        if (Program.RdrOpcodes)
                        {
                            var length = (CodeBlock[_offset + 2] << 8) | CodeBlock[_offset + 1];
                            AddInstruction(curoff, new HlInstruction(instruct, GetArray(length * 6 + 2), curoff));
                        }
                        else
                        {
                            int temp = CodeBlock[_offset + 1];
                            AddInstruction(curoff, new HlInstruction(instruct, GetArray(temp * 6 + 1), curoff));
                        }

                        break;
                    }
                    case Instruction.RAGE_TEXT_LABEL_ASSIGN_STRING:
                    case Instruction.RAGE_TEXT_LABEL_ASSIGN_INT:
                    case Instruction.RAGE_TEXT_LABEL_APPEND_STRING:
                    case Instruction.RAGE_TEXT_LABEL_APPEND_INT:
                        AddInstruction(curoff, new HlInstruction(instruct, GetArray(1), curoff));
                        break;
                    default:
                        if (instruct != Instruction.RageLast)
                            AddInstruction(curoff, new HlInstruction(instruct, curoff));
                        else throw new Exception("Unexpected Opcode");
                        break;
                }

                _offset++;
            }
        }


        /// <summary>
        ///     Adds an instruction to the list of instructions
        ///     then adds the offset to a dictionary
        /// </summary>
        /// <param name="offset">the offset in the code</param>
        /// <param name="instruction">the instruction</param>
        private void AddInstruction(int offset, HlInstruction instruction)
        {
            _instructions.Add(instruction);
            _instructionMap.Add(offset, _instructions.Count - 1);
        }

        /// <summary>
        ///     Decodes the instruction at the current offset
        /// </summary>
        public void DecodeInstruction()
        {
            if (IsNewCodePath()) HandleNewPath();
            switch (_instructions[_offset].Instruction)
            {
                case Instruction.RAGE_NOP:
                    break;
                case Instruction.RAGE_IADD:
                    _stack.Op_Add();
                    break;
                case Instruction.RAGE_FADD:
                    _stack.Op_Addf();
                    break;
                case Instruction.RAGE_ISUB:
                    _stack.Op_Sub();
                    break;
                case Instruction.RAGE_FSUB:
                    _stack.Op_Subf();
                    break;
                case Instruction.RAGE_IMUL:
                    _stack.Op_Mult();
                    break;
                case Instruction.RAGE_FMUL:
                    _stack.Op_Multf();
                    break;
                case Instruction.RAGE_IDIV:
                    _stack.Op_Div();
                    break;
                case Instruction.RAGE_FDIV:
                    _stack.Op_Divf();
                    break;
                case Instruction.RAGE_IMOD:
                    _stack.Op_Mod();
                    break;
                case Instruction.RAGE_FMOD:
                    _stack.Op_Modf();
                    break;
                case Instruction.RAGE_INOT:
                    _stack.Op_Not();
                    break;
                case Instruction.RAGE_INEG:
                    _stack.Op_Neg();
                    break;
                case Instruction.RAGE_FNEG:
                    _stack.Op_Negf();
                    break;
                case Instruction.RAGE_IEQ:
                case Instruction.RAGE_FEQ:
                    _stack.Op_CmpEQ();
                    break;
                case Instruction.RAGE_INE:
                case Instruction.RAGE_FNE:
                    _stack.Op_CmpNE();
                    break;
                case Instruction.RAGE_IGT:
                case Instruction.RAGE_FGT:
                    _stack.Op_CmpGT();
                    break;
                case Instruction.RAGE_IGE:
                case Instruction.RAGE_FGE:
                    _stack.Op_CmpGE();
                    break;
                case Instruction.RAGE_ILT:
                case Instruction.RAGE_FLT:
                    _stack.Op_CmpLT();
                    break;
                case Instruction.RAGE_ILE:
                case Instruction.RAGE_FLE:
                    _stack.Op_CmpLE();
                    break;
                case Instruction.RAGE_VADD:
                    _stack.Op_Vadd();
                    break;
                case Instruction.RAGE_VSUB:
                    _stack.Op_VSub();
                    break;
                case Instruction.RAGE_VMUL:
                    _stack.Op_VMult();
                    break;
                case Instruction.RAGE_VDIV:
                    _stack.Op_VDiv();
                    break;
                case Instruction.RAGE_VNEG:
                    _stack.Op_VNeg();
                    break;
                case Instruction.RAGE_IAND:
                    _stack.Op_And();
                    break;
                case Instruction.RAGE_IOR:
                    _stack.Op_Or();
                    break;
                case Instruction.RAGE_IXOR:
                    _stack.Op_Xor();
                    break;
                case Instruction.RageI2F:
                    _stack.Op_Itof();
                    break;
                case Instruction.RageF2I:
                    _stack.Op_FtoI();
                    break;
                case Instruction.RageF2V:
                    _stack.Op_FtoV();
                    break;
                case Instruction.RAGE_PUSH_CONST_U8:
                    _stack.Push(_instructions[_offset].GetOperand(0));
                    break;
                case Instruction.RAGE_PUSH_CONST_U8_U8:
                    _stack.Push(_instructions[_offset].GetOperand(0), _instructions[_offset].GetOperand(1));
                    break;
                case Instruction.RAGE_PUSH_CONST_U8_U8_U8:
                    _stack.Push(_instructions[_offset].GetOperand(0), _instructions[_offset].GetOperand(1),
                        _instructions[_offset].GetOperand(2));
                    break;
                case Instruction.RAGE_PUSH_CONST_U32:
                case Instruction.RAGE_PUSH_CONST_U24:
                {
                    var type = Stack.DataType.Int;
                    if (Program.IntStyle == Program.IntType.Uint)
                        _stack.Push(Program.Hashbank.GetHash(_instructions[_offset].GetOperandsAsUInt), type);
                    else
                        _stack.Push(Program.Hashbank.GetHash(_instructions[_offset].GetOperandsAsInt), type);
                    break;
                }
                case Instruction.RAGE_PUSH_CONST_S16:
                    _stack.Push(_instructions[_offset].GetOperandsAsInt);
                    break;
                case Instruction.RAGE_PUSH_CONST_F:
                    _stack.Push(_instructions[_offset].GetFloat);
                    break;
                case Instruction.RAGE_DUP:
                    _stack.Dup();
                    break;
                case Instruction.RAGE_DROP:
                {
                    var temp = _stack.Drop();
                    if (temp is string)
                        WriteLine(temp as string);
                    break;
                }
                case Instruction.RAGE_NATIVE:
                {
                    var natHash =
                        ScriptFile.X64NativeTable.GetNativeHashFromIndex(_instructions[_offset].GetNativeIndex);
                    var natStr = ScriptFile.X64NativeTable.GetNativeFromIndex(_instructions[_offset].GetNativeIndex);
                    NativeCount++;
                    if (!IsAggregate) Aggregate.Instance.Count(natStr);

                    var tempstring = _stack.NativeCallTest(natHash, natStr, _instructions[_offset].GetNativeParams,
                        _instructions[_offset].GetNativeReturns);
                    if (tempstring != "")
                        WriteLine(tempstring);
                    break;
                }
                case Instruction.RAGE_ENTER:
                    throw new Exception("Unexpected Function Definition");
                case Instruction.RAGE_LEAVE:
                {
                    if (_outerPath.IsSwitch)
                    {
                        var switchPath = (SwitchPath) _outerPath;
                        switchPath.EscapedCases[switchPath.ActiveOffset] = true;
                    }

                    var type = _instructions[_offset].GetOperand(1) == 1 ? _stack.TopType : Stack.DataType.Unk;
                    var tempString = _stack.PopListForCall(_instructions[_offset].GetOperand(1));
                    switch (_instructions[_offset].GetOperand(1))
                    {
                        case 0:
                        {
                            if (_offset < _instructions.Count - 1)
                                WriteLine("return;");
                            break;
                        }
                        case 1:
                        {
                            switch (type)
                            {
                                case Stack.DataType.Bool:
                                case Stack.DataType.Float:
                                case Stack.DataType.StringPtr:
                                case Stack.DataType.Int:
                                    ReturnType = type;
                                    break;
                                default:
                                    ReturnCheck(tempString);
                                    break;
                            }

                            WriteLine("return " + tempString + ";");
                            break;
                        }
                        default:
                        {
                            if (_stack.TopType == Stack.DataType.String)
                                ReturnType = Stack.DataType.String;
                            WriteLine("return " + tempString + ";");
                            break;
                        }
                    }

                    break;
                }
                case Instruction.RAGE_LOAD:
                    _stack.Op_RefGet();
                    break;
                case Instruction.RAGE_STORE:
                    if (_stack.PeekVar(1) == null)
                        WriteLine(_stack.Op_RefSet());
                    else if (_stack.PeekVar(1).IsArray)
                        _stack.Op_RefSet();
                    else
                        WriteLine(_stack.Op_RefSet());
                    break;
                case Instruction.RAGE_STORE_REV:
                    if (_stack.PeekVar(1) == null)
                        WriteLine(_stack.Op_PeekSet());
                    else if (_stack.PeekVar(1).IsArray)
                        _stack.Op_PeekSet();
                    else
                        WriteLine(_stack.Op_PeekSet());
                    break;
                case Instruction.RAGE_LOAD_N:
                    _stack.Op_ToStack();
                    break;
                case Instruction.RAGE_STORE_N:
                    WriteLine(_stack.Op_FromStack());
                    break;
                case Instruction.RAGE_ARRAY_U8:
                case Instruction.RAGE_ARRAY_U16:
                    _stack.Op_ArrayGetP(_instructions[_offset].GetOperandsAsUInt);
                    break;
                case Instruction.RAGE_ARRAY_U8_LOAD:
                case Instruction.RAGE_ARRAY_U16_LOAD:
                    _stack.Op_ArrayGet(_instructions[_offset].GetOperandsAsUInt);
                    break;
                case Instruction.RAGE_ARRAY_U8_STORE:
                case Instruction.RAGE_ARRAY_U16_STORE:
                    WriteLine(_stack.Op_ArraySet(_instructions[_offset].GetOperandsAsUInt));
                    break;
                case Instruction.RAGE_LOCAL_U8:
                case Instruction.RAGE_LOCAL_U16:
                    _stack.PushPVar(GetFrameVar(_instructions[_offset].GetOperandsAsUInt));
                    break;
                case Instruction.RAGE_LOCAL_U8_LOAD:
                case Instruction.RAGE_LOCAL_U16_LOAD:
                    _stack.PushVar(GetFrameVar(_instructions[_offset].GetOperandsAsUInt));
                    break;
                case Instruction.RAGE_LOCAL_U8_STORE:
                case Instruction.RAGE_LOCAL_U16_STORE:
                {
                    var var = GetFrameVar(_instructions[_offset].GetOperandsAsUInt);
                    var tempstring = _stack.Op_Set(var.Name, var);
                    if (var.DataType == Stack.DataType.Bool)
                        tempstring = tempstring.Replace("= 0;", "= false;").Replace("= 1;", "= true;");
                    if (!var.IsArray)
                        WriteLine(tempstring);
                    break;
                }
                case Instruction.RAGE_STATIC_U8:
                case Instruction.RAGE_STATIC_U16:
                    _stack.PushPVar(ScriptFile.Statics.GetVarAtIndex(_instructions[_offset].GetOperandsAsUInt).Fixed());
                    break;
                case Instruction.RAGE_STATIC_U8_LOAD:
                case Instruction.RAGE_STATIC_U16_LOAD:
                    _stack.PushVar(ScriptFile.Statics.GetVarAtIndex(_instructions[_offset].GetOperandsAsUInt).Fixed());
                    break;
                case Instruction.RAGE_STATIC_U8_STORE:
                case Instruction.RAGE_STATIC_U16_STORE:
                {
                    var var = ScriptFile.Statics.GetVarAtIndex(_instructions[_offset].GetOperandsAsUInt).Fixed();
                    var tempString =
                        _stack.Op_Set(ScriptFile.Statics.GetVarName(_instructions[_offset].GetOperandsAsUInt), var);
                    if (var.DataType == Stack.DataType.Bool)
                        tempString = tempString.Replace("= 0;", "= false;").Replace("= 1;", "= true;");
                    if (!var.IsArray)
                        WriteLine(tempString);
                    break;
                }
                case Instruction.RAGE_IADD_U8:
                case Instruction.RAGE_IADD_S16:
                    _stack.Op_AmmImm(_instructions[_offset].GetOperandsAsInt);
                    break;
                case Instruction.RAGE_IMUL_U8:
                case Instruction.RAGE_IMUL_S16:
                    _stack.Op_MultImm(_instructions[_offset].GetOperandsAsInt);
                    break;
                case Instruction.RAGE_IOFFSET:
                    _stack.Op_GetImmP();
                    break;
                case Instruction.RAGE_IOFFSET_U8:
                case Instruction.RAGE_IOFFSET_S16:
                    _stack.Op_GetImmP(_instructions[_offset].GetOperandsAsUInt);
                    break;
                case Instruction.RAGE_IOFFSET_U8_LOAD:
                case Instruction.RAGE_IOFFSET_S16_LOAD:
                    _stack.Op_GetImm(_instructions[_offset].GetOperandsAsUInt);
                    break;
                case Instruction.RAGE_IOFFSET_U8_STORE:
                case Instruction.RAGE_IOFFSET_S16_STORE:
                    WriteLine(_stack.Op_SetImm(_instructions[_offset].GetOperandsAsUInt));
                    break;
                case Instruction.RAGE_GLOBAL_U16:
                case Instruction.RAGE_GLOBAL_U24:
                    _stack.PushPGlobal(_instructions[_offset].GetGlobalString(IsAggregate));
                    break;
                case Instruction.RAGE_GLOBAL_U16_LOAD:
                case Instruction.RAGE_GLOBAL_U24_LOAD:
                    _stack.PushGlobal(_instructions[_offset].GetGlobalString(IsAggregate));
                    break;
                case Instruction.RAGE_GLOBAL_U16_STORE:
                case Instruction.RAGE_GLOBAL_U24_STORE:
                    WriteLine(_stack.Op_Set(_instructions[_offset].GetGlobalString(IsAggregate)));
                    break;
                case Instruction.RAGE_J:
                    HandleJumpCheck();
                    break;
                case Instruction.RAGE_JZ:
                    goto HandleJump;
                case Instruction.RAGE_IEQ_JZ:
                    _stack.Op_CmpEQ();
                    goto HandleJump;
                case Instruction.RAGE_INE_JZ:
                    _stack.Op_CmpNE();
                    goto HandleJump;
                case Instruction.RAGE_IGT_JZ:
                    _stack.Op_CmpGT();
                    goto HandleJump;
                case Instruction.RAGE_IGE_JZ:
                    _stack.Op_CmpGE();
                    goto HandleJump;
                case Instruction.RAGE_ILT_JZ:
                    _stack.Op_CmpLT();
                    goto HandleJump;
                case Instruction.RAGE_ILE_JZ:
                    _stack.Op_CmpLE();
                    goto HandleJump;
                case Instruction.RAGE_CALL:
                {
                    var tempf = GetFunctionNameFromOffset(_instructions[_offset].GetOperandsAsInt);
                    var tempstring = _stack.FunctionCall(tempf.Name, tempf.PCount, tempf.RCount);
                    if (tempstring != "")
                        WriteLine(tempstring);
                    break;
                }
                case Instruction.RAGE_SWITCH:
                    HandleSwitch();
                    break;
                case Instruction.RAGE_STRING:
                {
                    int tempint;
                    var tempstring = _stack.Pop().AsLiteral;
                    if (!Utils.IntParse(tempstring, out tempint))
                        _stack.Push("StringTable(" + tempstring + ")", Stack.DataType.StringPtr);
                    else if (!ScriptFile.StringTable.StringExists(tempint))
                        _stack.Push("StringTable(" + tempstring + ")", Stack.DataType.StringPtr);
                    else
                        _stack.Push("\"" + ScriptFile.StringTable[tempint] + "\"", Stack.DataType.StringPtr);
                    break;
                }
                case Instruction.RAGE_STRINGHASH:
                    _stack.Op_Hash();
                    break;
                case Instruction.RAGE_TEXT_LABEL_ASSIGN_STRING:
                    WriteLine(_stack.Op_StrCpy(_instructions[_offset].GetOperandsAsInt));
                    break;
                case Instruction.RAGE_TEXT_LABEL_ASSIGN_INT:
                    WriteLine(_stack.Op_ItoS(_instructions[_offset].GetOperandsAsInt));
                    break;
                case Instruction.RAGE_TEXT_LABEL_APPEND_STRING:
                    WriteLine(_stack.Op_StrAdd(_instructions[_offset].GetOperandsAsInt));
                    break;
                case Instruction.RAGE_TEXT_LABEL_APPEND_INT:
                    WriteLine(_stack.Op_StrAddI(_instructions[_offset].GetOperandsAsInt));
                    break;
                case Instruction.RAGE_TEXT_LABEL_COPY:
                    WriteLine(_stack.Op_SnCopy());
                    break;
                case Instruction.RAGE_CATCH:
                    throw new Exception(); // writeline("catch;"); break;
                case Instruction.RAGE_THROW:
                    throw new Exception(); // writeline("throw;"); break;
                case Instruction.RAGE_CALLINDIRECT:
                    foreach (var s in _stack.Pcall())
                        WriteLine(s);
                    break;
                case Instruction.RAGE_PUSH_CONST_M1:
                case Instruction.RAGE_PUSH_CONST_0:
                case Instruction.RAGE_PUSH_CONST_1:
                case Instruction.RAGE_PUSH_CONST_2:
                case Instruction.RAGE_PUSH_CONST_3:
                case Instruction.RAGE_PUSH_CONST_4:
                case Instruction.RAGE_PUSH_CONST_5:
                case Instruction.RAGE_PUSH_CONST_6:
                case Instruction.RAGE_PUSH_CONST_7:
                    _stack.Push(_instructions[_offset].GetImmBytePush);
                    break;
                case Instruction.RAGE_PUSH_CONST_FM1:
                case Instruction.RAGE_PUSH_CONST_F0:
                case Instruction.RAGE_PUSH_CONST_F1:
                case Instruction.RAGE_PUSH_CONST_F2:
                case Instruction.RAGE_PUSH_CONST_F3:
                case Instruction.RAGE_PUSH_CONST_F4:
                case Instruction.RAGE_PUSH_CONST_F5:
                case Instruction.RAGE_PUSH_CONST_F6:
                case Instruction.RAGE_PUSH_CONST_F7:
                    _stack.Push(_instructions[_offset].GetImmFloatPush);
                    break;

                // RDR Extended Instruction Set.
                case Instruction.RAGE_LOCAL_LOAD_S:
                case Instruction.RAGE_LOCAL_STORE_S:
                case Instruction.RAGE_LOCAL_STORE_SR:
                case Instruction.RAGE_STATIC_LOAD_S:
                case Instruction.RAGE_STATIC_STORE_S:
                case Instruction.RAGE_STATIC_STORE_SR:
                case Instruction.RAGE_LOAD_N_S:
                case Instruction.RAGE_STORE_N_S:
                case Instruction.RAGE_STORE_N_SR:
                case Instruction.RAGE_GLOBAL_LOAD_S:
                case Instruction.RAGE_GLOBAL_STORE_S:
                case Instruction.RAGE_GLOBAL_STORE_SR:
                    if (ScriptFile.CodeSet.Count <= 127) throw new Exception("Unexpected Instruction");
                    _stack.PushGlobal("RDR_" + _instructions[_offset].Instruction);
                    break;
                default:
                    throw new Exception("Unexpected Instruction");
                    HandleJump:
                    CheckConditional();
                    break;
            }

            _offset++;
        }

        //Bunch of methods that extracts what data type a static/frame variable is

        #region GetDataType

        public void CheckInstruction(int index, Stack.DataType type, int count = 1, bool functionPars = false)
        {
            if (type == Stack.DataType.Unk)
                return;
            for (var i = 0; i < count; i++)
            {
                var var = _stack.PeekVar(index + i);
                if (var != null && (_stack.IsLiteral(index + i) || _stack.IsPointer(index + i)))
                {
                    if (type.Precedence() < var.DataType.Precedence())
                        continue;
                    if (type == Stack.DataType.StringPtr && _stack.IsPointer(index + 1))
                        var.DataType = Stack.DataType.String;
                    else if (functionPars && _stack.IsPointer(index + i) && type.BaseType() != Stack.DataType.Unk)
                        var.DataType = type.BaseType();
                    else if (!functionPars)
                        var.DataType = type;
                    continue;
                }

                var func = _stack.PeekFunc(index + i);
                if (func != null)
                {
                    if (type.Precedence() < func.ReturnType.Precedence())
                        continue;
                    if (type == Stack.DataType.StringPtr && _stack.IsPointer(index + 1))
                        func.ReturnType = Stack.DataType.String;
                    else
                        func.ReturnType = type;
                    continue;
                }

                if (_stack.Isnat(index + i)) UpdateNativeReturnType(_stack.PeekNat64(index + i).Hash, type);
            }
        }

        public void CheckInstructionString(int index, int strsize, int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                var var = _stack.PeekVar(index + i);
                if (var != null && (_stack.IsLiteral(index + i) || _stack.IsPointer(index + i)))
                {
                    if (_stack.IsPointer(index + i))
                    {
                        if (var.Immediatesize == 1 || var.Immediatesize == strsize / 4)
                        {
                            var.DataType = Stack.DataType.String;
                            var.Immediatesize = strsize / 8;
                        }
                    }
                    else
                    {
                        var.DataType = Stack.DataType.StringPtr;
                    }

                    continue;
                }

                if (_stack.Isnat(index + i))
                    UpdateNativeReturnType(_stack.PeekNat64(index + i).Hash, Stack.DataType.StringPtr);
            }
        }

        public void SetImmediate(int size)
        {
            if (size == 15)
            {
            }

            var var = _stack.PeekVar(0);
            if (var != null && _stack.IsPointer(0))
            {
                if (var.DataType == Stack.DataType.String)
                {
                    if (var.Immediatesize != size)
                    {
                        var.Immediatesize = size;
                        var.Makestruct();
                    }
                }
                else
                {
                    var.Immediatesize = size;
                    var.Makestruct();
                }
            }
        }

        public void CheckImmediate(int size)
        {
            var var = _stack.PeekVar(0);
            if (var != null && _stack.IsPointer(0))
            {
                if (var.Immediatesize < size)
                    var.Immediatesize = size;
                var.Makestruct();
            }
        }

        public void CheckArray(uint width, int size = -1)
        {
            var var = _stack.PeekVar(0);
            if (var != null && _stack.IsPointer(0))
            {
                if (var.Value < size)
                    var.Value = size;
                var.Immediatesize = (int) width;
                var.Makearray();
            }

            CheckInstruction(1, Stack.DataType.Int);
        }

        public void SetArray(Stack.DataType type)
        {
            if (type == Stack.DataType.Unk)
                return;
            var var = _stack.PeekVar(0);
            if (var != null && _stack.IsPointer(0)) var.DataType = type;
        }

        public void ReturnCheck(string temp)
        {
            if (RCount != 1)
                return;
            if (ReturnType == Stack.DataType.Float)
                return;
            if (ReturnType == Stack.DataType.Int)
                return;
            if (ReturnType == Stack.DataType.Bool)
                return;
            if (temp.EndsWith("f"))
                ReturnType = Stack.DataType.Float;
            int tempint;
            if (Utils.IntParse(temp, out tempint))
            {
                ReturnType = Stack.DataType.Int;
                return;
            }

            if (temp.StartsWith("joaat("))
            {
                ReturnType = Stack.DataType.Int;
                return;
            }

            if (temp.StartsWith("func_"))
            {
                var loc = temp.Remove(temp.IndexOf("(")).Substring(5);
                if (int.TryParse(loc, out tempint))
                {
                    if (ScriptFile.Functions[tempint] == this) return;
                    if (!ScriptFile.Functions[tempint].Decoded)
                    {
                        if (!ScriptFile.Functions[tempint].DecodeStarted)
                            ScriptFile.Functions[tempint].Decode();
                        else
                            while (!ScriptFile.Functions[tempint].Decoded)
                                Thread.Sleep(1);
                    }

                    switch (ScriptFile.Functions[tempint].ReturnType)
                    {
                        case Stack.DataType.Float:
                        case Stack.DataType.Bool:
                        case Stack.DataType.Int:
                            ReturnType = ScriptFile.Functions[tempint].ReturnType;
                            break;
                    }

                    return;
                }
            }

            ReturnType = temp.EndsWith(")") && !temp.StartsWith("(") ? Stack.DataType.Unsure : Stack.DataType.Unsure;
        }


        public void DecodeInstructionsForVarInfo()
        {
            _stack = new Stack(this, true, IsAggregate);
            //ReturnType = Stack.DataType.Unk;
            for (var i = 0; i < _instructions.Count; i++)
            {
                var ins = _instructions[i];
                switch (ins.Instruction)
                {
                    case Instruction.RAGE_NOP:
                        break;
                    case Instruction.RAGE_IADD:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_Add();
                        break;
                    case Instruction.RAGE_FADD:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_Addf();
                        break;
                    case Instruction.RAGE_ISUB:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_Sub();
                        break;
                    case Instruction.RAGE_FSUB:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_Subf();
                        break;
                    case Instruction.RAGE_IMUL:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_Mult();
                        break;
                    case Instruction.RAGE_FMUL:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_Multf();
                        break;
                    case Instruction.RAGE_IDIV:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_Div();
                        break;
                    case Instruction.RAGE_FDIV:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_Divf();
                        break;
                    case Instruction.RAGE_IMOD:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_Mod();
                        break;
                    case Instruction.RAGE_FMOD:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_Modf();
                        break;
                    case Instruction.RAGE_INOT:
                        CheckInstruction(0, Stack.DataType.Bool);
                        _stack.Op_Not();
                        break;
                    case Instruction.RAGE_INEG:
                        CheckInstruction(0, Stack.DataType.Int);
                        _stack.Op_Neg();
                        break;
                    case Instruction.RAGE_FNEG:
                        CheckInstruction(0, Stack.DataType.Float);
                        _stack.Op_Negf();
                        break;
                    case Instruction.RAGE_IEQ:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_FEQ:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_INE:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_FNE:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_IGT:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_FGT:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_IGE:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_FGE:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_ILT:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_FLT:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_ILE:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_FLE:
                        CheckInstruction(0, Stack.DataType.Float, 2);
                        _stack.Op_CmpEQ();
                        break;
                    case Instruction.RAGE_VADD:
                        _stack.Op_Vadd();
                        break;
                    case Instruction.RAGE_VSUB:
                        _stack.Op_VSub();
                        break;
                    case Instruction.RAGE_VMUL:
                        _stack.Op_VMult();
                        break;
                    case Instruction.RAGE_VDIV:
                        _stack.Op_VDiv();
                        break;
                    case Instruction.RAGE_VNEG:
                        _stack.Op_VNeg();
                        break;
                    case Instruction.RAGE_IAND:
                        _stack.Op_And();
                        break;
                    case Instruction.RAGE_IOR:
                        _stack.Op_Or();
                        break;
                    case Instruction.RAGE_IXOR:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Op_Xor();
                        break;
                    case Instruction.RageI2F:
                        CheckInstruction(0, Stack.DataType.Int);
                        _stack.Op_Itof();
                        break;
                    case Instruction.RageF2I:
                        CheckInstruction(0, Stack.DataType.Float);
                        _stack.Op_FtoI();
                        break;
                    case Instruction.RageF2V:
                        CheckInstruction(0, Stack.DataType.Float);
                        _stack.Op_FtoV();
                        break;
                    case Instruction.RAGE_PUSH_CONST_U8:
                        _stack.Push(ins.GetOperand(0));
                        break;
                    case Instruction.RAGE_PUSH_CONST_U8_U8:
                        _stack.Push(ins.GetOperand(0), ins.GetOperand(1));
                        break;
                    case Instruction.RAGE_PUSH_CONST_U8_U8_U8:
                        _stack.Push(ins.GetOperand(0), ins.GetOperand(1), ins.GetOperand(2));
                        break;
                    case Instruction.RAGE_PUSH_CONST_U32:
                        _stack.Push(ins.GetOperandsAsInt.ToString(), Stack.DataType.Int);
                        break;
                    case Instruction.RAGE_PUSH_CONST_U24:
                    case Instruction.RAGE_PUSH_CONST_S16:
                        _stack.Push(ins.GetOperandsAsInt.ToString(), Stack.DataType.Int);
                        break;
                    case Instruction.RAGE_PUSH_CONST_F:
                        _stack.Push(ins.GetFloat);
                        break;
                    case Instruction.RAGE_DUP:
                        _stack.Dup();
                        break;
                    case Instruction.RAGE_DROP:
                        _stack.Drop();
                        break;
                    case Instruction.RAGE_NATIVE:
                    {
                        var hash = ScriptFile.X64NativeTable.GetNativeHashFromIndex(ins.GetNativeIndex);
                        ScriptFile.CrossReferenceNative(hash, this);
                        _stack.NativeCallTest(hash, ScriptFile.X64NativeTable.GetNativeFromIndex(ins.GetNativeIndex),
                            ins.GetNativeParams, ins.GetNativeReturns);
                        break;
                    }
                    case Instruction.RAGE_ENTER:
                        throw new Exception("Unexpected Function Definition");
                    case Instruction.RAGE_LEAVE:
                        _stack.PopListForCall(ins.GetOperand(1));
                        break;
                    case Instruction.RAGE_LOAD:
                        _stack.Op_RefGet();
                        break;
                    case Instruction.RAGE_STORE:
                    {
                        if (_stack.PeekVar(1) == null)
                        {
                            _stack.Drop();
                            _stack.Drop();
                            break;
                        }

                        if (_stack.TopType == Stack.DataType.Int)
                        {
                            int tempint;
                            var tempstring = _stack.Pop().AsLiteral;
                            if (Utils.IntParse(tempstring, out tempint))
                                _stack.PeekVar(0).Value = tempint;
                            break;
                        }

                        _stack.Drop();
                        break;
                    }
                    case Instruction.RAGE_STORE_REV:
                    {
                        if (_stack.PeekVar(1) == null)
                        {
                            _stack.Drop();
                            break;
                        }

                        if (_stack.TopType == Stack.DataType.Int)
                        {
                            int tempint;
                            var tempstring = _stack.Pop().AsLiteral;
                            if (Utils.IntParse(tempstring, out tempint))
                                _stack.PeekVar(0).Value = tempint;
                        }

                        break;
                    }
                    case Instruction.RAGE_LOAD_N:
                    {
                        int tempint;
                        if (Program.IntStyle == Program.IntType.Hex)
                            tempint = int.Parse(_stack.PeekItem(1).Substring(2), NumberStyles.HexNumber);
                        else
                            tempint = int.Parse(_stack.PeekItem(1));
                        SetImmediate(tempint);
                        _stack.Op_ToStack();
                        break;
                    }
                    case Instruction.RAGE_STORE_N:
                    {
                        int tempint;
                        if (Program.IntStyle == Program.IntType.Hex)
                            tempint = int.Parse(_stack.PeekItem(1).Substring(2), NumberStyles.HexNumber);
                        else
                            tempint = int.Parse(_stack.PeekItem(1));
                        SetImmediate(tempint);
                        _stack.Op_FromStack();
                        break;
                    }
                    case Instruction.RAGE_ARRAY_U8:
                    case Instruction.RAGE_ARRAY_U16:
                    {
                        int tempint;
                        if (!Utils.IntParse(_stack.PeekItem(1), out tempint))
                            tempint = -1;
                        CheckArray(ins.GetOperandsAsUInt, tempint);
                        _stack.Op_ArrayGetP(ins.GetOperandsAsUInt);
                        break;
                    }
                    case Instruction.RAGE_ARRAY_U8_LOAD:
                    case Instruction.RAGE_ARRAY_U16_LOAD:
                    {
                        int tempint;
                        if (!Utils.IntParse(_stack.PeekItem(1), out tempint))
                            tempint = -1;
                        CheckArray(ins.GetOperandsAsUInt, tempint);
                        _stack.Op_ArrayGet(ins.GetOperandsAsUInt);
                        break;
                    }
                    case Instruction.RAGE_ARRAY_U8_STORE:
                    case Instruction.RAGE_ARRAY_U16_STORE:
                    {
                        int tempint;
                        if (!Utils.IntParse(_stack.PeekItem(1), out tempint))
                            tempint = -1;
                        CheckArray(ins.GetOperandsAsUInt, tempint);
                        SetArray(_stack.ItemType(2));
                        var var = _stack.PeekVar(0);
                        if (var != null && _stack.IsPointer(0))
                            CheckInstruction(2, var.DataType);
                        _stack.Op_ArraySet(ins.GetOperandsAsUInt);
                        break;
                    }
                    case Instruction.RAGE_LOCAL_U8:
                    case Instruction.RAGE_LOCAL_U16:
                        _stack.PushPVar(GetFrameVar(ins.GetOperandsAsUInt));
                        GetFrameVar(ins.GetOperandsAsUInt).Call();
                        break;
                    case Instruction.RAGE_LOCAL_U8_LOAD:
                    case Instruction.RAGE_LOCAL_U16_LOAD:
                        _stack.PushVar(GetFrameVar(ins.GetOperandsAsUInt));
                        GetFrameVar(ins.GetOperandsAsUInt).Call();
                        break;
                    case Instruction.RAGE_LOCAL_U8_STORE:
                    case Instruction.RAGE_LOCAL_U16_STORE:
                    {
                        if (_stack.TopType != Stack.DataType.Unk)
                        {
                            if (_stack.TopType.Precedence() > GetFrameVar(ins.GetOperandsAsUInt).DataType.Precedence())
                                GetFrameVar(ins.GetOperandsAsUInt).DataType = _stack.TopType;
                        }
                        else
                        {
                            CheckInstruction(0, GetFrameVar(ins.GetOperandsAsUInt).DataType);
                        }

                        var tempstring = _stack.Pop().AsLiteral;
                        if (_stack.TopType == Stack.DataType.Int)
                        {
                            tempstring = _stack.Pop().AsLiteral;
                            if (ins.GetOperandsAsUInt > PCount)
                            {
                                int tempint;
                                if (Utils.IntParse(tempstring, out tempint))
                                    GetFrameVar(ins.GetOperandsAsUInt).Value = tempint;
                            }
                        }
                        else
                        {
                            _stack.Drop();
                        }

                        GetFrameVar(ins.GetOperandsAsUInt).Call();
                        break;
                    }
                    case Instruction.RAGE_STATIC_U8:
                    case Instruction.RAGE_STATIC_U16:
                        _stack.PushPVar(ScriptFile.Statics.GetVarAtIndex(ins.GetOperandsAsUInt).Fixed());
                        break;
                    case Instruction.RAGE_STATIC_U8_LOAD:
                    case Instruction.RAGE_STATIC_U16_LOAD:
                        _stack.PushVar(ScriptFile.Statics.GetVarAtIndex(ins.GetOperandsAsUInt).Fixed());
                        break;
                    case Instruction.RAGE_STATIC_U8_STORE:
                    case Instruction.RAGE_STATIC_U16_STORE:
                        if (_stack.TopType != Stack.DataType.Unk)
                            ScriptFile.UpdateStaticType(ins.GetOperandsAsUInt, _stack.TopType);
                        else
                            CheckInstruction(0, ScriptFile.Statics.GetTypeAtIndex(ins.GetOperandsAsUInt));
                        _stack.Drop();
                        break;
                    case Instruction.RAGE_IADD_U8:
                    case Instruction.RAGE_IADD_S16:
                    case Instruction.RAGE_IMUL_U8:
                    case Instruction.RAGE_IMUL_S16:
                        CheckInstruction(0, Stack.DataType.Int);
                        _stack.Op_AmmImm(ins.GetOperandsAsInt);
                        break;
                    case Instruction.RAGE_IOFFSET:
                        _stack.Op_GetImmP();
                        break;
                    case Instruction.RAGE_IOFFSET_U8:
                    case Instruction.RAGE_IOFFSET_S16:
                        CheckImmediate((int) ins.GetOperandsAsUInt + 1);
                        _stack.Op_GetImmP(ins.GetOperandsAsUInt);
                        break;
                    case Instruction.RAGE_IOFFSET_U8_LOAD:
                    case Instruction.RAGE_IOFFSET_S16_LOAD:
                        CheckImmediate((int) ins.GetOperandsAsUInt + 1);
                        _stack.Op_GetImm(ins.GetOperandsAsUInt);
                        break;
                    case Instruction.RAGE_IOFFSET_U8_STORE:
                    case Instruction.RAGE_IOFFSET_S16_STORE:
                        CheckImmediate((int) ins.GetOperandsAsUInt + 1);
                        _stack.Op_SetImm(ins.GetOperandsAsUInt);
                        break;
                    case Instruction.RAGE_GLOBAL_U16:
                    case Instruction.RAGE_GLOBAL_U24:
                        if (IsAggregate) _stack.PushPointer("Global_");
                        else _stack.PushPointer("Global_" + ins.GetOperandsAsUInt);
                        break;
                    case Instruction.RAGE_GLOBAL_U16_LOAD:
                    case Instruction.RAGE_GLOBAL_U24_LOAD:
                        if (IsAggregate) _stack.Push("Global_");
                        else _stack.Push("Global_" + ins.GetOperandsAsUInt);
                        break;
                    case Instruction.RAGE_GLOBAL_U16_STORE:
                    case Instruction.RAGE_GLOBAL_U24_STORE:
                        if (IsAggregate) _stack.Push("Global_");
                        else _stack.Op_Set("Global_" + ins.GetOperandsAsUInt);
                        break;
                    case Instruction.RAGE_J:
                        break;
                    case Instruction.RAGE_JZ:
                        CheckInstruction(0, Stack.DataType.Bool);
                        _stack.Drop();
                        break;
                    case Instruction.RAGE_IEQ_JZ:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Drop();
                        _stack.Drop();
                        break;
                    case Instruction.RAGE_INE_JZ:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Drop();
                        _stack.Drop();
                        break;
                    case Instruction.RAGE_IGT_JZ:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Drop();
                        _stack.Drop();
                        break;
                    case Instruction.RAGE_IGE_JZ:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Drop();
                        _stack.Drop();
                        break;
                    case Instruction.RAGE_ILT_JZ:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Drop();
                        _stack.Drop();
                        break;
                    case Instruction.RAGE_ILE_JZ:
                        CheckInstruction(0, Stack.DataType.Int, 2);
                        _stack.Drop();
                        _stack.Drop();
                        break;
                    case Instruction.RAGE_CALL:
                    {
                        var func = GetFunctionFromOffset(ins.GetOperandsAsInt);
                        if (!func.PreDecodeStarted)
                            func.PreDecode();
                        if (func.PreDecoded)
                            for (var j = 0; j < func.PCount; j++)
                            {
                                if (_stack.ItemType(func.PCount - j - 1) != Stack.DataType.Unk)
                                    if (_stack.ItemType(func.PCount - j - 1).Precedence() >
                                        func.Params.GetTypeAtIndex((uint) j).Precedence())
                                        if (func != this)
                                            func.UpdateFuncParamType((uint) j, _stack.ItemType(func.PCount - j - 1));
                                CheckInstruction(func.PCount - j - 1, func.Params.GetTypeAtIndex((uint) j), 1, true);
                            }

                        Associate(func);
                        _stack.FunctionCall(func);
                        break;
                    }
                    case Instruction.RAGE_SWITCH:
                        CheckInstruction(0, Stack.DataType.Int, Program.RdrOpcodes ? 2 : 1);
                        break;
                    case Instruction.RAGE_STRING:
                    {
                        var tempstring = _stack.Pop().AsLiteral;
                        _stack.PushString("");
                        break;
                    }
                    case Instruction.RAGE_STRINGHASH:
                        CheckInstruction(0, Stack.DataType.StringPtr);
                        _stack.Op_Hash();
                        break;
                    case Instruction.RAGE_TEXT_LABEL_ASSIGN_STRING:
                        CheckInstructionString(0, ins.GetOperandsAsInt, 2);
                        _stack.Op_StrCpy(ins.GetOperandsAsInt);
                        break;
                    case Instruction.RAGE_TEXT_LABEL_ASSIGN_INT:
                        CheckInstructionString(0, ins.GetOperandsAsInt);
                        CheckInstruction(1, Stack.DataType.Int);
                        _stack.Op_ItoS(ins.GetOperandsAsInt);
                        break;
                    case Instruction.RAGE_TEXT_LABEL_APPEND_STRING:
                        CheckInstructionString(0, ins.GetOperandsAsInt, 2);
                        _stack.Op_StrAdd(ins.GetOperandsAsInt);
                        break;
                    case Instruction.RAGE_TEXT_LABEL_APPEND_INT:
                        CheckInstructionString(0, ins.GetOperandsAsInt);
                        CheckInstruction(1, Stack.DataType.Int);
                        _stack.Op_StrAddI(ins.GetOperandsAsInt);
                        break;
                    case Instruction.RAGE_TEXT_LABEL_COPY:
                        _stack.Op_SnCopy();
                        break;
                    case Instruction.RAGE_CATCH:
                        break;
                    case Instruction.RAGE_THROW:
                        break;
                    case Instruction.RAGE_CALLINDIRECT:
                        _stack.Pcall();
                        break;
                    case Instruction.RAGE_PUSH_CONST_M1:
                    case Instruction.RAGE_PUSH_CONST_0:
                    case Instruction.RAGE_PUSH_CONST_1:
                    case Instruction.RAGE_PUSH_CONST_2:
                    case Instruction.RAGE_PUSH_CONST_3:
                    case Instruction.RAGE_PUSH_CONST_4:
                    case Instruction.RAGE_PUSH_CONST_5:
                    case Instruction.RAGE_PUSH_CONST_6:
                    case Instruction.RAGE_PUSH_CONST_7:
                        _stack.Push(ins.GetImmBytePush);
                        break;
                    case Instruction.RAGE_PUSH_CONST_FM1:
                    case Instruction.RAGE_PUSH_CONST_F0:
                    case Instruction.RAGE_PUSH_CONST_F1:
                    case Instruction.RAGE_PUSH_CONST_F2:
                    case Instruction.RAGE_PUSH_CONST_F3:
                    case Instruction.RAGE_PUSH_CONST_F4:
                    case Instruction.RAGE_PUSH_CONST_F5:
                    case Instruction.RAGE_PUSH_CONST_F6:
                    case Instruction.RAGE_PUSH_CONST_F7:
                        _stack.Push(ins.GetImmFloatPush);
                        break;

                    // RDR Extended Instruction Set.
                    case Instruction.RAGE_LOCAL_LOAD_S:
                    case Instruction.RAGE_LOCAL_STORE_S:
                    case Instruction.RAGE_LOCAL_STORE_SR:
                    case Instruction.RAGE_STATIC_LOAD_S:
                    case Instruction.RAGE_STATIC_STORE_S:
                    case Instruction.RAGE_STATIC_STORE_SR:
                    case Instruction.RAGE_LOAD_N_S:
                    case Instruction.RAGE_STORE_N_S:
                    case Instruction.RAGE_STORE_N_SR:
                    case Instruction.RAGE_GLOBAL_LOAD_S:
                    case Instruction.RAGE_GLOBAL_STORE_S:
                    case Instruction.RAGE_GLOBAL_STORE_SR:
                        if (ScriptFile.CodeSet.Count <= 127) throw new Exception("Unexpected Instruction");
                        break;
                    default:
                        throw new Exception("Unexpected Instruction");
                }
            }

            Vars.Checkvars();
            Params.Checkvars();
        }

        #endregion
    }

    public class FunctionName
    {
        internal FunctionName(string name, int pCount, int rCount, int minLoc, int maxLoc)
        {
            PCount = pCount;
            RCount = rCount;
            MinLoc = minLoc;
            //MaxLoc = maxLoc;
            Name = name;
            RetType = Stack.DataType.Unk;
        }

        public string Name { get; }

        public int PCount { get; }

        public int RCount { get; }

        internal int MinLoc { get; }

        //internal int MaxLoc { get; }

        public Stack.DataType RetType { get; set; }
    }
}
