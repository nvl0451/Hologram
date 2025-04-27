using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MusicServerUI
{
    public class KinectServer
    {
        private const int RECEIVE_PORT = 5000;
        private const int SEND_PORT = 5555;
        private const int MAGIC_NUMBER = 0x4B494E54; // "KINT" in hex
        private const int EXPECTED_DATA_LENGTH = 821; // 4 (magic) + 1 (bool) + 816 (kinect data)
        private const int KINECT_DATA_LENGTH = 816; // 25 joints * 32 bytes + 16 bytes for face quaternion
        private const int TIMEOUT_SECONDS = 5;
        private const int LOG_INTERVAL_SECONDS = 10;

        private UdpClient receiveClient;
        private UdpClient sendClient;
        private Thread receiveThread;
        private volatile bool isRunning = true;
        private string currentStatus = "MIA";
        private bool isLockTPose = false;
        private DateTime lastReceiveTime = DateTime.MinValue;
        private DateTime lastLogTime = DateTime.MinValue;

        public event EventHandler<string> StatusChanged;
        public event Action<bool> LockTPoseChanged;
        public bool IsLockTPose => isLockTPose;

        public void Start()
        {
            Console.WriteLine("KinectServer: Starting...");
            Console.WriteLine($"KinectServer: Listening on port {RECEIVE_PORT}, sending on port {SEND_PORT}");

            receiveClient = new UdpClient(RECEIVE_PORT);
            receiveClient.Client.ReceiveTimeout = 1000;
            sendClient = new UdpClient();
            receiveThread = new Thread(ReceiveData)
            {
                IsBackground = true
            };
            receiveThread.Start();

            Console.WriteLine("KinectServer: Started successfully.");
        }

        public void Stop()
        {
            Console.WriteLine("KinectServer: Stopping...");
            isRunning = false;
            receiveThread?.Join(100);
            if (receiveThread?.IsAlive ?? false) receiveThread.Abort();
            receiveClient?.Close();
            sendClient?.Close();
            Console.WriteLine("KinectServer: Stopped.");
        }

        private void ReceiveData()
        {
            Console.WriteLine("KinectServer: Entering receive loop.");
            try
            {
                while (isRunning)
                {
                    try
                    {
                        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = receiveClient.Receive(ref remoteEndPoint);
                        DateTime now = DateTime.Now;

                        bool shouldLog = (now - lastLogTime).TotalSeconds >= LOG_INTERVAL_SECONDS;

                        if (shouldLog)
                        {
                            Console.WriteLine($"KinectServer: Received {data.Length} bytes from {remoteEndPoint}");
                        }

                        if (data.Length != EXPECTED_DATA_LENGTH)
                        {
                            if (shouldLog)
                            {
                                Console.WriteLine($"KinectServer: Invalid data length. Expected {EXPECTED_DATA_LENGTH}, got {data.Length}");
                            }
                            continue;
                        }

                        using (var ms = new MemoryStream(data))
                        using (var reader = new BinaryReader(ms))
                        {
                            int magic = reader.ReadInt32();
                            if (magic != MAGIC_NUMBER)
                            {
                                Console.WriteLine($"KinectServer: Invalid magic number. Expected {MAGIC_NUMBER}, got {magic}");
                                continue;
                            }

                            bool isTrackingBody = reader.ReadBoolean();
                            if (shouldLog)
                            {
                                Console.WriteLine($"KinectServer: isTrackingBody = {isTrackingBody}");
                            }

                            byte[] kinectData = reader.ReadBytes(KINECT_DATA_LENGTH);
                            if (kinectData.Length != KINECT_DATA_LENGTH)
                            {
                                Console.WriteLine($"KinectServer: Incomplete kinect data. Expected {KINECT_DATA_LENGTH}, got {kinectData.Length}");
                                continue;
                            }

                            string newStatus = isTrackingBody ? "Tracking" : "Idle";
                            if (newStatus != currentStatus)
                            {
                                currentStatus = newStatus;
                                Console.WriteLine($"KinectServer: Status changed to {currentStatus}");
                                StatusChanged?.Invoke(this, currentStatus);
                            }

                            lastReceiveTime = now;
                            if (shouldLog)
                            {
                                Console.WriteLine("KinectServer: Updated last receive time.");
                                lastLogTime = now; // Reset log timer only when we log
                            }

                            if (isTrackingBody)
                            {
                                if (shouldLog)
                                {
                                    Console.WriteLine("KinectServer: Retransmitting kinect data.");
                                }
                                sendClient.Send(kinectData, kinectData.Length, "localhost", SEND_PORT);
                            }
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        if ((DateTime.Now - lastReceiveTime).TotalSeconds > TIMEOUT_SECONDS && currentStatus != "MIA")
                        {
                            currentStatus = "MIA";
                            Console.WriteLine("KinectServer: No data received for over 5 seconds. Setting status to MIA.");
                            StatusChanged?.Invoke(this, currentStatus);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"KinectServer: Exception in receive loop: {ex.Message}");
                    }
                }
            }
            finally
            {
                receiveClient?.Close();
                Console.WriteLine("KinectServer: Receive loop exited.");
            }
        }

        public void ToggleLockTPose()
        {
            isLockTPose = !isLockTPose;
            string message = isLockTPose ? "lock T-pose" : "unlock T-pose";
            Console.WriteLine($"KinectServer: Toggling T-pose lock to {isLockTPose}. Sending message: {message}");
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(message);
            sendClient.Send(messageBytes, messageBytes.Length, "localhost", SEND_PORT);
            LockTPoseChanged?.Invoke(isLockTPose);
        }
    }
}