using System.Collections.Generic;
using System.Linq;

namespace RageDecompiler
{
    //Not a fan of this code, should really have been handled using a tree but too far into project to change this now
    internal class CodePath
    {
        public int BreakOffset;

        public List<CodePath> ChildPaths;
        public int EndOffset;
        public bool Escaped = false;
        public CodePath Parent;
        public CodePathType Type;

        public CodePath(CodePathType type, int endOffset, int breakOffset)
        {
            Parent = null;
            Type = type;
            EndOffset = endOffset;
            BreakOffset = breakOffset;
            ChildPaths = new List<CodePath>();
        }

        public CodePath(CodePath parent, CodePathType type, int endOffset, int breakOffset)
        {
            Parent = parent;
            Type = type;
            EndOffset = endOffset;
            BreakOffset = breakOffset;
            ChildPaths = new List<CodePath>();
        }

        public bool IsSwitch => Type == CodePathType.Switch;

        public CodePath CreateCodePath(CodePathType type, int endOffset, int breakOffset)
        {
            var c = new CodePath(this, type, endOffset, breakOffset);
            ChildPaths.Add(c);
            return c;
        }

        public virtual bool AllEscaped()
        {
            var escaped = true;
            foreach (var p in ChildPaths)
                escaped &= p.Escaped;
            return escaped;
        }
    }

    internal enum CodePathType
    {
        While,
        If,
        Else,
        Main,
        Switch
    }

    internal class SwitchPath : CodePath
    {
        public int ActiveOffset = -1;

        public Dictionary<int, List<string>> Cases;
        public Dictionary<int, bool> EscapedCases = new Dictionary<int, bool>();
        public bool HasDefaulted = false;
        public List<int> Offsets;

        public SwitchPath(CodePathType type, int endOffset, int breakOffset)
            : base(type, endOffset, breakOffset)
        {
            Offsets = new List<int>();
            Cases = new Dictionary<int, List<string>>();
        }

        public SwitchPath(CodePath parent, CodePathType type, int endOffset, int breakOffset)
            : base(parent, type, endOffset, breakOffset)
        {
            Offsets = new List<int>();
            Cases = new Dictionary<int, List<string>>();
        }

        public SwitchPath(Dictionary<int, List<string>> cases, int endOffset, int breakOffset)
            : base(CodePathType.Switch, endOffset, breakOffset)
        {
            Cases = cases;
            Offsets = cases == null ? new List<int>() : cases.Keys.ToList();
            if (Program.RdrOpcodes) Offsets.Sort();
            Offsets.Add(breakOffset); // Ensure default is last offset
            foreach (var offset in Offsets)
                EscapedCases[offset] = false;
        }

        public SwitchPath(CodePath parent, Dictionary<int, List<string>> cases, int endOffset, int breakOffset)
            : base(parent, CodePathType.Switch, endOffset, breakOffset)
        {
            Cases = cases;
            Offsets = cases == null ? new List<int>() : cases.Keys.ToList();
            if (Program.RdrOpcodes) Offsets.Sort();
            Offsets.Add(breakOffset); // Ensure default is last offset
            foreach (var offset in Offsets)
                EscapedCases[offset] = false;
        }

        public override bool AllEscaped()
        {
            var escaped = base.AllEscaped();
            foreach (var entry in EscapedCases)
                escaped &= entry.Value;
            return escaped;
        }
    }
}
