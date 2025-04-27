using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System;

public class KinectAnimator : MonoBehaviour
{
    // Smoothing controls for each part
    [SerializeField, Range(1f, 20f)] private float upperArmLerpSpeed = 10f;  // Upper arms
    [SerializeField, Range(1f, 20f)] private float lowerArmLerpSpeed = 5f;   // Lower arms
    [SerializeField, Range(1f, 20f)] private float handLerpSpeed = 5f;       // Hands
    [SerializeField, Range(1f, 20f)] private float neckLerpSpeed = 10f;      // Neck
    [SerializeField, Range(1f, 20f)] private float headLerpSpeed = 10f;      // Head

    // Network and animator components
    private TcpClient client;
    private NetworkStream stream;
    private Animator animator;
    private byte[] buffer = new byte[4096];
    private List<byte> dataBuffer = new List<byte>();

    // Arm transforms and target rotations
    private Transform upperArmRightTransform;
    private Quaternion upperArmRightTargetRotation;
    private Transform lowerArmRightTransform;
    private Quaternion lowerArmRightTargetRotation;
    private Transform handRightTransform;
    private Quaternion handRightTargetRotation;
    private Transform upperArmLeftTransform;
    private Quaternion upperArmLeftTargetRotation;
    private Transform lowerArmLeftTransform;
    private Quaternion lowerArmLeftTargetRotation;
    private Transform handLeftTransform;
    private Quaternion handLeftTargetRotation;

    // Neck and head transforms and target rotations
    private Transform neckTransform;
    private Quaternion neckTargetRotation;
    private Transform headTransform;
    private Quaternion headTargetRotation;

    // Toggle for lower arm and hand tracking (arms only)
    private bool isArmTrackingEnabled = true;

    // Permutation handling
    private List<List<string>> permutations;
    private int currentPermIndex;

    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("Animator component not found!");
            return;
        }

        // Cache arm transforms
        upperArmRightTransform = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (upperArmRightTransform != null) upperArmRightTargetRotation = upperArmRightTransform.rotation;
        lowerArmRightTransform = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        if (lowerArmRightTransform != null) lowerArmRightTargetRotation = lowerArmRightTransform.rotation;
        handRightTransform = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (handRightTransform != null) handRightTargetRotation = handRightTransform.rotation;
        upperArmLeftTransform = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        if (upperArmLeftTransform != null) upperArmLeftTargetRotation = upperArmLeftTransform.rotation;
        lowerArmLeftTransform = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        if (lowerArmLeftTransform != null) lowerArmLeftTargetRotation = lowerArmLeftTransform.rotation;
        handLeftTransform = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (handLeftTransform != null) handLeftTargetRotation = handLeftTransform.rotation;

        // Cache neck and head transforms
        neckTransform = animator.GetBoneTransform(HumanBodyBones.Neck);
        if (neckTransform != null) neckTargetRotation = neckTransform.rotation;
        else Debug.LogError("Neck bone not found!");
        headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
        if (headTransform != null) headTargetRotation = headTransform.rotation;
        else Debug.LogError("Head bone not found!");

        // Generate permutations and set "Z W X Y" as default
        List<string> components = new List<string> { "x", "y", "z", "w" };
        permutations = GeneratePermutations(components);
        int defaultPermIndex = permutations.FindIndex(p => p[0] == "z" && p[1] == "w" && p[2] == "x" && p[3] == "y");
        if (defaultPermIndex != -1) currentPermIndex = defaultPermIndex;
        else Debug.LogError("Default permutation Z W X Y not found!");

        ConnectToServer();
    }

    void ConnectToServer()
    {
        try
        {
            client = new TcpClient("192.168.1.160", 9999); // Update IP as needed
            stream = client.GetStream();
            Debug.Log("Connected to Python server.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Connection failed: {e.Message}");
        }
    }

    void Update()
    {
        // Cycle permutations with arrow keys
        if (Input.GetKeyDown(KeyCode.LeftArrow))
            currentPermIndex = (currentPermIndex - 1 + permutations.Count) % permutations.Count;
        else if (Input.GetKeyDown(KeyCode.RightArrow))
            currentPermIndex = (currentPermIndex + 1) % permutations.Count;

        // Update upper arms (always tracking)
        if (upperArmRightTransform != null)
            upperArmRightTransform.rotation = Quaternion.Slerp(upperArmRightTransform.rotation, upperArmRightTargetRotation, upperArmLerpSpeed * Time.deltaTime);
        if (upperArmLeftTransform != null)
            upperArmLeftTransform.rotation = Quaternion.Slerp(upperArmLeftTransform.rotation, upperArmLeftTargetRotation, upperArmLerpSpeed * Time.deltaTime);

        // Update lower arms and hands based on toggle
        if (isArmTrackingEnabled)
        {
            if (lowerArmRightTransform != null)
                lowerArmRightTransform.rotation = Quaternion.Slerp(lowerArmRightTransform.rotation, lowerArmRightTargetRotation, lowerArmLerpSpeed * Time.deltaTime);
            if (handRightTransform != null)
                handRightTransform.rotation = Quaternion.Slerp(handRightTransform.rotation, handRightTargetRotation, handLerpSpeed * Time.deltaTime);
            if (lowerArmLeftTransform != null)
                lowerArmLeftTransform.rotation = Quaternion.Slerp(lowerArmLeftTransform.rotation, lowerArmLeftTargetRotation, lowerArmLerpSpeed * Time.deltaTime);
            if (handLeftTransform != null)
                handLeftTransform.rotation = Quaternion.Slerp(handLeftTransform.rotation, handLeftTargetRotation, handLerpSpeed * Time.deltaTime);
        }
        else
        {
            // Align lower arms and hands to upper arms
            if (lowerArmRightTransform != null)
                lowerArmRightTransform.rotation = Quaternion.Slerp(lowerArmRightTransform.rotation, upperArmRightTransform.rotation, lowerArmLerpSpeed * Time.deltaTime);
            if (handRightTransform != null)
                handRightTransform.rotation = Quaternion.Slerp(handRightTransform.rotation, lowerArmRightTransform.rotation, handLerpSpeed * Time.deltaTime);
            if (lowerArmLeftTransform != null)
                lowerArmLeftTransform.rotation = Quaternion.Slerp(lowerArmLeftTransform.rotation, upperArmLeftTransform.rotation, lowerArmLerpSpeed * Time.deltaTime);
            if (handLeftTransform != null)
                handLeftTransform.rotation = Quaternion.Slerp(handLeftTransform.rotation, lowerArmLeftTransform.rotation, handLerpSpeed * Time.deltaTime);
        }

        // Always update neck and head
        if (neckTransform != null)
            neckTransform.rotation = Quaternion.Slerp(neckTransform.rotation, neckTargetRotation, neckLerpSpeed * Time.deltaTime);
        if (headTransform != null)
            headTransform.rotation = Quaternion.Slerp(headTransform.rotation, headTargetRotation, headLerpSpeed * Time.deltaTime);

        // Handle network data
        if (stream == null || !client.Connected) return;
        try
        {
            if (stream.DataAvailable)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                lock (dataBuffer)
                {
                    dataBuffer.AddRange(new ArraySegment<byte>(buffer, 0, bytesRead));
                }
            }

            lock (dataBuffer)
            {
                while (dataBuffer.Count >= 4)
                {
                    int length = (dataBuffer[0] << 24) | (dataBuffer[1] << 16) | (dataBuffer[2] << 8) | dataBuffer[3];
                    if (dataBuffer.Count < 4 + length) break;
                    byte[] jsonBytes = dataBuffer.GetRange(4, length).ToArray();
                    dataBuffer.RemoveRange(0, 4 + length);
                    string jsonData = Encoding.UTF8.GetString(jsonBytes);
                    ApplyQuaternionData(jsonData);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Network read error: {e.Message}");
        }
    }

    void ApplyQuaternionData(string jsonData)
    {
        try
        {
            JointDataWrapper wrapper = JsonUtility.FromJson<JointDataWrapper>(jsonData);
            foreach (var joint in wrapper.joints)
            {
                List<string> currentPerm = permutations[currentPermIndex];
                Quaternion kinectQuat = GetQuaternionFromPermutation(currentPerm, joint.orientation);

                switch (joint.name)
                {
                    case "UpperArmRight":
                        if (upperArmRightTransform != null) upperArmRightTargetRotation = kinectQuat; break;
                    case "LowerArmRight":
                        if (lowerArmRightTransform != null) lowerArmRightTargetRotation = kinectQuat; break;
                    case "HandRight":
                        if (handRightTransform != null) handRightTargetRotation = kinectQuat; break;
                    case "UpperArmLeft":
                        if (upperArmLeftTransform != null) upperArmLeftTargetRotation = kinectQuat; break;
                    case "LowerArmLeft":
                        if (lowerArmLeftTransform != null) lowerArmLeftTargetRotation = kinectQuat; break;
                    case "HandLeft":
                        if (handLeftTransform != null) handLeftTargetRotation = kinectQuat; break;
                    case "Neck":
                        if (neckTransform != null) neckTargetRotation = kinectQuat; break;
                    case "Head":
                        if (headTransform != null) headTargetRotation = kinectQuat; break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"JSON parse error: {e.Message}");
        }
    }

    private Quaternion GetQuaternionFromPermutation(List<string> perm, OrientationData ori)
    {
        float x = GetComponentValue(perm[0], ori);
        float y = GetComponentValue(perm[1], ori);
        float z = GetComponentValue(perm[2], ori);
        float w = GetComponentValue(perm[3], ori);
        return new Quaternion(x, y, z, w);
    }

    private float GetComponentValue(string component, OrientationData ori)
    {
        switch (component)
        {
            case "x": return ori.x;
            case "y": return ori.y;
            case "z": return ori.z;
            case "w": return ori.w;
            default: return 0;
        }
    }

    private List<List<string>> GeneratePermutations(List<string> items)
    {
        if (items.Count == 1)
        {
            return new List<List<string>> { new List<string> { items[0] } };
        }

        List<List<string>> perms = new List<List<string>>();
        for (int i = 0; i < items.Count; i++)
        {
            string first = items[i];
            List<string> remaining = new List<string>(items);
            remaining.RemoveAt(i);
            List<List<string>> subPerms = GeneratePermutations(remaining);
            foreach (var subPerm in subPerms)
            {
                List<string> perm = new List<string> { first };
                perm.AddRange(subPerm);
                perms.Add(perm);
            }
        }
        return perms;
    }

    void OnGUI()
    {
        GUI.color = Color.black;
        GUILayout.BeginArea(new Rect(Screen.width - 200, 10, 200, 100));
        string displayText = string.Join(" ", permutations[currentPermIndex].ConvertAll(s => s.ToUpper()));
        GUILayout.Label("Permutation: " + displayText);
        isArmTrackingEnabled = GUILayout.Toggle(isArmTrackingEnabled, "Enable Lower Arm and Hand Tracking");
        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        if (stream != null) stream.Close();
        if (client != null) client.Close();
        Debug.Log("Disconnected from server.");
    }
}

[System.Serializable]
public class OrientationData
{
    public float x, y, z, w;
}

[System.Serializable]
public class JointData
{
    public string name;
    public OrientationData orientation;
}

[System.Serializable]
public class JointDataWrapper
{
    public List<JointData> joints;
}