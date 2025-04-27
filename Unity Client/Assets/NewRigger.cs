using UnityEngine;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System;

public class KinectArmAnimator : MonoBehaviour
{
    // **UDP Settings**
    private const int PORT = 5000; // Match your server's port
    private const string IP_ADDRESS = "192.168.1.158"; // Server IP
    private const int MAGIC_NUMBER = 0x4B494E54; // "KINT" in hex

    // **Animator and Transforms**
    private Animator animator;
    private Transform upperArmRightTransform;
    private Transform lowerArmRightTransform;
    private Transform handRightTransform;
    private Transform upperArmLeftTransform;
    private Transform lowerArmLeftTransform;
    private Transform handLeftTransform;
    private Transform headTransform;

    // **Smoothing Speeds**
    [SerializeField, Range(1f, 20f)] private float upperArmLerpSpeed = 10f;
    [SerializeField, Range(1f, 20f)] private float lowerArmLerpSpeed = 5f;
    [SerializeField, Range(1f, 20f)] private float handLerpSpeed = 5f;
    [SerializeField, Range(1f, 20f)] private float headLerpSpeed = 10f;

    // **Target Rotations**
    private Quaternion upperArmRightTargetRotation;
    private Quaternion lowerArmRightTargetRotation;
    private Quaternion handRightTargetRotation;
    private Quaternion upperArmLeftTargetRotation;
    private Quaternion lowerArmLeftTargetRotation;
    private Quaternion handLeftTargetRotation;
    private Quaternion headTargetRotation;

    // **UDP Client and Thread**
    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool isRunning = true;

    // **Latest Data with Thread Safety**
    private float[] latestData = null;
    private readonly object dataLock = new object();

    // **Start Method - Initialize Everything**
    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
        {
            Debug.LogError("Animator component not found or not set to Humanoid! Fix it.");
            return;
        }

        // Get transforms
        upperArmRightTransform = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        lowerArmRightTransform = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        handRightTransform = animator.GetBoneTransform(HumanBodyBones.RightHand);
        upperArmLeftTransform = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        lowerArmLeftTransform = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        handLeftTransform = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        headTransform = animator.GetBoneTransform(HumanBodyBones.Head);

        // Set initial rotations and check for nulls
        if (upperArmRightTransform != null) upperArmRightTargetRotation = upperArmRightTransform.rotation;
        else Debug.LogWarning("Right Upper Arm transform not found!");
        if (lowerArmRightTransform != null) lowerArmRightTargetRotation = lowerArmRightTransform.rotation;
        else Debug.LogWarning("Right Lower Arm transform not found!");
        if (handRightTransform != null) handRightTargetRotation = handRightTransform.rotation;
        else Debug.LogWarning("Right Hand transform not found!");
        if (upperArmLeftTransform != null) upperArmLeftTargetRotation = upperArmLeftTransform.rotation;
        else Debug.LogWarning("Left Upper Arm transform not found!");
        if (lowerArmLeftTransform != null) lowerArmLeftTargetRotation = lowerArmLeftTransform.rotation;
        else Debug.LogWarning("Left Lower Arm transform not found!");
        if (handLeftTransform != null) handLeftTargetRotation = handLeftTransform.rotation;
        else Debug.LogWarning("Left Hand transform not found!");
        if (headTransform != null) headTargetRotation = headTransform.rotation;
        else Debug.LogWarning("Head transform not found!");

        // Start UDP thread
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    // **Receive UDP Data**
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

    // **Convert Kinect to Unity Quaternion**
    private Quaternion ConvertKinectToUnity(Quaternion kinectQuat)
    {
        return new Quaternion(kinectQuat.z, kinectQuat.w, kinectQuat.x, kinectQuat.y);
    }

    // **LateUpdate - Apply Rotations**
    void LateUpdate()
    {
        float[] data;
        lock (dataLock)
        {
            if (latestData == null) return;
            data = latestData;
        }

        // Arm joint indices
        int elbowLeftIdx = 5 * 7;   // 35 - Left Elbow
        int wristLeftIdx = 6 * 7;   // 42 - Left Wrist
        int handLeftIdx = 7 * 7;    // 49 - Left Hand
        int elbowRightIdx = 9 * 7;  // 63 - Right Elbow
        int wristRightIdx = 10 * 7; // 70 - Right Wrist
        int handRightIdx = 11 * 7;  // 77 - Right Hand

        // Extract arm quaternions
        Quaternion elbowLeftQuat = new Quaternion(
            data[elbowLeftIdx + 3], data[elbowLeftIdx + 4], data[elbowLeftIdx + 5], data[elbowLeftIdx + 6]);
        Quaternion wristLeftQuat = new Quaternion(
            data[wristLeftIdx + 3], data[wristLeftIdx + 4], data[wristLeftIdx + 5], data[wristLeftIdx + 6]);
        Quaternion handLeftQuat = new Quaternion(
            data[handLeftIdx + 3], data[handLeftIdx + 4], data[handLeftIdx + 5], data[handLeftIdx + 6]);
        Quaternion elbowRightQuat = new Quaternion(
            data[elbowRightIdx + 3], data[elbowRightIdx + 4], data[elbowRightIdx + 5], data[elbowRightIdx + 6]);
        Quaternion wristRightQuat = new Quaternion(
            data[wristRightIdx + 3], data[wristRightIdx + 4], data[wristRightIdx + 5], data[wristRightIdx + 6]);
        Quaternion handRightQuat = new Quaternion(
            data[handRightIdx + 3], data[handRightIdx + 4], data[handRightIdx + 5], data[handRightIdx + 6]);

        // Set arm target rotations
        if (upperArmLeftTransform != null)
            upperArmLeftTargetRotation = ConvertKinectToUnity(elbowLeftQuat);
        if (lowerArmLeftTransform != null)
            lowerArmLeftTargetRotation = ConvertKinectToUnity(wristLeftQuat);
        if (handLeftTransform != null)
            handLeftTargetRotation = ConvertKinectToUnity(handLeftQuat);
        if (upperArmRightTransform != null)
            upperArmRightTargetRotation = ConvertKinectToUnity(elbowRightQuat);
        if (lowerArmRightTransform != null)
            lowerArmRightTargetRotation = ConvertKinectToUnity(wristRightQuat);
        if (handRightTransform != null)
            handRightTargetRotation = ConvertKinectToUnity(handRightQuat);

        // Set head rotation from face quaternion
        if (headTransform != null)
        {
            Quaternion faceQuat = new Quaternion(data[175], data[176], data[177], data[178]);
            headTargetRotation = ConvertKinectToUnity(faceQuat);
        }

        // Apply smoothed rotations
        if (upperArmLeftTransform != null)
            upperArmLeftTransform.rotation = Quaternion.Slerp(
                upperArmLeftTransform.rotation, upperArmLeftTargetRotation, upperArmLerpSpeed * Time.deltaTime);
        if (lowerArmLeftTransform != null)
            lowerArmLeftTransform.rotation = Quaternion.Slerp(
                lowerArmLeftTransform.rotation, lowerArmLeftTargetRotation, lowerArmLerpSpeed * Time.deltaTime);
        if (handLeftTransform != null)
            handLeftTransform.rotation = Quaternion.Slerp(
                handLeftTransform.rotation, handLeftTargetRotation, handLerpSpeed * Time.deltaTime);
        if (upperArmRightTransform != null)
            upperArmRightTransform.rotation = Quaternion.Slerp(
                upperArmRightTransform.rotation, upperArmRightTargetRotation, upperArmLerpSpeed * Time.deltaTime);
        if (lowerArmRightTransform != null)
            lowerArmRightTransform.rotation = Quaternion.Slerp(
                lowerArmRightTransform.rotation, lowerArmRightTargetRotation, lowerArmLerpSpeed * Time.deltaTime);
        if (handRightTransform != null)
            handRightTransform.rotation = Quaternion.Slerp(
                handRightTransform.rotation, handRightTargetRotation, handLerpSpeed * Time.deltaTime);
        if (headTransform != null)
            headTransform.rotation = Quaternion.Slerp(
                headTransform.rotation, headTargetRotation, headLerpSpeed * Time.deltaTime);
    }

    // **Cleanup**
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