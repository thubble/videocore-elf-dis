using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace videocoreelfdis
{
	public class DefProcessor_IV : IV_BASE
	{
		private Dictionary<string, int> _commonSymbolDefLocations = new Dictionary<string, int>();

		public DefProcessor_IV(
			Dictionary<ushort, SectionInfo> textAndDataSections,
			ushort sectionIndex,
			EntryInfo<SYMBOL_ENTRY>[] symEntries)
			: base(textAndDataSections, sectionIndex, symEntries)
		{
		}

		protected override void HandleInstruction(InstructionDefinition insn, byte[] bytes)
		{
			if (insn.DefProcessorCallbackContext == null)
				return;

			var boundInsn = insn.Bind(bytes, _curIndex, _section);
			boundInsn.InvokeCallback(this);
		}


		// CALLBACKS

		public void BRCHREL(BoundInstruction boundInsn, int targetAddrBase)
		{
			if (_section.RelEntries != null && _section.RelEntries.Any(r => r.r_offset == CurAbsoluteIndex))
			{
				var rEntry = _section.RelEntries.First(r => r.r_offset == CurAbsoluteIndex);
				var sEntry = _symEntries[rEntry.R_SYM];

				if (!string.IsNullOrEmpty(sEntry.name))
				{
					_section.RelocationSymbols[CurAbsoluteIndex] = sEntry.name;
					return;
				}
			}

			var actualTargetAddr = targetAddrBase + _section.SectionOffset;

			string s;
			if (_section.ObjectDefLocations.TryGetValue(actualTargetAddr, out s))
				_section.RelocationSymbols[CurAbsoluteIndex] = s;
			else if (_section.LabelDefLocations.TryGetValue(actualTargetAddr, out s))
				_section.RelocationSymbols[CurAbsoluteIndex] = s;
			else
			{
				// create label
				s = "L_" + actualTargetAddr.ToString("X").PadLeft(8, '0');
				_section.RelocationSymbols[CurAbsoluteIndex] = s;
				_section.LabelDefLocations[actualTargetAddr] = s;
			}
		}
	}
}
