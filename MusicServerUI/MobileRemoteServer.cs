// MobileRemoteServer.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Globalization;

namespace MusicServerUI
{
    public class MobileRemoteServer
    {
        private readonly AudioStreamer audioStreamer;
        private readonly ChatServer chatServer;
        private readonly KinectServer kinectServer;
        private readonly SkinChangerServer skinChangerServer;
        private TcpListener listener;
        private List<TcpClient> clients = new List<TcpClient>();
        private readonly object lockObject = new object();
        private string mobileStatus = "MIA";

        public event Action<string> ConnectionStatusChanged;
        public event Action<float> VolumeChanged;

        public MobileRemoteServer(AudioStreamer audioStreamer, ChatServer chatServer, KinectServer kinectServer, SkinChangerServer skinChangerServer)
        {
            this.audioStreamer = audioStreamer;
            this.chatServer = chatServer;
            this.kinectServer = kinectServer;
            this.skinChangerServer = skinChangerServer;
            listener = new TcpListener(IPAddress.Any, 7777);
            chatServer.MessageReceived += OnMessageReceived;
            chatServer.MessageDeleted += OnMessageDeleted;
            chatServer.UserMuteStatusChanged += OnUserMuteStatusChanged;
            kinectServer.StatusChanged += (sender, status) => Broadcast($"KINECT_STATUS {status}");
            kinectServer.LockTPoseChanged += (isLocked) => Broadcast($"TPOSE_LOCK {isLocked.ToString().ToLower()}");
            skinChangerServer.SkinChanged += (newSkin) => Broadcast($"SKIN {newSkin}");
        }

        public async Task Start()
        {
            listener.Start();
            Console.WriteLine("MobileRemoteServer started on port 7777.");
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                lock (lockObject)
                {
                    clients.Add(client);
                    mobileStatus = "Connected";
                    ConnectionStatusChanged?.Invoke(mobileStatus);
                }
                _ = HandleClientAsync(client);
            }
        }

        public void Stop()
        {
            listener.Stop();
            lock (lockObject)
            {
                foreach (var client in clients)
                {
                    client.Close();
                }
                clients.Clear();
                mobileStatus = "MIA";
                ConnectionStatusChanged?.Invoke(mobileStatus);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream);
                var writer = new StreamWriter(stream) { AutoFlush = true };

                SendPlaylist(writer);
                SendCurrentSong(writer);
                SendPosition(writer);
                SendPlaybackState(writer);
                SendVolume(writer);
                SendChatMessages(writer);
                SendMutedUsers(writer);

                var skins = string.Join("|", skinChangerServer.Skins);
                writer.WriteLine($"SKINS {skins}");
                writer.WriteLine($"SKIN {skinChangerServer.CurrentSkin}");
                writer.WriteLine($"TPOSE_LOCK {kinectServer.IsLockTPose.ToString().ToLower()}");

                bool expectHeartbeats = false;
                DateTime lastHeartbeatTime = DateTime.MinValue;
                CancellationTokenSource cts = new CancellationTokenSource();

                var heartbeatCheckTask = Task.Run(async () =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        await Task.Delay(1000);
                        if (expectHeartbeats && (DateTime.Now - lastHeartbeatTime) > TimeSpan.FromSeconds(5))
                        {
                            Console.WriteLine("Heartbeat timeout, disconnecting client.");
                            client.Close();
                            break;
                        }
                    }
                }, cts.Token);

                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    if (line == "HEARTBEAT")
                    {
                        lastHeartbeatTime = DateTime.Now;
                    }
                    else if (line == "APP_FOREGROUND")
                    {
                        expectHeartbeats = true;
                        lastHeartbeatTime = DateTime.Now;
                        mobileStatus = "Connected";
                        ConnectionStatusChanged?.Invoke(mobileStatus);
                    }
                    else if (line == "APP_BACKGROUND")
                    {
                        expectHeartbeats = false;
                        mobileStatus = "Idle";
                        ConnectionStatusChanged?.Invoke(mobileStatus);
                    }
                    else if (line == "START_VIDEO_STREAM")
                    {
                        mobileStatus = "Streaming";
                        ConnectionStatusChanged?.Invoke(mobileStatus);
                    }
                    else if (line == "STOP_VIDEO_STREAM")
                    {
                        mobileStatus = "Connected";
                        ConnectionStatusChanged?.Invoke(mobileStatus);
                    }
                    else
                    {
                        ProcessCommand(line);
                    }
                }

                cts.Cancel();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client handling error: {ex.Message}");
            }
            finally
            {
                lock (lockObject)
                {
                    clients.Remove(client);
                    if (clients.Count == 0)
                    {
                        mobileStatus = "MIA";
                        ConnectionStatusChanged?.Invoke(mobileStatus);
                    }
                }
                client.Close();
            }
        }

        private void ProcessCommand(string command)
        {
            var parts = command.Split(' ');
            Console.WriteLine($"Processing command: {command}");
            switch (parts[0].ToUpper())
            {
                case "PLAY":
                    if (int.TryParse(parts[1], out int index))
                    {
                        audioStreamer.Play(index);
                    }
                    break;
                case "PAUSE":
                    audioStreamer.Pause();
                    break;
                case "RESUME":
                    audioStreamer.Resume();
                    break;
                case "NEXT":
                    audioStreamer.Next();
                    break;
                case "PREVIOUS":
                    audioStreamer.Previous();
                    break;
                case "SEEK":
                    if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double position))
                    {
                        audioStreamer.Seek(TimeSpan.FromSeconds(position));
                    }
                    break;
                case "VOLUME":
                    if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float volume))
                    {
                        audioStreamer.SetVolume(volume / 100f);
                        VolumeChanged?.Invoke(volume / 100f);
                        SendVolumeUpdate(volume / 100f);
                    }
                    break;
                case "CHAT_SEND":
                    if (parts.Length > 1)
                    {
                        var messageText = string.Join(" ", parts.Skip(1));
                        chatServer.SendMessage(messageText);
                    }
                    break;
                case "CHAT_DELETE":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int id))
                    {
                        chatServer.DeleteMessage(id);
                    }
                    break;
                case "CHAT_MUTE":
                    if (parts.Length > 2)
                    {
                        var username = parts[1];
                        bool mute = parts[2].ToLower() == "true";
                        if (mute)
                        {
                            chatServer.MuteUser(username);
                        }
                        else
                        {
                            chatServer.UnmuteUser(username);
                        }
                    }
                    break;
                case "SKIN":
                    if (parts.Length > 1)
                    {
                        string skinName = string.Join(" ", parts.Skip(1));
                        skinChangerServer.SetSkin(skinName);
                    }
                    break;
                case "TOGGLE_TPOSE":
                    kinectServer.ToggleLockTPose();
                    break;
                default:
                    Console.WriteLine($"Unknown command: {parts[0]}");
                    break;
            }
        }

        public void SendPositionUpdate(TimeSpan position)
        {
            Broadcast($"POSITION {position.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
        }

        public void SendSongUpdate(Song song, int index)
        {
            Broadcast($"SONG {index} {song.Name} {song.Duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
        }

        public void SendPlaybackStateUpdate(bool isPlaying)
        {
            Broadcast($"PLAYING {(isPlaying ? 1 : 0)}");
        }

        public void SendVolumeUpdate(float volume)
        {
            Broadcast($"VOLUME {(int)(volume * 100)}");
        }

        private void SendPlaylist(StreamWriter writer)
        {
            var playlist = string.Join("|", audioStreamer.Playlist.Select(s => $"{s.Name} {s.Duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}"));
            writer.WriteLine($"PLAYLIST {playlist}");
        }

        private void SendCurrentSong(StreamWriter writer)
        {
            if (audioStreamer.CurrentSongIndex >= 0)
            {
                var song = audioStreamer.Playlist[audioStreamer.CurrentSongIndex];
                writer.WriteLine($"SONG {audioStreamer.CurrentSongIndex} {song.Name} {song.Duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        private void SendPosition(StreamWriter writer)
        {
            writer.WriteLine($"POSITION {audioStreamer.CurrentPosition.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
        }

        private void SendPlaybackState(StreamWriter writer)
        {
            writer.WriteLine($"PLAYING {(audioStreamer.IsPlaying ? 1 : 0)}");
        }

        private void SendVolume(StreamWriter writer)
        {
            writer.WriteLine($"VOLUME {(int)(audioStreamer.Volume * 100)}");
        }

        private void SendChatMessages(StreamWriter writer)
        {
            var messages = chatServer.GetActiveMessages();
            var messagesStr = string.Join(";", messages.Select(m => $"{m.ID}:{m.Username}:{m.Text}"));
            writer.WriteLine($"CHAT_MESSAGES {messagesStr}");
        }

        private void SendMutedUsers(StreamWriter writer)
        {
            var mutedUsers = chatServer.GetMutedUsers();
            var mutedStr = string.Join(";", mutedUsers);
            writer.WriteLine($"CHAT_MUTED {mutedStr}");
        }

        private void OnMessageReceived(Message msg)
        {
            Broadcast($"CHAT_MSG {msg.ID} {msg.Username} {msg.Text}");
        }

        private void OnMessageDeleted(int id)
        {
            Broadcast($"CHAT_DEL {id}");
        }

        private void OnUserMuteStatusChanged(string username)
        {
            var isMuted = chatServer.IsUserMuted(username);
            Broadcast($"CHAT_MUTE {username} {isMuted}");
        }

        private void Broadcast(string message)
        {
            lock (lockObject)
            {
                foreach (var client in clients.ToList())
                {
                    try
                    {
                        var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                        writer.WriteLine(message);
                    }
                    catch
                    {
                        clients.Remove(client);
                    }
                }
            }
        }
    }
}