using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MusicServerUI
{
    public partial class MainWindow : Window
    {
        private AudioStreamer audioStreamer;
        private ChatServer chatServer;
        private SkinChangerServer skinChangerServer;
        private KinectServer kinectServer;
        private MobileRemoteServer mobileRemoteServer;
        private VideoStreamServer videoStreamServer;
        private Slider? slider;
        private TextBlock? currentTimeLabel;
        private TextBlock? totalDurationLabel;
        private Slider? volumeSlider;
        private TextBlock? volumePercentageLabel;
        private bool isSliderDragging = false;
        private bool isSettingSelection = false;
        private ObservableCollection<MessageViewModel> messageViewModels = new ObservableCollection<MessageViewModel>();
        private ListBox? chatListBox;
        private Grid? mainContentGrid;
        private Grid musicStreamerGrid;
        private Grid chatGrid;
        private Grid hologramGrid;
        private TextBlock kinectStatusLabel;
        private Button lockTPoseButton;
        private Border? songPickerBorder;
        private Button? menuButton;
        private TranslateTransform? songPickerTransform;
        private ColumnDefinition? songPickerColumn;

        public MainWindow()
        {
            InitializeComponent();

            musicStreamerGrid = this.FindControl<Grid>("MusicStreamerGrid");
            chatGrid = this.FindControl<Grid>("ChatGrid");
            hologramGrid = this.FindControl<Grid>("HologramGrid");
            mainContentGrid = this.FindControl<Grid>("MainContentGrid");

            songPickerColumn = musicStreamerGrid.ColumnDefinitions[0];

            audioStreamer = new AudioStreamer();
            audioStreamer.LoadPlaylist();
            if (audioStreamer.Playlist.Count > 0)
            {
                audioStreamer.SelectSong(0);
                var songLabel = this.FindControl<TextBlock>("SongLabel");
                if (songLabel != null && audioStreamer.Playlist.Count > 0)
                {
                    songLabel.Text = audioStreamer.Playlist[0].Name;
                }
            }
            var listBox = this.FindControl<ListBox>("SongListBox");
            if (listBox != null)
            {
                listBox.ItemsSource = audioStreamer.Playlist;
                listBox.SelectedIndex = audioStreamer.CurrentSongIndex;
            }
            audioStreamer.PositionChanged += OnPositionChanged;
            audioStreamer.SongChanged += OnSongChanged;
            audioStreamer.PlaybackStateChanged += OnPlaybackStateChanged;

            chatServer = new ChatServer("admin");
            Task.Run(() => chatServer.Start());
            chatListBox = this.FindControl<ListBox>("ChatListBox");
            if (chatListBox != null)
            {
                chatListBox.ItemsSource = messageViewModels;
            }
            chatServer.MessageReceived += (msg) => Dispatcher.UIThread.InvokeAsync(() =>
            {
                messageViewModels.Add(new MessageViewModel(msg, chatServer));
                if (chatListBox != null && messageViewModels.Count > 0)
                {
                    chatListBox.ScrollIntoView(messageViewModels[^1]);
                }
            });
            chatServer.MessageDeleted += (id) => Dispatcher.UIThread.InvokeAsync(() =>
            {
                var vm = messageViewModels.FirstOrDefault(m => m.Message.ID == id);
                if (vm != null)
                {
                    messageViewModels.Remove(vm);
                }
            });

            skinChangerServer = new SkinChangerServer();
            Task.Run(() => skinChangerServer.Start());

            var skinListBox = this.FindControl<ListBox>("SkinListBox");
            if (skinListBox != null)
            {
                skinListBox.ItemsSource = skinChangerServer.Skins;
                skinListBox.SelectionChanged += (s, e) =>
                {
                    if (skinListBox.SelectedItem is string skinName)
                    {
                        skinChangerServer.SetSkin(skinName);
                    }
                };
                skinChangerServer.SkinChanged += (newSkin) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        skinListBox.SelectedItem = newSkin;
                    });
                };
            }

            kinectServer = new KinectServer();
            kinectServer.StatusChanged += OnKinectStatusChanged;
            kinectServer.LockTPoseChanged += OnLockTPoseChanged;
            Task.Run(() => kinectServer.Start());

            kinectStatusLabel = this.FindControl<TextBlock>("KinectStatusLabel");
            lockTPoseButton = this.FindControl<Button>("LockTPoseButton");
            if (lockTPoseButton != null)
            {
                lockTPoseButton.Click += (s, e) =>
                {
                    kinectServer.ToggleLockTPose();
                    UpdateLockTPoseButtonUI();
                };
            }

            if (kinectStatusLabel != null)
            {
                kinectStatusLabel.Text = "Kinect: Initial";
                kinectStatusLabel.Foreground = Brushes.Purple;
                Console.WriteLine("MainWindow: Set initial label to 'Kinect: Initial' with purple color");
            }

            mobileRemoteServer = new MobileRemoteServer(audioStreamer, chatServer, kinectServer, skinChangerServer);
            mobileRemoteServer.VolumeChanged += OnVolumeChanged;
            Task.Run(() => mobileRemoteServer.Start());
            mobileRemoteServer.ConnectionStatusChanged += OnMobileConnectionStatusChanged;
            mobileRemoteServer.ConnectionStatusChanged += skinChangerServer.OnMobileStatusChanged; // Added subscription
            var mobileStatusLabel = this.FindControl<TextBlock>("MobileStatusLabel");
            if (mobileStatusLabel != null)
            {
                mobileStatusLabel.Foreground = Brushes.Purple;
            }

            videoStreamServer = new VideoStreamServer(); // New
            Task.Run(() => videoStreamServer.Start());   // New

            songPickerBorder = this.FindControl<Border>("SongPickerBorder");
            songPickerTransform = songPickerBorder?.RenderTransform as TranslateTransform;
            menuButton = this.FindControl<Button>("MenuButton");
            if (menuButton != null)
            {
                menuButton.Click += OnMenuButtonClicked;
            }

            SetupEventHandlers();
            SetupToggleHandlers();
            UpdateModuleMargins();

            UpdateLockTPoseButtonUI();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            slider = this.FindControl<Slider>("ProgressSlider");
            currentTimeLabel = this.FindControl<TextBlock>("CurrentTimeLabel");
            totalDurationLabel = this.FindControl<TextBlock>("TotalDurationLabel");
            volumeSlider = this.FindControl<Slider>("VolumeSlider");
            volumePercentageLabel = this.FindControl<TextBlock>("VolumePercentageLabel");
            songPickerBorder = this.FindControl<Border>("SongPickerBorder");
            songPickerTransform = songPickerBorder?.RenderTransform as TranslateTransform;
            menuButton = this.FindControl<Button>("MenuButton");
            if (slider != null) slider.Value = 0;
            if (volumeSlider != null) volumeSlider.Value = 100;
        }

        private void OnKinectStatusChanged(object sender, string status)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (kinectStatusLabel != null)
                {
                    Console.WriteLine($"MainWindow: Received status: {status}");
                    kinectStatusLabel.Text = $"Kinect: {status}";
                    kinectStatusLabel.Foreground = status switch
                    {
                        "MIA" => Brushes.Red,
                        "Idle" => Brushes.Green,
                        "Tracking" => Brushes.Blue,
                        _ => Brushes.Yellow
                    };
                    Console.WriteLine($"MainWindow: Updated label to '{kinectStatusLabel.Text}' with color {kinectStatusLabel.Foreground}");
                }
                else
                {
                    Console.WriteLine("MainWindow: kinectStatusLabel is null");
                }
            });
        }

        private void OnMobileConnectionStatusChanged(string status)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var mobileStatusLabel = this.FindControl<TextBlock>("MobileStatusLabel");
                if (mobileStatusLabel != null)
                {
                    mobileStatusLabel.Text = $"Mobile: {status}";
                    mobileStatusLabel.Foreground = status switch
                    {
                        "Connected" => Brushes.Green,
                        "Idle" => Brushes.Orange,
                        "Streaming" => Brushes.Blue, // New status
                        "MIA" => Brushes.Purple,
                        _ => Brushes.Purple
                    };
                }
            });
        }

        private void OnLockTPoseChanged(bool isLocked)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateLockTPoseButtonUI();
            });
        }

        private void UpdateLockTPoseButtonUI()
        {
            if (lockTPoseButton != null)
            {
                lockTPoseButton.Content = kinectServer.IsLockTPose ? "Unlock T-Pose" : "Lock T-Pose";
                lockTPoseButton.Background = kinectServer.IsLockTPose ? Brushes.Blue : Brushes.Gray;
            }
        }

        private void OnVolumeChanged(float volume)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (volumeSlider != null)
                {
                    volumeSlider.Value = volume * 100;
                }
                if (volumePercentageLabel != null)
                {
                    volumePercentageLabel.Text = $"{(int)(volume * 100)}%";
                }
            });
        }

        private void SetupEventHandlers()
        {
            var playPauseButton = this.FindControl<Button>("PlayPauseButton");
            if (playPauseButton != null)
            {
                playPauseButton.Click += (s, e) =>
                {
                    if (audioStreamer.IsPlaying)
                    {
                        audioStreamer.Pause();
                        playPauseButton.Content = "▶";
                    }
                    else
                    {
                        if (audioStreamer.CurrentSongIndex == -1 && audioStreamer.Playlist.Count > 0)
                        {
                            audioStreamer.Play(0);
                            var songLabel = this.FindControl<TextBlock>("SongLabel");
                            if (songLabel != null && audioStreamer.Playlist.Count > 0)
                            {
                                songLabel.Text = audioStreamer.Playlist[0].Name;
                            }
                        }
                        else
                        {
                            audioStreamer.Resume();
                        }
                        playPauseButton.Content = "⏸";
                    }
                };
            }

            var previousButton = this.FindControl<Button>("PreviousButton");
            if (previousButton != null) previousButton.Click += (s, e) => audioStreamer.RestartOrPrevious();

            var nextButton = this.FindControl<Button>("NextButton");
            if (nextButton != null) nextButton.Click += (s, e) => audioStreamer.Next();

            var listBox = this.FindControl<ListBox>("SongListBox");
            if (listBox != null)
            {
                listBox.SelectionChanged += (s, e) =>
                {
                    if (!isSettingSelection && listBox.SelectedIndex != -1)
                    {
                        audioStreamer.Play(listBox.SelectedIndex);
                    }
                };
            }

            if (slider != null)
            {
                slider.AddHandler(PointerPressedEvent, (s, e) => isSliderDragging = true, RoutingStrategies.Tunnel);
                slider.AddHandler(PointerReleasedEvent, (s, e) =>
                {
                    isSliderDragging = false;
                    if (audioStreamer.CurrentSongIndex >= 0)
                    {
                        audioStreamer.Seek(TimeSpan.FromSeconds(slider.Value));
                    }
                }, RoutingStrategies.Tunnel);

                slider.ValueChanged += (s, e) =>
                {
                    if (isSliderDragging && currentTimeLabel != null)
                    {
                        var time = TimeSpan.FromSeconds(slider.Value);
                        currentTimeLabel.Text = FormatTimeSpan(time);
                    }
                };
            }

            if (volumeSlider != null)
            {
                volumeSlider.ValueChanged += (s, e) =>
                {
                    audioStreamer.SetVolume((float)e.NewValue / 100);
                    if (volumePercentageLabel != null) volumePercentageLabel.Text = $"{e.NewValue:0}%";
                    mobileRemoteServer.SendVolumeUpdate(audioStreamer.Volume);
                };
            }
        }

        private async void OnMenuButtonClicked(object sender, RoutedEventArgs e)
        {
            if (songPickerBorder == null || songPickerTransform == null || songPickerColumn == null) return;

            if (songPickerColumn.Width.Value == 0)
            {
                songPickerBorder.IsVisible = true;
                await AnimateOpen();
            }
            else
            {
                await AnimateClose();
                songPickerBorder.IsVisible = false;
            }
        }

        private async Task AnimateOpen()
        {
            const int steps = 30;
            double fromWidth = 0;
            double toWidth = 100;
            double fromX = -110;
            double toX = -10;
            TimeSpan duration = TimeSpan.FromSeconds(0.3);
            TimeSpan stepDuration = TimeSpan.FromTicks(duration.Ticks / steps);
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                songPickerColumn.Width = new GridLength(fromWidth + t * (toWidth - fromWidth));
                songPickerTransform.X = fromX + t * (toX - fromX);
                await Task.Delay(stepDuration);
            }
            songPickerColumn.Width = new GridLength(toWidth);
            songPickerTransform.X = toX;
        }

        private async Task AnimateClose()
        {
            const int steps = 30;
            double fromWidth = 100;
            double toWidth = 0;
            double fromX = -10;
            double toX = -110;
            TimeSpan duration = TimeSpan.FromSeconds(0.3);
            TimeSpan stepDuration = TimeSpan.FromTicks(duration.Ticks / steps);
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                songPickerColumn.Width = new GridLength(fromWidth + t * (toWidth - fromWidth));
                songPickerTransform.X = fromX + t * (toX - fromX);
                await Task.Delay(stepDuration);
            }
            songPickerColumn.Width = new GridLength(toWidth);
            songPickerTransform.X = toX;
        }

        private void SetupToggleHandlers()
        {
            var toggleMusicStreamer = this.FindControl<ToggleButton>("ToggleMusicStreamer");
            toggleMusicStreamer.Checked += async (s, e) =>
            {
                if (mainContentGrid != null)
                {
                    mainContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                    musicStreamerGrid.IsVisible = true;
                    UpdateMainContentVisibility();
                    await AnimateOpacity(musicStreamerGrid, 0, 1, TimeSpan.FromSeconds(0.3));
                    UpdateModuleMargins();
                }
            };
            toggleMusicStreamer.Unchecked += async (s, e) =>
            {
                if (mainContentGrid != null)
                {
                    await AnimateOpacity(musicStreamerGrid, 1, 0, TimeSpan.FromSeconds(0.3));
                    musicStreamerGrid.IsVisible = false;
                    mainContentGrid.ColumnDefinitions[0].Width = new GridLength(0);
                    UpdateMainContentVisibility();
                    UpdateModuleMargins();
                }
            };

            var toggleChat = this.FindControl<ToggleButton>("ToggleChat");
            toggleChat.Checked += async (s, e) =>
            {
                if (mainContentGrid != null)
                {
                    mainContentGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    chatGrid.IsVisible = true;
                    UpdateMainContentVisibility();
                    await AnimateOpacity(chatGrid, 0, 1, TimeSpan.FromSeconds(0.3));
                    UpdateModuleMargins();
                }
            };
            toggleChat.Unchecked += async (s, e) =>
            {
                if (mainContentGrid != null)
                {
                    await AnimateOpacity(chatGrid, 1, 0, TimeSpan.FromSeconds(0.3));
                    chatGrid.IsVisible = false;
                    mainContentGrid.ColumnDefinitions[1].Width = new GridLength(0);
                    UpdateMainContentVisibility();
                    UpdateModuleMargins();
                }
            };

            var toggleHologram = this.FindControl<ToggleButton>("ToggleHologram");
            toggleHologram.Checked += async (s, e) =>
            {
                if (mainContentGrid != null)
                {
                    mainContentGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                    hologramGrid.IsVisible = true;
                    UpdateMainContentVisibility();
                    await AnimateOpacity(hologramGrid, 0, 1, TimeSpan.FromSeconds(0.3));
                    UpdateModuleMargins();
                }
            };
            toggleHologram.Unchecked += async (s, e) =>
            {
                if (mainContentGrid != null)
                {
                    await AnimateOpacity(hologramGrid, 1, 0, TimeSpan.FromSeconds(0.3));
                    hologramGrid.IsVisible = false;
                    mainContentGrid.ColumnDefinitions[2].Width = new GridLength(0);
                    UpdateMainContentVisibility();
                    UpdateModuleMargins();
                }
            };
        }

        private void UpdateMainContentVisibility()
        {
            mainContentGrid.IsVisible = musicStreamerGrid.IsVisible || chatGrid.IsVisible || hologramGrid.IsVisible;
        }

        private async Task AnimateOpacity(Grid grid, double from, double to, TimeSpan duration)
        {
            const int steps = 30;
            double stepValue = (to - from) / steps;
            TimeSpan stepDuration = TimeSpan.FromTicks(duration.Ticks / steps);
            grid.Opacity = from;
            for (int i = 1; i <= steps; i++)
            {
                grid.Opacity = from + stepValue * i;
                await Task.Delay(stepDuration);
            }
            grid.Opacity = to;
        }

        private void UpdateModuleMargins()
        {
            var visibleModules = new List<Grid>();
            if (musicStreamerGrid.IsVisible) visibleModules.Add(musicStreamerGrid);
            if (chatGrid.IsVisible) visibleModules.Add(chatGrid);
            if (hologramGrid.IsVisible) visibleModules.Add(hologramGrid);

            for (int i = 0; i < visibleModules.Count; i++)
            {
                var module = visibleModules[i];
                if (visibleModules.Count == 1)
                {
                    module.Margin = new Thickness(10, 0, 10, 0);
                }
                else if (i == 0)
                {
                    module.Margin = new Thickness(10, 0, 5, 0);
                }
                else if (i == visibleModules.Count - 1)
                {
                    module.Margin = new Thickness(5, 0, 10, 0);
                }
                else
                {
                    module.Margin = new Thickness(5, 0, 5, 0);
                }
            }
        }

        private void OnPositionChanged(object? sender, TimeSpan position)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (currentTimeLabel != null && !isSliderDragging)
                {
                    currentTimeLabel.Text = FormatTimeSpan(position);
                }
                if (slider != null && !isSliderDragging)
                {
                    slider.Value = position.TotalSeconds;
                }
                mobileRemoteServer.SendPositionUpdate(position);
            });
        }

        private void OnSongChanged(object? sender, Song song)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var songLabel = this.FindControl<TextBlock>("SongLabel");
                if (songLabel != null) songLabel.Text = song.Name;
                var listBox = this.FindControl<ListBox>("SongListBox");
                if (listBox != null)
                {
                    isSettingSelection = true;
                    listBox.SelectedIndex = audioStreamer.CurrentSongIndex;
                    isSettingSelection = false;
                }
                if (slider != null)
                {
                    slider.Maximum = song.Duration.TotalSeconds;
                    slider.Value = audioStreamer.CurrentPosition.TotalSeconds;
                }
                if (totalDurationLabel != null) totalDurationLabel.Text = FormatTimeSpan(song.Duration);
                mobileRemoteServer.SendSongUpdate(song, audioStreamer.CurrentSongIndex);
            });
        }

        private void OnPlaybackStateChanged(object? sender, bool isPlaying)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var playPauseButton = this.FindControl<Button>("PlayPauseButton");
                if (playPauseButton != null) playPauseButton.Content = isPlaying ? "⏸" : "▶";
                mobileRemoteServer.SendPlaybackStateUpdate(isPlaying);
            });
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }

        private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SendAdminMessage();
        }

        private void SendAdminMessage()
        {
            var textBox = this.FindControl<TextBox>("ChatInputTextBox");
            var message = textBox?.Text?.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                chatServer.SendMessage(message);
                textBox.Text = "";
            }
        }

        private void OnSendButtonClicked(object sender, RoutedEventArgs e)
        {
            SendAdminMessage();
        }

        private void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is MessageViewModel vm)
            {
                chatServer.DeleteMessage(vm.Message.ID);
            }
        }

        private void OnMuteUnmuteClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is MessageViewModel vm)
            {
                var username = vm.Message.Username;
                if (chatServer.IsUserMuted(username))
                {
                    chatServer.UnmuteUser(username);
                }
                else
                {
                    chatServer.MuteUser(username);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            audioStreamer.Pause();
            chatServer.Stop();
            kinectServer.Stop();
            mobileRemoteServer.Stop();
        }
    }
}