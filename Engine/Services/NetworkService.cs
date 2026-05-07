using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace EasySave.Services
{
    public class NetworkService
    {
        private TcpClient _client;
        private readonly string _ipAddress;
        private readonly int _port;

        public NetworkService(string ipAddress = "10.144.128.51", int port = 11000)
        {
            _ipAddress = ipAddress;
            _port = port;
            StartConnectionLoop();
        }

        private void StartConnectionLoop()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        if (_client == null || !_client.Connected)
                        {
                            _client = new TcpClient();
                            await _client.ConnectAsync(_ipAddress, _port);
                            SendMessage("[CONNEXION] Client EasySave connecté.");
                        }
                    }
                    catch { }

                    await Task.Delay(5000);
                }
            });
        }

        public void SendMessage(string message)
        {
            if (_client != null && _client.Connected)
            {
                try
                {
                    // Ajout du \n pour assurer la bonne réception des logs volumineux
                    byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                    _client.GetStream().Write(data, 0, data.Length);
                }
                catch { }
            }
        }
    }
}