using UnityEngine;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System;

public class KinectRiggerBigger : MonoBehaviour
{
    // UDP Settings
    private const int PORT = 5000;
    private const string IP_ADDRESS = "192.168.1.156"; // Adjust to match your Kinect server
    private const int MAGIC_NUMBER = 0x4B494E54; // "KINT" in hex

    // Animator Component
    private Animator animator;

    // Bone Mappings with Enable/Disable Support
    private BoneMapping[] boneMappings = new BoneMapping[]
    {
        // Left Arm
        new BoneMapping { Bone = HumanBodyBones.LeftUpperArm, KinectJointIndex = 5, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.LeftLowerArm, KinectJointIndex = 6, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.LeftHand, KinectJointIndex = 7, Enabled = true },
        // Right Arm
        new BoneMapping { Bone = HumanBodyBones.RightUpperArm, KinectJointIndex = 9, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.RightLowerArm, KinectJointIndex = 10, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.RightHand, KinectJointIndex = 11, Enabled = true },
        // Left Leg
        new BoneMapping { Bone = HumanBodyBones.LeftUpperLeg, KinectJointIndex = 13, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.LeftLowerLeg, KinectJointIndex = 14, Enabled = true },
        // Right Leg
        new BoneMapping { Bone = HumanBodyBones.RightUpperLeg, KinectJointIndex = 17, Enabled = true },
        new BoneMapping { Bone = HumanBodyBones.RightLowerLeg, KinectJointIndex = 18, Enabled = true },
        // Torso
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
    private float[] latestData = null;
    private readonly object dataLock = new object();

    // Data Structures
    [System.Serializable]
    public struct JointDatagram
    {
        public Vector3 Position;
        public Quaternion Orientation;
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
        public bool Enabled = true; // For GUI enable/disable
    }

    #region Unity Lifecycle Methods

    void Start()
    {
        // Initialize Animator
        animator = GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
        {
            Debug.LogError("Animator component not found or not set to Humanoid!");
            return;
        }

        // Start UDP Receive Thread
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    void LateUpdate()
    {
        // Get the latest data safely
        float[] data;
        lock (dataLock)
        {
            if (latestData == null) return;
            data = latestData;
            latestData = null;
        }

        KinectDatagram datagram = ParseKinectData(data);
        if (datagram == null) return;

        // Process each bone
        foreach (var mapping in boneMappings)
        {
            if (mapping.Enabled)
            {
                Transform boneTransform = animator.GetBoneTransform(mapping.Bone);
                if (boneTransform != null)
                {
                    Quaternion kinectQuat = datagram.Joints[mapping.KinectJointIndex].Orientation;
                    Quaternion transformedQuat = TransformKinectQuaternion(kinectQuat, mapping.Bone);
                    boneTransform.rotation = Quaternion.Slerp(
                        boneTransform.rotation,
                        transformedQuat,
                        smoothingSpeed * Time.deltaTime
                    );
                }
            }
        }

        // Process head if enabled
        if (headEnabled)
        {
            Transform headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headTransform != null)
            {
                Quaternion faceQuat = datagram.FaceQuaternion;
                Quaternion transformedFaceQuat = TransformKinectQuaternion(faceQuat, HumanBodyBones.Head);
                headTransform.rotation = Quaternion.Slerp(
                    headTransform.rotation,
                    transformedFaceQuat,
                    smoothingSpeed * Time.deltaTime
                );
            }
        }
    }

    void OnDestroy()
    {
        isRunning = false;
        if (receiveThread != null)
        {
            receiveThread.Join(100);
            if (receiveThread.IsAlive) receiveThread.Abort();
        }
        if (udpClient != null) udpClient.Close();
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
                System.Net.IPEndPoint remoteEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                if (data.Length != 720) continue;

                using (MemoryStream ms = new MemoryStream(data))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    int magic = reader.ReadInt32();
                    if (magic != MAGIC_NUMBER) continue;

                    float[] floats = new float[179];
                    for (int i = 0; i < 179; i++)
                    {
                        floats[i] = reader.ReadSingle();
                    }

                    lock (dataLock)
                    {
                        latestData = floats;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"UDP receive error: {e.Message}");
        }
        finally
        {
            if (udpClient != null) udpClient.Close();
        }
    }

    #endregion

    #region Quaternion Transformation Logic

    private Quaternion TransformKinectQuaternion(Quaternion kinectQuat, HumanBodyBones bone)
    {
        // Step 1: Get X, Y, Z, W from Kinect and invert Y and Z
        Quaternion transformedQuat = new Quaternion(kinectQuat.x, -kinectQuat.y, -kinectQuat.z, kinectQuat.w);

        // Step 2: Apply additional rotations based on bone type
        if (IsLeftArmBone(bone) || IsRightLegBone(bone))
        {
            transformedQuat = transformedQuat * Quaternion.Euler(0, -90, 0);
        }
        else if (IsRightArmBone(bone) || IsLeftLegBone(bone))
        {
            transformedQuat = transformedQuat * Quaternion.Euler(0, 90, 0);
        }
        // For hips, spine, chest, and head: leave as X, -Y, -Z, W (no additional rotation)

        return transformedQuat;
    }

    // Helper methods to identify bone types
    private bool IsLeftArmBone(HumanBodyBones bone)
    {
        return bone == HumanBodyBones.LeftUpperArm ||
               bone == HumanBodyBones.LeftLowerArm ||
               bone == HumanBodyBones.LeftHand;
    }

    private bool IsRightArmBone(HumanBodyBones bone)
    {
        return bone == HumanBodyBones.RightUpperArm ||
               bone == HumanBodyBones.RightLowerArm ||
               bone == HumanBodyBones.RightHand;
    }

    private bool IsLeftLegBone(HumanBodyBones bone)
    {
        return bone == HumanBodyBones.LeftUpperLeg ||
               bone == HumanBodyBones.LeftLowerLeg;
    }

    private bool IsRightLegBone(HumanBodyBones bone)
    {
        return bone == HumanBodyBones.RightUpperLeg ||
               bone == HumanBodyBones.RightLowerLeg;
    }

    #endregion

    #region Data Parsing

    private KinectDatagram ParseKinectData(float[] data)
    {
        if (data == null || data.Length != 179)
        {
            Debug.LogError("Invalid Kinect data length");
            return null;
        }

        KinectDatagram datagram = new KinectDatagram();

        // Parse joint data
        for (int i = 0; i < 25; i++)
        {
            int startIdx = i * 7;
            Vector3 position = new Vector3(data[startIdx], data[startIdx + 1], data[startIdx + 2]);
            Quaternion orientation = new Quaternion(
                data[startIdx + 3],
                data[startIdx + 4],
                data[startIdx + 5],
                data[startIdx + 6]
            );
            datagram.Joints[i] = new JointDatagram { Position = position, Orientation = orientation };
        }

        // Parse face quaternion
        datagram.FaceQuaternion = new Quaternion(data[175], data[176], data[177], data[178]);

        return datagram;
    }

    #endregion
}