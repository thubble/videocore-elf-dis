using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace videocoreelfdis
{
	public class ELFReader<TDefProcessor, TDisProcessor>
		where TDefProcessor : IV_BASE
		where TDisProcessor : IV_BASE, IDisassemblerText
	{
		//private const string SYM_TAB_NAME = ".symtab";
		private const string SYM_TAB_NAME = ".dynsym";

		private string _filePath;

		private byte[] _bytes;

		private ELF_HEADER _elfHeader;
		private bool _needsSwapEndian;
		private PROGRAM_HEADER[] _programHeaders;
		private SectionHeaderInfo[] _sectionHeaders;
		Dictionary<int, string> _stringTable;
		EntryInfo<SYMBOL_ENTRY>[] _symEntries;
		private Dictionary<ushort, SectionInfo> _textAndDataSections;
		private DYN_ENTRY[] _dynEntries;

		public string Text { get; private set; }

		private TDefProcessor GetDefProcessor(
			Dictionary<ushort, SectionInfo> textAndDataSections,
			ushort index,
			EntryInfo<SYMBOL_ENTRY>[] synEntries)
		{
			return (TDefProcessor)
				Activator.CreateInstance(typeof(TDefProcessor), textAndDataSections, index, _symEntries);
		}

		private TDisProcessor GetDisProcessor(
			Dictionary<ushort, SectionInfo> textAndDataSections,
			ushort index,
			EntryInfo<SYMBOL_ENTRY>[] synEntries)
		{
			return (TDisProcessor)
				Activator.CreateInstance(typeof(TDisProcessor), textAndDataSections, index, _symEntries);
		}

		public ELFReader(string filePath)
		{
			_filePath = filePath;
		}
		
		private void ProcessNonELFRawBytecode()
		{
			_symEntries = new EntryInfo<SYMBOL_ENTRY>[0];
			
			var mainSI = new SectionInfo
			{
				SectionOffset = 0,
				SectionType = SectionType.Text,
				Bytes = new byte[_bytes.Length - 512],
			};
			//_bytes.CopyTo(mainSI.Bytes, 0);
			Buffer.BlockCopy
				(_bytes, 512, mainSI.Bytes, 0, mainSI.Bytes.Length);
			
			_textAndDataSections = new Dictionary<ushort, SectionInfo>(1);
			_textAndDataSections.Add(1, mainSI);
		}

		public void Read()
		{
			using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
			{
				_bytes = new byte[(int)fs.Length];
				fs.Read(_bytes, 0, (int)fs.Length);
			}
			
			if (_filePath.EndsWith("loader.bin") || _filePath.EndsWith("bootcode.bin") || _filePath.EndsWith("bootcode_new.bin"))
			{
				ProcessNonELFRawBytecode();
			}
			else
			{
				// do processing
				ReadHeaders();
				ReadOptionsSection();
				ReadSymbolTable();
				ReadTextAndDataSections();
			}

			ProcessTextAndDataRelocations();

			foreach (var kvp in _textAndDataSections)
			{
				var index = kvp.Key;
				var section = kvp.Value;

				if (section.SectionType == SectionType.Text)
				{
					var visitorDisassembler = GetDisProcessor(_textAndDataSections, index, _symEntries);
					visitorDisassembler.VisitInstructions();
					this.Text += section.Name + ":\r\n" + visitorDisassembler.ASM + "\r\n\r\n\r\n";
				}
				else if (section.SectionType == SectionType.Data)
				{
					/*
					var dataDisassembler = new DataSectionDisassembler(section);
					dataDisassembler.ProcessData();
					this.Text += section.Name + ":\r\n" + dataDisassembler.DataText + "\r\n\r\n\r\n";
					*/
				}
			}

			
		}

		private T GetStructureFromBytes<T>(int offset) where T : struct
		{
			return GetStructureFromBytes<T>(offset, _needsSwapEndian);
		}

		private T GetStructureFromBytes<T>(int offset, bool swapEndian) where T : struct
		{
			int size = typeof(T).StructLayoutAttribute.Size;
			IntPtr objPtr = Marshal.AllocHGlobal(size);
			Marshal.Copy(_bytes, offset, objPtr, size);
			T retValue = (T)Marshal.PtrToStructure(objPtr, typeof(T));
			Marshal.FreeHGlobal(objPtr);

			if (swapEndian)
				SwapEndianBytesInStructure(ref retValue);

			return retValue;
		}

		private void SwapEndianBytesInStructure<T>(ref T value) where T : struct
		{
			object vObj = (object)value;

			foreach (var fieldInfo in typeof(T).GetFields())
			{
				//if (fieldInfo.FieldType == typeof(UInt64))
				//    fieldInfo.SetValue(value, System.Net.IPAddress.NetworkToHostOrder((UInt64)fieldInfo.GetValue(value)));
				if (fieldInfo.FieldType == typeof(Int32))
					fieldInfo.SetValue(vObj, System.Net.IPAddress.NetworkToHostOrder((Int32)fieldInfo.GetValue(vObj)));
				else if (fieldInfo.FieldType == typeof(Int16))
					fieldInfo.SetValue(vObj, System.Net.IPAddress.NetworkToHostOrder((Int16)fieldInfo.GetValue(vObj)));
			}

			value = (T)vObj;
		}

		private void ReadHeaders()
		{
			// read main ELF header
			// special case - header determines whether we swap endian.
			_elfHeader = GetStructureFromBytes<ELF_HEADER>(0, false);
			_needsSwapEndian = (_elfHeader.EI_DATA != EI_DATA.ELFDATA2LSB);
			if (_needsSwapEndian)
				SwapEndianBytesInStructure(ref _elfHeader);

			// read each program header
			if (_elfHeader.e_phnum > 0)
			{
				_programHeaders = new PROGRAM_HEADER[_elfHeader.e_phnum];
				for (int i = 0; i < _programHeaders.Length; i++)
				{
					int offset = _elfHeader.e_phoff + (i * StructureInfo.PROGRAM_HEADER_SIZE);
					_programHeaders[i] = GetStructureFromBytes<PROGRAM_HEADER>(offset);
				}

				if (_programHeaders.Any(h => h.PT_TYPE == PT_TYPE.PT_DYNAMIC))
				{
					var dynHeader = _programHeaders.First(h => h.PT_TYPE == PT_TYPE.PT_DYNAMIC);
					_dynEntries = new DYN_ENTRY[dynHeader.p_filesz / StructureInfo.DYN_ENTRY_SIZE];
					for (int i = 0; i < _dynEntries.Length; i++)
					{
						int offset = dynHeader.p_offset + (i * StructureInfo.DYN_ENTRY_SIZE);
						_dynEntries[i] = GetStructureFromBytes<DYN_ENTRY>(offset);
					}
				}
			}

			// read each section header
			_sectionHeaders = new SectionHeaderInfo[_elfHeader.e_shnum];
			for (int i = 0; i < _sectionHeaders.Length; i++)
			{
				int offset = _elfHeader.e_shoff + (i * StructureInfo.SECTION_HEADER_SIZE);
				_sectionHeaders[i] = new SectionHeaderInfo();
				_sectionHeaders[i].SECTION_HEADER = GetStructureFromBytes<SECTION_HEADER>(offset);
			}

			// get the string table, indexed by offset.
			SECTION_HEADER stringTableHeader = _sectionHeaders[_elfHeader.e_shstrndx].SECTION_HEADER;
			_stringTable = GetStringTable(stringTableHeader.sh_offset, stringTableHeader.sh_size);

			// populate names of headers
			foreach (var curHeader in _sectionHeaders)
			{
				curHeader.SectionName = GetStringFromTableIndex(_stringTable, curHeader.SECTION_HEADER.sh_name);
			}
		}

		private void ReadSymbolTable()
		{
			// read symbol table
			var symtSectionHeader = _sectionHeaders.FirstOrDefault(s => s.SectionName == SYM_TAB_NAME);

			if (symtSectionHeader != null)
			{
				SECTION_HEADER symNameTableHeader = _sectionHeaders[symtSectionHeader.SECTION_HEADER.sh_link].SECTION_HEADER;
				Dictionary<int, string> symNameTable = GetStringTable(symNameTableHeader.sh_offset, symNameTableHeader.sh_size);

				int entryCount = symtSectionHeader.SECTION_HEADER.sh_size / StructureInfo.SYMBOL_ENTRY_SIZE;
				_symEntries = new EntryInfo<SYMBOL_ENTRY>[entryCount];
				for (int i = 0; i < entryCount; i++)
				{
					int offset = symtSectionHeader.SECTION_HEADER.sh_offset + (i * StructureInfo.SYMBOL_ENTRY_SIZE);

					_symEntries[i] = new EntryInfo<SYMBOL_ENTRY>();
					_symEntries[i].entry = GetStructureFromBytes<SYMBOL_ENTRY>(offset);

					// NOTE: ???
					if ((ushort)_symEntries[i].entry.st_shndx == 0xfff2)
					{
						// Common section - value holds alignment constraint, so irrelevant
						_symEntries[i].entry.st_value = 0;
					}

					_symEntries[i].name = GetStringFromTableIndex(symNameTable, _symEntries[i].entry.st_name);
				}
			}
		}

		private void ReadOptionsSection()
		{
			var optionsSectionHeader = _sectionHeaders.FirstOrDefault(s => s.SectionName == ".options");
			if (optionsSectionHeader == null)
				return;

			var firstHeader = GetStructureFromBytes<OPTIONS_HEADER>(optionsSectionHeader.SECTION_HEADER.sh_offset);
		}

		private void ReadTextAndDataSections()
		{
			_textAndDataSections = new Dictionary<ushort, SectionInfo>();
			ReadByteSections(_textSections, SectionType.Text);
			ReadByteSections(_dataSections, SectionType.Data);

			// create common section (used for relocation only)
			var sec = new SectionInfo();
			sec.Name = "COMMON";
			sec.Bytes = new byte[0];
			sec.SectionType = SectionType.Data;
			
			ushort sectionIndex = 0xfff2;
			_textAndDataSections[sectionIndex] = sec;
		}

		private void ReadByteSections(string[] sectionNames, SectionType type)
		{
			foreach (string name in sectionNames)
			{
				if (name == ".bss")
					continue;

				var sectionHeader = _sectionHeaders.FirstOrDefault(s => s.SectionName == name);
				if (sectionHeader == null)
					continue;

				ushort sectionIndex = (ushort)Array.IndexOf(_sectionHeaders, sectionHeader);

				string relaName = ".rela" + name;
				var relaSectionHeader = _sectionHeaders.FirstOrDefault(s => s.SectionName == relaName);

				var sec = new SectionInfo();
				sec.Name = name;
				sec.Bytes = new byte[sectionHeader.SECTION_HEADER.sh_size];
				Buffer.BlockCopy
					(_bytes, sectionHeader.SECTION_HEADER.sh_offset, sec.Bytes, 0, sectionHeader.SECTION_HEADER.sh_size);

				sec.SectionType = type;

				sec.SectionOffset = sectionHeader.SECTION_HEADER.sh_addr;

				if (relaSectionHeader != null)
				{
					int entryCount = relaSectionHeader.SECTION_HEADER.sh_size / StructureInfo.ELF_RELA_SIZE;
					sec.RelEntries = new ELF_RELA[entryCount];
					for (int i = 0; i < entryCount; i++)
					{
						int offset = relaSectionHeader.SECTION_HEADER.sh_offset + (i * StructureInfo.ELF_RELA_SIZE);
						sec.RelEntries[i] = GetStructureFromBytes<ELF_RELA>(offset);
					}
				}

				if (_symEntries != null)
				{
					foreach (var sEntry in _symEntries.Where(s => s.entry.st_shndx == sectionIndex))
					{
						int location = sEntry.entry.st_value;
						sec.ObjectDefLocations[location] = sEntry.name;
					}
				}

				_textAndDataSections[sectionIndex] = sec;
			}
		}

		private static readonly string[] _textSections = new string[]
		{
			".text"
		};

		/*private static readonly string[] _textSections = new string[]
		{
			".init", ".fini", ".crypto", ".text"
		};*/

		/*private static readonly string[] _textSections = new string[]
		{
			".init", ".fini", ".crypto"
		};*/

		private static readonly string[] _dataSections = new string[]
		{
			".data", ".rodata", ".bss", ".got"
		};

		private void ProcessTextAndDataRelocations()
		{
			//if (_symEntries == null)
			//	return;

			foreach (var kvp in _textAndDataSections)
			{
				var index = kvp.Key;
				var section = kvp.Value;

				if (section.SectionType == SectionType.Text)
				{
					GetDefProcessor(_textAndDataSections, index, _symEntries).VisitInstructions();
				}
			}
		}

		private Dictionary<int, string> GetStringTable(int startPos, int size)
		{
			Dictionary<int, string> stringTable = new Dictionary<int, string>();

			int startIndex = 1;
			int curIndex = startPos;
			List<byte> lst = new List<byte>();
			while (true)
			{
				if (curIndex >= (startPos + size) || _bytes[curIndex] == 0)
				{
					stringTable[startIndex] = System.Text.Encoding.ASCII.GetString(lst.ToArray());
					if (curIndex >= (startPos + size - 1))
						break;
					lst = new List<byte>();
					startIndex = curIndex - startPos + 1;
				}
				else
				{
					lst.Add(_bytes[curIndex]);
				}
				curIndex++;
			}

			return stringTable;
		}

		private string GetStringFromTableIndex(Dictionary<int, string> table, int index)
		{
			if (index <= 0)
				return "";

			string s;

			if (!table.TryGetValue(index, out s))
			{
				var stringEntry =
					table.OrderByDescending(kvp => kvp.Key).First(kvp => kvp.Key < index);

				s = stringEntry.Value.Substring(index - stringEntry.Key);
			}

			return s;
		}
	}


	public class SectionInfo
	{
		public string Name { get; set; }
		public byte[] Bytes { get; set; }
		public int SectionOffset { get; set; }

		public ELF_RELA[] RelEntries { get; set; }

		private Dictionary<int, string> _objectDefLocations = new Dictionary<int, string>();
		public Dictionary<int, string> ObjectDefLocations { get { return _objectDefLocations; } }

		private Dictionary<int, string> _labelDefLocations = new Dictionary<int, string>();
		public Dictionary<int, string> LabelDefLocations { get { return _labelDefLocations; } }

		private Dictionary<int, string> _relocationSymbols = new Dictionary<int, string>();
		public Dictionary<int, string> RelocationSymbols { get { return _relocationSymbols; } }

		public SectionType SectionType { get; set; }
	}

	public enum SectionType
	{
		Text,
		Data
	}


	public static class StructureInfo
	{
		public const int N_EIDENT = 16;
		public const int ELF_HEADER_SIZE = 0x34;
		public const int SECTION_HEADER_SIZE = 0x28;
		public const int ELF_REL_SIZE = 8;
		public const int ELF_RELA_SIZE = 12;
		public const int SYMBOL_ENTRY_SIZE = 16;
		public const int OPTIONS_HEADER_SIZE = 8;
		public const int PROGRAM_HEADER_SIZE = 32;
		public const int DYN_ENTRY_SIZE = 8;
	}



	public enum REL_TYPE : byte
	{
		// I HAVE NO IDEA WHAT THESE ACTUALLY ARE.
		// JUST INFERRING FROM WHAT I'VE SEEN.
		R_VC_NONE = 0,
		R_VC_BRCH32 = 7, /* branch; only seen this in 32-bit "bl" (so far) */
	}

	public enum EI_CLASS : byte
	{
		ELFCLASSNONE = 0,
		ELFCLASS32 = 1,
		ELFCLASS64 = 2
	}

	public enum EI_DATA : byte
	{
		ELFDATANONE = 0,
		ELFDATA2LSB = 1,
		ELFDATA2MSB = 2
	}

	[StructLayout(LayoutKind.Sequential, Size = StructureInfo.ELF_HEADER_SIZE)]
	public struct ELF_HEADER
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = StructureInfo.N_EIDENT)]
		public byte[] e_ident;

		public Int16 e_type;
		public Int16 e_machine;
		public Int32 e_version;
		public Int32 e_entry;
		public Int32 e_phoff;
		public Int32 e_shoff;
		public Int32 e_flags;
		public Int16 e_ehsize;
		public Int16 e_phentsize;
		public Int16 e_phnum;
		public Int16 e_shentsize;
		public Int16 e_shnum;
		public Int16 e_shstrndx;

		public EI_CLASS EI_CLASS { get { return (EI_CLASS)e_ident[4]; } }
		public EI_DATA EI_DATA { get { return (EI_DATA)e_ident[5]; } }
	}


	public enum PT_TYPE : int
	{
		PT_NULL = 0,
		PT_LOAD = 1,
		PT_DYNAMIC = 2,
		PT_INTERP = 3,
		PT_NOTE = 4,
		PT_SHLIB = 5,
		PT_PHDR = 6,
	}

	[StructLayout(LayoutKind.Sequential, Size = StructureInfo.PROGRAM_HEADER_SIZE)]
	public struct PROGRAM_HEADER
	{
		public Int32 p_type;
		public Int32 p_offset;
		public Int32 p_vaddr;
		public Int32 p_paddr;
		public Int32 p_filesz;
		public Int32 p_memsz;
		public Int32 p_flags;
		public Int32 p_align;

		public PT_TYPE PT_TYPE { get { return (PT_TYPE)p_type; } }
	}

	[StructLayout(LayoutKind.Sequential, Size = StructureInfo.DYN_ENTRY_SIZE)]
	public struct DYN_ENTRY
	{
		public Int32 d_tag;
		public Int32 d_val;
	}


	[StructLayout(LayoutKind.Sequential, Size = StructureInfo.SECTION_HEADER_SIZE)]
	public struct SECTION_HEADER
	{
		public Int32 sh_name;
		public Int32 sh_type;
		public Int32 sh_flags;
		public Int32 sh_addr;
		public Int32 sh_offset;
		public Int32 sh_size;
		public Int32 sh_link;
		public Int32 sh_info;
		public Int32 sh_addralign;
		public Int32 sh_entsize;
	}

	public class SectionHeaderInfo
	{
		public SECTION_HEADER SECTION_HEADER;
		public string SectionName;
	}



	[StructLayout(LayoutKind.Sequential, Size = StructureInfo.ELF_REL_SIZE)]
	public struct ELF_REL
	{
		public Int32 r_offset;
		public Int32 r_info;

		public Int32 R_SYM
		{
			get
			{
				return r_info >> 8;
			}
		}

		public REL_TYPE R_TYPE
		{
			get
			{
				return (REL_TYPE)(byte)(r_info & 0x000000FF);
			}
		}
	}



	[StructLayout(LayoutKind.Sequential, Size = StructureInfo.ELF_RELA_SIZE)]
	public struct ELF_RELA
	{
		public Int32 r_offset;
		public Int32 r_info;
		public Int32 r_addend;

		public Int32 R_SYM
		{
			get
			{
				return r_info >> 8;
			}
		}

		public REL_TYPE R_TYPE
		{
			get
			{
				return (REL_TYPE)(byte)(r_info & 0x000000FF);
			}
		}
	}



	[StructLayout(LayoutKind.Sequential, Size = StructureInfo.SYMBOL_ENTRY_SIZE)]
	public struct SYMBOL_ENTRY
	{
		public Int32 st_name;
		public Int32 st_value;
		public Int32 st_size;
		public byte st_info;
		public byte st_other;
		public Int16 st_shndx;

		public ST_BIND ST_BIND
		{
			get
			{
				return (ST_BIND)(byte)(st_info >> 4);
			}
		}

		public ST_TYPE ST_TYPE
		{
			get
			{
				return (ST_TYPE)(byte)(st_info & 0x0F);
			}
		}
	}

	public enum ST_BIND : byte
	{
		STB_LOCAL = 0,
		STB_GLOBAL = 1,
		STB_WEAK = 2,
		STB_LOPROC = 13,
		STB_HIPROC = 15
	}

	public enum ST_TYPE : byte
	{
		STT_NOTYPE = 0,
		STT_OBJECT = 1,
		STT_FUNC = 2,
		STT_SECTION = 3,
		STT_FILE = 4,
		STT_LOPROC = 13,
		STT_HIPROC = 15
	}

	public class EntryInfo<T>
	{
		public string name;
		public T entry;
	}

	[StructLayout(LayoutKind.Sequential, Size = StructureInfo.OPTIONS_HEADER_SIZE)]
	public struct OPTIONS_HEADER
	{
		public byte oh_kind;
		public byte oh_size;
		public Int16 oh_shndx;
		public Int32 oh_info;
	}
}
