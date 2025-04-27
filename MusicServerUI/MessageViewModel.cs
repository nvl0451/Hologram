using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusicServerUI
{
    public class MessageViewModel : INotifyPropertyChanged
    {
        public Message Message { get; }
        public ChatServer ChatServer { get; }

        public MessageViewModel(Message message, ChatServer chatServer)
        {
            Message = message;
            ChatServer = chatServer;
            ChatServer.UserMuteStatusChanged += OnUserMuteStatusChanged;
        }

        private void OnUserMuteStatusChanged(string username)
        {
            if (Message.Username == username)
            {
                OnPropertyChanged(nameof(MuteHeader));
            }
        }

        public bool CanMute => !Message.Username.Equals("admin", StringComparison.OrdinalIgnoreCase);
        public string MuteHeader => ChatServer.IsUserMuted(Message.Username) ? "Unmute" : "Mute";

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}