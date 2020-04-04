using System;
using System.Collections.Generic;
using System.Linq;

namespace RageDecompiler
{
    internal class HlInstruction
    {
        private readonly byte[] _operands;

        public HlInstruction(Instruction instruction, IEnumerable<byte> operands, int offset)
        {
            Instruction = instruction;
            _operands = operands.ToArray();
            Offset = offset;
        }

        public HlInstruction(byte instruction, IEnumerable<byte> operands, int offset)
        {
            Instruction = (Instruction) instruction;
            _operands = operands.ToArray();
            Offset = offset;
        }

        public HlInstruction(Instruction instruction, int offset)
        {
            Instruction = instruction;
            _operands = new byte[0];
            Offset = offset;
        }

        public HlInstruction(byte instruction, int offset)
        {
            Instruction = (Instruction) instruction;
            _operands = new byte[0];
            Offset = offset;
        }

        public Instruction Instruction { get; private set; }

        public int Offset { get; }

        public int InstructionLength => 1 + _operands.Count();

        public int GetOperandsAsInt
        {
            get
            {
                switch (_operands.Count())
                {
                    case 1:
                        return _operands[0];
                    case 2:
                        return Program.SwapEndian
                            ? Utils.SwapEndian(BitConverter.ToInt16(_operands, 0))
                            : BitConverter.ToInt16(_operands, 0);
                    case 3:
                        return Program.SwapEndian
                            ? (_operands[0] << 16) | (_operands[1] << 8) | _operands[2]
                            : (_operands[2] << 16) | (_operands[1] << 8) | _operands[0];
                    case 4:
                        return Program.SwapEndian
                            ? Utils.SwapEndian(BitConverter.ToInt32(_operands, 0))
                            : BitConverter.ToInt32(_operands, 0);
                    default:
                        throw new Exception("Invalid amount of operands (" + _operands.Count() + ")");
                }
            }
        }

        public ushort GetOperandsAsUInt16 => BitConverter.ToUInt16(_operands, 0);

        public float GetFloat
        {
            get
            {
                if (_operands.Count() != 4)
                    throw new Exception("Not a Float");
                return Program.SwapEndian
                    ? Utils.SwapEndian(BitConverter.ToSingle(_operands, 0))
                    : BitConverter.ToSingle(_operands, 0);
            }
        }

        public uint GetOperandsAsUInt
        {
            get
            {
                switch (_operands.Count())
                {
                    case 1:
                        return _operands[0];
                    case 2:
                        return Program.SwapEndian
                            ? (uint) Utils.SwapEndian(BitConverter.ToInt16(_operands, 0))
                            : BitConverter.ToUInt16(_operands, 0);
                    case 3:
                        return Program.SwapEndian
                            ? (uint) ((_operands[2] << 16) | (_operands[1] << 8) | _operands[0])
                            : (uint) ((_operands[2] << 16) | (_operands[1] << 8) | _operands[0]);
                    case 4:
                        return Program.SwapEndian
                            ? BitConverter.ToUInt32(_operands, 0)
                            : BitConverter.ToUInt32(_operands, 0);
                    default:
                        throw new Exception("Invalid amount of operands (" + _operands.Count() + ")");
                }
            }
        }

        public int GetJumpOffset
        {
            get
            {
                if (!IsJumpInstruction)
                    throw new Exception("Not A jump");
                var length = BitConverter.ToInt16(_operands, 0);
                return Offset + 3 + (Program.SwapEndian ? Utils.SwapEndian(length) : length);
            }
        }

        public byte GetNativeParams
        {
            get
            {
                if (Instruction == Instruction.RAGE_NATIVE) return (byte) (_operands[0] >> 2);
                throw new Exception("Not A Native");
            }
        }

        public byte GetNativeReturns
        {
            get
            {
                if (Instruction == Instruction.RAGE_NATIVE) return (byte) (_operands[0] & 0x3);
                throw new Exception("Not A Native");
            }
        }

        public ushort GetNativeIndex
        {
            get
            {
                if (Instruction == Instruction.RAGE_NATIVE)
                    return Utils.SwapEndian(BitConverter.ToUInt16(_operands, 1));
                throw new Exception("Not A Native");
            }
        }

        public int GetImmBytePush
        {
            get
            {
                var instruction = (int) Instruction;
                if (instruction >= (int) Instruction.RAGE_PUSH_CONST_M1 &&
                    instruction <= (int) Instruction.RAGE_PUSH_CONST_7)
                    return instruction - (int) Instruction.RAGE_PUSH_CONST_0;
                throw new Exception("Not An Immediate Int Push");
            }
        }

        public float GetImmFloatPush
        {
            get
            {
                var instruction = (int) Instruction;
                if (instruction >= (int) Instruction.RAGE_PUSH_CONST_FM1 &&
                    instruction <= (int) Instruction.RAGE_PUSH_CONST_F7)
                    return instruction - (int) Instruction.RAGE_PUSH_CONST_F0;
                throw new Exception("Not An Immediate Float Push");
            }
        }

        public bool IsJumpInstruction => (int) Instruction > (int) Instruction.RAGE_GLOBAL_U16_STORE &&
                                         (int) Instruction < (int) Instruction.RAGE_CALL;

        public bool IsConditionJump => (int) Instruction > (int) Instruction.RAGE_J &&
                                       (int) Instruction < (int) Instruction.RAGE_CALL;

        public bool IsWhileJump
        {
            get
            {
                if (Instruction == Instruction.RAGE_J)
                {
                    if (GetJumpOffset <= 0) return false;
                    return GetOperandsAsInt < 0;
                }

                return false;
            }
        }

        public void NopInstruction()
        {
            Instruction = Instruction.RAGE_NOP;
        }

        public byte GetOperand(int index)
        {
            return _operands[index];
        }

        /*public int GetSwitchCase(int index)
		{
			if (instruction == Instruction.Switch)
			{
				int cases = GetOperand(0);
				if (index >= cases)
					throw new Exception("Out Or Range Script Case");
				return Utils.SwapEndian(BitConverter.ToInt32(operands, 1 + index * 6));
			}
			throw new Exception("Not A Switch Statement");
		}*/

        public string GetSwitchStringCase(int index)
        {
            if (Instruction != Instruction.RAGE_SWITCH)
                throw new Exception("Not A Switch Statement");

            int cases;
            if (Program.RdrOpcodes)
            {
                if ((cases = BitConverter.ToUInt16(_operands, 0)) <= index)
                    throw new Exception("Out Or Range Script Case");

                if (Program.IntStyle == Program.IntType.Uint)
                {
                    var hash = BitConverter.ToUInt32(_operands, 2 + index * 6);
                    return Program.Hashbank.GetHash(Program.SwapEndian ? Utils.SwapEndian(hash) : hash);
                }
                else
                {
                    var hash = BitConverter.ToInt32(_operands, 2 + index * 6);
                    return Program.Hashbank.GetHash(Program.SwapEndian ? Utils.SwapEndian(hash) : hash);
                }
            }

            if ((cases = GetOperand(0)) <= index)
            {
                throw new Exception("Out Or Range Script Case");
            }

            if (Program.IntStyle == Program.IntType.Uint)
            {
                var hash = BitConverter.ToUInt32(_operands, 1 + index * 6);
                return Program.Hashbank.GetHash(Program.SwapEndian ? Utils.SwapEndian(hash) : hash);
            }
            else
            {
                var hash = BitConverter.ToInt32(_operands, 1 + index * 6);
                return Program.Hashbank.GetHash(Program.SwapEndian ? Utils.SwapEndian(hash) : hash);
            }
        }

        public int GetSwitchOffset(int index)
        {
            if (Instruction != Instruction.RAGE_SWITCH)
                throw new Exception("Not A Switch Statement");

            int cases;
            if (Program.RdrOpcodes)
            {
                if ((cases = BitConverter.ToUInt16(_operands, 0)) <= index)
                    throw new Exception("Out of range script case");
                var length = BitConverter.ToInt16(_operands, 6 + index * 6);
                return Offset + 8 + 1 + index * 6 + (Program.SwapEndian ? Utils.SwapEndian(length) : length);
            }
            else
            {
                if ((cases = GetOperand(0)) <= index)
                    throw new Exception("Out Or Range Script Case");
                var length = BitConverter.ToInt16(_operands, 5 + index * 6);
                return Offset + 8 + index * 6 + (Program.SwapEndian ? Utils.SwapEndian(length) : length);
            }
        }

        public string GetGlobalString(bool aggregateName)
        {
            switch (Instruction)
            {
                case Instruction.RAGE_GLOBAL_U16:
                case Instruction.RAGE_GLOBAL_U16_LOAD:
                case Instruction.RAGE_GLOBAL_U16_STORE:
                    if (aggregateName) return "Global";
                    return "Global_" + (Program.HexIndex
                        ? GetOperandsAsUInt.ToString("X")
                        : GetOperandsAsUInt.ToString());
                case Instruction.RAGE_GLOBAL_U24:
                case Instruction.RAGE_GLOBAL_U24_LOAD:
                case Instruction.RAGE_GLOBAL_U24_STORE:
                    if (aggregateName) return "Global";
                    return "Global_" + (Program.HexIndex
                        ? GetOperandsAsUInt.ToString("X")
                        : GetOperandsAsUInt.ToString());
            }

            throw new Exception("Not a global variable");
        }
    }

    public enum Instruction //opcodes reversed from gta v default.xex
    {
        RAGE_NOP = 0,
        RAGE_IADD, //1
        RAGE_ISUB, //2
        RAGE_IMUL, //3
        RAGE_IDIV, //4
        RAGE_IMOD, //5
        RAGE_INOT, //6
        RAGE_INEG, //7
        RAGE_IEQ, //8
        RAGE_INE, //9
        RAGE_IGT, //10
        RAGE_IGE, //11
        RAGE_ILT, //12
        RAGE_ILE, //13
        RAGE_FADD, //14
        RAGE_FSUB, //15
        RAGE_FMUL, //16
        RAGE_FDIV, //17
        RAGE_FMOD, //18
        RAGE_FNEG, //19
        RAGE_FEQ, //20
        RAGE_FNE, //21
        RAGE_FGT, //22
        RAGE_FGE, //23
        RAGE_FLT, //24
        RAGE_FLE, //25
        RAGE_VADD, //26
        RAGE_VSUB, //27
        RAGE_VMUL, //28
        RAGE_VDIV, //29
        RAGE_VNEG, //30
        RAGE_IAND, //31
        RAGE_IOR, //32
        RAGE_IXOR, //33
        RageI2F, //34
        RageF2I, //35
        RageF2V, //36
        RAGE_PUSH_CONST_U8, //37
        RAGE_PUSH_CONST_U8_U8, //38
        RAGE_PUSH_CONST_U8_U8_U8, //39
        RAGE_PUSH_CONST_U32, //40
        RAGE_PUSH_CONST_F, //41
        RAGE_DUP, //42
        RAGE_DROP, //43
        RAGE_NATIVE, //44
        RAGE_ENTER, //45
        RAGE_LEAVE, //46
        RAGE_LOAD, //47
        RAGE_STORE, //48
        RAGE_STORE_REV, //49
        RAGE_LOAD_N, //50
        RAGE_STORE_N, //51
        RAGE_ARRAY_U8, //52
        RAGE_ARRAY_U8_LOAD, //53
        RAGE_ARRAY_U8_STORE, //54
        RAGE_LOCAL_U8, //55
        RAGE_LOCAL_U8_LOAD, //56
        RAGE_LOCAL_U8_STORE, //57
        RAGE_STATIC_U8, //58
        RAGE_STATIC_U8_LOAD, //59
        RAGE_STATIC_U8_STORE, //60
        RAGE_IADD_U8, //61
        RAGE_IMUL_U8, //62
        RAGE_IOFFSET, //63
        RAGE_IOFFSET_U8, //64
        RAGE_IOFFSET_U8_LOAD, //65
        RAGE_IOFFSET_U8_STORE, //66
        RAGE_PUSH_CONST_S16, //67
        RAGE_IADD_S16, //68
        RAGE_IMUL_S16, //69
        RAGE_IOFFSET_S16, //70
        RAGE_IOFFSET_S16_LOAD, //71
        RAGE_IOFFSET_S16_STORE, //72
        RAGE_ARRAY_U16, //73
        RAGE_ARRAY_U16_LOAD, //74
        RAGE_ARRAY_U16_STORE, //75
        RAGE_LOCAL_U16, //76
        RAGE_LOCAL_U16_LOAD, //77
        RAGE_LOCAL_U16_STORE, //78
        RAGE_STATIC_U16, //79
        RAGE_STATIC_U16_LOAD, //80
        RAGE_STATIC_U16_STORE, //81
        RAGE_GLOBAL_U16, //82
        RAGE_GLOBAL_U16_LOAD, //83
        RAGE_GLOBAL_U16_STORE, //84
        RAGE_J, //85
        RAGE_JZ, //86
        RAGE_IEQ_JZ, //87
        RAGE_INE_JZ, //88
        RAGE_IGT_JZ, //89
        RAGE_IGE_JZ, //90
        RAGE_ILT_JZ, //91
        RAGE_ILE_JZ, //92
        RAGE_CALL, //93
        RAGE_GLOBAL_U24, //94
        RAGE_GLOBAL_U24_LOAD, //95
        RAGE_GLOBAL_U24_STORE, //96
        RAGE_PUSH_CONST_U24, //97
        RAGE_SWITCH, //98
        RAGE_STRING, //99
        RAGE_STRINGHASH, //100
        RAGE_TEXT_LABEL_ASSIGN_STRING, //101
        RAGE_TEXT_LABEL_ASSIGN_INT, //102
        RAGE_TEXT_LABEL_APPEND_STRING, //103
        RAGE_TEXT_LABEL_APPEND_INT, //104
        RAGE_TEXT_LABEL_COPY, //105
        RAGE_CATCH, //106, No handling of these as Im unsure exactly how they work
        RAGE_THROW, //107, No script files in the game use these opcodes
        RAGE_CALLINDIRECT, //108
        RAGE_PUSH_CONST_M1, //109
        RAGE_PUSH_CONST_0, //110
        RAGE_PUSH_CONST_1, //111
        RAGE_PUSH_CONST_2, //112
        RAGE_PUSH_CONST_3, //113
        RAGE_PUSH_CONST_4, //114
        RAGE_PUSH_CONST_5, //115
        RAGE_PUSH_CONST_6, //116
        RAGE_PUSH_CONST_7, //117
        RAGE_PUSH_CONST_FM1, //118
        RAGE_PUSH_CONST_F0, //119
        RAGE_PUSH_CONST_F1, //120
        RAGE_PUSH_CONST_F2, //121
        RAGE_PUSH_CONST_F3, //122
        RAGE_PUSH_CONST_F4, //123
        RAGE_PUSH_CONST_F5, //124
        RAGE_PUSH_CONST_F6, //125
        RAGE_PUSH_CONST_F7, //126

        // Extended RDR Instructions
        RAGE_LOCAL_LOAD_S, //127
        RAGE_LOCAL_STORE_S, //128
        RAGE_LOCAL_STORE_SR, //129
        RAGE_STATIC_LOAD_S, //130
        RAGE_STATIC_STORE_S, //131
        RAGE_STATIC_STORE_SR, //132
        RAGE_LOAD_N_S, //133
        RAGE_STORE_N_S, //134
        RAGE_STORE_N_SR, //135
        RAGE_GLOBAL_LOAD_S, //136
        RAGE_GLOBAL_STORE_S, //137
        RAGE_GLOBAL_STORE_SR, //138
        RageLast //139
    }

    /// <summary>
    ///     Wrapped used for converting opcodes.
    /// </summary>
    public class OpcodeSet
    {
        /// <summary>
        ///     Index of RAGE_last
        /// </summary>
        public virtual int Count => 127;

        /// <summary>
        ///     Convert a codeblock byte to Instruction.
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        public virtual Instruction Map(byte v)
        {
            return v < Count ? (Instruction) v : Instruction.RageLast;
        }

        /// <summary>
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        public virtual List<int> ConvertCodeblock(List<byte> list)
        {
            var cCodeBlock = new List<int>();
            for (var j = 0; j < list.Count; ++j) cCodeBlock.Add((int) Map(list[j]));
            return cCodeBlock;
        }
    }

    /// <summary>
    ///     Unshuffled instruction sets used for console editions.
    /// </summary>
    public class RdrConsoleOpcodeSet : OpcodeSet
    {
        /// <summary>
        ///     Index of RAGE_last
        /// </summary>
        public override int Count => 139;

        public override Instruction Map(byte v)
        {
            return v < Count ? (Instruction) v : Instruction.RageLast;
        }
    }

    /// <summary>
    /// </summary>
    public class RdrOpcodeSet : OpcodeSet
    {
        public static readonly Dictionary<Instruction, int> ShuffledInstructions = new Dictionary<Instruction, int>
        {
            {Instruction.RAGE_NOP, 77},
            {Instruction.RAGE_IADD, 105},
            {Instruction.RAGE_ISUB, 79},
            {Instruction.RAGE_IMUL, 12},
            {Instruction.RAGE_IDIV, 27},
            {Instruction.RAGE_IMOD, 124},
            {Instruction.RAGE_INOT, 89},
            {Instruction.RAGE_INEG, 120},
            {Instruction.RAGE_IEQ, 138},
            {Instruction.RAGE_INE, 2},
            {Instruction.RAGE_IGT, 101},
            {Instruction.RAGE_IGE, 133},
            {Instruction.RAGE_ILT, 11},
            {Instruction.RAGE_ILE, 53},
            {Instruction.RAGE_FADD, 115},
            {Instruction.RAGE_FSUB, 84},
            {Instruction.RAGE_FMUL, 51},
            {Instruction.RAGE_FDIV, 70},
            {Instruction.RAGE_FMOD, 135},
            {Instruction.RAGE_FNEG, 37},
            {Instruction.RAGE_FEQ, 50},
            {Instruction.RAGE_FNE, 57},
            {Instruction.RAGE_FGT, 131},
            {Instruction.RAGE_FGE, 130},
            {Instruction.RAGE_FLT, 90},
            {Instruction.RAGE_FLE, 68},
            {Instruction.RAGE_VADD, 88},
            {Instruction.RAGE_VSUB, 5},
            {Instruction.RAGE_VMUL, 60},
            {Instruction.RAGE_VDIV, 55},
            {Instruction.RAGE_VNEG, 121},
            {Instruction.RAGE_IAND, 44},
            {Instruction.RAGE_IOR, 36},
            {Instruction.RAGE_IXOR, 75},
            {Instruction.RageI2F, 35},
            {Instruction.RageF2I, 104},
            {Instruction.RageF2V, 18},
            {Instruction.RAGE_PUSH_CONST_U8, 128},
            {Instruction.RAGE_PUSH_CONST_U8_U8, 85},
            {Instruction.RAGE_PUSH_CONST_U8_U8_U8, 21},
            {Instruction.RAGE_PUSH_CONST_U32, 80},
            {Instruction.RAGE_PUSH_CONST_F, 103},
            {Instruction.RAGE_DUP, 16},
            {Instruction.RAGE_DROP, 92},
            {Instruction.RAGE_NATIVE, 95},
            {Instruction.RAGE_ENTER, 134},
            {Instruction.RAGE_LEAVE, 107},
            {Instruction.RAGE_LOAD, 10},
            {Instruction.RAGE_STORE, 33},
            {Instruction.RAGE_STORE_REV, 69},
            {Instruction.RAGE_LOAD_N, 137},
            {Instruction.RAGE_STORE_N, 67},
            {Instruction.RAGE_ARRAY_U8, 97},
            {Instruction.RAGE_ARRAY_U8_LOAD, 28},
            {Instruction.RAGE_ARRAY_U8_STORE, 71},
            {Instruction.RAGE_LOCAL_U8, 112},
            {Instruction.RAGE_LOCAL_U8_LOAD, 136},
            {Instruction.RAGE_LOCAL_U8_STORE, 127},
            {Instruction.RAGE_STATIC_U8, 119},
            {Instruction.RAGE_STATIC_U8_LOAD, 48},
            {Instruction.RAGE_STATIC_U8_STORE, 123},
            {Instruction.RAGE_IADD_U8, 9},
            {Instruction.RAGE_IMUL_U8, 106},
            {Instruction.RAGE_IOFFSET, 125},
            {Instruction.RAGE_IOFFSET_U8, 100},
            {Instruction.RAGE_IOFFSET_U8_LOAD, 110},
            {Instruction.RAGE_IOFFSET_U8_STORE, 3},
            {Instruction.RAGE_PUSH_CONST_S16, 26},
            {Instruction.RAGE_IADD_S16, 54},
            {Instruction.RAGE_IMUL_S16, 91},
            {Instruction.RAGE_IOFFSET_S16, 38},
            {Instruction.RAGE_IOFFSET_S16_LOAD, 6},
            {Instruction.RAGE_IOFFSET_S16_STORE, 93},
            {Instruction.RAGE_ARRAY_U16, 111},
            {Instruction.RAGE_ARRAY_U16_LOAD, 64},
            {Instruction.RAGE_ARRAY_U16_STORE, 25},
            {Instruction.RAGE_LOCAL_U16, 56},
            {Instruction.RAGE_LOCAL_U16_LOAD, 39},
            {Instruction.RAGE_LOCAL_U16_STORE, 4},
            {Instruction.RAGE_STATIC_U16, 114},
            {Instruction.RAGE_STATIC_U16_LOAD, 76},
            {Instruction.RAGE_STATIC_U16_STORE, 94},
            {Instruction.RAGE_GLOBAL_U16, 30},
            {Instruction.RAGE_GLOBAL_U16_LOAD, 126},
            {Instruction.RAGE_GLOBAL_U16_STORE, 23},
            {Instruction.RAGE_J, 96},
            {Instruction.RAGE_JZ, 66},
            {Instruction.RAGE_IEQ_JZ, 129},
            {Instruction.RAGE_INE_JZ, 31},
            {Instruction.RAGE_IGT_JZ, 1},
            {Instruction.RAGE_IGE_JZ, 99},
            {Instruction.RAGE_ILT_JZ, 29},
            {Instruction.RAGE_ILE_JZ, 118},
            {Instruction.RAGE_CALL, 34},
            {Instruction.RAGE_GLOBAL_U24, 86},
            {Instruction.RAGE_GLOBAL_U24_LOAD, 7},
            {Instruction.RAGE_GLOBAL_U24_STORE, 65},
            {Instruction.RAGE_PUSH_CONST_U24, 46},
            {Instruction.RAGE_SWITCH, 102},
            {Instruction.RAGE_STRING, 116},
            {Instruction.RAGE_STRINGHASH, 62},
            {Instruction.RAGE_TEXT_LABEL_ASSIGN_STRING, 78},
            {Instruction.RAGE_TEXT_LABEL_ASSIGN_INT, 32},
            {Instruction.RAGE_TEXT_LABEL_APPEND_STRING, 40},
            {Instruction.RAGE_TEXT_LABEL_APPEND_INT, 72},
            {Instruction.RAGE_TEXT_LABEL_COPY, 109},
            {Instruction.RAGE_CATCH, 117},
            {Instruction.RAGE_THROW, 47},
            {Instruction.RAGE_CALLINDIRECT, 22},
            {Instruction.RAGE_PUSH_CONST_M1, 24},
            {Instruction.RAGE_PUSH_CONST_0, 13},
            {Instruction.RAGE_PUSH_CONST_1, 98},
            {Instruction.RAGE_PUSH_CONST_2, 45},
            {Instruction.RAGE_PUSH_CONST_3, 0},
            {Instruction.RAGE_PUSH_CONST_4, 108},
            {Instruction.RAGE_PUSH_CONST_5, 83},
            {Instruction.RAGE_PUSH_CONST_6, 73},
            {Instruction.RAGE_PUSH_CONST_7, 15},
            {Instruction.RAGE_PUSH_CONST_FM1, 17},
            {Instruction.RAGE_PUSH_CONST_F0, 14},
            {Instruction.RAGE_PUSH_CONST_F1, 52},
            {Instruction.RAGE_PUSH_CONST_F2, 122},
            {Instruction.RAGE_PUSH_CONST_F3, 81},
            {Instruction.RAGE_PUSH_CONST_F4, 49},
            {Instruction.RAGE_PUSH_CONST_F5, 63},
            {Instruction.RAGE_PUSH_CONST_F6, 41},
            {Instruction.RAGE_PUSH_CONST_F7, 87},

            // Temporary Mapping
            {Instruction.RAGE_LOCAL_LOAD_S, 8},
            {Instruction.RAGE_LOCAL_STORE_S, 19},
            {Instruction.RAGE_LOCAL_STORE_SR, 20},
            {Instruction.RAGE_STATIC_LOAD_S, 42},
            {Instruction.RAGE_STATIC_STORE_S, 43},
            {Instruction.RAGE_STATIC_STORE_SR, 58},
            {Instruction.RAGE_LOAD_N_S, 59},
            {Instruction.RAGE_STORE_N_S, 61},
            {Instruction.RAGE_STORE_N_SR, 74},
            {Instruction.RAGE_GLOBAL_LOAD_S, 82},
            {Instruction.RAGE_GLOBAL_STORE_S, 113},
            {Instruction.RAGE_GLOBAL_STORE_SR, 132}
        };

        public static readonly Dictionary<int, Instruction> Remap =
            ShuffledInstructions.ToDictionary(i => i.Value, i => i.Key);

        public override int Count => 139;

        public override Instruction Map(byte v)
        {
            return v < Count ? Remap[v] : Instruction.RageLast;
        }
    }
}
