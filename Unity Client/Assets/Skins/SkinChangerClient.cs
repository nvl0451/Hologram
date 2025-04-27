using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;
using System;

public class SkinChangerClient : MonoBehaviour
{
    // Serialized fields for the Inspector
    [SerializeField] private SkinnedMeshRenderer skinnedMeshRenderer; // Reference to char1's SkinnedMeshRenderer
    [SerializeField] private TMP_Dropdown dropdown;                   // TextMeshPro dropdown for skin selection
    [SerializeField] private string texturePropertyName = "_MainTex"; // Texture property name (e.g., "_MainTex", "_BaseMap")
    [Header("Additional Skins")]
    [SerializeField] private List<Skin> skins = new List<Skin>();     // List of alternative skins added in the Inspector
    [Header("Phone Model")]
    [SerializeField] private GameObject phoneModel;                   // Phone model to show in hand
    [Header("Debug Mode")]
    [SerializeField] private bool debugMode = false;                  // Enable for local dropdown selection

    private string selectedSkinName;
    private string selectedMobileStatus;
    private object lockObj = new object();
    private readonly string ipAddress = "localhost"; // Configurable IP
    private readonly int port = 4444;                // Matches server port

    void Start()
    {
        // Automatically find the SkinnedMeshRenderer on "char1" if not assigned
        if (skinnedMeshRenderer == null)
        {
            Transform char1 = transform.Find("char1");
            if (char1 != null)
            {
                skinnedMeshRenderer = char1.GetComponent<SkinnedMeshRenderer>();
            }
            if (skinnedMeshRenderer == null)
            {
                Debug.LogError("SkinnedMeshRenderer not found on 'char1'. Please assign it manually in the Inspector.");
                return;
            }
        }

        // Capture the default skin using the specified texture property
        Texture2D defaultTexture = skinnedMeshRenderer.material.GetTexture(texturePropertyName) as Texture2D;
        if (defaultTexture == null)
        {
            Debug.LogWarning($"No texture found for property '{texturePropertyName}' in the material. Falling back to '_MainTex'.");
            defaultTexture = skinnedMeshRenderer.material.GetTexture("_MainTex") as Texture2D;
            texturePropertyName = "_MainTex"; // Fallback to "_MainTex"
        }

        Skin defaultSkin = new Skin
        {
            name = "Default",
            mesh = skinnedMeshRenderer.sharedMesh,
            texture = defaultTexture
        };

        // Add default skin to the list
        skins.Insert(0, defaultSkin);

        // Set up the dropdown
        if (dropdown == null)
        {
            Debug.LogError("TMP_Dropdown not assigned. Please assign it in the Inspector.");
            return;
        }

        PopulateDropdown();

        if (debugMode)
        {
            dropdown.interactable = true;
            dropdown.value = 0; // Start with "Default"
            ChangeSkin(0); // Apply default skin
            dropdown.onValueChanged.AddListener(ChangeSkin); // Listen for changes
        }
        else
        {
            dropdown.interactable = false;
            Task.Run(() => ConnectToServer());
        }
    }

    // Populate dropdown with skin names
    private void PopulateDropdown()
    {
        dropdown.ClearOptions();
        List<string> options = new List<string>();
        foreach (var skin in skins)
        {
            options.Add(skin.name);
        }
        dropdown.AddOptions(options);
    }

    // Change the skin based on index
    private void ChangeSkin(int index)
    {
        if (index >= 0 && index < skins.Count)
        {
            skinnedMeshRenderer.sharedMesh = skins[index].mesh; // Update mesh
            Material newMat = new Material(skinnedMeshRenderer.material);
            newMat.SetTexture(texturePropertyName, skins[index].texture);
            skinnedMeshRenderer.material = newMat; // Assign new material
        }
    }

    private async void ConnectToServer()
    {
        while (true)
        {
            TcpClient client = null;
            try
            {
                client = new TcpClient();
                await client.ConnectAsync(ipAddress, port);
                var stream = client.GetStream();
                var writer = new StreamWriter(stream) { AutoFlush = true };
                writer.WriteLine("ready"); // Send "ready" to server
                var reader = new StreamReader(stream);

                while (true)
                {
                    string message = await reader.ReadLineAsync();
                    if (message == null) break;
                    if (message.StartsWith("SKIN "))
                    {
                        string skinName = message.Substring("SKIN ".Length);
                        lock (lockObj)
                        {
                            selectedSkinName = skinName;
                        }
                    }
                    else if (message.StartsWith("MOBILE_STATUS "))
                    {
                        string status = message.Substring("MOBILE_STATUS ".Length);
                        lock (lockObj)
                        {
                            selectedMobileStatus = status;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Unknown message: {message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in ConnectToServer: {ex.Message}");
            }
            finally
            {
                client?.Close();
                // Wait before retrying
                await Task.Delay(5000); // Wait 5 seconds before reconnecting
            }
        }
    }

    void Update()
    {
        if (!debugMode)
        {
            lock (lockObj)
            {
                if (selectedSkinName != null)
                {
                    ApplySkin(selectedSkinName);
                    selectedSkinName = null;
                }
                if (selectedMobileStatus != null)
                {
                    UpdatePhoneVisibility(selectedMobileStatus);
                    selectedMobileStatus = null;
                }
            }
        }
    }

    private void ApplySkin(string skinName)
    {
        int index = skins.FindIndex(s => s.name == skinName);
        if (index >= 0)
        {
            Debug.Log($"Applying skin: {skinName}");
            ChangeSkin(index);
        }
        else
        {
            Debug.LogWarning($"Skin '{skinName}' not found.");
        }
    }

    private void UpdatePhoneVisibility(string status)
    {
        if (phoneModel != null)
        {
            bool shouldShowPhone = status == "Connected";
            phoneModel.SetActive(shouldShowPhone);
            Debug.Log($"Updated phone visibility: {shouldShowPhone} based on status '{status}'");
        }
        else
        {
            Debug.LogWarning("phoneModel is not assigned in the Inspector.");
        }
    }
}

[System.Serializable]
public class Skin
{
    public string name;     // Name for dropdown (e.g., "Hoodie")
    public Mesh mesh;       // Mesh for this skin
    public Texture2D texture; // Texture for this skin (.png file)
}