using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace MusicServerUI
{
    public class Song
    {
        public string FilePath { get; set; }
        public string Name { get; set; }
        public TimeSpan Duration { get; set; }

        public override string ToString() => Name ?? "Unknown";
    }

    public class AudioStreamer
    {
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler<Song>? SongChanged;
        public event EventHandler<bool>? PlaybackStateChanged;

        public List<Song> Playlist { get; private set; } = new List<Song>();
        public int CurrentSongIndex { get; private set; } = -1;
        public TimeSpan CurrentPosition { get; private set; } = TimeSpan.Zero;
        public bool IsPlaying { get; private set; } = false;
        public float Volume => volumeMultiplier; // Added for mobile app access

        private string playlistFolderName = "playlist";
        private Thread? streamingThread;
        private Process? ffmpegProcess;
        private readonly object lockObject = new object();
        private CancellationTokenSource? seekCancellationTokenSource;
        private float volumeMultiplier = 1.0f;

        public void LoadPlaylist()
        {
            Playlist.Clear();
            if (Directory.Exists(playlistFolderName))
            {
                string[] mp3Files = Directory.GetFiles(playlistFolderName, "*.mp3");
                Array.Sort(mp3Files);
                foreach (var file in mp3Files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    TimeSpan duration = GetSongDuration(file);
                    Playlist.Add(new Song { FilePath = file, Name = name, Duration = duration });
                }
            }
            else
            {
                Console.WriteLine("Папка плейлиста не найдена.");
            }
        }

        private TimeSpan GetSongDuration(string songPath)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{songPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
            {
                return TimeSpan.FromSeconds(duration);
            }
            else
            {
                Console.WriteLine($"Не удалось определить длительность песни. Ошибка: '{error}'");
                return TimeSpan.Zero;
            }
        }

        public void SelectSong(int songIndex)
        {
            if (songIndex < 0 || songIndex >= Playlist.Count)
                return;

            StopStreaming();
            CurrentSongIndex = songIndex;
            CurrentPosition = TimeSpan.Zero;
            SongChanged?.Invoke(this, Playlist[songIndex]);
        }

        public void Play(int songIndex)
        {
            if (songIndex < 0 || songIndex >= Playlist.Count)
                return;

            StopStreaming();
            CurrentSongIndex = songIndex;
            CurrentPosition = TimeSpan.Zero;
            IsPlaying = true;
            SendUdpMessage($"songname {Playlist[songIndex].Name}");
            StartStreaming(TimeSpan.Zero);
            SongChanged?.Invoke(this, Playlist[songIndex]);
            PlaybackStateChanged?.Invoke(this, true);
        }

        public void Resume()
        {
            if (CurrentSongIndex < 0 || CurrentSongIndex >= Playlist.Count)
                return;

            if (!IsPlaying)
            {
                IsPlaying = true;
                SendUdpMessage($"songname {Playlist[CurrentSongIndex].Name}");
                StartStreaming(CurrentPosition);
                PlaybackStateChanged?.Invoke(this, true);
            }
        }

        public void Pause()
        {
            if (IsPlaying)
            {
                IsPlaying = false;
                StopStreaming();
                SendUdpMessage("stop");
                PlaybackStateChanged?.Invoke(this, false);
            }
        }

        public void Next()
        {
            int nextIndex = (CurrentSongIndex + 1) % Playlist.Count;
            Play(nextIndex);
        }

        public void Previous()
        {
            int prevIndex = (CurrentSongIndex - 1 + Playlist.Count) % Playlist.Count;
            Play(prevIndex);
        }

        public void RestartOrPrevious()
        {
            if (CurrentSongIndex >= 0 && CurrentPosition > TimeSpan.FromSeconds(0.05 * Playlist[CurrentSongIndex].Duration.TotalSeconds))
            {
                Seek(TimeSpan.Zero);
            }
            else
            {
                Previous();
            }
        }

        public void Seek(TimeSpan position)
        {
            if (CurrentSongIndex < 0 || CurrentSongIndex >= Playlist.Count)
                return;

            StopStreaming();
            CurrentPosition = position;
            PositionChanged?.Invoke(this, CurrentPosition);
            if (IsPlaying)
            {
                StartStreaming(position);
            }
        }

        public void SetVolume(float volume)
        {
            volumeMultiplier = volume;
        }

        private void StartStreaming(TimeSpan startTime)
        {
            lock (lockObject)
            {
                if (streamingThread?.IsAlive == true)
                {
                    StopStreaming();
                }
                seekCancellationTokenSource = new CancellationTokenSource();
                streamingThread = new Thread(() => StreamingLoop(startTime, seekCancellationTokenSource.Token));
                streamingThread.IsBackground = true;
                streamingThread.Start();
            }
        }

        private void StopStreaming()
        {
            lock (lockObject)
            {
                seekCancellationTokenSource?.Cancel();
                if (ffmpegProcess != null)
                {
                    try
                    {
                        if (!ffmpegProcess.HasExited)
                        {
                            ffmpegProcess.Kill();
                            ffmpegProcess.WaitForExit(1000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при остановке процесса FFmpeg: {ex.Message}");
                    }
                    ffmpegProcess.Dispose();
                    ffmpegProcess = null;
                }
                streamingThread = null;
            }
        }

        private void StreamingLoop(TimeSpan startTime, CancellationToken cancellationToken)
        {
            if (CurrentSongIndex < 0 || CurrentSongIndex >= Playlist.Count)
                return;

            try
            {
                string startTimeString = startTime.ToString(@"hh\:mm\:ss\.fff");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-ss {startTimeString} -i \"{Playlist[CurrentSongIndex].FilePath}\" -f f32le -ar 48000 -ac 2 -",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();

                lock (lockObject)
                {
                    ffmpegProcess = process;
                }

                using var udpClient = new UdpClient();
                var endPoint = new IPEndPoint(IPAddress.Loopback, 2222);

                const int samplesPerChunk = 1024;
                const int channels = 2;
                const int bytesPerSample = 4;
                int bufferSize = samplesPerChunk * channels * bytesPerSample;
                var buffer = new byte[bufferSize];

                var stopwatch = Stopwatch.StartNew();
                double chunkDuration = (double)samplesPerChunk / 48000;
                int chunkIndex = 0;
                bool songEndedNaturally = false;

                while (IsPlaying && !cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = process.StandardOutput.BaseStream.Read(buffer, 0, bufferSize);
                    if (bytesRead == 0)
                    {
                        songEndedNaturally = true;
                        break;
                    }

                    float localMultiplier = volumeMultiplier;
                    if (localMultiplier != 1.0f)
                    {
                        var audioSpan = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytesRead));
                        for (int i = 0; i < audioSpan.Length; i++)
                        {
                            audioSpan[i] *= localMultiplier;
                        }
                    }

                    udpClient.Send(buffer, bytesRead, endPoint);

                    lock (lockObject)
                    {
                        CurrentPosition += TimeSpan.FromSeconds(chunkDuration);
                        PositionChanged?.Invoke(this, CurrentPosition);
                    }

                    double expectedTime = chunkIndex * chunkDuration;
                    double elapsed = stopwatch.Elapsed.TotalSeconds;
                    double waitTime = expectedTime - elapsed;
                    if (waitTime > 0)
                    {
                        Thread.Sleep((int)(waitTime * 1000));
                    }
                    chunkIndex++;
                }

                if (songEndedNaturally)
                {
                    Next();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка потоковой передачи: {ex.Message}");
            }
        }

        private void SendUdpMessage(string message)
        {
            using var udpClient = new UdpClient();
            var endPoint = new IPEndPoint(IPAddress.Loopback, 1111);
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, endPoint);
        }
    }
}