using System;
using System.Collections.Generic;
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
			ASM += boundInsn.GetText();
			ASM += "    ; " + GetBytecode(bytes);
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
	}
}
