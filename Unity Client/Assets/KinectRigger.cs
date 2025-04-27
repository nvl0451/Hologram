using UnityEngine;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System;

public class KinectRigger : MonoBehaviour
{
    // UDP Settings
    private const int PORT = 5000;
    private const string IP_ADDRESS = "192.168.1.158"; // Adjust to match server
    private const int MAGIC_NUMBER = 0x4B494E54; // "KINT" in hex

    // Animator
    private Animator animator;

    // Bone Mappings
    private BoneMapping[] boneMappings = new BoneMapping[]
    {

        new BoneMapping { Bone = HumanBodyBones.LeftUpperArm, KinectJointIndex = 5, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.LeftLowerArm, KinectJointIndex = 6, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.LeftHand, KinectJointIndex = 7, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.RightUpperArm, KinectJointIndex = 9, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.RightLowerArm, KinectJointIndex = 10, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.RightHand, KinectJointIndex = 11, Enabled = false },


        new BoneMapping { Bone = HumanBodyBones.LeftUpperLeg, KinectJointIndex = 13, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.LeftLowerLeg, KinectJointIndex = 14, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.LeftFoot, KinectJointIndex = 15, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.RightUpperLeg, KinectJointIndex = 17, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.RightLowerLeg, KinectJointIndex = 18, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.RightFoot, KinectJointIndex = 19, Enabled = false },
        
        new BoneMapping { Bone = HumanBodyBones.LeftToes, KinectJointIndex = 15, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.RightToes, KinectJointIndex = 19, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.Hips, KinectJointIndex = 0, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.Spine, KinectJointIndex = 1, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.Chest, KinectJointIndex = 20, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.Neck, KinectJointIndex = 2, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.LeftShoulder, KinectJointIndex = 4, Enabled = false },
        new BoneMapping { Bone = HumanBodyBones.RightShoulder, KinectJointIndex = 8, Enabled = false }
    };

    [SerializeField] private bool headEnabled = true;
    [SerializeField, Range(1f, 20f)] private float smoothingSpeed = 10f;

    // Permutation Management
    private string[] permutations = new string[]
    {
        "XYZW", "XYWZ", "XZYW", "XZWY", "XWYZ", "XWZY",
        "YXZW", "YXWZ", "YZXW", "YZWX", "YWXZ", "YWZX",
        "ZXYW", "ZXWY", "ZYXW", "ZYWX", "ZWXY", "ZWYX",
        "WXYZ", "WXZY", "WYXZ", "WYZX", "WZXY", "WZYX"
    };
    private int selectedBoneIndex = 0;

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
        public bool Enabled = true;
        public string Permutation = "XYZW";
    }

    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
        {
            Debug.LogError("Animator component not found or not set to Humanoid!");
            return;
        }

        // Start UDP thread
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

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

    void Update()
    {
        if (boneMappings.Length == 0) return;

        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            selectedBoneIndex = (selectedBoneIndex - 1 + boneMappings.Length) % boneMappings.Length;
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            selectedBoneIndex = (selectedBoneIndex + 1) % boneMappings.Length;
        }
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            CyclePermutation(-1);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            CyclePermutation(1);
        }
        else if (Input.GetKeyDown(KeyCode.Space))
        {
            ToggleSelectedBoneEnabled();
        }
    }

    void LateUpdate()
    {
        float[] data;
        lock (dataLock)
        {
            if (latestData == null) return;
            data = latestData;
            latestData = null;
        }

        KinectDatagram datagram = ParseKinectData(data);
        if (datagram == null) return;

        foreach (var mapping in boneMappings)
        {
            if (mapping.Enabled)
            {
                Transform boneTransform = animator.GetBoneTransform(mapping.Bone);
                if (boneTransform != null)
                {
                    Quaternion kinectQuat = datagram.Joints[mapping.KinectJointIndex].Orientation;
                    Quaternion permutedQuat = ApplyPermutation(kinectQuat, mapping.Permutation);
                    boneTransform.rotation = Quaternion.Slerp(
                        boneTransform.rotation,
                        permutedQuat,
                        smoothingSpeed * Time.deltaTime
                    );
                }
            }
        }

        if (headEnabled)
        {
            Transform headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headTransform != null)
            {
                Quaternion faceQuat = datagram.FaceQuaternion;
                Quaternion permutedFaceQuat = ApplyPermutation(faceQuat, "ZWXY");
                headTransform.rotation = Quaternion.Slerp(
                    headTransform.rotation,
                    permutedFaceQuat,
                    smoothingSpeed * Time.deltaTime
                );
            }
        }
    }

    void OnGUI()
    {
        if (boneMappings.Length == 0) return;

        BoneMapping selectedMapping = boneMappings[selectedBoneIndex];
        string boneName = selectedMapping.Bone.ToString();
        string statusText = selectedMapping.Enabled ? "" : " (disabled)";
        string permutation = selectedMapping.Permutation;

        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.fontSize = 20;

        GUI.Label(new Rect(Screen.width - 300, 10, 300, 30), $"Bone: {boneName}{statusText}", style);
        GUI.Label(new Rect(Screen.width - 300, 40, 300, 30), $"Permutation: {permutation}", style);
    }

    private void CyclePermutation(int direction)
    {
        if (boneMappings.Length == 0) return;

        BoneMapping selectedMapping = boneMappings[selectedBoneIndex];
        int currentIndex = Array.IndexOf(permutations, selectedMapping.Permutation);
        if (currentIndex == -1) currentIndex = 0;
        int newIndex = (currentIndex + direction + permutations.Length) % permutations.Length;
        selectedMapping.Permutation = permutations[newIndex];
    }

    private void ToggleSelectedBoneEnabled()
    {
        if (boneMappings.Length == 0) return;

        BoneMapping selectedMapping = boneMappings[selectedBoneIndex];
        selectedMapping.Enabled = !selectedMapping.Enabled;
    }

    private Quaternion ApplyPermutation(Quaternion q, string permutation)
    {
        float[] components = new float[4] { q.x, q.y, q.z, q.w };
        char[] permChars = permutation.ToUpper().ToCharArray();
        float[] permuted = new float[4];

        for (int i = 0; i < 4; i++)
        {
            char c = permChars[i];
            switch (c)
            {
                case 'X': permuted[i] = components[0]; break;
                case 'Y': permuted[i] = components[1]; break;
                case 'Z': permuted[i] = components[2]; break;
                case 'W': permuted[i] = components[3]; break;
                default: throw new ArgumentException("Invalid permutation character");
            }
        }
        Quaternion qq = new Quaternion(permuted[0], -permuted[1], -permuted[2], permuted[3]);
        Quaternion p = Quaternion.Euler(180, 0, 0); // 180° rotation around y-axis
        Quaternion q_adjusted = p * qq * Quaternion.Inverse(p);

        return qq * Quaternion.Euler(0, -90, 0);
    }

    private KinectDatagram ParseKinectData(float[] data)
    {
        if (data == null || data.Length != 179)
        {
            Debug.LogError("Invalid Kinect data length");
            return null;
        }

        KinectDatagram datagram = new KinectDatagram();

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

        datagram.FaceQuaternion = new Quaternion(data[175], data[176], data[177], data[178]);

        return datagram;
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
}