using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace MusicServerUI
{
    public class SkinChangerServer
    {
        public string[] Skins { get; } = new string[] { "Default", "Hoodie", "Hoodie Up", "Big Hoodie", "Big Hoodie Up" };
        private TcpListener listener;
        private readonly string ipAddress = "localhost"; // Configurable IP
        private readonly int port = 4444;                // Fixed port
        private TcpClient currentClient;                 // Current connected client
        private StreamWriter clientWriter;               // Persist writer for sending skin changes
        private string currentMobileStatus = "MIA";      // Current mobile status

        public string CurrentSkin { get; private set; } = "Default";
        public event Action<string> SkinChanged;

        public async Task Start()
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"SkinChangerServer listening on {ipAddress}:{port}");
            while (true)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    Console.WriteLine("Client connected");
                    _ = HandleClientAsync(client); // Handle client in a separate task
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accepting client: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                currentClient = client;
                var stream = client.GetStream();
                var reader = new StreamReader(stream);
                clientWriter = new StreamWriter(stream) { AutoFlush = true };
                string message = await reader.ReadLineAsync();
                if (message == "ready")
                {
                    Console.WriteLine($"Received 'ready' from client, sending current skin '{CurrentSkin}' and mobile status '{currentMobileStatus}'");
                    clientWriter.WriteLine($"SKIN {CurrentSkin}");
                    clientWriter.WriteLine($"MOBILE_STATUS {currentMobileStatus}");
                }
                else
                {
                    Console.WriteLine($"Received unexpected message '{message}' from client");
                }
                // Keep the connection open and monitor for disconnection
                while (true)
                {
                    string line = await reader.ReadLineAsync();
                    if (line == null) // Client disconnected
                    {
                        break;
                    }
                    // Optionally handle additional client messages here
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}");
            }
            finally
            {
                if (currentClient == client)
                {
                    currentClient = null;
                    clientWriter = null;
                }
                client.Close();
                Console.WriteLine("Client disconnected");
            }
        }

        public void SetSkin(string skinName)
        {
            if (!Skins.Contains(skinName))
            {
                Console.WriteLine($"Invalid skin name '{skinName}' received; ignoring");
                return;
            }
            CurrentSkin = skinName;
            SkinChanged?.Invoke(skinName);
            SendSkinChange(skinName);
        }

        private void SendSkinChange(string skinName)
        {
            Console.WriteLine($"Skin changed to '{skinName}'");
            if (currentClient != null && currentClient.Connected && clientWriter != null)
            {
                try
                {
                    clientWriter.WriteLine($"SKIN {skinName}");
                    Console.WriteLine($"Successfully sent skin '{skinName}' to client");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending skin '{skinName}': {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"Cannot send skin '{skinName}'; no client connected");
            }
        }

        public void OnMobileStatusChanged(string status)
        {
            currentMobileStatus = status;
            if (currentClient != null && currentClient.Connected && clientWriter != null)
            {
                try
                {
                    clientWriter.WriteLine($"MOBILE_STATUS {status}");
                    Console.WriteLine($"Successfully sent mobile status '{status}' to client");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending mobile status '{status}': {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"Cannot send mobile status '{status}'; no client connected");
            }
        }
    }
}