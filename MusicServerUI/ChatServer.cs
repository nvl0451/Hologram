using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MusicServerUI
{
    public class Message
    {
        public int ID { get; set; }
        public string Username { get; set; }
        public string Text { get; set; }
    }

    public class User
    {
        public TcpClient Client { get; set; }
        public string Nickname { get; set; }
        public bool IsMuted { get; set; }
    }

    public class ChatServer
    {
        private TcpListener listener;
        private List<User> users = new List<User>();
        private List<Message> activeMessages = new List<Message>();
        private int nextMessageId = 0;
        private string adminNickname;

        public event Action<Message> MessageReceived;
        public event Action<int> MessageDeleted;
        public event Action<string> UserMuteStatusChanged;

        public ChatServer(string adminNick = "admin")
        {
            adminNickname = adminNick;
            listener = new TcpListener(IPAddress.Any, 3333);
        }

        public async Task Start()
        {
            listener.Start();
            Console.WriteLine("Chat server started on port 3333.");
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream);
                var writer = new StreamWriter(stream) { AutoFlush = true };

                var nickLine = await reader.ReadLineAsync();
                if (nickLine?.StartsWith("NICK:") != true)
                {
                    client.Close();
                    return;
                }

                var nickname = nickLine.Substring(5).Trim();
                var user = new User { Client = client, Nickname = nickname, IsMuted = false };
                lock (users)
                {
                    users.Add(user);
                    Console.WriteLine($"User connected: {nickname}");
                }

                lock (activeMessages)
                {
                    foreach (var msg in activeMessages)
                    {
                        writer.WriteLine($"MSG:{msg.ID}:{msg.Username}:{msg.Text}");
                    }
                }

                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    if (line.StartsWith("MSG:"))
                    {
                        var messageText = line.Substring(4).Trim();
                        lock (users)
                        {
                            var sender = users.First(u => u.Client == client);
                            if (!sender.IsMuted)
                            {
                                var id = Interlocked.Increment(ref nextMessageId);
                                var msg = new Message { ID = id, Username = sender.Nickname, Text = messageText };
                                lock (activeMessages)
                                {
                                    activeMessages.Add(msg);
                                }
                                Broadcast($"MSG:{id}:{msg.Username}:{msg.Text}");
                                MessageReceived?.Invoke(msg);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client handling error: {ex.Message}");
            }
            finally
            {
                lock (users)
                {
                    var disconnectedUser = users.FirstOrDefault(u => u.Client == client);
                    if (disconnectedUser != null)
                    {
                        Console.WriteLine($"User disconnected: {disconnectedUser.Nickname}");
                        users.RemoveAll(u => u.Client == client);
                    }
                }
                client.Close();
            }
        }

        private void Broadcast(string message)
        {
            lock (users)
            {
                foreach (var user in users.ToList())
                {
                    try
                    {
                        var writer = new StreamWriter(user.Client.GetStream()) { AutoFlush = true };
                        writer.WriteLine(message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Broadcast failed to {user.Nickname}: {ex.Message}");
                        users.Remove(user);
                    }
                }
            }
        }

        public void SendMessage(string message)
        {
            var id = Interlocked.Increment(ref nextMessageId);
            var msg = new Message { ID = id, Username = adminNickname, Text = message };
            lock (activeMessages)
            {
                activeMessages.Add(msg);
            }
            Broadcast($"MSG:{id}:{msg.Username}:{msg.Text}");
            MessageReceived?.Invoke(msg);
        }

        public void DeleteMessage(int id)
        {
            lock (activeMessages)
            {
                var msg = activeMessages.FirstOrDefault(m => m.ID == id);
                if (msg != null)
                {
                    activeMessages.Remove(msg);
                    Broadcast($"DEL:{id}");
                    MessageDeleted?.Invoke(id);
                }
            }
        }

        public void MuteUser(string username)
        {
            lock (users)
            {
                var user = users.FirstOrDefault(u => u.Nickname == username);
                if (user != null && !user.Nickname.Equals(adminNickname, StringComparison.OrdinalIgnoreCase))
                {
                    user.IsMuted = true;
                    try
                    {
                        var writer = new StreamWriter(user.Client.GetStream()) { AutoFlush = true };
                        writer.WriteLine("MUTED");
                        Console.WriteLine($"User muted: {username}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to notify mute to {username}: {ex.Message}");
                    }
                    UserMuteStatusChanged?.Invoke(username);
                }
            }
        }

        public void UnmuteUser(string username)
        {
            lock (users)
            {
                var user = users.FirstOrDefault(u => u.Nickname == username);
                if (user != null)
                {
                    user.IsMuted = false;
                    try
                    {
                        var writer = new StreamWriter(user.Client.GetStream()) { AutoFlush = true };
                        writer.WriteLine("UNMUTED");
                        Console.WriteLine($"User unmuted: {username}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to notify unmute to {username}: {ex.Message}");
                    }
                    UserMuteStatusChanged?.Invoke(username);
                }
            }
        }

        public bool IsUserMuted(string username)
        {
            lock (users)
            {
                var user = users.FirstOrDefault(u => u.Nickname == username);
                return user?.IsMuted ?? false;
            }
        }

        public List<Message> GetActiveMessages()
        {
            lock (activeMessages)
            {
                return new List<Message>(activeMessages);
            }
        }

        public List<string> GetMutedUsers()
        {
            lock (users)
            {
                return users.Where(u => u.IsMuted).Select(u => u.Nickname).ToList();
            }
        }

        public void Stop()
        {
            listener.Stop();
            lock (users)
            {
                foreach (var user in users)
                {
                    user.Client.Close();
                }
                users.Clear();
                Console.WriteLine("Chat server stopped.");
            }
        }
    }
}