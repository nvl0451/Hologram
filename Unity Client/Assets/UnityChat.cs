using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace MusicServerUI
{
    [Serializable]
    public class Message
    {
        public int ID { get; set; }
        public string Username { get; set; }
        public string Text { get; set; }
    }

    public class UnityChat : MonoBehaviour
    {
        [Header("UI Elements (Assign in Inspector)")]
        [SerializeField] private ScrollRect chatScrollView;
        [SerializeField] private TMP_InputField messageInputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private GameObject messagePrefab;

        [Header("Server Settings")]
        [SerializeField] private string serverIP = "127.0.0.1";
        [SerializeField] private int serverPort = 3333;
        [SerializeField] private string nickname = "viewer";

        private TcpClient client;
        private NetworkStream stream;
        private List<Message> messages = new List<Message>();
        private bool isMuted = false;
        private bool needsUpdate = false;

        void Start()
        {
            if (chatScrollView.content.GetComponent<VerticalLayoutGroup>() == null)
            {
                var layout = chatScrollView.content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 5f;
                layout.padding = new RectOffset(15, 0, 15, 0);
            }

            if (chatScrollView.content.GetComponent<ContentSizeFitter>() == null)
            {
                var fitter = chatScrollView.content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            }

            if (chatScrollView.verticalScrollbar != null)
            {
                chatScrollView.verticalScrollbar.gameObject.SetActive(false);
                chatScrollView.verticalScrollbar = null;
            }

            sendButton.onClick.AddListener(SendMessage);

            new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        ConnectAndListen();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Connection lost: {e.Message}");
                        Thread.Sleep(5000); // Retry after 5 seconds
                    }
                }
            }).Start();
        }

        private void ConnectAndListen()
        {
            client = new TcpClient(serverIP, serverPort);
            stream = client.GetStream();
            var writer = new StreamWriter(stream) { AutoFlush = true };
            writer.WriteLine($"NICK:{nickname}");
            var reader = new StreamReader(stream);
            while (true)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                lock (messages)
                {
                    if (line.StartsWith("MSG:"))
                    {
                        var parts = line.Substring(4).Split(new char[] { ':' }, 3);
                        if (parts.Length == 3 && int.TryParse(parts[0], out int id))
                        {
                            messages.Add(new Message
                            {
                                ID = id,
                                Username = parts[1],
                                Text = parts[2]
                            });
                        }
                    }
                    else if (line.StartsWith("DEL:"))
                    {
                        if (int.TryParse(line.Substring(4), out int id))
                        {
                            var msg = messages.FirstOrDefault(m => m.ID == id);
                            if (msg != null)
                            {
                                messages.Remove(msg);
                            }
                        }
                    }
                    else if (line == "MUTED")
                    {
                        isMuted = true;
                    }
                    else if (line == "UNMUTED")
                    {
                        isMuted = false;
                    }
                    needsUpdate = true;
                }
            }
            client.Close();
        }

        void Update()
        {
            lock (messages)
            {
                if (needsUpdate)
                {
                    RefreshChatDisplay();
                    UpdateMuteState();
                    needsUpdate = false;
                }
            }
        }

        void RefreshChatDisplay()
        {
            foreach (Transform child in chatScrollView.content)
            {
                Destroy(child.gameObject);
            }

            var recentMessages = messages.TakeLast(10).ToList();

            foreach (var msg in recentMessages)
            {
                var messageObj = Instantiate(messagePrefab, chatScrollView.content);
                var tmpText = messageObj.GetComponent<TMP_Text>();
                if (tmpText != null)
                {
                    tmpText.text = $"{msg.Username}: {msg.Text}";
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(chatScrollView.content);
            Canvas.ForceUpdateCanvases();
            chatScrollView.verticalNormalizedPosition = 0f;
        }

        void UpdateMuteState()
        {
            messageInputField.interactable = !isMuted;
            sendButton.interactable = !isMuted;
        }

        void SendMessage()
        {
            if (!isMuted && !string.IsNullOrWhiteSpace(messageInputField.text))
            {
                try
                {
                    var writer = new StreamWriter(stream) { AutoFlush = true };
                    writer.WriteLine($"MSG:{messageInputField.text}");
                    messageInputField.text = "";
                    messageInputField.ActivateInputField();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Send error: {e.Message}");
                }
            }
        }

        void OnDestroy()
        {
            if (client != null)
            {
                client.Close();
            }
        }
    }
}