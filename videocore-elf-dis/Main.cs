using System;
using System.IO;

namespace videocoreelfdis
{
	class MainClass
	{
		public static void Main (string[] args)
		{
			ProcessPath(args[0]);
		}

		private static void ProcessPath(string path)
		{
			var elfReader = new ELFReader<DefProcessor_IV, Disassembler_IV>(path);
			elfReader.Read();

			//Console.Write(elfReader.Text);

			string OUTPUT = @"C:\DIS.ASM";
			using (var file = new FileStream(OUTPUT, FileMode.Create, FileAccess.Write))
			{
				using (var writer = new StreamWriter(file))
				{
					writer.Write(elfReader.Text);
				}
			}
		}
	}
}
