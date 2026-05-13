using System.Text;

namespace CryptoSoft
{
    class CryptoSoft
    {
        static int Main(string[] args)
        {
            bool createdNew;

            // Concurrence
            using (Mutex mutex = new Mutex(true, "Global\\CryptoSoft_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    return -2;
                }

                if (args.Length < 2)
                {
                    return -1;
                }

                string sourceFile = args[0];
                string targetFile = args[1];

                string key = args.Length >= 3 ? args[2] : "EasySaveKey";
                if (string.IsNullOrWhiteSpace(key)) key = "EasySaveKey";

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
}