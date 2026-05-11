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
                    lock (_lock)
                    {
                        _clients.Add(client, string.Empty);
                    }

                    _ = HandleClient(client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERREUR] Erreur d'acceptation : {ex.Message}");
                }
            }
        }

        private static async Task HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            byte[] buffer = new byte[4096];
            string incompleteData = "";

            string clientId = "En attente d'identification...";
            bool isIdentified = false;

            try
            {
                while (true)
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

                        if (!isIdentified)
                        {
                            if (msg.StartsWith("[IDENTIFY]|"))
                            {
                                clientId = msg.Substring(11).Trim();
                                isIdentified = true;

                                lock (_lock)
                                {
                                    _clients[client] = clientId;
                                }

                                Console.WriteLine($"[CONNEXION] Client identifié : {clientId}");

                                _ = ServerLogger.Instance.WriteConnectionLogAsync(new ServerLogEntry
                                {
                                    Action = "CONNEXION",
                                    Message = "Nouveau client connecté"
                                }, clientId);
                            }
                        }
                        else
                        {
                            if (msg.StartsWith("[LOG]|"))
                            {
                                var parts = msg.Split(new[] { '|' }, 4);
                                if (parts.Length == 4)
                                {
                                    string jobId = parts[1];
                                    string format = parts[2];
                                    string jsonPayload = parts[3];

                                    _ = ServerLogger.Instance.WriteClientBackupLogAsync(jsonPayload, clientId, jobId, format);
                                }
                            }
                            else if (msg.StartsWith("[STATE]|"))
                            {
                                var parts = msg.Split(new[] { '|' }, 2);
                                if (parts.Length == 2)
                                {
                                    string jsonPayload = parts[1];
                                    _ = ServerStateManager.Instance.WriteClientStateAsync(jsonPayload, clientId);
                                }
                            }
                            else if (msg.StartsWith("[GET_STATES]"))
                            {
                                List<string> activeClientIds;
                                lock (_lock)
                                {
                                    activeClientIds = _clients.Values.Where(id => !string.IsNullOrEmpty(id)).ToList();
                                }

                                string statesJson = await ServerStateManager.Instance.GetAllClientStatesAsync(clientId, activeClientIds);
                                byte[] responseData = Encoding.UTF8.GetBytes($"[STATES_RESPONSE]|{statesJson}\n");
                                try { await stream.WriteAsync(responseData, 0, responseData.Length); } catch { }
                            }
                            else if (msg.StartsWith("[END]"))
                            {
                                string jobInfo = msg.Replace("[END]", "").Trim();
                                Console.WriteLine($"[STATUT] {clientId} a terminé : {jobInfo}");

                                // On inclut l'ID de la machine dans le message de broadcast pour déclencher l'animation du côté des visionneurs
                                Broadcast($"[END]|{clientId}|{jobInfo}", client);
                            }
                            else if (msg.StartsWith("[START]"))
                            {
                                Console.WriteLine($"[STATUT] {clientId} a démarré : {msg.Replace("[START]", "").Trim()}");
                                Broadcast(msg, client);
                            }
                            else if (msg.StartsWith("[ERROR]"))
                            {
                                Console.WriteLine($"[ERREUR] {clientId} : {msg.Replace("[ERROR]", "").Trim()}");
                                Broadcast(msg, client);
                            }
                            else
                            {
                                Broadcast(msg, client);
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                lock (_lock)
                {
                    _clients.Remove(client);
                }

                if (isIdentified)
                {
                    Console.WriteLine($"[DECONNEXION] Le client {clientId} s'est déconnecté.");
                    _ = ServerLogger.Instance.WriteConnectionLogAsync(new ServerLogEntry
                    {
                        Action = "DECONNEXION",
                        Message = "Fin de la connexion"
                    }, clientId);
                }
                else
                {
                    Console.WriteLine("[DECONNEXION] Un client non identifié s'est déconnecté.");
                }

                client.Close();
            }
        }

        private static void Broadcast(string msg, TcpClient sender)
        {
            byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
            lock (_lock)
            {
                foreach (var c in _clients.Keys)
                {
                    if (c != sender && c.Connected)
                    {
                        try { c.GetStream().Write(data, 0, data.Length); } catch { }
                    }
                }
            }
        }
    }
}