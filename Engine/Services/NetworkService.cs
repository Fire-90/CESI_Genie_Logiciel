using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace EasySave.Services
{
    public enum ConnectionStatus
    {
        Connecting,
        Connected,
        Disconnected
    }

    public class NetworkService
    {
        private TcpClient _client;
        private readonly ConfigManager _configManager;
        private readonly int _port;

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

                            // Chargement dynamique de l'IP et du Nom (permet les modifs à chaud)
                            var settings = _configManager.LoadSettings();
                            string ipAddress = string.IsNullOrWhiteSpace(settings.ServerIP) ? "127.0.0.1" : settings.ServerIP;
                            string clientName = string.IsNullOrWhiteSpace(settings.ClientName) ? "UnknownClient" : settings.ClientName;

                            _client = new TcpClient();
                            await _client.ConnectAsync(ipAddress, _port);

                            // Envoi immédiat de l'identifiant
                            SendMessage($"[IDENTIFY]|{clientName}");

                            OnConnectionStatusChanged?.Invoke(ConnectionStatus.Connected);

                            _ = ReceiveLoop(_client);
                        }
                    }
                    catch
                    {
                        OnConnectionStatusChanged?.Invoke(ConnectionStatus.Disconnected);
                    }

                    await Task.Delay(5000); // Réessaie toutes les 5 secondes
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
                    if (read == 0) break; // Le serveur a coupé la connexion

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
                // Si on sort de la boucle de lecture, on est déconnecté
                OnConnectionStatusChanged?.Invoke(ConnectionStatus.Disconnected);
            }
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