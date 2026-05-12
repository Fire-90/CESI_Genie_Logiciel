using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace EasyServer
{
    class Program
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
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    incompleteData += receivedData;

                    int newlineIndex;
                    while ((newlineIndex = incompleteData.IndexOf('\n')) >= 0)
                    {
                        string msg = incompleteData.Substring(0, newlineIndex).TrimEnd('\r');
                        incompleteData = incompleteData.Substring(newlineIndex + 1);

                        if (!isIdentified)
                        {
                            if (msg.StartsWith("[IDENTIFY]|"))
                            {
                                clientId = msg.Substring(11).Trim();
                                lock (_lock) { _clients[client] = clientId; }
                                isIdentified = true;
                                Console.WriteLine($"[INFO] Client connecté : {clientId}");
                                _ = ServerLogger.Instance.WriteConnectionLogAsync(new ServerLogEntry { Action = "CONNEXION", Message = "Client authentifié" }, clientId);
                            }
                        }
                        else
                        {
                            string broadcastMsg = msg;

                            if (msg.StartsWith("[LOG]|"))
                            {
                                var parts = msg.Split(new[] { '|' }, 4);
                                if (parts.Length == 4)
                                {
                                    string logFormat = parts[2];
                                    string jsonLog = parts[3];
                                    _ = ServerLogger.Instance.WriteClientLogAsync(clientId, jsonLog, logFormat);
                                }
                                continue;
                            }
                            else if (msg.StartsWith("[STATE]|"))
                            {
                                string stateJson = msg.Substring(8);
                                await ServerStateManager.Instance.WriteClientStateAsync(stateJson, clientId);
                                continue;
                            }
                            else if (msg.StartsWith("[GET_STATES]"))
                            {
                                List<string> activeClients;
                                lock (_lock) { activeClients = _clients.Values.Where(v => !string.IsNullOrEmpty(v)).ToList(); }
                                string statesJson = await ServerStateManager.Instance.GetAllClientStatesAsync(clientId, activeClients);
                                byte[] response = Encoding.UTF8.GetBytes($"[STATES_RESPONSE]|{statesJson}\n");
                                await stream.WriteAsync(response, 0, response.Length);
                                continue;
                            }

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