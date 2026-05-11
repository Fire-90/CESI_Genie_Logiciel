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

        public event Action<string> OnMessageReceived;

        public NetworkService(ConfigManager configManager, int port = 11000)
        {
            var settings = configManager.LoadSettings();
            _ipAddress = string.IsNullOrWhiteSpace(settings.ServerIP) ? "127.0.0.1" : settings.ServerIP;
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

                            // Lancement de l'écoute des messages dès que la connexion est établie
                            _ = ReceiveLoop(_client);
                        }
                    }
                    catch { }

                    await Task.Delay(5000);
                }
            });
        }

        private async Task ReceiveLoop(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                byte[] buffer = new byte[4096];
                string incompleteData = "";

                while (client.Connected)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0) break;

                    string receivedChunk = Encoding.UTF8.GetString(buffer, 0, read);
                    incompleteData += receivedChunk;

                    int newlineIndex;
                    while ((newlineIndex = incompleteData.IndexOf('\n')) >= 0)
                    {
                        string msg = incompleteData.Substring(0, newlineIndex).TrimEnd('\r');
                        incompleteData = incompleteData.Substring(newlineIndex + 1);
                        OnMessageReceived?.Invoke(msg);
                    }
                }
            }
            catch { }
        }

        public void SendMessage(string message)
        {
            if (_client != null && _client.Connected)
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                    _client.GetStream().Write(data, 0, data.Length);
                }
                catch { }
            }
        }
    }
}