using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace MusicServerUI
{
    public class VideoStreamServer
    {
        private readonly HttpListener listener;
        private readonly List<WebSocket> clientWebSockets = new List<WebSocket>();
        private readonly object clientLock = new object();

        public VideoStreamServer()
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://*:8888/"); // Source WebSocket
            listener.Prefixes.Add("http://*:8080/"); // Client WebSocket
        }

        public async Task Start()
        {
            listener.Start();
            Console.WriteLine("VideoStreamServer started on ports 8888 and 8080 for WebSockets.");
            while (true)
            {
                var context = await listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    _ = HandleWebSocketAsync(context);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }

        private async Task HandleWebSocketAsync(HttpListenerContext context)
        {
            WebSocket ws;
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                ws = wsContext.WebSocket;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket accept error: {ex.Message}");
                return;
            }

            int port = context.Request.Url.Port;
            if (port == 8888)
            {
                await HandleSourceWebSocketAsync(ws);
            }
            else if (port == 8080)
            {
                await HandleClientWebSocketAsync(ws);
            }
            else
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid port", CancellationToken.None);
            }
        }

        private async Task HandleSourceWebSocketAsync(WebSocket ws)
        {
            try
            {
                var messageData = new List<byte>();
                while (ws.State == WebSocketState.Open)
                {
                    var buffer = new byte[4096];
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        messageData.AddRange(buffer.Take(result.Count));
                        if (result.EndOfMessage)
                        {
                            byte[] data = messageData.ToArray();
                            Console.WriteLine($"Received complete message of {data.Length} bytes from source");
                            await BroadcastToClientsAsync(data);
                            messageData.Clear();
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Source WebSocket error: {ex.Message}");
            }
            finally
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }

        private async Task HandleClientWebSocketAsync(WebSocket ws)
        {
            lock (clientLock)
            {
                clientWebSockets.Add(ws);
            }
            try
            {
                var buffer = new byte[1]; // Small buffer since we don't expect data
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    // Ignore any received data
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client WebSocket error: {ex.Message}");
            }
            finally
            {
                lock (clientLock)
                {
                    clientWebSockets.Remove(ws);
                }
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }

        private async Task BroadcastToClientsAsync(byte[] data)
        {
            List<WebSocket> clients;
            lock (clientLock)
            {
                clients = new List<WebSocket>(clientWebSockets);
            }
            var sendTasks = new List<Task>();
            foreach (var client in clients)
            {
                if (client.State == WebSocketState.Open)
                {
                    sendTasks.Add(client.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None));
                }
            }
            await Task.WhenAll(sendTasks);
        }
    }
}