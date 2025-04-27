using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using TMPro;

public class AudioStreamer : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nowPlayingLabel;

    private List<float[]> audioChunks = new List<float[]>();
    private object lockObj = new object();
    private int currentChunkIndex = 0;
    private int currentSampleIndex = 0;
    private bool isPlaying = false;
    private UdpClient udpClient;
    private Thread receiveThread;
    private int filterReadCount = 0;

    private UdpClient messageUdpClient;
    private Thread messageReceiveThread;
    private string currentSongName = "";

    void Start()
    {
        // Set up AudioSource component
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.clip = null; // No clip, using OnAudioFilterRead for streaming
        audioSource.Play();

        // Start UDP listener on port 2222 for audio
        udpClient = new UdpClient(2222);
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        // Start UDP listener on port 1111 for song info
        messageUdpClient = new UdpClient(1111);
        messageReceiveThread = new Thread(ReceiveMessageData);
        messageReceiveThread.IsBackground = true;
        messageReceiveThread.Start();

        Debug.Log("AudioStreamer started, listening on port 2222 for audio and 1111 for song info...");
    }

    void OnDestroy()
    {
        // Clean up resources
        receiveThread?.Abort();
        udpClient?.Close();
        messageReceiveThread?.Abort();
        messageUdpClient?.Close();
    }

    private void ReceiveData()
    {
        while (true)
        {
            try
            {
                // Receive UDP packets for audio
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                float[] floats = new float[data.Length / 4];
                Buffer.BlockCopy(data, 0, floats, 0, data.Length);

                // Add the received chunk to the buffer
                lock (lockObj)
                {
                    audioChunks.Add(floats);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Receive error: {e.Message}");
            }
        }
    }

    private void ReceiveMessageData()
    {
        while (true)
        {
            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = messageUdpClient.Receive(ref remoteEndPoint);
                string message = Encoding.UTF8.GetString(data);
                ProcessMessage(message);
            }
            catch (Exception e)
            {
                Debug.LogError($"Message receive error: {e.Message}");
            }
        }
    }

    private void ProcessMessage(string message)
    {
        if (message.StartsWith("songname "))
        {
            string songName = message.Substring(9); // Remove "songname "
            SetNowPlaying(songName);
        }
        else if (message == "stop")
        {
            ClearNowPlaying();
        }
    }

    private void SetNowPlaying(string songName)
    {
        currentSongName = songName;
    }

    private void ClearNowPlaying()
    {
        currentSongName = "";
    }

    void Update()
    {
        if (nowPlayingLabel != null)
        {
            if (!string.IsNullOrEmpty(currentSongName))
            {
                nowPlayingLabel.text = $"Now playing: {currentSongName}";
            }
            else
            {
                nowPlayingLabel.text = "";
            }
        }
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        lock (lockObj)
        {
            // Buffer 0.5 seconds of audio (24000 samples at 48kHz) before starting playback
            int minFramesBuffered = 24000;
            int totalSamplesBuffered = audioChunks.Count * (audioChunks.Count > 0 ? audioChunks[0].Length / channels : 0);
            if (totalSamplesBuffered > minFramesBuffered && !isPlaying)
            {
                isPlaying = true;
                Debug.Log("Starting playback...");
            }

            if (isPlaying)
            {
                int dataIndex = 0;
                while (dataIndex < data.Length)
                {
                    if (currentChunkIndex >= audioChunks.Count)
                    {
                        // Buffer underrun: fill remaining data with silence
                        while (dataIndex < data.Length)
                        {
                            data[dataIndex++] = 0f;
                        }
                        Debug.LogWarning("Buffer underrun, filling with silence.");
                        break;
                    }

                    // Copy samples from the current chunk to the output data array
                    float[] currentChunk = audioChunks[currentChunkIndex];
                    int samplesToCopy = Mathf.Min(currentChunk.Length - currentSampleIndex, data.Length - dataIndex);
                    Array.Copy(currentChunk, currentSampleIndex, data, dataIndex, samplesToCopy);

                    dataIndex += samplesToCopy;
                    currentSampleIndex += samplesToCopy;

                    // Move to the next chunk if the current one is fully consumed
                    if (currentSampleIndex >= currentChunk.Length)
                    {
                        currentSampleIndex = 0;
                        currentChunkIndex++;
                    }
                }

                // Clean up consumed chunks from the buffer
                if (currentChunkIndex > 0)
                {
                    audioChunks.RemoveRange(0, currentChunkIndex);
                    currentChunkIndex = 0;
                }
            }
            else
            {
                // Not enough data buffered yet: fill with silence
                Array.Clear(data, 0, data.Length);
            }

            // Periodic logging of buffer status (every 100 calls)
            filterReadCount++;
            if (filterReadCount % 100 == 0)
            {
                double bufferedTime = (audioChunks.Count * 1024) / 48000.0;
                Debug.Log($"Buffer status: {audioChunks.Count} chunks, {bufferedTime:F2} seconds");
            }
        }
    }
}