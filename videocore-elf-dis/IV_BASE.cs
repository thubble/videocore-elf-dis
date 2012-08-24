using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using NCalc;

namespace videocoreelfdis
{
	public interface IDisassemblerText
	{
		string ASM { get; }
	}

	public abstract class IV_BASE
	{
		protected static readonly InstructionDefinition[] _instructions;
		public static readonly Dictionary<char, string[]> _tables = new Dictionary<char, string[]>();

		static IV_BASE()
		{
			using (var file = new FileStream("videocoreiv.arch", FileMode.Open, FileAccess.Read))
			{
				using (var reader = new StreamReader(file))
				{
					var insnsList = new List<InstructionDefinition>();

					while (!reader.EndOfStream)
					{
						var line = reader.ReadLine().Trim();
						if (string.IsNullOrEmpty(line))
							continue;
						if (line.StartsWith("#"))
							continue;

						if (line.StartsWith("(define-table"))
						{
							var tableDef = line.Substring(14).Trim();
							char tableID = tableDef[0];
							tableDef = tableDef.Substring(tableDef.IndexOf('[') + 1);

							var list = new List<string>();
							foreach (var tableEntry in tableDef.Split(','))
							{
								var actualEntry = tableEntry
									.Trim()
									.Replace("\"", "")
									.Replace("[", "")
									.Replace("]", "")
									.Replace(")", "");
								list.Add(actualEntry);
							}

							_tables[tableID] = list.ToArray();
						}
						else if (line.StartsWith("("))
						{
							// not handled
						}
						else
						{
							int firstQuote = line.IndexOf('"');
							var byteFormat = line.Substring(0, firstQuote)
								.Trim()
								.Replace(" ", "");

							var action = line.Substring(firstQuote + 1);
							action = action.Substring(0, action.IndexOf('"'));

							string callback = null;

							firstQuote = line.IndexOf('"', firstQuote + 1); // skip first ending quote
							firstQuote = line.IndexOf('"', firstQuote + 1);
							if (firstQuote > 0)
							{
								callback = line.Substring(firstQuote + 1);
								callback = callback.Substring(0, callback.IndexOf('"'));
							}

							insnsList.Add(new InstructionDefinition(byteFormat, action, callback));
						}
					}

					_instructions = insnsList.ToArray();
				}
			}
		}


		public delegate string GetStringForInstruction(IV_BASE obj, UInt32 instruction);


		public bool NeedsEndianSwap { get; set; }


		protected int CurAbsoluteIndex
		{
			get
			{
				return _curIndex + _section.SectionOffset;
			}
		}


		protected Dictionary<ushort, SectionInfo> _textAndDataSections;
		protected ushort _sectionIndex;
		protected EntryInfo<SYMBOL_ENTRY>[] _symEntries;
		protected SectionInfo _section;

		protected int _curIndex;

		public IV_BASE(
			Dictionary<ushort, SectionInfo> textAndDataSections,
			ushort sectionIndex,
			EntryInfo<SYMBOL_ENTRY>[] symEntries)
		{
			_textAndDataSections = textAndDataSections;
			_sectionIndex = sectionIndex;
			_symEntries = symEntries;

			_section = _textAndDataSections[_sectionIndex];

			NeedsEndianSwap = false;
		}

		public void VisitInstructions()
		{
			byte[] machineCode = _section.Bytes;
			int length = machineCode.Length;

			int i = 0;

			// TODO - TEMP
			/*var defLoc = _section.ObjectDefLocations.FirstOrDefault(kvp => kvp.Value == "vc_intra4x4_clearcontext");
			i = defLoc.Key - _section.SectionOffset; // defLoc.Key;

			defLoc = _section.ObjectDefLocations.FirstOrDefault(kvp => kvp.Value == "vce_launch_h264_mbloop_download_h264_mbloop_invars_addr");
			length = defLoc.Key - _section.SectionOffset;*/

			while (i < length)
			{
				_curIndex = i;

				var possibleBytes = GetPossibleInstructionBytes(machineCode, i);
				byte[] insnBytes = null;

				var insn = _instructions.First(x => x.IsMatch(possibleBytes, out insnBytes));

				HandleInstruction(insn, insnBytes);

				i += insn.ByteLength;
			}
		}


		protected abstract void HandleInstruction(InstructionDefinition insn, byte[] bytes);


		protected virtual void BeforeProcessInstruction(int index, ref int curLoopIndex)
		{
		}

		protected virtual void AfterInstructionProcessed(int index, string insnText)
		{
		}


		private byte[][] GetPossibleInstructionBytes(byte[] a, int i)
		{

			var insns = new List<byte[]>
			{
				new byte[] {a[i+1], a[i+0]}
			};

			var maxLength = a.Length - i;

			if (maxLength >= 4)
				insns.Add(new byte[] {a[i+1], a[i+0], a[i+3], a[i+2]});
			if (maxLength >= 6)
				insns.Add(new byte[] {a[i+1], a[i+0], a[i+5], a[i+4], a[i+3], a[i+2]});
			if (maxLength >= 8)
				//insns.Add(new byte[] {a[i+1], a[i+0], a[i+7], a[i+6], a[i+5], a[i+4], a[i+3], a[i+2]});
				insns.Add(new byte[] {a[i+1], a[i+0], a[i+5], a[i+4], a[i+3], a[i+2], a[i+7], a[i+6]});
			if (maxLength >= 10)
				//insns.Add(new byte[] {a[i+1], a[i+0], a[i+9], a[i+8], a[i+7], a[i+6], a[i+5], a[i+4], a[i+3], a[i+2]});
				insns.Add(new byte[] {a[i+1], a[i+0], a[i+5], a[i+4], a[i+3], a[i+2], a[i+9], a[i+8], a[i+7], a[i+6]});

			return insns.ToArray();
		}
	}


	public class InstructionDefinition
	{
		public string ByteFormat { get; private set; }
		public int ByteLength { get; private set; }
		public string Action { get; private set; }
		public string Callback { get; private set; }

		public string PrintFormat { get; private set; }
		public object[] RequiredFormatParams { get; private set; }

		public string CallbackFunctionName { get; private set; }
		public object[] RequiredCallbackParams { get; private set; }

		private readonly byte[] _matchBytes;
		private readonly byte[] _matchMask;

		public InstructionDefinition(string byteFormat, string action, string callback)
		{
			this.ByteFormat = byteFormat;
			this.ByteLength = byteFormat.Length / 8;
			this.Action = action;
			this.Callback = callback;

			_matchBytes = new byte[this.ByteLength];
			_matchMask = new byte[this.ByteLength];

			for (int curByte = 0; curByte < this.ByteLength; curByte++)
			{
				byte curMatch = 0;
				byte curMask = 0;

				for (int bit = 0; bit < 8; bit++)
				{
					char curChar = byteFormat[(curByte * 8) + bit];

					curMatch = (byte)((curMatch << 1) | ((curChar == '1') ? 1 : 0));
					curMask = (byte)((curMask << 1) | ((curChar == '1' || curChar == '0') ? 1 : 0));
				}

				_matchBytes[curByte] = curMatch;
				_matchMask[curByte] = curMask;
			}

			InitializePrintFormat();
			InitializeCallback();
		}

		public bool IsMatch(byte[][] possibleBytes, out byte[] actualBytes)
		{
			actualBytes = null;
			var compareBytesIndex = (this.ByteLength / 2) - 1;
			if (possibleBytes.Length <= compareBytesIndex)
				return false;
			var insn = possibleBytes[compareBytesIndex];

			for (int curByte = 0; curByte < this.ByteLength; curByte++)
			{
				var insnByte = insn[curByte];
				if ((insnByte & _matchMask[curByte]) != _matchBytes[curByte])
					return false;
			}

			actualBytes = insn;
			return true;
		}

		private int ReadInteger(string action, ref int i)
		{
			var c = action[i];
			int ret = 0;
			while ((c >= '0' && c <= '9'))
			{
				ret = (ret * 10) + (c - '0');
				c = action[++i];
			}
			return ret;
		}

		private void InitializePrintFormat()
		{
			var sb = new StringBuilder();

			int i = 0;
			int? width = null;
			char? type = null;
			int curFormatIndex = 0;
			var formatParams = new List<object>();
			while (i < Action.Length)
			{
				var c = Action[i];
				if (c == '%')
				{
					/*
					flags = width = precision = length = type = null;
					  i++;
					  flags = is_printf_flag(inst.action[i]) ? inst.action[i++] : 0;
					  if (is_printf_digit(inst.action[i]))
						width = get_number();
					  if (is_printf_precision(inst.action[i])) {
						i++;
						precision = get_number();
					  }
					  length = is_printf_length(inst.action[i]) ? inst.action[i++] : 0;
					  type = inst.action[i];
					 */
					/*
						function is_printf_flag(c) { return (c=='+') || (c==' ') || (c=='#') || (c=='0'); }
						function is_printf_digit(c) { return (c>='0') && (c<='9'); }
						function is_printf_precision(c) { return (c=='.'); }
						function is_printf_length(c) { return (c=='h') || (c=='l') || (c=='L') || (c=='z') || (c=='j') || (c=='t'); }
					*/

					i++;
					c = Action[i];

					// flag - IGNORED
					if ((c == '+') || (c == ' ') || (c == '#') || (c == '0'))
						i++;
					c = Action[i];

					// width
					if ((c >= '0' && c <= '9'))
						width = ReadInteger(Action, ref i);
					c = Action[i];

					// precision - IGNORED
					if (c == '.')
						ReadInteger(Action, ref i);
					c = Action[i];

					// length - IGNORED
					if ((c == 'h') || (c == 'l') || (c == 'L') || (c == 'z') || (c == 'j') || (c == 't'))
						i++;
					c = Action[i];

					// type
					type = c;
				}
				else if (c == '@')
				{
					formatParams.Add('@');

					sb.Append("{");
					sb.Append(curFormatIndex++);
					sb.Append("}");
				}
				else if (c == '{')
				{
					var fmt = Action.Substring(i);
					int endBraceIndex = fmt.IndexOf('}');
					fmt = fmt.Substring(1, endBraceIndex - 1);
					i += endBraceIndex;

					fmt = fmt.Replace('$', 'A');

					if (fmt.Length == 1)
					{
						// single character
						formatParams.Add(fmt[0]);
					}
					else
					{
						// formula expression
						var expr = new Expression(fmt);
						formatParams.Add(expr);
					}

					sb.Append("{");
					sb.Append(curFormatIndex++);
					if (type != null && type != 's')
					{
						sb.Append(":");
						sb.Append(type == 'x' ? "X" : "D");
						if (width != null && width > 0)
							sb.Append(width);
					}
					sb.Append("}");

					width = type = null;
				}
				else
				{
					sb.Append(c);
				}

				i++;
			}

			PrintFormat = sb.ToString();
			RequiredFormatParams = formatParams.ToArray();
		}

		private void InitializeCallback()
		{
			if (string.IsNullOrEmpty(Callback))
				return;

			int beginParams = Callback.IndexOf('(');
			CallbackFunctionName = Callback.Substring(0, beginParams).Trim();

			var parmList = new List<object>();

			var parmsString = Callback.Substring(beginParams + 1);
			parmsString = parmsString.Substring(0, parmsString.LastIndexOf(')'));
			foreach (var sParm in parmsString.Split(','))
			{
				var s = sParm.Trim();
				var openBrace = s.IndexOf('{');
				if (openBrace < 0)
				{
					parmList.Add(s);
					continue;
				}

				var fmt = s.Substring(1, s.IndexOf('}') - 1);
				fmt = fmt.Replace('$', 'A');
				if (fmt.Length == 1)
				{
					// single character
					parmList.Add(fmt[0]);
				}
				else
				{
					// formula expression
					var expr = new Expression(fmt);
					parmList.Add(expr);
				}
			}

			RequiredCallbackParams = parmList.ToArray();
		}

		public MethodInfo GetCallbackMethod(object target)
		{
			if (_callbackMethod != null)
				return _callbackMethod;
			if (string.IsNullOrEmpty(CallbackFunctionName))
				return null;
			_callbackMethod = target.GetType().GetMethod(CallbackFunctionName);
			return _callbackMethod;
		}
		private MethodInfo _callbackMethod = null;

		public BoundInstruction Bind(byte[] bytes, int curOffset, SectionInfo section)
		{
			return new BoundInstruction(this, bytes, curOffset, section);
		}
	}


	public class BoundInstruction
	{
		public InstructionDefinition Instruction { get; private set; }

		private readonly Dictionary<char, int> _boundValues = new Dictionary<char, int>();
		private readonly Dictionary<char, int> _boundLengths = new Dictionary<char, int>();

		private readonly SectionInfo _section;
		private readonly int _curAbsoluteOffset;

		private readonly Expression _pcRelBranchExpr;

		public BoundInstruction(InstructionDefinition insn, byte[] bytes, int curOffset, SectionInfo section)
		{
			_section = section;
			_curAbsoluteOffset = curOffset + _section.SectionOffset;
			this.Instruction = insn;
			InitializeBoundData(bytes);
			_boundValues['A'] = curOffset;
			_pcRelBranchExpr = new Expression("A+o*2");
		}

		public string GetText()
		{
			if (string.IsNullOrEmpty(Instruction.PrintFormat))
				return string.Empty;

			var evaluatedParams = new object[Instruction.RequiredFormatParams.Length];
			int i = -1;
			foreach (object p in Instruction.RequiredFormatParams)
			{
				i++;

				var expr = p as Expression;
				if (expr != null)
				{
					evaluatedParams[i] = EvaluateExpression(expr);
					continue;
				}

				if (p is char)
				{
					var boundIndex = (char)p;

					if (boundIndex == '@')
					{
						// handle relocation
						string relocSym;
						if (!_section.RelocationSymbols.TryGetValue(_curAbsoluteOffset, out relocSym))
							relocSym = string.Format("0x{0:X8}", EvaluateExpression(_pcRelBranchExpr));
						evaluatedParams[i] = relocSym;

						continue;
					}

					int boundValue;
					if (_boundValues.TryGetValue(boundIndex, out boundValue))
					{
						string[] table;
						if (IV_BASE._tables.TryGetValue(boundIndex, out table))
							evaluatedParams[i] = table[boundValue];
						else
							evaluatedParams[i] = boundValue;

						continue;
					}

					throw new Exception("INVALID CHARACTER");
				}

				throw new Exception("INVALID PRINT PARAMETER");
			}

			return string.Format(Instruction.PrintFormat, evaluatedParams);
		}

		private object EvaluateExpression(Expression expr)
		{
			foreach (var bk in _boundValues.Keys)
			{
				if (IV_BASE._tables.ContainsKey(bk))
					continue;
				expr.Parameters[bk.ToString()] = _boundValues[bk];
			}
			return expr.Evaluate();
		}

		public void InvokeCallback(object target)
		{
			var funcRef = Instruction.GetCallbackMethod(target);
			if (funcRef == null)
				return;

			var evaluatedParams = new object[Instruction.RequiredCallbackParams.Length + 1];
			evaluatedParams[0] = this;
			int i = 0;
			foreach (object p in Instruction.RequiredCallbackParams)
			{
				i++;

				var expr = p as Expression;
				if (expr != null)
				{
					evaluatedParams[i] = EvaluateExpression(expr);
					continue;
				}

				if (p is char)
				{
					var boundIndex = (char)p;

					int boundValue;
					if (_boundValues.TryGetValue(boundIndex, out boundValue))
					{
						string[] table;
						if (IV_BASE._tables.TryGetValue(boundIndex, out table))
							evaluatedParams[i] = table[boundValue];
						else
							evaluatedParams[i] = boundValue;

						continue;
					}

					throw new Exception("INVALID CHARACTER");
				}

				throw new Exception("INVALID PRINT PARAMETER");
			}

			funcRef.Invoke(target, evaluatedParams);
		}

		private void InitializeBoundData(byte[] bytes)
		{
			for (int i = 0; i < Instruction.ByteLength * 8; i++)
			{
				char boundIndex = Instruction.ByteFormat[i];
				if (boundIndex == '0' || boundIndex == '1')
					continue;

				byte currentBit = (byte)((bytes[i / 8] >> (7 - (i % 8))) & 0x1);

				if (!_boundValues.ContainsKey(boundIndex))
				{
					// handle sign extension
					_boundValues[boundIndex] = ((boundIndex == 'o' || boundIndex == 'i') && currentBit == 0x1) ? -1 : 0;
					_boundLengths[boundIndex] = 0;
				}

				_boundValues[boundIndex] = (_boundValues[boundIndex] << 1) | currentBit;
				_boundLengths[boundIndex]++;
			}
		}
	}
}
