using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Timers;

public class KinectStreamer : IDisposable
{
    private KinectSensor sensor;
    private BodyFrameReader bodyReader;
    private FaceFrameSource faceSource;
    private FaceFrameReader faceReader;
    private Timer sendTimer;
    private UdpClient udpClient;
    private readonly object lockObject = new object();
    private Joint[] latestJoints = new Joint[25];
    private Vector4[] latestJointOrientations = new Vector4[25];
    private TrackingState[] latestTrackingStates = new TrackingState[25];
    private Vector4 latestFaceQuaternion = new Vector4 { X = 0, Y = 0, Z = 0, W = 1 };
    private bool isTrackingBody = false; // New field for tracking status
    private const int MAGIC_NUMBER = 0x4B494E54; // "KINT" in hex
    private const int PORT = 5000;
    private const string IP_ADDRESS = "192.168.1.156"; // Adjust as needed

    public KinectStreamer()
    {
        sensor = KinectSensor.GetDefault();
        sensor.Open();
        bodyReader = sensor.BodyFrameSource.OpenReader();
        bodyReader.FrameArrived += BodyFrameArrived;
        faceSource = new FaceFrameSource(sensor, 0, FaceFrameFeatures.RotationOrientation);
        faceReader = faceSource.OpenReader();
        faceReader.FrameArrived += FaceFrameArrived;
        udpClient = new UdpClient();

        for (int i = 0; i < 25; i++)
        {
            latestJointOrientations[i] = new Vector4 { X = 0, Y = 0, Z = 0, W = 1 };
        }

        sendTimer = new Timer(33);
        sendTimer.Elapsed += SendData;
        sendTimer.Start();
    }

    private void BodyFrameArrived(object sender, BodyFrameArrivedEventArgs e)
    {
        using (var frame = e.FrameReference.AcquireFrame())
        {
            if (frame != null)
            {
                var bodies = new Body[frame.BodyCount];
                frame.GetAndRefreshBodyData(bodies);
                bool anyBodyTracked = false;
                foreach (var body in bodies)
                {
                    if (body.IsTracked)
                    {
                        anyBodyTracked = true;
                        lock (lockObject)
                        {
                            for (int i = 0; i < 25; i++)
                            {
                                JointType jointType = (JointType)i;
                                latestJoints[i] = body.Joints[jointType];
                                latestTrackingStates[i] = body.Joints[jointType].TrackingState;
                                latestJointOrientations[i] = body.JointOrientations.ContainsKey(jointType)
                                    ? body.JointOrientations[jointType].Orientation
                                    : new Vector4 { X = 0, Y = 0, Z = 0, W = 1 };
                            }
                            faceSource.TrackingId = body.TrackingId;
                        }
                        break;
                    }
                }
                isTrackingBody = anyBodyTracked;
            }
        }
    }

    private void FaceFrameArrived(object sender, FaceFrameArrivedEventArgs e)
    {
        using (var frame = e.FrameReference.AcquireFrame())
        {
            if (frame != null)
            {
                var result = frame.FaceFrameResult;
                lock (lockObject)
                {
                    latestFaceQuaternion = result != null ? result.FaceRotationQuaternion : new Vector4 { X = 0, Y = 0, Z = 0, W = 1 };
                }
            }
        }
    }

    private void SendData(object sender, ElapsedEventArgs e)
    {
        lock (lockObject)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write(MAGIC_NUMBER);
                    writer.Write(isTrackingBody); // Add tracking status
                    for (int i = 0; i < 25; i++)
                    {
                        var joint = latestJoints[i];
                        var orientation = latestJointOrientations[i];
                        var trackingState = (float)latestTrackingStates[i];
                        writer.Write(joint.Position.X);
                        writer.Write(joint.Position.Y);
                        writer.Write(joint.Position.Z);
                        writer.Write(orientation.X);
                        writer.Write(orientation.Y);
                        writer.Write(orientation.Z);
                        writer.Write(orientation.W);
                        writer.Write(trackingState);
                    }
                    writer.Write(latestFaceQuaternion.X);
                    writer.Write(latestFaceQuaternion.Y);
                    writer.Write(latestFaceQuaternion.Z);
                    writer.Write(latestFaceQuaternion.W);
                }
                var data = ms.ToArray();
                udpClient.Send(data, data.Length, IP_ADDRESS, PORT);
            }
        }
    }

    public void Dispose()
    {
        sendTimer.Stop();
        sendTimer.Dispose();
        bodyReader.Dispose();
        faceReader.Dispose();
        sensor.Close();
        udpClient.Close();
    }
}

class Program
{
    static void Main(string[] args)
    {
        using (var streamer = new KinectStreamer())
        {
            Console.WriteLine("Streaming Kinect data. Press any key to stop...");
            Console.ReadKey();
        }
    }
}