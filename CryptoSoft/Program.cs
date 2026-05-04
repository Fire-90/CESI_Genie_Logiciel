using System;
using System.IO;
using System.Text;

namespace CryptoSoft
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                return -1;
            }

            string sourceFile = args[0];
            string targetFile = args[1];

            string key = "EasySaveKey";

            try
            {

                string targetDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                byte[] fileBytes = File.ReadAllBytes(sourceFile);
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);

                for (int i = 0; i < fileBytes.Length; i++)
                {
                    fileBytes[i] = (byte)(fileBytes[i] ^ keyBytes[i % keyBytes.Length]);
                }

                File.WriteAllBytes(targetFile, fileBytes);

                System.Threading.Thread.Sleep(50);

                return 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}