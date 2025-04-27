using UnityEngine;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Net;

public class KinectRigger2 : MonoBehaviour
{
    // UDP Settings
    private const int PORT = 5555;
    private const string IP_ADDRESS = "127.0.0.1";
    private const int MAGIC_NUMBER = 0x4B494E54;

    [SerializeField] private bool debugOutput = false; // Toggle for debug logs

    // Animator Component
    private Animator animator;

    // Bone Mappings with Enable/Disable Support
    private BoneMapping[] boneMappings = new BoneMapping[]
    {
        new BoneMapping { Bone = HumanBodyBones.LeftUpperArm, KinectJointIndex = 5, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.LeftLowerArm, KinectJointIndex = 6, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.LeftHand, KinectJointIndex = 7, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.RightUpperArm, KinectJointIndex = 9, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.RightLowerArm, KinectJointIndex = 10, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.RightHand, KinectJointIndex = 11, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.LeftUpperLeg, KinectJointIndex = 13, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.LeftLowerLeg, KinectJointIndex = 14, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.RightUpperLeg, KinectJointIndex = 17, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.RightLowerLeg, KinectJointIndex = 18, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.Hips, KinectJointIndex = 0, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.Spine, KinectJointIndex = 1, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.Chest, KinectJointIndex = 20, Enabled = true }
    };

    // Head Control
    [SerializeField] private bool headEnabled = true;
    [SerializeField, Range(1f, 20f)] private float smoothingSpeed = 10f;

    // UDP Client and Thread
    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool isRunning = true;

    // Latest Data
    private byte[] latestData = null;
    private readonly object dataLock = new object();

    // T-Pose Orientations
    private Dictionary<HumanBodyBones, Quaternion> tPoseOrientations = new Dictionary<HumanBodyBones, Quaternion>();
    private bool isLockTPose = false;
    private float lastDataTime = -999f; // Time when last valid data was applied

    // Data Structures
    [System.Serializable]
    public struct JointDatagram
    {
        public Vector3 Position;
        public Quaternion Orientation;
        public float TrackingState;
    }

    [System.Serializable]
    public class KinectDatagram
    {
        public JointDatagram[] Joints { get; set; } = new JointDatagram[25];
        public Quaternion FaceQuaternion { get; set; }
    }

    [System.Serializable]
    public class BoneMapping
    {
        public HumanBodyBones Bone;
        public int KinectJointIndex;
        public bool Enabled = true;
    }

    #region Unity Lifecycle Methods

    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
        {
            Debug.LogError("Animator component not found or not set to Humanoid!");
            return;
        }

        StartCoroutine(CaptureTPoseAfterDelay(1f));
        receiveThread = new Thread(ReceiveData)
        {
            IsBackground = true
        };
        receiveThread.Start();
    }

    void LateUpdate()
    {
        if (tPoseOrientations.Count == 0)
        {
            if (debugOutput) Debug.Log("Unity: T-pose orientations not captured yet.");
            return;
        }

        byte[] data;
        lock (dataLock)
        {
            data = latestData;
            latestData = null;
        }

        if (data != null)
        {
            if (data.Length == 816) // Retransmitted kinect data
            {
                if (debugOutput) Debug.Log("Unity: Received kinect data (816 bytes).");
                if (!isLockTPose)
                {
                    KinectDatagram datagram = ParseKinectData(data);
                    if (datagram != null)
                    {
                        ApplyKinectData(datagram);
                        lastDataTime = Time.time;
                    }
                    else
                    {
                        if (debugOutput) Debug.Log("Unity: Failed to parse kinect data.");
                    }
                }
            }
            else // Control message
            {
                string message = System.Text.Encoding.UTF8.GetString(data).Trim();
                if (debugOutput) Debug.Log($"Unity: Received control message: '{message}'");

                if (message == "lock T-pose")
                {
                    isLockTPose = true;
                    if (debugOutput) Debug.Log("Unity: Locking T-pose.");
                }
                else if (message == "unlock T-pose")
                {
                    isLockTPose = false;
                    if (debugOutput) Debug.Log("Unity: Unlocking T-pose.");
                }
                else
                {
                    if (debugOutput) Debug.Log("Unity: Unknown control message.");
                }
            }
        }
        else if (!isLockTPose && (Time.time - lastDataTime) > 2f)
        {
            if (debugOutput) Debug.Log("Unity: No data received for 2 seconds, applying T-pose.");
            ApplyTPose();
        }

        if (isLockTPose)
        {
            ApplyTPose();
        }
    }

    void OnDestroy()
    {
        isRunning = false;
        receiveThread?.Join(100);
        if (receiveThread?.IsAlive ?? false) receiveThread.Abort();
        udpClient?.Close();
    }

    #endregion

    #region UDP Data Receiving

    private void ReceiveData()
    {
        try
        {
            udpClient = new UdpClient(PORT);
            while (isRunning)
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                if (debugOutput) Debug.Log($"Unity: Received {data.Length} bytes from {remoteEndPoint}");
                lock (dataLock)
                {
                    latestData = data;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"UDP receive error: {e.Message}");
        }
        finally
        {
            udpClient?.Close();
        }
    }

    #endregion

    #region Quaternion Transformation Logic

    private Quaternion TransformKinectQuaternion(Quaternion kinectQuat, HumanBodyBones bone)
    {
        Quaternion transformedQuat = new Quaternion(kinectQuat.x, -kinectQuat.y, -kinectQuat.z, kinectQuat.w);
        if (IsLeftArmBone(bone) || IsRightLegBone(bone)) transformedQuat *= Quaternion.Euler(0, -90, 0);
        else if (IsRightArmBone(bone) || IsLeftLegBone(bone)) transformedQuat *= Quaternion.Euler(0, 90, 0);
        return transformedQuat;
    }

    private bool IsLeftArmBone(HumanBodyBones bone) => bone == HumanBodyBones.LeftUpperArm || bone == HumanBodyBones.LeftLowerArm || bone == HumanBodyBones.LeftHand;
    private bool IsRightArmBone(HumanBodyBones bone) => bone == HumanBodyBones.RightUpperArm || bone == HumanBodyBones.RightLowerArm || bone == HumanBodyBones.RightHand;
    private bool IsLeftLegBone(HumanBodyBones bone) => bone == HumanBodyBones.LeftUpperLeg || bone == HumanBodyBones.LeftLowerLeg;
    private bool IsRightLegBone(HumanBodyBones bone) => bone == HumanBodyBones.RightUpperLeg || bone == HumanBodyBones.RightLowerLeg;

    #endregion

    #region T-Pose Capture

    private IEnumerator CaptureTPoseAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        foreach (var mapping in boneMappings)
        {
            Transform boneTransform = animator.GetBoneTransform(mapping.Bone);
            if (boneTransform != null) tPoseOrientations[mapping.Bone] = boneTransform.rotation;
        }
        if (headEnabled)
        {
            Transform headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headTransform != null) tPoseOrientations[HumanBodyBones.Head] = headTransform.rotation;
        }
        Debug.Log("T-Pose rotations captured.");
    }

    private void ApplyTPose()
    {
        foreach (var mapping in boneMappings)
        {
            if (mapping.Enabled)
            {
                Transform boneTransform = animator.GetBoneTransform(mapping.Bone);
                if (boneTransform != null && tPoseOrientations.ContainsKey(mapping.Bone))
                {
                    boneTransform.rotation = Quaternion.Slerp(boneTransform.rotation, tPoseOrientations[mapping.Bone], smoothingSpeed * Time.deltaTime);
                }
            }
        }
        if (headEnabled)
        {
            Transform headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headTransform != null && tPoseOrientations.ContainsKey(HumanBodyBones.Head))
            {
                headTransform.rotation = Quaternion.Slerp(headTransform.rotation, tPoseOrientations[HumanBodyBones.Head], smoothingSpeed * Time.deltaTime);
            }
        }
    }

    #endregion

    #region Data Parsing and Application

    private void ApplyKinectData(KinectDatagram datagram)
    {
        bool dataApplied = false;
        foreach (var mapping in boneMappings)
        {
            if (mapping.Enabled)
            {
                Transform boneTransform = animator.GetBoneTransform(mapping.Bone);
                if (boneTransform != null)
                {
                    JointDatagram jointData = datagram.Joints[mapping.KinectJointIndex];
                    if (jointData.TrackingState == 2.0f) // Fully tracked
                    {
                        Quaternion kinectQuat = jointData.Orientation;
                        Quaternion transformedQuat = TransformKinectQuaternion(kinectQuat, mapping.Bone);
                        boneTransform.rotation = Quaternion.Slerp(boneTransform.rotation, transformedQuat, smoothingSpeed * Time.deltaTime);
                        dataApplied = true;
                        if (debugOutput) Debug.Log($"Unity: Applied rotation to {mapping.Bone}");
                    }
                    // Else, do nothing and hold current rotation
                }
            }
        }

        if (headEnabled)
        {
            Transform headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headTransform != null)
            {
                Quaternion faceQuat = datagram.FaceQuaternion;
                Quaternion transformedFaceQuat = TransformKinectQuaternion(faceQuat, HumanBodyBones.Head);
                headTransform.rotation = Quaternion.Slerp(headTransform.rotation, transformedFaceQuat, smoothingSpeed * Time.deltaTime);
                dataApplied = true;
                if (debugOutput) Debug.Log("Unity: Applied rotation to Head");
            }
        }

        if (!dataApplied && debugOutput) Debug.Log("Unity: No valid tracking data applied this frame.");
    }

    private KinectDatagram ParseKinectData(byte[] data)
    {
        if (data == null || data.Length != 816)
        {
            Debug.LogError($"Invalid Kinect data length: Expected 816, got {data?.Length ?? 0}");
            return null;
        }

        using (var ms = new MemoryStream(data))
        using (var reader = new BinaryReader(ms))
        {
            KinectDatagram datagram = new KinectDatagram();
            try
            {
                for (int i = 0; i < 25; i++)
                {
                    Vector3 position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    Quaternion orientation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    float trackingState = reader.ReadSingle();
                    datagram.Joints[i] = new JointDatagram { Position = position, Orientation = orientation, TrackingState = trackingState };
                }
                datagram.FaceQuaternion = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if (debugOutput) Debug.Log("Unity: Parsed kinect data successfully.");
                return datagram;
            }
            catch (Exception e)
            {
                Debug.LogError($"Unity: Error parsing kinect data: {e.Message}");
                return null;
            }
        }
    }

    #endregion
}