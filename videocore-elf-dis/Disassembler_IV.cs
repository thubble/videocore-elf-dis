using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace videocoreelfdis
{
	public class Disassembler_IV : IV_BASE, IDisassemblerText
	{
		public string ASM { get; private set; }


		public Disassembler_IV(
			Dictionary<ushort, SectionInfo> textAndDataSections,
			ushort sectionIndex,
			EntryInfo<SYMBOL_ENTRY>[] symEntries)
			: base(textAndDataSections, sectionIndex, symEntries)
		{
		}

		protected override void HandleInstruction(InstructionDefinition insn, byte[] bytes)
		{
			string s;

			if (_section.ObjectDefLocations.TryGetValue(CurAbsoluteIndex, out s))
				ASM += "\r\n" + s + "\r\n";
			if (_section.LabelDefLocations.TryGetValue(CurAbsoluteIndex, out s))
				ASM += s + ":\r\n";

			var boundInsn = insn.Bind(bytes, _curIndex, _section);
			//ASM += _curIndex.ToString("0000") + " ";
			ASM += "\t";
			ASM += boundInsn.GetText(this);
			//ASM += "    ; " + GetBytecode(bytes);
			ASM += "\r\n";
			ASM += GetBinary(bytes);
			ASM += "\r\n";
		}

		private string GetBytecode (byte[] bytes)
		{
			ushort curByte = 0;
			var sb = new StringBuilder();

			for (int i = 0; i < bytes.Length; i++)
			{
				if ((i % 2) == 0)
				{
					curByte = (ushort)(bytes[i] << 8);
				}
				else
				{
					curByte = (ushort)(curByte | bytes[i]);
					sb.Append("0x");
					sb.Append(curByte.ToString("X4"));
					sb.Append(" ");
					curByte = 0;
				}
			}

			return sb.ToString();
		}

		private string GetBinary (byte[] bytes)
		{
			var sb = new StringBuilder();

			for (int i = 0; i < bytes.Length; i++)
			{
				var s = Convert.ToString(bytes[i], 2).PadLeft(8, '0');
				sb.Append(s.Substring(0, 4));
				sb.Append(" ");
				sb.Append(s.Substring(4));
				sb.Append(" ");
			}

			return sb.ToString();
		}


		// CALLBACKS

		public string VECTARG48 (BoundInstruction boundInsn, int vectorArg, int scalar)
		{
			if (GetBits(vectorArg, 1, 3) == 0x0007)
				return "r" + scalar;

			var sb = new StringBuilder();

			string argSize = ARG_BIT_SIZES[GetBits(vectorArg, 1, 2)];

			sb.Append(IsFlagSet(vectorArg, ARG_VERTICAL_FLAG) ? "V" : "H");
			sb.Append(argSize);
			sb.Append("(");
			sb.Append(GetBits(vectorArg, 5, 6));
			sb.Append(", ");
			if (argSize == "")
				sb.Append((int)(GetBits(vectorArg, 2, 2) << 4));
			else if (argSize == "X")
				sb.Append((int)(GetBits(vectorArg, 3, 1) << 5));
			else if (argSize == "Y")
				sb.Append("0");
			sb.Append(")");

			return sb.ToString();
		}

		public string VECTARG80 (BoundInstruction boundInsn, int vectorArg, int flags)
		{
			if (GetBits(vectorArg, 1, 3) == 0x0007)
				return "r" + GetExtraBits(flags, 2, 3);

			var sb = new StringBuilder();

			string argSize = ARG_BIT_SIZES[GetBits(vectorArg, 1, 2)];
			bool vert = IsFlagSet(vectorArg, ARG_VERTICAL_FLAG);
			bool increment = IsExtraFlagSet(flags, EARG_INCREMENT_FLAG);
			
			sb.Append(vert ? "V" : "H");
			sb.Append(argSize);
			sb.Append("(");
			sb.Append(GetBits(vectorArg, 5, 6));
			if (increment && !vert)
				sb.Append("++");
			sb.Append(", ");
			if (argSize == "")
				sb.Append((int)(GetBits(vectorArg, 2, 2) << 4));
			else if (argSize == "X")
				sb.Append((int)(GetBits(vectorArg, 3, 1) << 5));
			else if (argSize == "Y")
				sb.Append("0");
			if (increment && vert)
				sb.Append("++");
			sb.Append(")");

			return sb.ToString();
		}
		
		public string REPNUM(BoundInstruction boundInsn, int rep)
		{
			if (rep > 0 && rep < 7)
				return " REP " + (0x1 << rep);
			return "";
		}

		private bool IsFlagSet(int value, int bit)
		{
			bit = 10 - bit;
			return (((value >> bit) & 0x1) == 0x1);
		}
		
		private bool IsExtraFlagSet(int value, int bit)
		{
			bit = 6 - bit;
			return (((value >> bit) & 0x1) == 0x1);
		}

		private int GetBits(int value, int startBit, int length)
		{
			return ((value >> (10 - (length + startBit - 1))) & (0x03FF >> (10 - length)));
		}
		
		private int GetExtraBits(int value, int startBit, int length)
		{
			return ((value >> (6 - (length + startBit - 1))) & (0x03FF >> (6 - length)));
		}


		private static string[] ARG_BIT_SIZES = new string[]
		{
			"", "", "X", "Y"
		};

		// bit flags for vector args
		private const int ARG_VERTICAL_FLAG = 4;
		
		// bit flags for vector args (extra flags for 80-bit)
		private const int EARG_INCREMENT_FLAG = 5;
	}
}
