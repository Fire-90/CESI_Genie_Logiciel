using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EasyServer
{
    class ServerProgram
    {
        private static TcpListener _listener;
        private static readonly Dictionary<TcpClient, string> _clients = new Dictionary<TcpClient, string>();
        private static readonly object _lock = new object();

        static async Task Main(string[] args)
        {
            _listener = new TcpListener(IPAddress.Any, 11000);
            _listener.Start();
            Console.WriteLine("[SERVEUR] Serveur EasyServer démarré sur le port 11000...");

            while (true)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    lock (_lock) { _clients.Add(client, string.Empty); }
                    _ = HandleClient(client);
                }
                catch (Exception ex) { Console.WriteLine($"[ERREUR] Erreur d'acceptation : {ex.Message}"); }
            }
        }

        private static async Task HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            byte[] buffer = new byte[4096];
            string incompleteData = "";
            string clientId = "Inconnu";
            bool isIdentified = false;

            try
            {
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

                        if (msg.StartsWith("[ID]"))
                        {
                            clientId = msg.Substring(4).Trim();
                            lock (_lock) { _clients[client] = clientId; }
                            isIdentified = true;
                            Console.WriteLine($"[OK] Client identifié : {clientId}");
                            await ServerLogger.Instance.WriteConnectionLogAsync(new ServerLogEntry { Action = "CONNEXION", Message = $"Client {clientId} connecté" }, clientId);
                        }
                        else if (msg.StartsWith("[STATE]|"))
                        {
                            string json = msg.Substring(8);
                            await ServerStateManager.Instance.WriteClientStateAsync(json, clientId);
                        }
                        else if (msg == "[GET_STATES]")
                        {
                            List<string> activeIds;
                            lock (_lock) { activeIds = _clients.Values.Where(v => !string.IsNullOrEmpty(v)).ToList(); }
                            string allStates = await ServerStateManager.Instance.GetAllClientStatesAsync(clientId, activeIds);
                            byte[] response = Encoding.UTF8.GetBytes($"[STATES_RESPONSE]|{allStates}\n");
                            await stream.WriteAsync(response, 0, response.Length);
                        }
                        else if (msg.StartsWith("[LOG]|"))
                        {
                            var parts = msg.Split(new[] { '|' }, 4);
                            if (parts.Length == 4)
                            {
                                await ServerLogger.Instance.WriteClientLogAsync(parts[3], clientId, parts[1], parts[2]);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[MESSAGE] Reçu de {clientId} : {msg}");

                            string broadcastMsg = msg;
                            if (msg.StartsWith("[START]") || msg.StartsWith("[END]") || msg.StartsWith("[PROGRESS]") || msg.StartsWith("[ERROR]"))
                            {
                                int firstBracketEnd = msg.IndexOf(']');
                                string tag = msg.Substring(0, firstBracketEnd + 1);
                                string content = msg.Substring(firstBracketEnd + 1).Trim();
                                broadcastMsg = $"{tag}|{clientId}|{content}";
                            }
                            Broadcast(broadcastMsg, client);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                lock (_lock) { _clients.Remove(client); }
                if (isIdentified)
                {
                    Console.WriteLine($"[INFO] Client {clientId} déconnecté.");
                    _ = ServerLogger.Instance.WriteConnectionLogAsync(new ServerLogEntry { Action = "DECONNEXION", Message = "Fin de la connexion" }, clientId);

                    ServerStateManager.Instance.RemoveClientState(clientId);
                }
                client.Close();
            }
        }

        private static void Broadcast(string msg, TcpClient sender)
        {
            byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
            lock (_lock)
            {
                foreach (var kvp in _clients)
                {
                    if (kvp.Key != sender && kvp.Key.Connected) { try { kvp.Key.GetStream().Write(data, 0, data.Length); } catch { } }
                }
            }
        }
    }
}