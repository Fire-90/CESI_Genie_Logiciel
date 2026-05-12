using System;
using System.Net.Sockets;
using System.Text;
using EasySave.Models;
using System.Threading.Tasks;

namespace EasySave.Services
{
    public class NetworkService
    {
        private TcpClient _client;
        private readonly ConfigManager _configManager;
        private readonly int _port;
        private readonly object _sendLock = new object();

        public event Action<string> OnMessageReceived;
        public event Action<ConnectionStatus> OnConnectionStatusChanged;

        public NetworkService(ConfigManager configManager, int port = 11000)
        {
            _configManager = configManager;
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
                            OnConnectionStatusChanged?.Invoke(ConnectionStatus.Connecting);
                            var settings = _configManager.LoadSettings();
                            string ipAddress = string.IsNullOrWhiteSpace(settings.ServerIP) ? "127.0.0.1" : settings.ServerIP;

                            _client = new TcpClient();
                            _client.NoDelay = true;

                            await _client.ConnectAsync(ipAddress, _port);

                            if (_client.Connected)
                            {
                                OnConnectionStatusChanged?.Invoke(ConnectionStatus.Connected);

                                SendMessage($"[ID] {settings.ClientName}");

                                _ = ListenToServer(_client);
                            }
                        }
                    }
                    catch
                    {
                        OnConnectionStatusChanged?.Invoke(ConnectionStatus.Disconnected);
                    }
                    await Task.Delay(5000);
                }
            });
        }

        private async Task ListenToServer(TcpClient client)
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
            finally
            {
                OnConnectionStatusChanged?.Invoke(ConnectionStatus.Disconnected);
            }
        }

        public void SendMessage(string message)
        {
            if (_client != null && _client.Connected)
            {
                lock (_sendLock)
                {
                    try
                    {
                        var stream = _client.GetStream();
                        byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                        stream.Write(data, 0, data.Length);
                        stream.Flush();
                    }
                    catch { }
                }
            }
        }
    }
}