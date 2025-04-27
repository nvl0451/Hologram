using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class VideoStreamScript : MonoBehaviour
{
    public RawImage displayImage;
    private ClientWebSocket ws;
    private Process ffmpegProcess;
    private Texture2D frameTexture;
    public GameObject hologramChar;
    private HologramFader hologramFader;

    private BlockingCollection<byte[]> h264Queue = new BlockingCollection<byte[]>();
    private volatile byte[] latestFrame;
    private int frameWidth = 1280;
    private int frameHeight = 720;
    private int frameSize;
    private bool isRunning = true;
    private Thread webSocketThread;
    private Thread writingThread;
    private Thread readingThread;
    private float lastFrameTime = 0;
    private bool isStreamActive = false;
    private bool wasStreamActive = false;

    // Logging counters
    private int receivedCount = 0;
    private int writeCount = 0;
    private float lastLogTime = 0;

    void Start()
    {
        displayImage.uvRect = new Rect(0, 1, 1, -1);

        frameSize = frameWidth * frameHeight * 4; // RGBA
        frameTexture = new Texture2D(frameWidth, frameHeight, TextureFormat.RGBA32, false);
        displayImage.texture = frameTexture;

        if (hologramChar != null)
        {
            hologramFader = hologramChar.GetComponent<HologramFader>();
            if (hologramFader == null)
            {
                UnityEngine.Debug.LogWarning("HologramChar does not have a HologramFader component.");
            }
            else
            {
                UnityEngine.Debug.LogWarning("HologramChar does have a HologramFader component.");
            }
            //hologramFader.FadeIn(10f);
        }

        // Start FFmpeg and its writing thread
        StartFFmpeg();

        // Start WebSocket operations on a separate thread
        webSocketThread = new Thread(WebSocketThread);
        webSocketThread.Start();
    }

    void WebSocketThread()
    {
        ws = new ClientWebSocket();
        try
        {
            ws.ConnectAsync(new Uri("ws://localhost:8080"), CancellationToken.None).GetAwaiter().GetResult();
            UnityEngine.Debug.Log("WebSocket connected from thread");

            byte[] buffer = new byte[1024 * 1024];
            while (isRunning && ws.State == WebSocketState.Open)
            {
                var result = ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).GetAwaiter().GetResult();
                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    byte[] h264Data = new byte[result.Count];
                    Array.Copy(buffer, h264Data, result.Count);
                    h264Queue.Add(h264Data);
                    receivedCount++;

                    if (receivedCount <= 5)
                    {
                        UnityEngine.Debug.Log($"Received H.264 data: {h264Data.Length} bytes, h264Queue size: {h264Queue.Count}");
                    }
                    else if (receivedCount % 100 == 0)
                    {
                        UnityEngine.Debug.Log($"Received {receivedCount} H.264 packets, latest: {h264Data.Length} bytes, h264Queue size: {h264Queue.Count}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError("WebSocket error in thread: " + e.Message);
        }
    }

    void StartFFmpeg()
    {
        ffmpegProcess = new Process();
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "/opt/local/bin/ffmpeg",
            Arguments = $"-f h264 -probesize 32 -analyzeduration 0 -flags low_delay -fflags nobuffer -i pipe:0 -f rawvideo -pix_fmt rgba -s {frameWidth}x{frameHeight} -threads 0 pipe:1",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        ffmpegProcess.StartInfo = startInfo;

        try
        {
            ffmpegProcess.Start();
            UnityEngine.Debug.Log("FFmpeg started successfully");

            writingThread = new Thread(WriteToFFmpegThread);
            writingThread.Start();

            readingThread = new Thread(ReadFramesThread);
            readingThread.Start();

            Thread errorThread = new Thread(() =>
            {
                while (isRunning && !ffmpegProcess.HasExited)
                {
                    string errorLine = ffmpegProcess.StandardError.ReadLine();
                    if (!string.IsNullOrEmpty(errorLine))
                    {
                        if (errorLine.ToLower().Contains("error") || errorLine.ToLower().Contains("failed"))
                        {
                            UnityEngine.Debug.LogError("FFmpeg error: " + errorLine);
                        }
                        else
                        {
                            UnityEngine.Debug.Log("FFmpeg info: " + errorLine);
                        }
                    }
                }
            });
            errorThread.Start();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError("Failed to start FFmpeg: " + e.Message);
        }
    }

    void WriteToFFmpegThread()
    {
        while (isRunning)
        {
            if (h264Queue.TryTake(out byte[] data, 100))
            {
                try
                {
                    ffmpegProcess.StandardInput.BaseStream.Write(data, 0, data.Length);
                    ffmpegProcess.StandardInput.BaseStream.Flush();
                    writeCount++;

                    if (writeCount <= 5)
                    {
                        UnityEngine.Debug.Log($"Wrote {data.Length} bytes to FFmpeg, h264Queue size: {h264Queue.Count}");
                    }
                    else if (writeCount % 100 == 0)
                    {
                        UnityEngine.Debug.Log($"Wrote {writeCount} packets to FFmpeg, latest: {data.Length} bytes, h264Queue size: {h264Queue.Count}");
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("Error writing to FFmpeg: " + e.Message);
                }
            }
        }
    }

    void ReadFramesThread()
    {
        Stream outputStream = ffmpegProcess.StandardOutput.BaseStream;
        byte[] frameData = new byte[frameSize];
        int bytesReadTotal = 0;

        while (isRunning && !ffmpegProcess.HasExited)
        {
            try
            {
                while (bytesReadTotal < frameSize)
                {
                    int bytesRead = outputStream.Read(frameData, bytesReadTotal, frameSize - bytesReadTotal);
                    if (bytesRead == 0)
                    {
                        UnityEngine.Debug.Log("FFmpeg stream ended");
                        return;
                    }
                    bytesReadTotal += bytesRead;
                }

                latestFrame = frameData;
                frameData = new byte[frameSize];
                bytesReadTotal = 0;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("FFmpeg read error: " + e.Message);
                break;
            }
        }
    }

    void Update()
    {
        if (latestFrame != null)
        {
            if (!displayImage.gameObject.activeSelf)
            {
                displayImage.gameObject.SetActive(true);
                if (hologramFader != null)
                {
                    UnityEngine.Debug.Log("Calling FadeOut on hologram");
                    hologramFader.FadeOut(2f);
                }
            }
            frameTexture.LoadRawTextureData(latestFrame);
            frameTexture.Apply();
            latestFrame = null;
            lastFrameTime = Time.time;
            isStreamActive = true;
        }
        else
        {
            float timeSinceLastFrame = Time.time - lastFrameTime;
            if (timeSinceLastFrame >= 1.0f)
            {
                isStreamActive = false;
            }
        }

        float targetAlpha = isStreamActive ? 1f : 0f;
        Color rawImageColor = displayImage.color;
        rawImageColor.a = Mathf.Lerp(rawImageColor.a, targetAlpha, Time.deltaTime * 5f);
        displayImage.color = rawImageColor;

        if (!isStreamActive && rawImageColor.a < 0.01f)
        {
            displayImage.gameObject.SetActive(false);
        }

        if (wasStreamActive && !isStreamActive)
        {
            if (hologramFader != null)
            {
                UnityEngine.Debug.Log("Calling FadeIn on hologram");
                hologramFader.FadeIn(2f);
            }
        }
        wasStreamActive = isStreamActive;

        if (Time.time - lastLogTime >= 1.0f)
        {
            UnityEngine.Debug.Log($"Periodic check - h264Queue size: {h264Queue.Count}");
            lastLogTime = Time.time;
        }
    }

    void OnDestroy()
    {
        isRunning = false;
        h264Queue.CompleteAdding();

        if (ws != null && ws.State == WebSocketState.Open)
        {
            ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).GetAwaiter().GetResult();
        }

        if (ffmpegProcess != null && !ffmpegProcess.HasExited)
        {
            ffmpegProcess.Kill();
            ffmpegProcess.Dispose();
        }

        if (webSocketThread != null && webSocketThread.IsAlive) webSocketThread.Join();
        if (writingThread != null && writingThread.IsAlive) writingThread.Join();
        if (readingThread != null && readingThread.IsAlive) readingThread.Join();
    }
}