using System;

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
			Console.Write(elfReader.Text);
		}
	}
}
