using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace EasyServer
{
    class Program
    {
        private static TcpListener _listener;
        private static readonly List<TcpClient> _clients = new List<TcpClient>();
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
                    lock (_lock) _clients.Add(client);

                    string clientId = GetClientId(client);
                    Console.WriteLine($"[CONNEXION] Client connecté : {clientId}");

                    _ = ServerLogger.Instance.WriteConnectionLogAsync(new ServerLogEntry
                    {
                        Action = "CONNEXION",
                        Message = "Nouveau client connecté"
                    }, clientId);

                    _ = HandleClient(client, clientId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERREUR] Erreur d'acceptation : {ex.Message}");
                }
            }
        }

        private static string GetClientId(TcpClient client)
        {
            if (client.Client.RemoteEndPoint is IPEndPoint ipEndPoint)
            {
                return ipEndPoint.Address.ToString();
            }
            return "Unknown-IP";
        }

        private static async Task HandleClient(TcpClient client, string clientId)
        {
            var stream = client.GetStream();
            byte[] buffer = new byte[4096];
            string incompleteData = "";

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
                        else
                        {
                            Console.WriteLine($"[RECEPTION] [{clientId}] {msg}");
                            Broadcast(msg, client);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Déconnexion forcée
            }
            finally
            {
                lock (_lock) _clients.Remove(client);
                Console.WriteLine($"[DECONNEXION] Le client {clientId} s'est déconnecté.");

                _ = ServerLogger.Instance.WriteConnectionLogAsync(new ServerLogEntry
                {
                    Action = "DECONNEXION",
                    Message = "Fin de la connexion"
                }, clientId);

                client.Close();
            }
        }

        private static void Broadcast(string msg, TcpClient sender)
        {
            byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
            lock (_lock)
            {
                foreach (var c in _clients)
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