using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SSPlayer.Logs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT;
using WinRT.Interop;
using static SSPlayer.Win32;

namespace SSPlayer;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2257", Justification = "False positive — INotifyPropertyChanged is not IDynamicInterfaceCastable")]
public sealed partial class MainWindow : Window
{
    private Thread backgroundThread;
    public MediaPlayerElement _player { get; private set; }
    public Slider _progressSlider { get; private set; }
    public Slider _volumeSlider { get; private set; }
    private TextBlock _elapsedText;
    private TextBlock _durationText;
    private bool _isDragging = false;
    private AppWindow m_AppWindow;
    private DesktopAcrylicController _acrylicController;
    private SystemBackdropConfiguration _configurationSource;
    private Border _toolbarLoadingRing;
    private ProgressRing _toolbarProgressRing;
    public Border _controlOverlay { get; private set; }
    private MenuFlyoutItem _loadSrtMenuItem;
    private MenuFlyoutItem _addVideOverlayMenuItem;
    private MenuFlyoutItem _addImageOverlayMenuItem;
    private double _targetAspectRatio = 16.0 / 9.0;
    private IntPtr _hwnd;
    private SUBCLASSPROC _subclassProc;
    private long _lastClickTicks = 0;
    private const int OVERLAY_WIDTH = 600;
    private const int TOTAL_HORIZONTAL_GAP = 40;
    public Border _playlistPanel { get; private set; }
    private Border _borderlessToolbar;
    private ListView _playlistView;
    private ObservableCollection<PlaylistItem> _playlistCollection = new();
    private bool _isPlaylistVisible = false;
    private const double PLAYLIST_WIDTH = 300;
    private AudioEngine audioEngine;
    private Border _playPauseStatusOverlay;
    private TextBlock _playPauseStatusText;
    public bool liveWallpaperIsOn { get; private set; }
    public MediaTimelineController timelineController { get; private set; } = new MediaTimelineController();
    public static readonly CancellationTokenSource MainTokenSource = new CancellationTokenSource();
    public static CancellationTokenSource RequestTokenSource() => CancellationTokenSource.CreateLinkedTokenSource(MainTokenSource.Token);
    public StorageFile storageFile { get; set; }
    public bool IsLoading { get; private set; }
    public FileType fileType { get; private set; } = FileType.Video;
    private Grid radioPanel;
    private bool radioWasInitialized = false;
    private double prevvolvalue;
    private bool volumetickstop = false;
    private CancellationTokenSource clickTokenSource;
    private Storyboard _nowPlayingStoryboard;
    private bool _isResizing = false;
    private int _resizeHitTest = 0;          // HTTOP, HTLEFT, etc.
    private const int RESIZE_BORDER_THICKNESS = 8;  // pixels (adjust as needed)
    private Image _animatedImageHost;
    private bool _isDraggingWindow = false;
    private Point _dragStartMousePos;      // Relative to the window content (in DIPs)
    private Point _dragStartWindowPos;      // Screen position of the window (in DIPs
    private Point _dragStartMouseScreenPos;   // screen coordinates
    private Border _infoOverlay;
    private PointerEventHandler _borderlessPressedHandler;
    private PointerEventHandler _borderlessMovedHandler;
    private PointerEventHandler _borderlessReleasedHandler;
    public enum FileType { Unknown = 0, Video = 1, Audio = 2, Image = 3, Radio = 4 }
    private readonly string[] _supportedExtensions =
    { 
        // Video
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".3gp", ".3g2", ".ogv", ".hevc",
        // Audio
        ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".ogg", ".opus",
        // Your Custom Animation Support
        ".gif", ".png"
    };

    private string _settingsPath;
    private PlayerSettings _currentSettings;
    private int _currentPlaylistIndex = -1;
    private string _lastPlaylistPath = string.Empty;
    private string _lastPlaylistPathFile;
    public static MainWindow main { get; private set; }
    public Grid rootGrid { get; private set; }
    private Popup _thumbnailPopup;
    private StorageFile _currentThumbnailFile;
    private const int STRIP_FRAME_COUNT = 40;
    private const int THUMB_W = 160;
    private const int THUMB_H = 90;
    public Grid playerContainer { get; private set; }
    public Grid _overlayLayer { get; private set; }
    public PlayerPlayState PlayState { get; private set; }
    public ShortcutManager shortcutManager { get; private set; }
    bool visualIzerWasInitilized = false;
    private MenuFlyout contextMenu;
    FontIcon _volBtnIcon = null;
    public EqualizerSettings GetMainEqualizer => _currentSettings.Equalizer;
    private readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mp4v", ".3g2", ".3gp2", ".3gp", ".3gpp",
        ".mkv",
        ".ts", ".m2ts", ".mts", ".tt", ".tts",
        ".asf", ".wm", ".wmv", ".avi",
        ".mov", ".qt",
        ".mpg", ".mpeg", ".m1v", ".m2v", ".mod", ".vob",
        ".webm",
    };
    private readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".ico",
        ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp"
    };
    private readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".m4a",   // MPEG-4 Audio (AAC/ALAC)
        ".aac",   // Advanced Audio Coding
        ".adts",  // Audio Data Transport Stream
        ".wma",   // Windows Media Audio
        ".wav",   // Waveform Audio
        ".wave",
        ".flac",  // Native support added in Windows 10
        ".alac",  // Apple Lossless (via M4A container)
        ".ogg",   // Ogg Container
        ".oga",   // Ogg Audio
        ".opus",  // Opus Codec
        ".aif",   // Audio Interchange File Format
        ".aiff",
        ".aifc",
        ".au",    // Sun Microsystems/Next
        ".snd",
        ".mp2",   // MPEG Layer II
        ".mka",   // Matroska Audio
        ".amr",   // Adaptive Multi-Rate
        ".3gp",   // 3GPP container audio
        ".3g2"    // 3GPP2 container audio
    };
    private bool IsVideo(StorageFile f) => VideoExtensions.Contains(System.IO.Path.GetExtension(f.Name));
    private bool IsImage(StorageFile f) => ImageExtensions.Contains(System.IO.Path.GetExtension(f.Name));
    private bool IsAudio(StorageFile f) => AudioExtensions.Contains(System.IO.Path.GetExtension(f.Name));
    private Border _volumeOverlay;
    private ProgressBar _volumeOverlayBar;
    private TextBlock _volumeOverlayText;
    private TimeSpan _volumeOverlayTimer = TimeSpan.Zero;
    private double _loopStart = 0, _loopEnd = 0;
    private TimeSpan inaciveTimer = TimeSpan.Zero;
    private Border _topNowPlayingBar;
    private Border _visualizerContainer;
    private CancellationTokenSource _resizeCts;
    private TextBlock _emptyStateText;
    public Func<TimeSpan, bool> BackgroundTimer;
    private Grid _loadingOverlay;
    private ProgressRing _loadingRing;
    private TextBlock _loadingText;
    private int wasDoubleTapped = 0;
    private int isProcessingClick = 0; // Class-level field
    private CancellationTokenSource _playPauseOverlayCts;
    private Storyboard _fadeOutStoryboard = new Storyboard();
    public enum PlayerPlayState { Stop = 0, Playing = 1, Paused = 2 }
    public bool IsPointerInControlOverlay { get; private set; }
    public bool IsPointerInToolbarOverlay { get; private set; }
    public bool IsPointerInPlaylistOverlay { get; private set; }
    void PrintTest(string text)
    {
        File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "ssplayer_crash.txt"), $"\n\n=== debug {DateTime.Now} ===\n{text}");
    }
    public MainWindow()
    {
        try
        {
#if DEBUG
            NativeLogOverlay.Initialize();
#endif
            InitializeComponent();

            if (main != null) return;
            main = this;

            _visualizerContainer = new Border
            {
                //DONT USE ROUNDING HERE 
                ChildTransitions = new TransitionCollection { new EntranceThemeTransition() },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.Transparent),
                Margin = new Thickness(0),
                Padding = new Thickness(0)
            };

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = System.IO.Path.Combine(localAppData, "SSPlayer");

            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            _lastPlaylistPathFile = System.IO.Path.Combine(appFolder, "lastplaylist.txt");
            _settingsPath = System.IO.Path.Combine(appFolder, "settings.json");
            rootGrid = new Grid { UseLayoutRounding = true, Background = new SolidColorBrush(Colors.Transparent), RequestedTheme = ElementTheme.Dark, AllowDrop = true };
            rootGrid.Loaded += OnLoaded;
            rootGrid.DragOver += OnFileDraggingOver;
            rootGrid.Drop += OnFileDropped;
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Content = rootGrid;
        }
        catch (Exception ex)
        {
            if (!this.IsSafeOrThrow(ex))
            {
                Log.Print(ex.Message);
            }
        }
    }
    private void InitialLoad()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        playerContainer = new Grid { UseLayoutRounding = true, AllowDrop = true, Background = new SolidColorBrush(Colors.Transparent) };
        playerContainer.DragOver += OnFileDraggingOver;
        playerContainer.Drop += OnFileDropped;
        LoadSettings();
        ExtendsContentIntoTitleBar = true;
        m_AppWindow = GetAppWindowForCurrentWindow();
        m_AppWindow.Resize(new SizeInt32((int)(1920 / 2.5), (int)(1080 / 2.5)));

        if (m_AppWindow != null)
        {
            var titleBar = m_AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(25, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(51, 255, 255, 255);
        }

        TrySetAcrylicBackdrop();

        Grid.SetRow(playerContainer, 1);

        bool pconWasPressed = false;
        bool pconWasDragging = false;
        Point pconStartPoint = default;
        const double DragThreshold = 4.0;

        playerContainer.PointerPressed += (s, e) =>
        {
            playerContainer.CapturePointer(e.Pointer);
            var ptr = e.GetCurrentPoint(playerContainer);
            pconStartPoint = ptr.Position;
            pconWasPressed = true;
            pconWasDragging = false;
            long currentTicks = DateTime.Now.Ticks;
            _lastClickTicks = currentTicks;
        };

        playerContainer.DoubleTapped += (x, y) =>
        {
            Volatile.Write(ref wasDoubleTapped, 1);
            y.Handled = true;
            ToggleFullscreen();
        };

        playerContainer.PointerMoved += (s, e) =>
        {
            if (pconWasPressed)
            {
                var currentPoint = e.GetCurrentPoint(playerContainer).Position;

                // Calculate the distance from start point
                double deltaX = Math.Abs(currentPoint.X - pconStartPoint.X);
                double deltaY = Math.Abs(currentPoint.Y - pconStartPoint.Y);

                // Only count as a drag if the movement is significant
                if (deltaX > DragThreshold || deltaY > DragThreshold)
                {
                    pconWasDragging = true;
                }
            }
        };

        playerContainer.PointerReleased += (s, e) =>
        {
            if (pconWasPressed && !pconWasDragging)
            {
                OnMediaPlayerClicked(s, e);
            }

            playerContainer.ReleasePointerCapture(e.Pointer);
            pconWasDragging = false;
            pconWasPressed = false;
        };

        _player = new MediaPlayerElement
        {
            ElementSoundMode = ElementSoundMode.Off,
            AllowDrop = true,
            UseLayoutRounding = true,
            AreTransportControlsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Stretch = Stretch.Uniform,
            Background = new SolidColorBrush(Colors.Transparent)
        };

        _player.SetMediaPlayer(new MediaPlayer());
        var smtc = _player.MediaPlayer.SystemMediaTransportControls;
        _player.MediaPlayer.RealTimePlayback = true;
        smtc.IsEnabled = false;
        smtc.IsPlayEnabled = true;
        smtc.IsPauseEnabled = true;
        smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
        smtc.DisplayUpdater.Update();

        _player.Loaded += (s, e) => SetupMediaPlayerEvents();
        _player.Drop += OnFileDropped;
        _player.DragOver += OnFileDraggingOver;

        if (canvasDevice == null) canvasDevice = CanvasDevice.GetSharedDevice();

        Border playerClipper = new Border
        {
            UseLayoutRounding = true,
            ChildTransitions = new TransitionCollection { new EntranceThemeTransition() },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            Child = _player
        };

        playerContainer.RightTapped += (s, e) =>
        {
            if (storageFile != null && !IsVideo(storageFile))
            {
                return;
            }

            var contextMenu = ShowOptionMenu();
            contextMenu.ShowAt(playerContainer, e.GetPosition(playerContainer));
        };

        _animatedImageHost = new Image
        {
            UseLayoutRounding = true,
            Visibility = Visibility.Collapsed,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        InitializeEmptyStateText(playerContainer);
        InitializeThumbnailPopup();
        playerContainer.Children.Add(playerClipper);
        playerContainer.Children.Add(_visualizerContainer);
        playerContainer.Children.Add(_animatedImageHost);
        InitializeOverlayLayer(playerContainer);
        InitializePlaylist();
        //Dont move this
        InitializeMainControlOverlay();
        rootGrid.Children.Add(playerContainer);

        rootGrid.PointerMoved += (s, e) =>
        {
            ResetInactivityTimer();
            HandlePlaylistHover(e);
        };

        rootGrid.PointerExited += (s, e) =>
        {
            IsPointerInControlOverlay = false;
            IsPointerInPlaylistOverlay = false;
            IsPointerInToolbarOverlay = false;
            HidePlaylist();
        };

        //rootGrid.KeyDown += RootGrid_KeyDown;

        BackgroundTimer += (timespan) =>
        {
            if (inaciveTimer == TimeSpan.Zero)
            {
                inaciveTimer = timespan + TimeSpan.FromSeconds(5);
                return false;
            }

            if (timespan < inaciveTimer) return false;

            inaciveTimer = TimeSpan.Zero;
            HideControls();
            return true;
        };
        _subclassProc = new SUBCLASSPROC(WindowSubclass);
        SetWindowSubclass(_hwnd, _subclassProc, 0, IntPtr.Zero);
        TimeSpan playbacktick = TimeSpan.Zero;

        BackgroundTimer += (timespan) =>
        {
            if (playbacktick == TimeSpan.Zero)
            {
                playbacktick = timespan + TimeSpan.FromSeconds(0.2);
                return false;
            }

            if (timespan < playbacktick) return false;

            playbacktick = TimeSpan.Zero;
            PlaybackTimer_Tick(null, null);
            return true;
        };

        // Hook container events once
        AttachItemEvents();

        // Load last used playlist on startup
        _ = LoadLastPlaylistAsync();

        ToggleBorderless();

        m_AppWindow?.Changed += OnAppwindowChanged;
        SetupNowPlayingScroller();
        playerClipper.CornerRadius = GetSystemCornerRadius();
    }
    /// <summary>Called by App when launched via "Open With".</summary>
    public async Task OpenWithFilesAsync(IReadOnlyList<Windows.Storage.IStorageItem> items)
    {
        StorageFile firstFile = null;
        foreach (var item in items)
        {
            if (item is StorageFile file && _supportedExtensions.Contains(file.FileType.ToLower()))
            {
                await AddToPlaylist(file);
                firstFile ??= file;
            }
        }
        if (firstFile != null)
            await PlayItemByPath(firstFile.Path, InternalPlayStatus.ForcePlay);
    }
    private void OnAppwindowChanged(object sender, AppWindowChangedEventArgs e)
    {
        try
        {

            if (e.DidSizeChange || e.DidVisibilityChange)
            {
                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }

            if (e.DidPresenterChange || e.DidSizeChange)
            {
                if (e.DidPresenterChange)
                {
                    if (m_AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
                    {
                        uint round = DWMWCP_ROUND;
                        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(uint));
                    }
                }
            }

            if (m_AppWindow.IsVisible)
            {
                if (_configurationSource != null)
                    _configurationSource.IsInputActive = true;

                if (_acrylicController != null && _configurationSource != null)
                    _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
            }

            if (e.DidSizeChange)
            {
                if (_player?.MediaPlayer?.Source != null && fileType == FileType.Audio)
                {
                    _resizeCts?.Cancel();
                    _resizeCts = CancellationTokenSource.CreateLinkedTokenSource(MainTokenSource.Token);
                    var token = _resizeCts.Token;

                    Task.Delay(200, token).ContinueWith(t =>
                    {
                        try
                        {
                            if (t.IsCompletedSuccessfully && !token.IsCancelled())
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    ShowVisualizerOverlay(true);
                                });
                            }
                        }
                        catch (Exception ex) { this.IsSafeOrThrow(ex); }
                    }, TaskScheduler.Default);
                }
            }
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private void OnLoaded(object sender, RoutedEventArgs evt)
    {
        try
        {
                       InitializeLongRunningBackgroundWorkerTimer();
            InitialLoad();
            audioEngine = new AudioEngine();
            Closed += CleanupResources;

            InitializeVolumeOverlay(playerContainer);
            InitializeLoadingOverlay(playerContainer);

            playerContainer.PointerWheelChanged += (s, e) =>
            {
                var properties = e.GetCurrentPoint(playerContainer).Properties;
                int delta = properties.MouseWheelDelta;
                double step = 5;
                double newVolume = _currentSettings.Volume + (delta > 0 ? step : -step);

                newVolume = Math.Clamp(newVolume, 0, 100);
                UpdateVolume(newVolume);
                e.Handled = true;
            };

            playerContainer.PointerWheelChanged += (s, e) =>
            {
                var properties = e.GetCurrentPoint(playerContainer).Properties;
                int delta = properties.MouseWheelDelta;
                double step = 5;
                double newVolume = _currentSettings.Volume + (delta > 0 ? step : -step);
                newVolume = Math.Clamp(newVolume, 0, 100);

                UpdateVolume(newVolume);
                e.Handled = true;
            };

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = System.IO.Path.Combine(baseDirectory, "Assets", "ssplayer-logo-blocked.ico");

            if (File.Exists(iconPath))
            {
                m_AppWindow.SetIcon(iconPath);
            }
            else
            {
                Log.Print($"Icon missing at: {iconPath}");
            }

            SetPlaylistPanelAlwaysOnTop();
            InitializePlayPauseOverlay(playerContainer);
            shortcutManager = ShortcutManager.Initialize(this);
            InitializeTopResizeHandle();
            TriggerCodecCheck();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            throw ex;
        }
    }
    private CornerRadius GetSystemCornerRadius()
    {
        if (Application.Current.Resources.TryGetValue("ControlCornerRadius", out object value) && value is CornerRadius radius)
        {
            return radius;
        }

        return new CornerRadius(7);
    }
    private void InitializePlaylist()
    {
        var tab = new TabView
        {
            UseLayoutRounding = true,
            IsAddTabButtonVisible = false,
            TabWidthMode = TabViewWidthMode.SizeToContent
        };

        _playlistView = new ListView
        {
            UseLayoutRounding = true,
            ItemsSource = _playlistCollection,
            SelectionMode = ListViewSelectionMode.Single,
            Background = new SolidColorBrush(Colors.Transparent),
            CanReorderItems = true,
            AllowDrop = true,
            CanDragItems = true,
            ItemTemplate = BuildPlaylistItemTemplate()
        };

        _playlistView.DoubleTapped += async (s, e) =>
        {
            e.Handled = true;
            if (_playlistView.SelectedItem is PlaylistItem item)
                await PlayItemByPath(item.Path, InternalPlayStatus.ForcePlay);
        };

        _playlistCollection.CollectionChanged += (s, e) => AutoSavePlaylist();

        StackPanel plHeader = new StackPanel { UseLayoutRounding = true, Orientation = Orientation.Horizontal, Padding = new Thickness(15), Spacing = 10 };
        plHeader.Children.Add(new TextBlock { UseLayoutRounding = true, Text = "tracks", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White) });
        plHeader.Children.Add(CreatePlaylistHeaderButton("\uE109", "Add Files", async (s, e) => await OnOpenMultipleFiles()));
        plHeader.Children.Add(CreatePlaylistHeaderButton("\uE105", "Export Playlist", OnExportPlaylist));
        plHeader.Children.Add(CreatePlaylistHeaderButton("\uE8B5", "Import Playlist", OnImportPlaylist));

        var repeatBtn = CreatePlaylistHeaderButton(_currentSettings.RepeatPlaylist ? "\uE1CD" : "\uE1CE", _currentSettings.RepeatPlaylist ? "Repeat: On" : "Repeat: Off", null);
        repeatBtn.Tag = "repeatBtn";
        repeatBtn.Click += (s, e) =>
        {
            _currentSettings.RepeatPlaylist = !_currentSettings.RepeatPlaylist;
            var icon = (FontIcon)repeatBtn.Content;
            icon.Glyph = _currentSettings.RepeatPlaylist ? "\uE1CD" : "\uE1CE";
            ToolTipService.SetToolTip(repeatBtn, _currentSettings.RepeatPlaylist ? "Repeat: On" : "Repeat: Off");
            SaveSettings();
        };

        plHeader.Children.Add(repeatBtn);

        // Playlist Content Container
        Grid plGrid = new Grid { UseLayoutRounding = true };
        plGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        plGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(plHeader, 0);
        Grid.SetRow(_playlistView, 1);
        plGrid.Children.Add(plHeader);
        plGrid.Children.Add(_playlistView);

        // --- STRUCTURAL FIX: Outer layout to keep handle visible ---
        Grid playlistLayout = new Grid { UseLayoutRounding = true };
        playlistLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        playlistLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); // Dedicated resize column

        // Resize handle - Now sibling to the TabView
        var resizeHandle = new Border
        {
            UseLayoutRounding = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = true,
            BorderThickness = new Thickness(0)
        };

        bool isResizingPlaylist = false;
        double resizeStartX = 0;
        double resizeStartWidth = 0;

        resizeHandle.PointerEntered += (s, e) => resizeHandle.Background = new SolidColorBrush(ColorHelper.FromArgb(80, 255, 255, 255));
        resizeHandle.PointerExited += (s, e) =>
        {
            if (!isResizingPlaylist)
                resizeHandle.Background = new SolidColorBrush(Colors.Transparent);
        };

        resizeHandle.PointerPressed += (s, e) =>
        {
            isResizingPlaylist = true;
            resizeStartX = e.GetCurrentPoint(playerContainer).Position.X;
            resizeStartWidth = _playlistPanel.Width;
            resizeHandle.CapturePointer(e.Pointer);
            e.Handled = true;
        };

        resizeHandle.PointerMoved += (s, e) =>
        {
            if (!isResizingPlaylist) return;
            double delta = e.GetCurrentPoint(playerContainer).Position.X - resizeStartX;
            _playlistPanel.Width = Math.Clamp(resizeStartWidth + delta, _playlistPanel.MinWidth, _playlistPanel.MaxWidth);
            e.Handled = true;
        };

        resizeHandle.PointerReleased += (s, e) =>
        {
            isResizingPlaylist = false;
            resizeHandle.Background = new SolidColorBrush(Colors.Transparent);
            resizeHandle.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        };

        Grid.SetColumn(tab, 0);
        Grid.SetColumn(resizeHandle, 1);
        playlistLayout.Children.Add(tab);
        playlistLayout.Children.Add(resizeHandle);

        var playlistTab = new TabViewItem
        {
            UseLayoutRounding = true,
            IsClosable = false,
            Header = "tracks",
            IconSource = new SymbolIconSource { Symbol = Symbol.MusicInfo },
            Content = plGrid
        };

        radioPanel = new Grid { UseLayoutRounding = true, RequestedTheme = ElementTheme.Dark };
        var radioTab = new TabViewItem
        {
            UseLayoutRounding = true,
            Header = "radio",
            IconSource = new SymbolIconSource { Symbol = Symbol.World },
            Content = radioPanel,
            IsClosable = false
        };

        tab.TabItems.Add(playlistTab);
        tab.TabItems.Add(radioTab);

        _playlistPanel = new Border
        {
            UseLayoutRounding = true,
            MinWidth = PLAYLIST_WIDTH,
            MaxWidth = PLAYLIST_WIDTH * 2,
            AllowDrop = true,
            Width = PLAYLIST_WIDTH,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(ColorHelper.FromArgb(220, 10, 10, 10)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Translation = new System.Numerics.Vector3((float)-PLAYLIST_WIDTH, 0, 0),
            Opacity = 0,
            Child = playlistLayout // Shell contains TabView + Resize Handle
        };

        _playlistPanel.PointerPressed += (s, e) => { e.Handled = true; };
        _playlistPanel.PointerReleased += (s, e) => { e.Handled = true; };
        _playlistPanel.DoubleTapped += (s, e) => { e.Handled = true; };
        _playlistPanel.PointerEntered += (s, e) => { IsPointerInPlaylistOverlay = true; e.Handled = true; };
        _playlistPanel.PointerExited += (s, e) => { IsPointerInPlaylistOverlay = false; e.Handled = true; };

        _playlistPanel.SizeChanged += (s, e) =>
        {
            if (!_isPlaylistVisible)
                _playlistPanel.Translation = new System.Numerics.Vector3((float)-_playlistPanel.Width, 0, 0);
        };

        tab.SelectionChanged += async (s, e) =>
        {
            if (tab.SelectedItem is TabViewItem selectedTab)
            {
                var str = selectedTab.Header.ToString();
                string header = !string.IsNullOrEmpty(str) ? str : string.Empty;

                if (header == "radio")
                {
                    if (!radioWasInitialized)
                    {
                        radioWasInitialized = true;
                        ShowInternetRadioPanel();
                    }
                }
            }
        };

        playerContainer.Children.Add(_playlistPanel);
    }

    private async void AboutMe(object sender, RoutedEventArgs evt)
    {
        var aboutStack = new StackPanel
        {
            UseLayoutRounding = true,
            Width = 280,
            Spacing = 10,
            Padding = new Thickness(16)
        };

        var titleText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = "SSPlayer",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var versionText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = "Version 1.0.0.1",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -5, 0, 10)
        };

        var descriptionText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = "Fluent & Modern Media Player.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            TextAlignment = TextAlignment.Center
        };

        aboutStack.Children.Add(titleText);
        aboutStack.Children.Add(versionText);
        aboutStack.Children.Add(descriptionText);

        var aboutDialog = new ContentDialog
        {
            UseLayoutRounding = true,
            Content = aboutStack,
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            XamlRoot = Content.XamlRoot
        };

        aboutDialog.PrimaryButtonText = "";
        aboutDialog.CloseButtonText = "OK";

        if (MainTokenSource.IsCancelled()) return;
        await aboutDialog.ShowAsync();
    }

    private void InitializeMainControlOverlay()
    {
        _controlOverlay = new Border { UseLayoutRounding = true, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 40), Padding = new Thickness(20, 5, 20, 5), CornerRadius = GetSystemCornerRadius(), Background = new SolidColorBrush(Color.FromArgb(140, 15, 15, 15)), BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(0), Width = OVERLAY_WIDTH };
        _controlOverlay.PointerEntered += (x, y) => { IsPointerInControlOverlay = true; };
        _controlOverlay.PointerExited += (x, y) => { IsPointerInControlOverlay = false; };
        _controlOverlay.PointerPressed += (s, e) => e.Handled = true;

        Canvas.SetZIndex(_controlOverlay, 10);

        StackPanel controlStack = new StackPanel { UseLayoutRounding = true, Spacing = 2 };
        Grid progressGrid = new Grid { UseLayoutRounding = true };
        progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _elapsedText = new TextBlock { UseLayoutRounding = true, Text = "00:00", Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), FontSize = 12 };
        _durationText = new TextBlock { UseLayoutRounding = true, Text = "00:00", Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 12 };
        _progressSlider = new Slider { UseLayoutRounding = true, Minimum = 0, Maximum = 100, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 153, 255)), VerticalAlignment = VerticalAlignment.Center };

        _progressSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) =>
        {
            _isDragging = true;

            if (s is Slider slider)
            {
                var pt = e.GetCurrentPoint(slider);
                // Account for the thumb radius on each side (~10px default in WinUI)
                double thumbRadius = slider.ActualHeight / 2.0;
                double trackWidth = slider.ActualWidth - (thumbRadius * 2);
                double clampedX = Math.Clamp(pt.Position.X - thumbRadius, 0, trackWidth);
                double p = clampedX / trackWidth;
                _progressSlider.Value = p * 100;
                SeekToPercentage(p);
            }

            e.Handled = true;
        }), true);

        _progressSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, e) =>
        {
            _isDragging = false;
            e.Handled = true;
        }), true);

        _progressSlider.ValueChanged += (s, e) =>
        {
            if (_isDragging) SeekToPercentage(e.NewValue / 100.0);
        };

        _progressSlider.PointerMoved += OnSliderThumbnailMoving;
        _progressSlider.PointerExited += ProgressSlider_PointerExited;

        Grid.SetColumn(_elapsedText, 0);
        Grid.SetColumn(_progressSlider, 1);
        Grid.SetColumn(_durationText, 2);

        progressGrid.Children.Add(_elapsedText);
        progressGrid.Children.Add(_progressSlider);
        progressGrid.Children.Add(_durationText);
        Grid buttonGrid = new Grid { UseLayoutRounding = true, Margin = new Thickness(0, -7, 0, 0), IsHitTestVisible = true };
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _loadSrtMenuItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Load SRT file", Icon = new SymbolIcon(Symbol.Caption), IsEnabled = false };
        _loadSrtMenuItem.Click += LoadSrt;

        Button markerBtn = CreateIconButton("\uE707", "Timeline Marker", (s, e) => { ShowVideoMarkerFlyout((FrameworkElement)s); });
        Button optionsBtn = CreateIconButton("\uE10C", "Options", (s, e) => { });
        Button mirrorBtn = CreateIconButton("\uE8A7", "Mirror", (s, e) => { ShowDisplaySelectionFlyout((FrameworkElement)s); });

        optionsBtn.Flyout = ShowOptionMenu();
        StackPanel leftAreaButtons = new StackPanel { UseLayoutRounding = true, Orientation = Orientation.Horizontal, Spacing = 0 };
        leftAreaButtons.Children.Add(optionsBtn);
        leftAreaButtons.Children.Add(markerBtn);
        leftAreaButtons.Children.Add(mirrorBtn);
        Grid.SetColumn(leftAreaButtons, 0);
        StackPanel centerAreaButtons = new StackPanel { UseLayoutRounding = true, Orientation = Orientation.Horizontal, Spacing = 0 };

        var playcenter = CreateIconButton2("\uF5B0", "Play", null, Colors.LimeGreen, false);
        playcenter.Click += (x, y) => { PlayMedia(); };
        var pausecenter = CreateIconButton2("\uF8AE", "Pause", null, Colors.LightBlue, false);
        pausecenter.Click += (x, y) => { PauseMedia(PlayerPlayState.Paused); };
        var stopcenter = CreateIconButton2("\uE73B", "Stop", null, Colors.LightCoral, false);

        stopcenter.Click += (s, e) =>
        {
            if (_player.MediaPlayer != null)
            {
                PauseMedia(PlayerPlayState.Stop);
                timelineController.Position = TimeSpan.Zero;
            }
        };

        centerAreaButtons.Children.Add(pausecenter);
        centerAreaButtons.Children.Add(playcenter);
        centerAreaButtons.Children.Add(stopcenter);
        var repeatButton = CreateIconButton(!_currentSettings.RepeatForever ? "\uE8EE" : "\uE8ED", "Repeat", null);

        repeatButton.Click += (s, e) =>
        {
            _currentSettings.RepeatForever = !_currentSettings.RepeatForever;

            if (repeatButton.Content is Grid gd)
            {
                var itm = gd.Children[0] as FontIcon;
                itm?.Glyph = !_currentSettings.RepeatForever ? "\uE8EE" : "\uE8ED";
                Log.Print($"Looping: {_currentSettings.RepeatForever}");
            }

            SaveSettings();
        };

        Grid.SetColumn(centerAreaButtons, 1);
        StackPanel rightAreaButtons = new StackPanel { UseLayoutRounding = true, Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Spacing = 10 };

        var volBtn = CreateIconButton("\uE767", "Volume", null, null);
        _volBtnIcon = (volBtn.Content as Grid)?.Children[0] as FontIcon;
        _volumeSlider = new Slider { UseLayoutRounding = true, Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Value = _currentSettings.Volume, Width = 20, Height = 100, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Colors.WhiteSmoke) };

        _volumeSlider.ValueChanged += (s, e) =>
        {
            if (fileType == FileType.Radio && _player?.MediaPlayer != null)
            {
                _player.MediaPlayer.IsMuted = false;

                try
                {
                    _player.MediaPlayer.Volume = e.NewValue / 100.0;
                    _currentSettings.Volume = e.NewValue;
                    SaveSettings();

                    if (_radioPlayer != null)
                    {
                        SetRadioVolume(e.NewValue);
                    }
                }
                catch { }
                return;
            }

            _player?.MediaPlayer.IsMuted = true;
            audioEngine?.SetVolume(e.NewValue);
            _currentSettings.Volume = e.NewValue;
            SaveSettings();
        };

        volBtn.Click += (x, y) => ShowVerticalVolumeFlyout(volBtn, _volumeSlider);
        rightAreaButtons.Children.Add(repeatButton);
        rightAreaButtons.Children.Add(CreateIconButton("\uE7F3", "EQ", (x, y) => ShowEqualizerOverlay(x as FrameworkElement)));
        rightAreaButtons.Children.Add(volBtn);

        Grid.SetColumn(rightAreaButtons, 2);
        buttonGrid.Children.Add(leftAreaButtons);
        buttonGrid.Children.Add(centerAreaButtons);
        buttonGrid.Children.Add(rightAreaButtons);
        controlStack.Children.Add(progressGrid);
        controlStack.Children.Add(buttonGrid);
        _controlOverlay.Child = controlStack;
        playerContainer.Children.Add(_controlOverlay);
    }

    private void ShowVerticalVolumeFlyout(FrameworkElement anchor, Slider existingSlider)
    {
        Flyout volumeFlyout = new Flyout();
        volumeFlyout.ShouldConstrainToRootBounds = false;
        var flyoutStyle = new Style(typeof(FlyoutPresenter));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MarginProperty, new Thickness(0)));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, new SolidColorBrush(Colors.Transparent)));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(0)));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, 0.0));
        volumeFlyout.FlyoutPresenterStyle = flyoutStyle;

        var translateTransform = new TranslateTransform { Y = 20 };

        Border container = new Border
        {
            UseLayoutRounding = true,
            Background = new SolidColorBrush(Color.FromArgb(150, 25, 25, 25)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(0, 15, 0, 15),
            Width = 80,
            Height = 220,
            RenderTransform = translateTransform,
            Opacity = 0
        };

        StackPanel layout = new StackPanel { UseLayoutRounding = true, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };

        TextBlock volText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = $"{(int)existingSlider.Value}",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Colors.White),
            Opacity = 0.9
        };

        RangeBaseValueChangedEventHandler valueUpdater = (s, e) =>
        {
            volText.Text = $"{(int)e.NewValue}";
        };

        existingSlider.ValueChanged += valueUpdater;

        if (existingSlider.Parent is Panel p) p.Children.Remove(existingSlider);

        existingSlider.Height = 160;
        existingSlider.Width = 30;
        existingSlider.Margin = new Thickness(0);
        existingSlider.HorizontalAlignment = HorizontalAlignment.Center;
        existingSlider.Foreground = new SolidColorBrush(Colors.White);
        layout.Children.Add(volText);
        layout.Children.Add(existingSlider);
        container.Child = layout;
        volumeFlyout.Content = container;

        container.Loaded += (s, e) =>
        {
            var sb = new Storyboard();
            var fadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
            var slideUp = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(350),
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(fadeIn, container);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            Storyboard.SetTarget(slideUp, translateTransform);
            Storyboard.SetTargetProperty(slideUp, "Y");

            sb.Children.Add(fadeIn);
            sb.Children.Add(slideUp);
            sb.Begin();
        };

        volumeFlyout.Closed += (s, e) =>
        {
            existingSlider.ValueChanged -= valueUpdater;

            if (layout.Children.Contains(existingSlider)) layout.Children.Remove(existingSlider);
        };

        volumeFlyout.ShowAt(anchor);
    }
    private MenuFlyout ShowOptionMenu()
    {
        var autoPlayOption = new MenuFlyoutItem
        {
            UseLayoutRounding = true,
            Text = _currentSettings.AutoPlay ? "AutoPlay: On" : "AutoPlay: Off",
            Icon = new SymbolIcon(Symbol.Play),
            IsEnabled = true
        };
        autoPlayOption.Click += (s, e) =>
        {
            _currentSettings.AutoPlay = !_currentSettings.AutoPlay;
            autoPlayOption.Text = _currentSettings.AutoPlay ? "AutoPlay: On" : "AutoPlay: Off";
            SaveSettings();
        };

        var alwaysOnTop = new MenuFlyoutSubItem { UseLayoutRounding = true, Text = "Always On Top", Icon = new SymbolIcon(Symbol.Up) };
        var alwaysOnTopOnItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "  On", IsEnabled = true };
        var alwaysOnTopOffItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "✓ Off", IsEnabled = true };
        alwaysOnTop.Items.Add(alwaysOnTopOnItem);
        alwaysOnTop.Items.Add(alwaysOnTopOffItem);
        alwaysOnTopOnItem.Click += (s, e) =>
        {
            ToggleAlwaysOnTop(true);
            alwaysOnTopOnItem.Text = "✓ On";
            alwaysOnTopOffItem.Text = "  Off";
        };
        alwaysOnTopOffItem.Click += (s, e) =>
        {
            ToggleAlwaysOnTop(false);
            alwaysOnTopOnItem.Text = "  On";
            alwaysOnTopOffItem.Text = "✓ Off";
        };

        var playbackSpeedOption = new MenuFlyoutSubItem { UseLayoutRounding = true, Text = "Playback Speed", Icon = new SymbolIcon(Symbol.Clock) };
        var speedValues = new double[] { 0.2, 0.4, 0.6, 0.8, 1.0, 1.2, 1.4, 1.6, 1.8, 2.0, 2.2 };
        foreach (var speed in speedValues)
        {
            bool isNormal = speed == 1.0;
            bool isActive = Math.Abs(_currentSettings.PlaybackSpeed - speed) < 0.01;
            string label = isNormal ? "Normal" : $"{speed:0.0}x";
            var speedItem = new MenuFlyoutItem
            {
                UseLayoutRounding = true,
                Text = isActive ? $"✓ {label}" : $"  {label}",
            };
            speedItem.Click += (s, e) =>
            {
                foreach (var menuItem in playbackSpeedOption.Items.OfType<MenuFlyoutItem>())
                    menuItem.Text = menuItem.Text.Replace("✓", " ");
                speedItem.Text = $"✓ {label}";
                _currentSettings.PlaybackSpeed = speed;
                SaveSettings();
                if (_player.MediaPlayer != null)
                    _player.MediaPlayer.PlaybackSession.PlaybackRate = speed;
            };
            playbackSpeedOption.Items.Add(speedItem);
        }

        var jumptoframe = new MenuFlyoutItem { UseLayoutRounding = true, Text = "ToFrame", Icon = new SymbolIcon(Symbol.Next), IsEnabled = true };
        var jumptotime = new MenuFlyoutItem { UseLayoutRounding = true, Text = "ToTime", Icon = new SymbolIcon(Symbol.Next), IsEnabled = true };
        jumptoframe.Click += (x, y) =>
        {
            if (_player.MediaPlayer.PlaybackSession == null || _player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.None) { Log.Print("Jump to Frame: No media loaded."); return; }
            JumpToFrameWithDialog();
        };
        jumptotime.Click += async (x, y) =>
        {
            if (_player.MediaPlayer.PlaybackSession == null || _player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.None) { Log.Print("Jump to Time: No media loaded."); return; }
            JumpToTimeWithDialog();
        };
        var jumpItems = new MenuFlyoutSubItem { UseLayoutRounding = true, Text = "Jump", Icon = new SymbolIcon(Symbol.Clock) };
        jumpItems.Items.Add(jumptoframe);
        jumpItems.Items.Add(jumptotime);

        var mediaInfo = new ToggleMenuFlyoutItem { UseLayoutRounding = true, Text = "Media Info", Icon = new SymbolIcon(Symbol.Video), IsEnabled = true };
        mediaInfo.Click += (x, y) =>
        {
            if (_player.MediaPlayer.PlaybackSession == null || _player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.None) { Log.Print("Media Info: No media loaded."); return; }
            ToggleInfoOverlay();
        };

        var zoomoption = new MenuFlyoutSubItem { UseLayoutRounding = true, Text = "Zoom", Icon = new SymbolIcon(Symbol.Zoom), IsEnabled = true };
        var zoom02 = new ToggleMenuFlyoutItem { UseLayoutRounding = true, Text = "0.2x", IsEnabled = true, Tag = 0.2 };
        var zoom05 = new ToggleMenuFlyoutItem { UseLayoutRounding = true, Text = "0.5x", IsEnabled = true, Tag = 0.5 };
        var zoom1 = new ToggleMenuFlyoutItem { UseLayoutRounding = true, Text = "Normal", IsEnabled = true, IsChecked = true, Tag = 1.0 };
        var zoom12 = new ToggleMenuFlyoutItem { UseLayoutRounding = true, Text = "1.2x", IsEnabled = true, Tag = 1.2 };
        var zoom15 = new ToggleMenuFlyoutItem { UseLayoutRounding = true, Text = "1.5x", IsEnabled = true, Tag = 1.5 };
        var zoom20 = new ToggleMenuFlyoutItem { UseLayoutRounding = true, Text = "2.0x", IsEnabled = true, Tag = 2.0 };
        var zoomItems = new List<ToggleMenuFlyoutItem> { zoom02, zoom05, zoom1, zoom12, zoom15, zoom20 };
        foreach (var item in zoomItems)
        {
            item.UseLayoutRounding = true;
            zoomoption.Items.Add(item);
            item.Click += (s, e) =>
            {
                var clickedItem = (ToggleMenuFlyoutItem)s;
                foreach (var otherItem in zoomItems) otherItem.IsChecked = (otherItem == clickedItem);
                if (clickedItem.Tag is double zoomValue) SetZoom(zoomValue);
            };
        }

        var videoAdjustMenu = new MenuFlyoutSubItem { UseLayoutRounding = true, Text = "Video Adjustments", Icon = new SymbolIcon(Symbol.Video) };
        var rotateBtn = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Rotate 90°", Icon = new SymbolIcon(Symbol.Refresh) };
        rotateBtn.Click += (s, e) => { _currentSettings.Rotation = (_currentSettings.Rotation + 90) % 360; ApplyRenderTransform(); SaveSettings(); };
        videoAdjustMenu.Items.Add(rotateBtn);
        var flipBtn = new MenuFlyoutItem { UseLayoutRounding = true, Text = _currentSettings.FlipHorizontal ? "Mirror: On" : "Mirror: Off", Icon = new FontIcon { UseLayoutRounding = true, Glyph = "\uE8BC" } };
        flipBtn.Click += (s, e) => { _currentSettings.FlipHorizontal = !_currentSettings.FlipHorizontal; flipBtn.Text = _currentSettings.FlipHorizontal ? "Mirror: On" : "Mirror: Off"; ApplyRenderTransform(); SaveSettings(); };
        videoAdjustMenu.Items.Add(flipBtn);

        var viewOption = new MenuFlyoutSubItem { UseLayoutRounding = true, Text = "View", Icon = new SymbolIcon(Symbol.NewWindow), IsEnabled = true };
        viewOption.Items.Add(alwaysOnTop);
        viewOption.Items.Add(mediaInfo);

        var aboutSsplayer = new MenuFlyoutItem { UseLayoutRounding = true, Text = "SSPlayer", Icon = new SymbolIcon(Symbol.Home) };
        aboutSsplayer.Click += AboutMe;

        var optionsMenu = new MenuFlyout();
        optionsMenu.Items.Add(autoPlayOption);
        optionsMenu.Items.Add(jumpItems);
        optionsMenu.Items.Add(new MenuFlyoutSeparator { UseLayoutRounding = true });
        optionsMenu.Items.Add(_loadSrtMenuItem);
        optionsMenu.Items.Add(new MenuFlyoutSeparator { UseLayoutRounding = true });
        optionsMenu.Items.Add(viewOption);
        optionsMenu.Items.Add(playbackSpeedOption);
        optionsMenu.Items.Add(zoomoption);
        optionsMenu.Items.Add(videoAdjustMenu);
        optionsMenu.Items.Add(aboutSsplayer);

        optionsMenu.Opening += (s, e) =>
        {
            foreach (var menuItem in playbackSpeedOption.Items.OfType<MenuFlyoutItem>())
            {
                string raw = menuItem.Text.TrimStart('✓', ' ');
                bool isNormalItem = raw == "Normal";
                double itemSpeed = isNormalItem ? 1.0 : double.Parse(raw.TrimEnd('x'), System.Globalization.CultureInfo.InvariantCulture);
                bool isActive = Math.Abs(_currentSettings.PlaybackSpeed - itemSpeed) < 0.01;
                menuItem.Text = isActive ? $"✓ {raw}" : $"  {raw}";
            }
        };

        return optionsMenu;
    }
    public void ShowHideNowPlaying(bool state) => _topNowPlayingBar.Visibility = state ? Visibility.Visible : Visibility.Collapsed;
    private void SetupNowPlayingScroller()
    {
        _topNowPlayingBar = new Border
        {
            UseLayoutRounding = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 280,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Opacity = 0.5,
            IsHitTestVisible = false
        };

        Grid.SetRow(_topNowPlayingBar, 0);
        Grid.SetRowSpan(_topNowPlayingBar, 2);
        Canvas.SetZIndex(_topNowPlayingBar, 199);
        Canvas textCanvas = new Canvas { Width = 280, UseLayoutRounding = true };

        _topNowPlayingBar.Child = textCanvas;
        _nowPlayingStoryboard = new Storyboard();

        if (_borderlessToolbar == null)
        {
            _borderlessToolbar = CreateToolbarOverlay();

            if (Content is Grid rg)
            {
                rg.Children.Add(_borderlessToolbar);
                InitializeToolbarLoadingRing(rg);
            }

            _borderlessToolbar.Visibility = Visibility.Collapsed;
        }

        _borderlessToolbar.SizeChanged += (s, e) =>
        {
            double size = _borderlessToolbar.ActualHeight;
            double nowPlayingRight = 12 + _borderlessToolbar.ActualWidth + 6;

            _topNowPlayingBar.Height = size;
            _topNowPlayingBar.Margin = new Thickness(0, 8, nowPlayingRight, 0);
            _topNowPlayingBar.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 280, size) };
        };

        playerContainer.Children.Add(_topNowPlayingBar);
    }

    private string FormatDisplayTitle(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return string.Empty;

        ReadOnlySpan<char> span = fileName.AsSpan();
        int lastSlash = fileName.LastIndexOfAny(new[] { '\\', '/' });
        if (lastSlash != -1) span = span.Slice(lastSlash + 1);

        int lastDot = span.LastIndexOf('.');
        if (lastDot != -1) span = span.Slice(0, lastDot);

        if (span.IsEmpty) return string.Empty;
        int spaceCount = 0;

        for (int i = 1; i < span.Length; i++)
        {
            bool lowerToUpper = char.IsLower(span[i - 1]) && char.IsUpper(span[i]);
            bool acronymEnd = false;
            if (i < span.Length - 1)
            {
                acronymEnd = char.IsUpper(span[i - 1]) && char.IsUpper(span[i]) && char.IsLower(span[i + 1]);
            }

            if (lowerToUpper || acronymEnd) spaceCount++;
        }

        int finalLength = span.Length + spaceCount;

        // 3. Final Step: Direct buffer manipulation
        return string.Create(finalLength, span, (dest, src) =>
        {
            int writeIdx = 0;
            for (int i = 0; i < src.Length; i++)
            {
                // Inject space if logic matches
                if (i > 0)
                {
                    bool lowerToUpper = char.IsLower(src[i - 1]) && char.IsUpper(src[i]);
                    bool acronymEnd = (i < src.Length - 1) && char.IsUpper(src[i - 1]) && char.IsUpper(src[i]) && char.IsLower(src[i + 1]);

                    if (lowerToUpper || acronymEnd)
                    {
                        dest[writeIdx++] = ' ';
                    }
                }

                // Copy char with UpperCase for the first letter
                if (writeIdx == 0)
                    dest[writeIdx++] = char.ToUpperInvariant(src[i]);
                else
                    dest[writeIdx++] = src[i];
            }
        });
    }

    private async Task PlayPlaylistIndex(int index)
    {
        try
        {
            if (index < 0 || index >= _playlistCollection.Count) return;

            var item = _playlistCollection[index];
            bool isRadio = item.Path.StartsWith("radio://", StringComparison.OrdinalIgnoreCase);

            if (!isRadio && !System.IO.File.Exists(item.Path))
            {
                Log.Print($"File missing: {item.Path}");
                return;
            }

            _currentPlaylistIndex = index;
            await PlayItemByPath(item.Path, PlayState == PlayerPlayState.Playing ? InternalPlayStatus.ForcePlay : InternalPlayStatus.None);
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private void ShowVideoMarkerFlyout(FrameworkElement placementTarget)
    {
        if (PlayState == PlayerPlayState.Playing)
        {
            PauseMedia(PlayerPlayState.Paused);
        }

        Flyout markerFlyout = new Flyout();
        markerFlyout.ShouldConstrainToRootBounds = false;
        Style flyoutStyle = new Style(typeof(FlyoutPresenter));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, new SolidColorBrush(ColorHelper.FromArgb(180, 20, 20, 20))));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BorderBrushProperty, new SolidColorBrush(Colors.DimGray)));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(1)));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(12)));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, 650));
        flyoutStyle.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        flyoutStyle.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        markerFlyout.FlyoutPresenterStyle = flyoutStyle;

        Grid mainLayout = new Grid
        {
            UseLayoutRounding = true,
            Width = 650,
            Padding = new Thickness(0, 10, 0, 10),
            RowDefinitions = {
            new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Auto }},
            ColumnDefinitions = {
            new ColumnDefinition { Width = new GridLength(120) },
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            new ColumnDefinition { Width = new GridLength(120) }
        }
        };

        TextBlock titleText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = "Timeline Marker",
            FontSize = 10,
            Opacity = 0.6,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetRow(titleText, 0);
        Grid.SetColumn(titleText, 1);

        DropDownButton saveDropDown = new DropDownButton
        {
            UseLayoutRounding = true,
            Content = new TextBlock { UseLayoutRounding = true, Text = "Save", FontSize = 11 },
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 10, 0)
        };

        MenuFlyout saveMenu = new MenuFlyout();
        MenuFlyoutItem mp4Item = new MenuFlyoutItem { Text = "MP4" };
        MenuFlyoutItem gifItem = new MenuFlyoutItem { Text = "GIF" };
        MenuFlyoutItem mp3Item = new MenuFlyoutItem { Text = "MP3" };
        MenuFlyoutItem m4aItem = new MenuFlyoutItem { Text = "M4A" };
        MenuFlyoutItem flacItem = new MenuFlyoutItem { Text = "FLAC" };
        MenuFlyoutItem wavItem = new MenuFlyoutItem { Text = "WAV" };

        var videoSubMenu = new MenuFlyoutSubItem { UseLayoutRounding = true, Text = "Video" };
        videoSubMenu.Items.Add(mp4Item);
        videoSubMenu.Items.Add(gifItem);

        var audioSubMenu = new MenuFlyoutSubItem { UseLayoutRounding = true, Text = "Audio" };
        audioSubMenu.Items.Add(mp3Item);
        audioSubMenu.Items.Add(m4aItem);
        audioSubMenu.Items.Add(flacItem);
        audioSubMenu.Items.Add(wavItem);

        saveMenu.Items.Add(videoSubMenu);
        saveMenu.Items.Add(audioSubMenu);
        saveDropDown.Flyout = saveMenu;

        Grid.SetRow(saveDropDown, 0); Grid.SetColumn(saveDropDown, 2);
        TextBlock startTimeText = new TextBlock { UseLayoutRounding = true, Text = "00:00:00.00", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 14, Foreground = new SolidColorBrush(Colors.DodgerBlue) };
        TextBlock endTimeText = new TextBlock { UseLayoutRounding = true, Text = "00:00:00.00", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 14, Foreground = new SolidColorBrush(Colors.LimeGreen) };

        mp3Item.Click += async (s, e) =>
        {
            try
            {
                markerFlyout.Hide();
                string[] formats = { @"hh\:mm\:ss\.ff", @"mm\:ss\.ff", @"m\:ss\.ff", @"ss\.ff" };
                var culture = System.Globalization.CultureInfo.InvariantCulture;

                if (!TimeSpan.TryParseExact(startTimeText.Text, formats, culture, out var start) || !TimeSpan.TryParseExact(endTimeText.Text, formats, culture, out var end))
                {
                    await ShowSimpleDialog("Error", "Could not read timestamps.");
                    return;
                }

                await ExportMarkerRangeAsAudio(start, end, "mp3");
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        };

        m4aItem.Click += async (s, e) =>
        {
            try
            {
                markerFlyout.Hide();
                string[] formats = { @"hh\:mm\:ss\.ff", @"mm\:ss\.ff", @"m\:ss\.ff", @"ss\.ff" };
                var culture = System.Globalization.CultureInfo.InvariantCulture;

                if (!TimeSpan.TryParseExact(startTimeText.Text, formats, culture, out var start) || !TimeSpan.TryParseExact(endTimeText.Text, formats, culture, out var end))
                {
                    await ShowSimpleDialog("Error", "Could not read timestamps.");
                    return;
                }

                await ExportMarkerRangeAsAudio(start, end, "m4a");
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        };

        wavItem.Click += async (s, e) =>
        {
            try
            {
                markerFlyout.Hide();
                string[] formats = { @"hh\:mm\:ss\.ff", @"mm\:ss\.ff", @"m\:ss\.ff", @"ss\.ff" };
                var culture = System.Globalization.CultureInfo.InvariantCulture;

                if (!TimeSpan.TryParseExact(startTimeText.Text, formats, culture, out var start) || !TimeSpan.TryParseExact(endTimeText.Text, formats, culture, out var end))
                {
                    await ShowSimpleDialog("Error", "Could not read timestamps.");
                    return;
                }

                await ExportMarkerRangeAsAudio(start, end, "wav");
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        };

        flacItem.Click += async (s, e) =>
        {
            try
            {
                markerFlyout.Hide();
                string[] formats = { @"hh\:mm\:ss\.ff", @"mm\:ss\.ff", @"m\:ss\.ff", @"ss\.ff" };
                var culture = System.Globalization.CultureInfo.InvariantCulture;

                if (!TimeSpan.TryParseExact(startTimeText.Text, formats, culture, out var start) || !TimeSpan.TryParseExact(endTimeText.Text, formats, culture, out var end))
                {
                    await ShowSimpleDialog("Error", "Could not read timestamps.");
                    return;
                }

                await ExportMarkerRangeAsAudio(start, end, "flac");
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        };

        mp4Item.Click += async (s, e) =>
        {
            try
            {
                markerFlyout.Hide();
                string[] formats = { @"hh\:mm\:ss\.ff", @"mm\:ss\.ff", @"m\:ss\.ff", @"ss\.ff" };
                var culture = System.Globalization.CultureInfo.InvariantCulture;

                if (!TimeSpan.TryParseExact(startTimeText.Text, formats, culture, out var start) || !TimeSpan.TryParseExact(endTimeText.Text, formats, culture, out var end))
                {
                    await ShowSimpleDialog("Error", "Could not read timestamps.");
                    if (MainTokenSource.IsCancelled()) return;
                    return;
                }

                await ExportMarkerRangeAsMp4(start, end);
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        };

        gifItem.Click += async (s, e) =>
        {
            try
            {
                markerFlyout.Hide();
                await ExportMarkerRangeAsGif(startTimeText, endTimeText);
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        };

        Grid.SetRow(startTimeText, 1);
        Grid.SetColumn(startTimeText, 0);
        Grid.SetRow(endTimeText, 1);
        Grid.SetColumn(endTimeText, 2);
        Grid timelineGrid = new Grid { UseLayoutRounding = true, Height = 50, Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(timelineGrid, 1);
        Grid.SetColumn(timelineGrid, 1);
        Rectangle track = new Rectangle { UseLayoutRounding = true, Height = 3, Fill = new SolidColorBrush(Colors.Gray), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        timelineGrid.Children.Add(track);
        var startArrow = CreateMarker("\u25BC", Colors.DodgerBlue);
        var endArrow = CreateMarker("\u25BC", Colors.LimeGreen);
        timelineGrid.Children.Add(startArrow);
        timelineGrid.Children.Add(endArrow);

        timelineGrid.Loaded += (s, e) =>
        {
            void Restore()
            {
                double width = timelineGrid.ActualWidth;
                if (width <= 0) return;

                double startX = _loopStart * (width - 20);
                double endX = _loopEnd > 0 ? _loopEnd * (width - 20) : width - 20;

                ((TranslateTransform)startArrow.RenderTransform).X = startX;
                ((TranslateTransform)endArrow.RenderTransform).X = endX;

                var duration = _player.MediaPlayer.PlaybackSession.NaturalDuration;
                UpdateMarkerTime(startTimeText, _loopStart, duration);
                UpdateMarkerTime(endTimeText, _loopEnd > 0 ? _loopEnd : 1.0, duration);
            }

            if (timelineGrid.ActualWidth > 0)
            {
                Restore();
            }
            else
            {
                // Width not ready yet — wait for first layout pass
                void OnSizeChanged(object sender, SizeChangedEventArgs args)
                {
                    timelineGrid.SizeChanged -= OnSizeChanged;
                    Restore();
                }

                timelineGrid.SizeChanged += OnSizeChanged;
            }
        };

        SetupMarkerDragging(timelineGrid, startArrow, endArrow, startTimeText, endTimeText, ratio => _loopStart = ratio, ratio => _loopEnd = ratio);

        mainLayout.Children.Add(titleText);
        mainLayout.Children.Add(saveDropDown);
        mainLayout.Children.Add(startTimeText);
        mainLayout.Children.Add(timelineGrid);
        mainLayout.Children.Add(endTimeText);

        markerFlyout.Content = mainLayout;
        var rootSize = Content.XamlRoot.Size;

        FlyoutShowOptions options = new FlyoutShowOptions
        {
            Position = new Point(rootSize.Width / 2, rootSize.Height / 2),
            Placement = FlyoutPlacementMode.Auto, // Auto will center the flyout on the Position point
            ShowMode = FlyoutShowMode.Standard
        };

        markerFlyout.ShowAt(Content.XamlRoot.Content as FrameworkElement, options);
    }
    private async Task ShowSimpleDialog(string title, string message)
    {
        try
        {
            var d = new ContentDialog
            {
                UseLayoutRounding = true,
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Background = new SolidColorBrush(Color.FromArgb(160, 15, 15, 15))
            };
            await d.ShowAsync();
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    public void ToggleNonBlockingLoading(bool visible)
    {
        _toolbarLoadingRing.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _toolbarProgressRing.IsActive = visible;
    }
    private void UpdateLoadingRingPosition()
    {
        if (_borderlessToolbar == null || _toolbarLoadingRing == null) return;

        double size = _borderlessToolbar.ActualHeight;
        _toolbarLoadingRing.Width = size;
        _toolbarLoadingRing.Height = size;

        double radius = size / 2;
        _toolbarLoadingRing.CornerRadius = new CornerRadius(radius);

        double ringSize = Math.Max(0, size - 10);
        _toolbarProgressRing.Width = ringSize;
        _toolbarProgressRing.Height = ringSize;

        double rightOffset = 12 + _borderlessToolbar.ActualWidth + 2;
        _toolbarLoadingRing.Margin = new Thickness(0, 8, rightOffset, 0);
    }
    private void SetupMarkerDragging(Grid container, FrameworkElement start, FrameworkElement end, TextBlock sText, TextBlock eText, Action<double> onStartChanged = null, Action<double> onEndChanged = null)
    {
        FrameworkElement active = null;
        double minGap = 20.0;

        container.PointerPressed += (s, e) =>
        {
            var pt = e.GetCurrentPoint(container).Position.X;
            double sX = ((TranslateTransform)start.RenderTransform).X + 10;
            double eX = ((TranslateTransform)end.RenderTransform).X + 10;
            double distToStart = Math.Abs(pt - sX);
            double distToEnd = Math.Abs(pt - eX);

            if (distToStart < 30 || distToEnd < 30)
            {
                active = (distToStart <= distToEnd) ? start : end;
                container.CapturePointer(e.Pointer);
            }
        };

        container.PointerMoved += (s, e) =>
        {
            if (active == null) return;

            var pt = e.GetCurrentPoint(container).Position.X;
            double width = container.ActualWidth;
            double startX = ((TranslateTransform)start.RenderTransform).X;
            double endX = ((TranslateTransform)end.RenderTransform).X;
            double min = (active == start) ? 0 : startX + minGap;
            double max = (active == start) ? endX - minGap : width - 20;
            double newX = Math.Clamp(pt - 10, Math.Min(min, max), Math.Max(min, max));
            ((TranslateTransform)active.RenderTransform).X = newX;

            var duration = _player.MediaPlayer.PlaybackSession.NaturalDuration;
            double ratio = (width > 20) ? newX / (width - 20) : 0;

            if (active == start)
            {
                UpdateMarkerTime(sText, ratio, duration);
                onStartChanged?.Invoke(ratio); // ← saves to _loopStart

                if (PlayState != PlayerPlayState.Paused || PlayState != PlayerPlayState.Stop)
                {
                    PauseMedia(PlayerPlayState.Paused);
                }

                double totalSecs = duration.TotalSeconds > 0 ? duration.TotalSeconds : 0;
                SeekUnified(TimeSpan.FromSeconds(totalSecs * Math.Clamp(ratio, 0, 1)));
            }
            else
            {
                UpdateMarkerTime(eText, ratio, duration);
                onEndChanged?.Invoke(ratio);   // ← saves to _loopEnd

                if (PlayState != PlayerPlayState.Paused || PlayState != PlayerPlayState.Stop)
                {
                    PauseMedia(PlayerPlayState.Paused);
                }
                double totalSecs = duration.TotalSeconds > 0 ? duration.TotalSeconds : 0;
                SeekUnified(TimeSpan.FromSeconds(totalSecs * Math.Clamp(ratio, 0, 1)));
            }
        };

        container.PointerReleased += (s, e) => { active = null; container.ReleasePointerCapture(e.Pointer); };
    }

    private FrameworkElement CreateMarker(string unicode, Color color)
    {
        return new StackPanel
        {
            UseLayoutRounding = true,
            Width = 20,
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = new TranslateTransform(),
            Background = new SolidColorBrush(Colors.Transparent),
            Children = { new TextBlock { UseLayoutRounding = true, Text = unicode, FontSize = 24, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center } }
        };
    }

    private void InitializeVolumeOverlay(Grid parent)
    {
        _volumeOverlay = new Border
        {
            UseLayoutRounding = true,
            IsHitTestVisible = false,
            Opacity = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(ColorHelper.FromArgb(180, 20, 20, 20)),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(20),
            Width = 120,
            Height = 120,
            Translation = new System.Numerics.Vector3(0, 0, 0)
        };

        StackPanel stack = new StackPanel { UseLayoutRounding = true, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };

        _volumeOverlayText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = "100%",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };

        // Circular or Horizontal Progress (using horizontal for modern sleek look)
        _volumeOverlayBar = new ProgressBar
        {
            UseLayoutRounding = true,
            Minimum = 0,
            Maximum = 100,
            Height = 4,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 153, 255)),
            Background = new SolidColorBrush(ColorHelper.FromArgb(50, 255, 255, 255))
        };

        var _volumeOverlayIcon = new FontIcon { UseLayoutRounding = true, Glyph = "\uE767", Foreground = new SolidColorBrush(Colors.White), FontSize = 32 };
        stack.Children.Add(_volumeOverlayIcon);
        stack.Children.Add(_volumeOverlayText);
        stack.Children.Add(_volumeOverlayBar);
        _volumeOverlayBar.ValueChanged += (x, y) =>
        {
            _volumeOverlayIcon.Glyph = y.NewValue <= 0 ? "\uE74F" : "\uE767";
            _volBtnIcon.Glyph = y.NewValue <= 0 ? "\uE74F" : "\uE767";
        };

        _volumeOverlay.Child = stack;
        Canvas.SetZIndex(_volumeOverlay, 100);
        parent.Children.Add(_volumeOverlay);

        BackgroundTimer += (timespan) =>
        {
            if (_volumeOverlayTimer == TimeSpan.Zero && !volumetickstop)
            {
                _volumeOverlayTimer = timespan + TimeSpan.FromSeconds(1.5);
                return false;
            }

            if (timespan < _volumeOverlayTimer) return false;

            _volumeOverlayTimer = TimeSpan.Zero;
            _volumeOverlay.Opacity = 0;
            volumetickstop = true;
            return true;
        };
    }

    private void PreventPlayingFromMultipleClicks()
    {
        try
        {
            clickTokenSource?.Cancel();
            clickTokenSource?.Dispose();
        }
        catch { }
    }
    public void ShowPlayPauseStatus(string glyph, double delays)
    {
        if (_player.MediaPlayer.Source == null || PlayState == PlayerPlayState.Stop) return;

        if (_playPauseOverlayCts != null)
        {
            _playPauseOverlayCts.Cancel();
            _playPauseOverlayCts.Dispose();
            _playPauseOverlayCts = null;
        }

        _fadeOutStoryboard?.Stop();
        _playPauseStatusText.Text = glyph;
        _playPauseStatusOverlay.Opacity = 1;
        _playPauseOverlayCts?.Cancel();
        _playPauseOverlayCts = CancellationTokenSource.CreateLinkedTokenSource(MainTokenSource.Token);
        var token = _playPauseOverlayCts.Token;

        Task.Delay(TimeSpan.FromSeconds(delays), token).ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion && !token.IsCancellationRequested)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;

                    DoubleAnimation fadeAnim = new DoubleAnimation
                    {
                        To = 0.0,
                        Duration = new Duration(TimeSpan.FromMilliseconds(400)),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };

                    Storyboard.SetTarget(fadeAnim, _playPauseStatusOverlay);
                    Storyboard.SetTargetProperty(fadeAnim, "Opacity");

                    _fadeOutStoryboard = new Storyboard();
                    _fadeOutStoryboard.Children.Add(fadeAnim);
                    _fadeOutStoryboard.Begin();
                });
            }
        }, TaskScheduler.Default);
    }
    private void ProgressSlider_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _thumbnailPopup.IsOpen = false;
    }

    private DataTemplate BuildPlaylistItemTemplate()
    {
        const string xaml = @"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
              <Grid Padding=""10,6"" Background=""Transparent"">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width=""16""/>
                  <ColumnDefinition Width=""64""/>
                  <ColumnDefinition Width=""*""/>
                  <ColumnDefinition Width=""Auto""/>
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column=""0"" Text="""" FontSize=""10""
                           Foreground=""White"" VerticalAlignment=""Center""
                           Margin=""0,0,4,0""/>
                <Border Grid.Column=""1"" Width=""54"" Height=""36"" CornerRadius=""4""
                        Background=""#1AFFFFFF"" Margin=""0,0,8,0"" VerticalAlignment=""Center"">
                  <Grid>
                    <Image Stretch=""UniformToFill"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                    <TextBlock Text="""" FontSize=""18"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""
                               FontFamily=""Segoe MDL2 Assets"" Foreground=""White"" Opacity=""0.6""/>
                  </Grid>
                </Border>
                <StackPanel Grid.Column=""2"" VerticalAlignment=""Center"">
                  <TextBlock Text="""" Foreground=""White"" FontSize=""12""
                             TextTrimming=""CharacterEllipsis""/>
                  <TextBlock Text="""" Foreground=""White"" FontSize=""10"" Opacity=""0.6""/>
                </StackPanel>
                <TextBlock Grid.Column=""3"" Text="""" Foreground=""White""
                           FontSize=""10"" Opacity=""0.45"" VerticalAlignment=""Center"" Margin=""8,0,0,0""/>
              </Grid>
            </DataTemplate>";

        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private MenuFlyout BuildItemContextMenu(PlaylistItem item)
    {
        var menu = new MenuFlyout();

        var playItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Play", Icon = new SymbolIcon(Symbol.Play) };
        playItem.Click += async (s, e) =>
        {
            await PlayItemByPath(item.Path, InternalPlayStatus.ForcePlay);
        };
        menu.Items.Add(playItem);

        var pauseItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Pause", Icon = new SymbolIcon(Symbol.Pause) };
        pauseItem.Click += (s, e) => PauseMedia(PlayerPlayState.Paused);
        menu.Items.Add(pauseItem);

        var stopItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Stop", Icon = new SymbolIcon(Symbol.Stop) };
        stopItem.Click += (s, e) =>
        {
            if (_player.MediaPlayer != null)
            {
                PauseMedia(PlayerPlayState.Stop);
            }
        };

        menu.Items.Add(stopItem);
        menu.Items.Add(new MenuFlyoutSeparator { UseLayoutRounding = true });

        var repeatOneItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Repeat forever", Icon = new SymbolIcon(Symbol.RepeatOne) };
        repeatOneItem.Click += async (s, e) => await RepeatItemForever(item);
        menu.Items.Add(repeatOneItem);
        menu.Items.Add(new MenuFlyoutSeparator { UseLayoutRounding = true });

        var editMetadataItem = new MenuFlyoutItem
        {
            UseLayoutRounding = true,
            Text = "Metadata Editor",
            Icon = new SymbolIcon(Symbol.Edit)
        };

        editMetadataItem.Click += async (s, e) =>
        {
            // FIX: Use 'item' from the method parameter, not the SelectedItem property
            if (item != null)
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.Path);

                    string ext = System.IO.Path.GetExtension(file.Path).ToLower();
                    bool isSupported = VideoExtensions.Contains(ext) ||
                                       new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".ogg", ".opus" }.Contains(ext);

                    if (isSupported)
                    {
                        // Pass 'editMetadataItem' as the sender so it knows where to anchor
                        ShowMetadataDialog(editMetadataItem, e, file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Print($"Metadata Error: {ex.Message}");
                }
            }
        };

        menu.Items.Add(editMetadataItem);

        var removeItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Remove from playlist", Icon = new SymbolIcon(Symbol.Delete) };
        removeItem.Click += (s, e) =>
        {
            if (_player.MediaPlayer.Source != null)
            {
                if (storageFile != null && item.Path == storageFile.Path)
                {
                    return;
                }
            }

            int idx = _playlistCollection.IndexOf(item);
            _playlistCollection.Remove(item);
            if (_currentPlaylistIndex == idx) _currentPlaylistIndex = -1;
            else if (_currentPlaylistIndex > idx) _currentPlaylistIndex--;
            RefreshPlaylistUI();
            AutoSavePlaylist();
        };
        menu.Items.Add(removeItem);

        return menu;
    }
    private async void ShowMetadataDialog(object sender, RoutedEventArgs e, StorageFile file)
    {
        // 1. Get the actual UI element that was clicked
        if (e.OriginalSource is FrameworkElement anchor)
        {
            try
            {
                // 2. Resolve the file from context if needed
                StorageFile targetFile = file;
                if (targetFile == null && anchor.DataContext is PlaylistItem item)
                {
                    targetFile = await StorageFile.GetFileFromPathAsync(item.Path);
                }

                if (targetFile == null) return;

                // 3. Create the flyout
                var flyout = MetadataEditor.CreateFlyout(_playlistPanel, targetFile, () =>
                {
                    if (anchor.DataContext is PlaylistItem pi) pi.Title = pi.Title;
                });
            }
            catch (Exception ex)
            {
                Log.Print($"Metadata Error: {ex.Message}");
            }
        }
    }
    private async Task RepeatItemForever(PlaylistItem item)
    {
        if (!File.Exists(item.Path)) return;
        var file = await StorageFile.GetFileFromPathAsync(item.Path);
        if (MainTokenSource.IsCancellationRequested) return;
        int idx = _playlistCollection.IndexOf(item);
        _currentPlaylistIndex = idx;

        await LoadMultimediaFile(file);

        if (MainTokenSource.IsCancellationRequested) return;

        if (_player.MediaPlayer != null)
        {
            _currentSettings.RepeatForever = true;
        }

        RefreshPlaylistUI();
    }
    private void PlaybackTimer_Tick(object sender, object e)
    {
        if (_isDragging) return;
        if (_player?.MediaPlayer == null) return;

        var session = _player.MediaPlayer.PlaybackSession;

        if (session?.NaturalDuration.TotalSeconds > 0)
        {
            // Suppress timer updates until pipeline catches up to last seeked position
            if (_lastSeekedPosition != TimeSpan.MinValue)
            {
                double drift = Math.Abs((session.Position - _lastSeekedPosition).TotalSeconds);
                if (drift > 0.5) return; // still settling — don't overwrite slider
                else _lastSeekedPosition = TimeSpan.MinValue; // close enough, resume normal updates
            }

            _progressSlider.Value = (session.Position.TotalSeconds / session.NaturalDuration.TotalSeconds) * 100;
            _elapsedText.Text = session.Position.ToString(@"hh\:mm\:ss").TrimStart('0').TrimStart(':');
            _durationText.Text = session.NaturalDuration.ToString(@"hh\:mm\:ss").TrimStart('0').TrimStart(':');
        }

        RefreshActiveItemElapsed();
    }
    private void RefreshActiveItemElapsed()
    {
        if (_currentPlaylistIndex < 0 || _currentPlaylistIndex >= _playlistCollection.Count) return;
        if (_playlistView.ContainerFromIndex(_currentPlaylistIndex) is ListViewItem lvi)
            PopulateItemContainer(lvi, _playlistCollection[_currentPlaylistIndex]);
    }

    private void RefreshPlaylistUI()
    {
        for (int i = 0; i < _playlistCollection.Count; i++)
        {
            if (_playlistView.ContainerFromIndex(i) is ListViewItem lvi)
                PopulateItemContainer(lvi, _playlistCollection[i]);
        }
    }

    // ─── Playlist item container loaded (hook right-click + set display name) ──

    private void AttachItemEvents()
    {
        // Guard: only register once
        _playlistView.ContainerContentChanging -= OnContainerContentChanging;
        _playlistView.ContainerContentChanging += OnContainerContentChanging;
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs e)
    {
        if (e.ItemContainer is not ListViewItem lvi || e.Item is not PlaylistItem item) return;

        lvi.ContextFlyout = BuildItemContextMenu(item);

        if (e.Phase == 0)
        {
            e.RegisterUpdateCallback(OnContainerContentChanging);
            return;
        }

        PopulateItemContainer(lvi, item);
        e.Handled = true;
    }

    private void PopulateItemContainer(ListViewItem lvi, PlaylistItem item)
    {
        if (lvi.ContentTemplateRoot is not Grid root) return;

        int idx = _playlistCollection.IndexOf(item);
        bool isActive = idx == _currentPlaylistIndex;
        var session = _player.MediaPlayer?.PlaybackSession;
        bool isPlaying = isActive && session?.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing;

        string dur = item.Duration != TimeSpan.Zero ? item.Duration.ToString(@"hh\:mm\:ss").TrimStart('0').TrimStart(':') : "--:--";
        string elapsed = isPlaying
            ? (session?.Position.ToString(@"hh\:mm\:ss").TrimStart('0').TrimStart(':') ?? "")
            : "";

        // Column 0 — play indicator
        if (root.Children[0] is TextBlock ind)
        {
            ind.Text = isActive ? (isPlaying ? "▶" : "◼") : "";
            ind.Foreground = isPlaying
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 153, 255))
                : new SolidColorBrush(ColorHelper.FromArgb(160, 255, 255, 255));
        }

        // Column 1 — thumbnail or music icon
        if (root.Children[1] is Border thumbBorder)
        {
            if (thumbBorder.Child is Grid thumbGrid)
            {
                var thumbImage = thumbGrid.Children[0] as Image;
                var musicIcon = thumbGrid.Children[1] as TextBlock;

                string ext = System.IO.Path.GetExtension(item.Path).ToLowerInvariant();
                bool isAudio = AudioExtensions.Contains(ext);

                if (isAudio)
                {
                    if (thumbImage != null) thumbImage.Source = null;
                    if (musicIcon != null) musicIcon.Text = "\uE8D6"; // Music note icon
                }
                else
                {
                    if (musicIcon != null) musicIcon.Text = "";
                    if (thumbImage != null)
                    {
                        if (item.Thumbnail != null)
                        {
                            thumbImage.Source = item.Thumbnail;
                        }
                        else
                        {
                            thumbImage.Source = null;
                            // Tag the image with the item path so we don't double-load on recycle
                            if (thumbImage.Tag as string != item.Path)
                            {
                                thumbImage.Tag = item.Path;

                                _ = LoadThumbnailAsync(item).ContinueWith(_ =>
                                {
                                    DispatcherQueue.TryEnqueue(() =>
                                    {
                                        if (thumbImage.Tag as string == item.Path && item.Thumbnail != null)
                                            thumbImage.Source = item.Thumbnail;
                                    });
                                }, System.Threading.Tasks.TaskScheduler.Default);
                            }
                        }
                    }
                }
            }
        }

        // Column 2 — title + status
        if (root.Children[2] is StackPanel sp)
        {
            if (sp.Children[0] is TextBlock titleTb)
            {
                titleTb.Text = System.IO.Path.GetFileNameWithoutExtension(item.Title);
                titleTb.Foreground = isActive
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 153, 255))
                    : new SolidColorBrush(Colors.White);
            }
            if (sp.Children.Count > 1 && sp.Children[1] is TextBlock statusTb)
            {
                statusTb.Text = isPlaying ? $"▶  {elapsed} / {dur}" : dur;
                statusTb.Foreground = isPlaying
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 153, 255))
                    : new SolidColorBrush(ColorHelper.FromArgb(160, 255, 255, 255));
            }
        }

        // Column 3 — duration
        if (root.Children[3] is TextBlock durTb)
            durTb.Text = dur;
    }
    private async Task LoadThumbnailAsync(PlaylistItem item)
    {
        if (item.Thumbnail != null) return;
        if (string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path)) return;
        try
        {
            byte[]? pngBytes = await Task.Run(() =>
            {
                try
                {
                    var iid = typeof(IShellItemImageFactory).GUID;
                    int hr = SHCreateItemFromParsingName(item.Path, IntPtr.Zero, ref iid, out var factory);
                    if (hr != 0 || factory == null) return null;
                    try
                    {
                        var size = new SIZE { cx = 80, cy = 54 };
                        hr = factory.GetImage(size, SIIGBF.ThumbnailOnly | SIIGBF.ResizeToFit, out IntPtr hbm);
                        if (hr != 0 || hbm == IntPtr.Zero) return null;
                        try { return HBitmapToPng(hbm); }
                        finally { DeleteObject(hbm); }
                    }
                    finally { Marshal.ReleaseComObject(factory); }
                }
                catch (Exception ex) { this.IsSafeOrThrow(ex); return null; }
            });
            if (pngBytes == null || pngBytes.Length == 0) return;
            var bitmapImage = new BitmapImage();
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var writer = new Windows.Storage.Streams.DataWriter(ms.GetOutputStreamAt(0));
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            writer.Dispose();
            ms.Seek(0);
            await bitmapImage.SetSourceAsync(ms);
            item.Thumbnail = bitmapImage;
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private static unsafe byte[] HBitmapToPng(IntPtr hbm)
    {
        // Get bitmap dimensions
        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();

        IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            // Get bitmap info
            GetDIBits(hdc, hbm, 0, 0, Array.Empty<byte>(), ref bmi, 0);
            int w = (int)bmi.bmiHeader.biWidth;
            int h = Math.Abs(bmi.bmiHeader.biHeight);
            if (w <= 0 || h <= 0) return Array.Empty<byte>();

            // Read pixels as 32-bit BGRA
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB
            bmi.bmiHeader.biHeight = -h;     // top-down
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];

            fixed (byte* ptr = pixels)
                GetDIBits(hdc, hbm, 0, (uint)h, (IntPtr)ptr, ref bmi, 0);

            return EncodePng(pixels, w, h, stride);
        }
        finally { DeleteDC(hdc); }
    }
    private static byte[] EncodePng(byte[] bgra, int w, int h, int stride)
    {
        //BGRA - RGBA swap
        var rgba = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2]; // R
            rgba[i + 1] = bgra[i + 1]; // G
            rgba[i + 2] = bgra[i];     // B
            rgba[i + 3] = bgra[i + 3]; // A
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        void WriteChunk(string type, byte[] data)
        {
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            var lenBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(data.Length));
            ms.Write(lenBytes);
            ms.Write(typeBytes);
            ms.Write(data);
            uint crc = Crc32(typeBytes, data);
            ms.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)crc)));
        }

        // IHDR
        var ihdr = new byte[13];
        void WriteInt(byte[] b, int off, int v) { var n = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(v)); Array.Copy(n, 0, b, off, 4); }
        WriteInt(ihdr, 0, w); WriteInt(ihdr, 4, h);
        ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB (drop alpha for simplicity)
        WriteChunk("IHDR", ihdr);

        using var idat = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(idat,
            System.IO.Compression.CompressionLevel.Fastest, true))
        {
            for (int y = 0; y < h; y++)
            {
                deflate.WriteByte(0); // filter type None
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    deflate.WriteByte(rgba[i]);
                    deflate.WriteByte(rgba[i + 1]);
                    deflate.WriteByte(rgba[i + 2]);
                }
            }
        }
        var raw = idat.ToArray();
        using var zlib = new MemoryStream();
        zlib.Write(new byte[] { 0x78, 0x9C });
        zlib.Write(raw);
        uint adler = Adler32(rgba, w, h, stride);
        zlib.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)adler)));
        WriteChunk("IDAT", zlib.ToArray());
        WriteChunk("IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in type) crc = (crc >> 8) ^ _crcTable[(crc ^ b) & 0xFF];
        foreach (var b in data) crc = (crc >> 8) ^ _crcTable[(crc ^ b) & 0xFF];
        return crc ^ 0xFFFFFFFF;
    }
    private static uint Adler32(byte[] rgba, int w, int h, int stride)
    {
        uint s1 = 1, s2 = 0;
        for (int y = 0; y < h; y++) { s1 = (s1 + 0) % 65521; s2 = (s2 + s1) % 65521; for (int x = 0; x < w; x++) { int i = y * stride + x * 4; byte[] px = { rgba[i], rgba[i + 1], rgba[i + 2] }; foreach (var b in px) { s1 = (s1 + b) % 65521; s2 = (s2 + s1) % 65521; } } }
        return (s2 << 16) | s1;
    }
    private static readonly uint[] _crcTable = Enumerable.Range(0, 256).Select(i =>
    {
        uint c = (uint)i;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();
    private void SetupMediaPlayerEvents()
    {
        if (_player.MediaPlayer != null)
        {
            _player.MediaPlayer.TimelineController = timelineController;
            _player.MediaPlayer.MediaOpened -= OnMediaOpened;
            _player.MediaPlayer.MediaOpened += OnMediaOpened;
            _player.MediaPlayer.MediaEnded -= OnMediaEnded;
            _player.MediaPlayer.MediaEnded += OnMediaEnded;
            _player.MediaPlayer.Volume = _currentSettings.Volume / 100.0;
            timelineController.ClockRate = _currentSettings.PlaybackSpeed;

            ApplyRenderTransform();
        }
    }
    private TimeSpan _lastSeekedPosition = TimeSpan.MinValue;

    public void SeekUnified(TimeSpan position)
    {
        if (fileType != FileType.Audio && fileType != FileType.Video) return;

        _lastSeekedPosition = position;
        timelineController.Position = position;
        audioEngine.Seek(position);

        //Update slider immediately to the target — no waiting for pipeline
        if (_player?.MediaPlayer?.PlaybackSession?.NaturalDuration.TotalSeconds > 0)
        {
            double pct = position.TotalSeconds / _player.MediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds;
            DispatcherQueue.TryEnqueue(() => _progressSlider.Value = pct * 100);
        }
    }
    public void SeekForward(TimeSpan time)
    {
        if (_player.MediaPlayer.Source == null) return;

        if (_player.MediaPlayer.PlaybackSession.Position + time > _player.MediaPlayer.NaturalDuration)
        {
            SeekUnified(_player.MediaPlayer.NaturalDuration);
        }
        else
        {
            SeekUnified(_player.MediaPlayer.PlaybackSession.Position + time);
        }
    }
    public void SeekBackward(TimeSpan time)
    {
        if (_player.MediaPlayer.Source == null) return;

        if (_player.MediaPlayer.PlaybackSession.Position - time < TimeSpan.Zero)
        {
            SeekUnified(TimeSpan.Zero);
        }
        else
        {
            SeekUnified(_player.MediaPlayer.PlaybackSession.Position - time);
        }
    }
    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_emptyStateText != null)
                _emptyStateText.Visibility = Visibility.Collapsed;

            bool isFullscreen = m_AppWindow?.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            if (!isFullscreen)
                SnapToAspectRatio();

            if (_currentPlaylistIndex >= 0 && _currentPlaylistIndex < _playlistCollection.Count)
            {
                var dur = sender.PlaybackSession.NaturalDuration;
                if (dur.TotalSeconds > 0)
                    _playlistCollection[_currentPlaylistIndex].Duration = dur;
            }

            RefreshPlaylistUI();
            Log.Print("Media is now fully opened and ready for playback.");
        });
    }
    private void OnBufferingEnded(MediaPlaybackSession sender, object args)
    {
        sender.BufferingEnded -= OnBufferingEnded;

        Log.Print("Buffering was caught ending.");

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_currentSettings.AutoPlay)
                PlayMedia();
            else
                PauseMedia(PlayerPlayState.Paused);
        });
    }
    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (_currentSettings.RepeatForever) { SeekUnified(TimeSpan.Zero); PlayMedia(); return; }
                if (_playlistCollection.Count == 0) return;

                int next = _currentPlaylistIndex + 1;

                if (next >= _playlistCollection.Count)
                {
                    if (_currentSettings.RepeatPlaylist) next = 0;
                    else return;
                }

                var item = _playlistCollection[next];
                bool isRadio = item.Path.StartsWith("radio://", StringComparison.OrdinalIgnoreCase);

                if (!isRadio && !File.Exists(item.Path)) return;

                await PlayItemByPath(item.Path, InternalPlayStatus.None);
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        });
    }


    private Button CreatePlaylistHeaderButton(string glyph, string tip, RoutedEventHandler h)
    {
        Button b = new Button
        {
            UseLayoutRounding = true,
            Content = new FontIcon { UseLayoutRounding = true, Glyph = glyph, FontSize = 14, Foreground = new SolidColorBrush(Colors.White) },
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(5),
            IsTabStop = false
        };

        ToolTipService.SetToolTip(b, tip);
        if (h != null) b.Click += h;
        return b;
    }
    private void ApplyRenderTransform()
    {
        _player.RenderTransformOrigin = new Point(0.5, 0.5); var group = new TransformGroup();
        group.Children.Add(new RotateTransform { Angle = _currentSettings.Rotation });
        group.Children.Add(new ScaleTransform { ScaleX = _currentSettings.FlipHorizontal ? -1 : 1, ScaleY = 1 });
        _player.RenderTransform = group;
    }

    private void ResetInactivityTimer()
    {
        if (!_isPlaylistVisible)
        {
            ShowControls();
        }

        inaciveTimer = TimeSpan.Zero;
    }
    private void HideControls()
    {
        if (GetCurrentStorageFile() == null) return;

        if ((!IsVideo(GetCurrentStorageFile()) && !IsImage(GetCurrentStorageFile())) || _isDragging || _isPlaylistVisible) return;

        bool isPlaying = _player.MediaPlayer?.PlaybackSession?.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing;

        if (!isPlaying) return;

        _controlOverlay.Opacity = 0;
        ShowHideNowPlaying(false);
        if (_borderlessToolbar != null) _borderlessToolbar.Opacity = 0;

    }
    private void ShowControls()
    {
        ShowHideNowPlaying(true);
        _controlOverlay.Opacity = 1;
        if (_borderlessToolbar != null) _borderlessToolbar.Opacity = 1;
    }
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CanvasObject))]
    private void InitializeTopResizeHandle()
    {
        // 1. Create a standard Grid - no custom class needed
        var topHandle = new CanvasObject
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Top,
            Height = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(8, 0, 8, 0)
        };

        Canvas.SetZIndex(topHandle, 999);

        // Variables for the dragging logic
        bool isDragging = false;
        System.Drawing.Point lastMousePos = new();

        // 2. Attach events directly to the Grid
        topHandle.PointerEntered += (s, e) =>
        {
            topHandle.Cursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        };

        topHandle.PointerExited += (s, e) =>
        {
            topHandle.Cursor = null; // Revert to default
        };

        topHandle.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(topHandle).Properties.IsLeftButtonPressed)
            {
                isDragging = true;
                topHandle.CapturePointer(e.Pointer);
                GetCursorPos(out lastMousePos);
                e.Handled = true;
            }
        };

        topHandle.PointerMoved += (s, e) =>
        {
            if (isDragging)
            {
                GetCursorPos(out System.Drawing.Point currentPos);
                int deltaY = currentPos.Y - lastMousePos.Y;

                if (deltaY != 0)
                {
                    var pos = m_AppWindow.Position;
                    var size = m_AppWindow.Size;
                    int newHeight = size.Height - deltaY;

                    if (newHeight > 100)
                    {
                        int newWidth = (int)(newHeight * _targetAspectRatio);
                        m_AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                            pos.X,
                            pos.Y + deltaY,
                            newWidth,
                            newHeight));
                        lastMousePos = currentPos;
                    }
                }
                e.Handled = true;
            }
        };

        topHandle.PointerReleased += (s, e) =>
        {
            isDragging = false;
            topHandle.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        };

        // 3. Add to the container with the dispatcher to avoid the layout-pass crash
        DispatcherQueue.TryEnqueue(() =>
        {
            if (playerContainer != null && !playerContainer.Children.Contains(topHandle))
            {
                playerContainer.Children.Add(topHandle);
            }
        });
    }
    private IntPtr WindowSubclass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uId, IntPtr dwRef)
    {
        const uint WM_NCCALCSIZE = 0x0083;
        const uint WM_SETCURSOR = 0x0020;
        const uint WM_NCHITTEST = 0x0084;

        // Only active in borderless mode (subclassing is always on, presenter check is cheap)
        bool isBorderless = m_AppWindow?.Presenter is OverlappedPresenter op && !op.HasTitleBar;

        if (isBorderless && uMsg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
        {
            GetWindowRect(hWnd, out RECT windowRect);
            IntPtr result = DefSubclassProc(hWnd, uMsg, wParam, lParam);
            var clientRect = Marshal.PtrToStructure<RECT>(lParam);
            // Leave exactly 1px of NC area at the top — invisible but enough for
            // Windows to honour HTTOP/HTTOPLEFT/HTTOPRIGHT resize hit-tests.
            clientRect.Top = windowRect.Top;  // fully collapse top NC strip
            Marshal.StructureToPtr(clientRect, lParam, false);
            return result;
        }

        if (isBorderless && uMsg == WM_NCHITTEST)
        {
            IntPtr defResult = DefSubclassProc(hWnd, uMsg, wParam, lParam);

            int mx = (short)(lParam.ToInt64() & 0xFFFF);
            int my = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            GetWindowRect(hWnd, out RECT r);
            int b = RESIZE_BORDER_THICKNESS;

            if (my >= r.Top && my < r.Top + b)
            {
                if (mx <= r.Left + b) return (IntPtr)HTTOPLEFT;
                if (mx >= r.Right - b) return (IntPtr)HTTOPRIGHT;

                // If it's the top but NOT the corners, return HTCLIENT 
                // so the CustomResizeGrip (XAML) can take it.
                return (IntPtr)HTCLIENT;
            }

            if (my >= r.Bottom - b && my <= r.Bottom)
            {
                if (mx <= r.Left + b) return (IntPtr)HTBOTTOMLEFT;
                if (mx >= r.Right - b) return (IntPtr)HTBOTTOMRIGHT;
                return (IntPtr)HTBOTTOM;
            }

            // Also check sides if you want them to work
            if (mx <= r.Left + b) return (IntPtr)HTLEFT;
            if (mx >= r.Right - b) return (IntPtr)HTRIGHT;
            return defResult;
        }

        if (isBorderless && uMsg == WM_SETCURSOR)
        {
            int hit = (int)(lParam.ToInt64() & 0xFFFF);
            const int IDC_SIZENWSE = 32642;
            const int IDC_SIZENESW = 32643;
            const int IDC_SIZEWE = 32644;
            const int IDC_SIZENS = 32645;

            int cursorId = hit switch
            {
                HTTOPLEFT or HTBOTTOMRIGHT => IDC_SIZENWSE,
                HTTOPRIGHT or HTBOTTOMLEFT => IDC_SIZENESW,
                HTLEFT or HTRIGHT => IDC_SIZEWE,
                HTTOP or HTBOTTOM => IDC_SIZENS,
                _ => 0
            };

            if (cursorId != 0)
            {
                SetCursor(LoadCursor(IntPtr.Zero, cursorId));
                return (IntPtr)1; // STOP the glitching/flickering
            }
        }

        if (uMsg == 0x0024)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            GetWindowRect(_hwnd, out var wr);
            GetClientRect(_hwnd, out var cr);
            int cw = (wr.Right - wr.Left) - (cr.Right - cr.Left);
            int ch = (wr.Bottom - wr.Top) - (cr.Bottom - cr.Top);
            uint dpi = GetDpiForWindow(_hwnd);
            int minW = Math.Max(OVERLAY_WIDTH + TOTAL_HORIZONTAL_GAP + cw, (int)((1920 / 2.5) * dpi / 96));
            int minH = Math.Max((int)((OVERLAY_WIDTH + TOTAL_HORIZONTAL_GAP) / _targetAspectRatio) + ch, (int)((1080 / 2.5) * dpi / 96));
            mmi.ptMinTrackSize.X = minW;
            mmi.ptMinTrackSize.Y = minH;
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        }

        if (uMsg == 0x0214 && m_AppWindow?.Presenter.Kind == AppWindowPresenterKind.Overlapped)
        {
            var p = m_AppWindow.Presenter as OverlappedPresenter;
            if (p?.State == OverlappedPresenterState.Restored)
            {
                var r = Marshal.PtrToStructure<RECT>(lParam);
                GetWindowRect(_hwnd, out var wr);
                GetClientRect(_hwnd, out var cr);

                int ch = (wr.Bottom - wr.Top) - (cr.Bottom - cr.Top);
                int cw = (wr.Right - wr.Left) - (cr.Right - cr.Left);

                int edge = (int)wParam;

                // WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4, 
                // WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8

                switch (edge)
                {
                    case 1: // Left
                    case 2: // Right
                    case 7: // BottomLeft
                    case 8: // BottomRight
                            // Width is primary: Adjust Bottom based on Width
                        r.Bottom = r.Top + (int)((r.Right - r.Left - cw) / _targetAspectRatio) + ch;
                        break;

                    case 3: // Top
                    case 6: // Bottom
                            // Height is primary: Adjust Right based on Height
                        r.Right = r.Left + (int)((r.Bottom - r.Top - ch) * _targetAspectRatio) + cw;
                        break;

                    case 4: // TopLeft
                            // Special case: If dragging Top-Left, adjusting Right/Bottom 
                            // causes the "float". Instead, adjust Top/Left based on which 
                            // dimension changed more, or pick one as primary.
                            // Here we let Width lead and adjust Top to compensate:
                        r.Top = r.Bottom - (int)((r.Right - r.Left - cw) / _targetAspectRatio) - ch;
                        break;

                    case 5: // TopRight
                            // Adjust Top based on Width
                        r.Top = r.Bottom - (int)((r.Right - r.Left - cw) / _targetAspectRatio) - ch;
                        break;
                }

                Marshal.StructureToPtr(r, lParam, false);
                return (IntPtr)1;
            }
        }

        if (uMsg == 0x0232 && m_AppWindow?.Presenter.Kind == AppWindowPresenterKind.Overlapped) SnapToAspectRatio();
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
    private void CenterAppWindow()
    {
        var displayArea = DisplayArea.GetFromWindowId(m_AppWindow.Id, DisplayAreaFallback.Nearest);

        if (displayArea != null)
        {
            var workArea = displayArea.WorkArea;
            var currentSize = m_AppWindow.Size;
            PointInt32 newPos;
            newPos.X = workArea.X + (workArea.Width - currentSize.Width) / 2;
            newPos.Y = workArea.Y + (workArea.Height - currentSize.Height) / 2;
            m_AppWindow.Move(newPos);
        }
    }

    public void SwitchToRadioMode()
    {
        if (fileType != FileType.Radio)
        {
            if (PlayState == PlayerPlayState.Playing)
            {
                timelineController.Pause();
                audioEngine.Pause();
            }
            PlayState = PlayerPlayState.Stop;
        }

        fileType = FileType.Radio;
    }

    private void InitializeOverlayLayer(Grid parent)
    {
        // 1. Create the main overlay container
        _overlayLayer = new Grid
        {
            UseLayoutRounding = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent), // Transparent but allows hits
            IsHitTestVisible = true,
            AllowDrop = true
        };

        Canvas.SetZIndex(_overlayLayer, 5);
        parent.Children.Add(_overlayLayer);
    }
    private bool TrySetAcrylicBackdrop()
    {
        try
        {
            if (_acrylicController != null)
            {
                _acrylicController.RemoveAllSystemBackdropTargets();
                _acrylicController.Dispose();
                this.Activated -= OnAcrylicActivated;
                _acrylicController = null;

            }

            if (DesktopAcrylicController.IsSupported())
            {
                _configurationSource = new SystemBackdropConfiguration();
                this.Activated += OnAcrylicActivated;
                _acrylicController = new DesktopAcrylicController { TintOpacity = 0.1f, LuminosityOpacity = 0.50f, TintColor = ColorHelper.FromArgb(255, 10, 10, 10) };
                _acrylicController.FallbackColor = ColorHelper.FromArgb(180, 10, 10, 10);

                var target = CastExtensions.As<ICompositionSupportsSystemBackdrop>(this);

                if (target != null)
                {
                    _acrylicController.AddSystemBackdropTarget(target);
                    _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); return false; }
    }

    private void OnAcrylicActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_configurationSource != null) _configurationSource.IsInputActive = e.WindowActivationState != WindowActivationState.Deactivated;
    }

    private AppWindow GetAppWindowForCurrentWindow() { return AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd)); }

    private async void IndependentProbe_Click(object sender, RoutedEventArgs e)
    {
        var fp = new FileOpenPicker();
        InitializeWithWindow.Initialize(fp, _hwnd);
        fp.FileTypeFilter.Add(".mp4");
        fp.FileTypeFilter.Add(".mkv");
        fp.FileTypeFilter.Add(".avi");
        fp.FileTypeFilter.Add(".mov");

        var vf = await fp.PickSingleFileAsync(); if (vf == null) return;
        var fdp = new FolderPicker();
        InitializeWithWindow.Initialize(fdp, _hwnd);
        fdp.FileTypeFilter.Add("*");
        var tf = await fdp.PickSingleFolderAsync();

        if (tf == null) return;

        var clip = await MediaClip.CreateFromFileAsync(vf);
        var pr = clip.GetVideoEncodingProperties();
        double fps = (pr != null && pr.FrameRate.Denominator != 0) ? (double)pr.FrameRate.Numerator / pr.FrameRate.Denominator : 30.0;
        int total = (int)(clip.OriginalDuration.TotalSeconds * fps);
        long tpf = (long)(TimeSpan.TicksPerSecond / fps);
        ProgressBar pb = new ProgressBar { UseLayoutRounding = true, Minimum = 0, Maximum = 100, Width = 400, Margin = new Thickness(0, 15, 0, 10) };
        TextBlock ts = new TextBlock { UseLayoutRounding = true, Text = "Initializing...", FontSize = 12 };
        ContentDialog d = new ContentDialog { UseLayoutRounding = true, Title = "Probing", Content = new StackPanel { UseLayoutRounding = true, Children = { ts, pb } }, CloseButtonText = "Cancel", XamlRoot = this.Content.XamlRoot };
        bool can = false; d.Closing += (s, a) => can = true;
        _ = d.ShowAsync();
        SemaphoreSlim sem = new SemaphoreSlim(4);
        int done = 0;
        List<Task> tasks = new List<Task>();

        for (int i = 0; i < total; i++)
        {
            if (can) break; int idx = i;

            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync(); try
                {
                    var c = await MediaClip.CreateFromFileAsync(vf); var comp = new MediaComposition { Clips = { c } };
                    var outFile = await tf.CreateFileAsync($"Frame_{idx:D6}.jpg", CreationCollisionOption.ReplaceExisting);
                    await ExtractFrameAsJpegAsync(comp, TimeSpan.FromTicks(tpf * idx), outFile, new BitmapPropertySet());
                    var dCount = Interlocked.Increment(ref done);
                    _ = DispatcherQueue.TryEnqueue(() => { pb.Value = (double)dCount / total * 100; ts.Text = $"Frame {dCount}/{total}"; });
                }
                finally { sem.Release(); }
            }));
        }
        await Task.WhenAll(tasks); ts.Text = "Finished!"; d.CloseButtonText = "Done";
    }


    private Button CreateIconButton(string glyph, string tooltip, RoutedEventHandler clickHandler, Color? iconColor = null, bool extraThick = false)
    {
        Color finalColor = iconColor ?? Colors.WhiteSmoke;

        var contentGrid = new Grid
        {
            UseLayoutRounding = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var icon = new FontIcon
        {
            UseLayoutRounding = true,
            Name = "IconoGliphy",
            Glyph = glyph,
            FontSize = 20,
            Foreground = new SolidColorBrush(finalColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0)
        };

        if (extraThick)
        {
            icon.FontWeight = FontWeights.ExtraBold;
        }

        var underline = new Rectangle
        {
            UseLayoutRounding = true,
            Height = 4,
            Width = 20,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new SolidColorBrush(Colors.DodgerBlue),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(2, 0, 2, 2)
        };

        contentGrid.Children.Add(icon);
        contentGrid.Children.Add(underline);

        var btn = new Button
        {
            UseLayoutRounding = true,
            Content = contentGrid,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Width = 45,
            Height = 45,
            BorderThickness = new Thickness(1.5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            IsTabStop = false
        };

        btn.PointerExited += (s, e) =>
        {
            underline.Visibility = Visibility.Collapsed;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(contentGrid);
            visual.CenterPoint = new System.Numerics.Vector3(45f / 2f, 45f / 2f, 0f);
            var compositor = visual.Compositor;
            var ret = compositor.CreateSpringScalarAnimation();
            ret.FinalValue = 1.0f; ret.DampingRatio = 0.5f; ret.Period = TimeSpan.FromMilliseconds(100);
            visual.StartAnimation("Scale.X", ret);
            visual.StartAnimation("Scale.Y", ret);
        };

        btn.PointerPressed += (s, e) =>
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(contentGrid);
            visual.CenterPoint = new System.Numerics.Vector3(45f / 2f, 45f / 2f, 0f);
            var squish = visual.Compositor.CreateSpringScalarAnimation();
            squish.FinalValue = 0.82f; squish.DampingRatio = 1.0f; squish.Period = TimeSpan.FromMilliseconds(50);
            visual.StartAnimation("Scale.X", squish);
            visual.StartAnimation("Scale.Y", squish);
        };

        btn.PointerReleased += (s, e) =>
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(contentGrid);
            visual.CenterPoint = new System.Numerics.Vector3(45f / 2f, 45f / 2f, 0f);
            var pop = visual.Compositor.CreateSpringScalarAnimation();
            pop.InitialValue = 0.82f;
            pop.FinalValue = 1.18f;  // ← changed
            pop.DampingRatio = 0.25f; pop.Period = TimeSpan.FromMilliseconds(60);
            visual.StartAnimation("Scale.X", pop);
            visual.StartAnimation("Scale.Y", pop);
        };

        btn.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(ColorHelper.FromArgb(60, 255, 255, 255));
        btn.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Colors.LightSteelBlue);
        btn.Resources["ButtonBorderBrushPressed"] = new SolidColorBrush(Colors.LimeGreen);

        if (clickHandler != null) btn.Click += clickHandler;
        ToolTipService.SetToolTip(btn, tooltip);

        btn.Tag = underline;
        return btn;
    }
    private Button CreateIconButton2(string glyph, string tooltip, RoutedEventHandler clickHandler, Color? iconColor = null, bool extraThick = false)
    {
        Color finalColor = iconColor ?? Colors.WhiteSmoke;

        var contentGrid = new Grid
        {
            UseLayoutRounding = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var shadowIcon = new FontIcon
        {
            UseLayoutRounding = true,
            Glyph = glyph,
            FontSize = extraThick ? 24 : 20,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(120, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 3, 0, 0),
            IsHitTestVisible = false
        };

        var icon = new FontIcon
        {
            UseLayoutRounding = true,
            Name = "IconoGliphy",
            Glyph = glyph,
            FontSize = extraThick ? 24 : 20,
            Foreground = new SolidColorBrush(finalColor),
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0)
        };

        if (extraThick)
        {
            icon.FontWeight = FontWeights.ExtraBold;
            shadowIcon.FontWeight = FontWeights.ExtraBold;
        }

        var underline = new Rectangle
        {
            UseLayoutRounding = true,
            Height = 4,
            Width = 20,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new SolidColorBrush(finalColor),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(2, 0, 2, 2),
            IsTabStop = false
        };

        contentGrid.Children.Add(shadowIcon);
        contentGrid.Children.Add(icon);
        contentGrid.Children.Add(underline);

        var btn = new Button
        {
            UseLayoutRounding = true,
            Content = contentGrid,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Width = 45,
            Height = 45,
            BorderThickness = new Thickness(1.5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            IsTabStop = false
        };

        btn.PointerEntered += (s, e) =>
        {
            underline.Visibility = Visibility.Visible;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(contentGrid);
            var compositor = visual.Compositor;
            var bounce = compositor.CreateSpringScalarAnimation();
            bounce.InitialValue = 0.88f;
            bounce.FinalValue = 1.1f;
            bounce.DampingRatio = 0.3f;
            bounce.Period = TimeSpan.FromMilliseconds(65);
            visual.StartAnimation("Scale.X", bounce);
            visual.StartAnimation("Scale.Y", bounce);
        };
        btn.PointerEntered += (s, e) =>
        {
            underline.Visibility = Visibility.Visible;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(contentGrid);
            visual.CenterPoint = new System.Numerics.Vector3(45f / 2f, 45f / 2f, 0f);
            var compositor = visual.Compositor;
            var bounce = compositor.CreateSpringScalarAnimation();
            bounce.InitialValue = 0.88f;
            bounce.FinalValue = 1.18f;
            bounce.DampingRatio = 0.3f;
            bounce.Period = TimeSpan.FromMilliseconds(65);
            visual.StartAnimation("Scale.X", bounce);
            visual.StartAnimation("Scale.Y", bounce);
        };
        btn.PointerExited += (s, e) =>
        {
            underline.Visibility = Visibility.Collapsed;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(contentGrid);
            visual.CenterPoint = new System.Numerics.Vector3(45f / 2f, 45f / 2f, 0f);
            var compositor = visual.Compositor;
            var ret = compositor.CreateSpringScalarAnimation();
            ret.FinalValue = 1.0f; ret.DampingRatio = 0.5f; ret.Period = TimeSpan.FromMilliseconds(100);
            visual.StartAnimation("Scale.X", ret);
            visual.StartAnimation("Scale.Y", ret);
        };
        btn.PointerPressed += (s, e) =>
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(contentGrid);
            visual.CenterPoint = new System.Numerics.Vector3(45f / 2f, 45f / 2f, 0f);
            var squish = visual.Compositor.CreateSpringScalarAnimation();
            squish.FinalValue = 0.82f; squish.DampingRatio = 1.0f; squish.Period = TimeSpan.FromMilliseconds(50);
            visual.StartAnimation("Scale.X", squish);
            visual.StartAnimation("Scale.Y", squish);
        };
        btn.PointerReleased += (s, e) =>
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(contentGrid);
            visual.CenterPoint = new System.Numerics.Vector3(45f / 2f, 45f / 2f, 0f);
            var pop = visual.Compositor.CreateSpringScalarAnimation();
            pop.InitialValue = 0.82f;
            pop.FinalValue = 1.18f;
            pop.DampingRatio = 0.25f; pop.Period = TimeSpan.FromMilliseconds(60);
            visual.StartAnimation("Scale.X", pop);
            visual.StartAnimation("Scale.Y", pop);
        };

        btn.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(ColorHelper.FromArgb(60, 255, 255, 255));
        btn.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Colors.LightSteelBlue);
        btn.Resources["ButtonBorderBrushPressed"] = new SolidColorBrush(Colors.LimeGreen);

        if (clickHandler != null) btn.Click += clickHandler;
        ToolTipService.SetToolTip(btn, tooltip);
        btn.Tag = underline;
        return btn;
    }

    private Border CreateToolbarOverlay()
    {
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        var flyoutOpen = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Open", Icon = new SymbolIcon(Symbol.OpenFile) };

        var loaddiscItem = new MenuFlyoutItem
        {
            UseLayoutRounding = true,
            Text = "Load Disc... ",
            Icon = new FontIcon
            {
                Glyph = "\uE958",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
            }
        };

        loaddiscItem.Click += async (x, y) => { await LoadPhysicalDiscAsync(); };

        flyoutOpen.Click += OnOpenSingleFile;
        flyout.Items.Add(flyoutOpen);
        flyout.Items.Add(loaddiscItem);
        flyout.Items.Add(new MenuFlyoutSeparator { UseLayoutRounding = true });

        var flyoutExport = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Export Raw Frames", Icon = new SymbolIcon(Symbol.Video) };
        flyoutExport.Click += IndependentProbe_Click;
        flyout.Items.Add(flyoutExport);

        var flyoutSettings = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Settings", Icon = new SymbolIcon(Symbol.Setting) };
        flyoutSettings.Click += (s, e) => { ShowConfigWindow(); };
        flyout.Items.Add(flyoutSettings);
        flyout.Items.Add(new MenuFlyoutSeparator { UseLayoutRounding = true });

        var flyoutExit = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Exit", Icon = new SymbolIcon(Symbol.Cancel) };
        flyoutExit.Click += (s, e) => Close();
        flyout.Items.Add(flyoutExit);

        var fileBtn = new Button
        {
            UseLayoutRounding = true,
            Content = new FontIcon
            {
                UseLayoutRounding = true,
                Glyph = "\uE838",
                FontSize = 18,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = new SolidColorBrush(Colors.White)
            },

            Flyout = flyout,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            IsTabStop = false
        };

        Rectangle Separator() => new Rectangle
        {
            UseLayoutRounding = true,
            Width = 1,
            Height = 16,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(80, 255, 255, 255)),
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        Button MakeWinBtn(string glyph, string tip, Action onClick)
        {
            var b = new Button
            {
                UseLayoutRounding = true,
                Content = new FontIcon { UseLayoutRounding = true, Glyph = glyph, FontSize = 11, Foreground = new SolidColorBrush(Colors.White) },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(9, 6, 9, 6),
                CornerRadius = new CornerRadius(8),
                IsTabStop = false
            };

            ToolTipService.SetToolTip(b, tip);
            b.Click += (s, e) => onClick();
            return b;
        }

        var minimizeBtn = MakeWinBtn("", "Minimize", () =>
        {
            if (m_AppWindow.Presenter is OverlappedPresenter op)
            {
                rootGrid?.UpdateLayout();
                op.Minimize();
            }
        });

        var windowedBtn = MakeWinBtn("", "Restore", async () =>
        {
            if (m_AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            {
                ToggleFullscreen();
            }
        });

        var fullscreenBtn = MakeWinBtn("\uE740", "Fullscreen", async () =>
        {
            if (m_AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
            {
                ToggleFullscreen();
            }
        });

        var closeBtn = MakeWinBtn("", "Close", () => Close());
        closeBtn.PointerEntered += (s, e) => closeBtn.Background = new SolidColorBrush(ColorHelper.FromArgb(200, 196, 43, 28));
        closeBtn.PointerExited += (s, e) => closeBtn.Background = new SolidColorBrush(Colors.Transparent);

        var stack = new StackPanel
        {
            UseLayoutRounding = true,
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        stack.Children.Add(fileBtn);
        stack.Children.Add(Separator());
        stack.Children.Add(minimizeBtn);
        stack.Children.Add(windowedBtn);
        stack.Children.Add(fullscreenBtn);
        stack.Children.Add(closeBtn);

        var toolbar = new Border
        {
            UseLayoutRounding = true,
            Child = stack,
            Background = new SolidColorBrush(ColorHelper.FromArgb(160, 18, 18, 18)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = GetSystemCornerRadius(),
            Padding = new Thickness(4, 3, 4, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 12, 0),
            Visibility = Visibility.Collapsed
        };

        toolbar.PointerEntered += (x, y) => { IsPointerInToolbarOverlay = true; };
        toolbar.PointerExited += (x, y) => { IsPointerInToolbarOverlay = false; };
        Grid.SetRow(toolbar, 0);
        Grid.SetRowSpan(toolbar, 2);
        Canvas.SetZIndex(toolbar, 200);
        return toolbar;
    }
    public void RemoveNativeWindowBorder()
    {
        const uint WS_DLGFRAME = 0x00400000;
        var color = 0xFFFFFFFE;
        DwmSetWindowAttribute(_hwnd, 34, ref color, sizeof(int));
        SetWindowLong(_hwnd, GWL_STYLE, (int)(GetWindowLong(_hwnd, GWL_STYLE) & ~(WS_DLGFRAME)));
        var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 1, cyBottomHeight = 0 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
    }

    private void ToggleBorderless()
    {
        if (m_AppWindow == null) return;

        if (m_AppWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.HasTitleBar)
            {
                ExtendsContentIntoTitleBar = false;
                presenter.SetBorderAndTitleBar(false, false);
                uint style = (uint)GetWindowLong(_hwnd, GWL_STYLE);
                style &= ~(WS_CAPTION | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
                style |= WS_THICKFRAME;
                SetWindowLong(_hwnd, GWL_STYLE, (int)style);

                if (Content is Grid root)
                {
                    root.RowDefinitions[0].Height = new GridLength(0);
                    root.Margin = new Thickness(0, 0, 0, 0);
                }


                if (_borderlessToolbar == null)
                {
                    _borderlessToolbar = CreateToolbarOverlay();

                    if (Content is Grid rg)
                    {
                        rg.Children.Add(_borderlessToolbar);
                        InitializeToolbarLoadingRing(rg);
                    }
                }

                _borderlessToolbar.Visibility = Visibility.Visible;
                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER);
                uint cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));
                EnableBorderlessInteractions(rootGrid);
                RemoveNativeWindowBorder();
            }
            else
            {
                presenter.SetBorderAndTitleBar(true, true);
                ExtendsContentIntoTitleBar = true;
                uint style = (uint)GetWindowLong(_hwnd, GWL_STYLE);
                style |= WS_CAPTION | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
                SetWindowLong(_hwnd, GWL_STYLE, (int)style);

                if (this.Content is Grid root)
                {
                    root.RowDefinitions[0].Height = GridLength.Auto;
                    root.Margin = new Thickness(0, 0, 0, 0);
                }

                if (_borderlessToolbar != null)
                    _borderlessToolbar.Visibility = Visibility.Collapsed;

                var titleBar = m_AppWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonForegroundColor = Colors.White;

                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER);
                uint cornerPreference = DWMWCP_DEFAULT;
                DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));
                DisableBorderlessInteractions(rootGrid);
            }

            SaveSettings();
        }
    }

    private void InitializeEmptyStateText(Grid container)
    {
        var emptyText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = "+ Click to load or Drag & Drop to play the file",
            FontSize = 22,
            Foreground = new SolidColorBrush(Colors.Gray),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = true,
            AllowDrop = true
        };

        Canvas.SetZIndex(emptyText, 500);
        // WIRE UP THE CLICK HANDLER HERE
        emptyText.PointerPressed += (s, e) =>
        {
            // Check for left click
            if (e.GetCurrentPoint(emptyText).Properties.IsLeftButtonPressed)
            {
                OnOpenSingleFile(s, e);
            }
        };

        container.Children.Add(emptyText);
        _emptyStateText = emptyText;
    }
    private void InitializePlayPauseOverlay(Grid parent)
    {
        _playPauseStatusText = new TextBlock
        {
            UseLayoutRounding = true,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 64, // Icons usually look better slightly larger
            Foreground = new SolidColorBrush(Colors.LightGreen),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };

        _playPauseStatusOverlay = new Border
        {
            UseLayoutRounding = true,
            Child = _playPauseStatusText,
            IsHitTestVisible = false,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(ColorHelper.FromArgb(120, 0, 0, 0)),
            CornerRadius = new CornerRadius(55),
            Width = 110,
            Height = 110
        };

        Canvas.SetZIndex(_playPauseStatusOverlay, 100);
        parent.Children.Add(_playPauseStatusOverlay);
    }
    WindowCaptureManager activeMirror; public async void ShowDisplaySelectionFlyout(FrameworkElement anchor)
    {
        if (canvasDevice == null || _player.MediaPlayer.Source == null)
        {
            await ShowErrorDialogAsync("Media Source Error", "Make sure a video/audio file is loaded to start mirror mode.");
            return;
        }

        var displays = DisplayArea.FindAll();
        var stack = new StackPanel { UseLayoutRounding = true, Spacing = 10, Padding = new Thickness(10) };
        stack.Children.Add(new TextBlock { UseLayoutRounding = true, Text = "Select Target Display:", FontWeight = FontWeights.Bold });
        var displayCombo = new ComboBox { UseLayoutRounding = true, HorizontalAlignment = HorizontalAlignment.Stretch };

        for (int i = 0; i < displays.Count; i++)
        {
            var area = displays[i];
            string name = area.IsPrimary ? $"Display {i + 1} (Primary)" : $"Display {i + 1}";
            displayCombo.Items.Add($"{name} - {area.OuterBounds.Width}x{area.OuterBounds.Height}");
        }

        displayCombo.SelectedIndex = 0;
        stack.Children.Add(displayCombo);
        stack.Children.Add(new TextBlock { UseLayoutRounding = true, Text = "Window Mode:", FontWeight = FontWeights.Bold });
        var modeCombo = new ComboBox { UseLayoutRounding = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        modeCombo.Items.Add("Windowed (16:9)");
        modeCombo.Items.Add("Fullscreen");
        modeCombo.Items.Add("Maximized");
        modeCombo.SelectedIndex = 0;
        stack.Children.Add(modeCombo);

        var confirmBtn = new Button
        {
            UseLayoutRounding = true,
            Content = "Start Mirror Mode",
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            IsTabStop = false
        };

        stack.Children.Add(confirmBtn);
        var flyout = new Flyout { Content = stack };

        confirmBtn.Click += (s, e) =>
        {
            if (activeMirror != null)
            {
                _ = ShowErrorDialogAsync("Warning", "Only 1 instance of mirror available at a time");
                return;
            }

            int displayIdx = displayCombo.SelectedIndex;
            int modeIdx = modeCombo.SelectedIndex;
            if (displayIdx < 0) return;

            var targetDisplay = displays[displayIdx];

            DispatcherQueue.TryEnqueue(async () =>
            {
                var viewWindow = new WindowCaptureManager();
                bool success = await viewWindow.StartPickerCaptureAsync(this, canvasDevice);

                if (!success) return;

                activeMirror = viewWindow;
                var appWin = viewWindow.window.AppWindow;
                var workArea = targetDisplay.WorkArea;

                // Apply Windowing Mode
                switch (modeIdx)
                {
                    case 0: // Windowed
                        appWin.SetPresenter(AppWindowPresenterKind.Overlapped);
                        // Standard 720p 16:9 start
                        appWin.MoveAndResize(new RectInt32(workArea.X + 50, workArea.Y + 50, 1280, 720));
                        break;

                    case 1: // Fullscreen
                        appWin.SetPresenter(AppWindowPresenterKind.FullScreen);
                        // Move to the target display before entering fullscreen
                        appWin.Move(new PointInt32(workArea.X, workArea.Y));
                        break;

                    case 2: // Maximized
                        var presenter = appWin.Presenter as OverlappedPresenter;
                        if (presenter != null)
                        {
                            // Ensure it's on the right screen before maximizing
                            appWin.Move(new PointInt32(workArea.X, workArea.Y));
                            presenter.Maximize();
                        }
                        break;
                }

                viewWindow.window.Closed += (x, y) => { activeMirror = null; };
                viewWindow.window.Activate();
            });

            flyout.Hide();
        };

        flyout.ShowAt(anchor);
    }
    private void CleanupResources(object sender, WindowEventArgs evt)
    {
        Closed -= CleanupResources;
        try { _radioLoadCts?.Cancel(); _radioLoadCts?.Dispose(); _radioLoadCts = null; } catch { }
        try { MainTokenSource.Cancel(); } catch { }
        try { activeThumbnailWorker?.Cancel(); activeThumbnailWorker?.Dispose(); activeThumbnailWorker = null; } catch { }
        try { _thumbLoaderCts?.Cancel(); _thumbLoaderCts?.Dispose(); _thumbLoaderCts = null; } catch { }
        try { _resizeCts?.Cancel(); _resizeCts?.Dispose(); _resizeCts = null; } catch { }
        try { _playPauseOverlayCts?.Cancel(); _playPauseOverlayCts?.Dispose(); _playPauseOverlayCts = null; } catch { }
        try { clickTokenSource?.Cancel(); clickTokenSource?.Dispose(); clickTokenSource = null; } catch { }
        try { _infoUpdateTimer?.Stop(); _infoUpdateTimer = null; } catch { }
        try { _nowPlayingStoryboard?.Stop(); _nowPlayingStoryboard = null; } catch { }
        try { _fadeOutStoryboard?.Stop(); _fadeOutStoryboard = null; } catch { }

        try
        {
            if (_radioPlayer != null)
            {
                _radioPlayer.Pause();
                _radioPlayer.Source = null;
                _radioPlayer.Dispose();
                _radioPlayer = null;
            }
        }
        catch { }

        try
        {
            if (_player?.MediaPlayer != null)
            {
                timelineController.Pause();
                _player.MediaPlayer.TimelineController = null;
                _player.MediaPlayer.Source = null;
            }
            timelineController = null;
        }
        catch { }

        try
        {
            audioEngine?.Pause();
            audioEngine?.Dispose();
            audioEngine = null;
        }
        catch { }

        try
        {
            if (canvas != null)
            {
                canvas.Update -= _updateHandler;
                canvas.Draw -= _drawHandler;
                canvas.Paused = true;
            }
            _updateHandler = null;
            _drawHandler = null;
        }
        catch { }

        // 6. Release GPU atlas resources
        try
        {
            foreach (var atm in _gpuAtlases)
                atm?.Dispose();
            _gpuAtlases.Clear();
        }
        catch { }

        try { _canvasImageSource = null; } catch { }
        try { canvasDevice?.Dispose(); canvasDevice = null; } catch { }

        try
        {
            if (_bgWindow != null)
            {
                _bgWindow.Shutdown();
                _bgWindow.Close();
                _bgWindow = null;
            }
        }
        catch { }

        try
        {
            if (_hwnd != IntPtr.Zero && _subclassProc != null)
            {
                RemoveWindowSubclass(_hwnd, _subclassProc, 0);
                _subclassProc = null;
            }
        }
        catch { }

        try { _acrylicController?.Dispose(); _acrylicController = null; } catch { }

        try
        {
            foreach (var src in _thumbnailSources.Values)
                src?.Dispose();
            _thumbnailSources.Clear();
        }
        catch { }

        try
        {
            if (_thumbnailPopup != null)
            {
                _thumbnailPopup.IsOpen = false;
                _thumbnailPopup = null;
            }
            try { _radioLoadCts?.Cancel(); _radioLoadCts?.Dispose(); _radioLoadCts = null; } catch { }
            try { _radioUrlMap?.Clear(); } catch { }
            try { _allStations?.Clear(); } catch { }
            try { _radioCollection?.Clear(); } catch { }
            backgroundThread.Join();
        }
        catch { }

        try { SaveSettings(); } catch { }
    }


    private void ShowConfigWindow()
    {
        var settingsWindow = new Window();
        settingsWindow.Title = "Configuration";
        IntPtr hwnd = WindowNative.GetWindowHandle(settingsWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWin = AppWindow.GetFromWindowId(windowId);

        // 1. Force Size & Disable Resize
        appWin.Resize(new SizeInt32(500, 500));

        if (appWin.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        // 2. FORCE DARK MODE TITLE BAR
        var titleBar = appWin.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true; // Allows us to color the background

        // Set button colors to match a dark theme so they don't disappear
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Colors.White;
        titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(50, 255, 255, 255);
        titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(90, 255, 255, 255);

        Grid rootGrid = new Grid
        {
            UseLayoutRounding = true,
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 28, 28))
        };

        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // For custom title bar space
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleSpacer = new Grid { UseLayoutRounding = true, Height = 32, Background = new SolidColorBrush(Colors.Transparent) };
        settingsWindow.SetTitleBar(titleSpacer);
        Grid.SetRow(titleSpacer, 0);
        rootGrid.Children.Add(titleSpacer);

        var scrollViewer = new ScrollViewer { UseLayoutRounding = true, Padding = new Thickness(20) };
        Grid.SetRow(scrollViewer, 1);

        var contentStack = new StackPanel { UseLayoutRounding = true, Spacing = 15 };

        contentStack.Children.Add(new TextBlock
        {
            UseLayoutRounding = true,
            FontSize = 20,
            FontWeight = FontWeights.Light,
            Foreground = new SolidColorBrush(Colors.WhiteSmoke)
        });

        contentStack.Children.Add(new ToggleSwitch
        {
            UseLayoutRounding = true,
            Header = "High Quality Scaling (Bicubic)",
            IsOn = true,
            Foreground = new SolidColorBrush(Colors.WhiteSmoke)
        });

        contentStack.Children.Add(new ToggleSwitch
        {
            UseLayoutRounding = true,
            Header = "GPU Hardware Decoding",
            IsOn = true,
            Foreground = new SolidColorBrush(Colors.WhiteSmoke)
        });

        contentStack.Children.Add(new Slider
        {
            UseLayoutRounding = true,
            Header = "Audio Pre-buffer (ms)",
            Minimum = 0,
            Maximum = 2000,
            Value = 500,
            Foreground = new SolidColorBrush(Colors.WhiteSmoke)
        });

        var saveBtn = new Button
        {
            UseLayoutRounding = true,
            Content = "Apply Changes",
            Margin = new Thickness(0, 20, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsTabStop = false
        };

        saveBtn.Click += (s, e) => settingsWindow.Close();
        contentStack.Children.Add(saveBtn);
        scrollViewer.Content = contentStack;
        rootGrid.Children.Add(scrollViewer);

        settingsWindow.Content = rootGrid;
        settingsWindow.Activate();
    }

    private void ShowVisualizerOverlay(bool createNew = false)
    {
        if (visualIzerWasInitilized && !createNew)
        {
            RegisterCanvasVisualizer();
            return;
        }

        visualIzerWasInitilized = true;

        canvas = new CanvasAnimatedControl
        {
            ClearColor = Colors.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            Background = new SolidColorBrush(Colors.Transparent),
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };

        contextMenu = new MenuFlyout();

        var wallpaperItem = new ToggleMenuFlyoutItem
        {
            UseLayoutRounding = true,
            Text = "Set As Live Wallpaper",
            Icon = new SymbolIcon(Symbol.Pictures),
            IsChecked = liveWallpaperIsOn  // ← initialize from current state
        };

        wallpaperItem.Click += (s, e) =>
        {
            liveWallpaperIsOn = wallpaperItem.IsChecked;
            SetLiveWallpaper(liveWallpaperIsOn);
        };

        contextMenu.Items.Add(wallpaperItem);
        contextMenu.Items.Add(new MenuFlyoutSeparator { UseLayoutRounding = true });

        var modeSubMenu = new MenuFlyoutSubItem
        {
            UseLayoutRounding = true,
            Text = "Visualizer Presets",
            Icon = new SymbolIcon(Symbol.Audio)
        };

        foreach (VisualizerMode mode in Enum.GetValues(typeof(VisualizerMode)))
        {
            var item = new ToggleMenuFlyoutItem
            {
                UseLayoutRounding = true,
                Text = mode.ToString(),
                IsChecked = (audioEngine != null && audioEngine.Mode == mode)
            };

            item.Click += (s, e) =>
            {
                if (audioEngine != null)
                {
                    audioEngine.Mode = mode;
                    foreach (var m in modeSubMenu.Items)
                        if (m is ToggleMenuFlyoutItem t) t.IsChecked = (t.Text == mode.ToString());
                }
            };

            modeSubMenu.Items.Add(item);
        }

        contextMenu.Items.Add(modeSubMenu);

        _visualizerContainer.RightTapped += (s, e) =>
        {
            contextMenu.ShowAt(_visualizerContainer, e.GetPosition(_visualizerContainer));
        };

        _updateHandler = (s, e) => audioEngine?.Update(s, e);
        _drawHandler = (s, e) => audioEngine?.Draw(s, e);
        _visualizerContainer.Child = canvas;
        RegisterCanvasVisualizer();
    }
    private void ShowEqualizerOverlay(FrameworkElement anchor)
    {
        var flyout = new Flyout();
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, 1000));
        style.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, new SolidColorBrush(Colors.Transparent)));
        style.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(0)));
        flyout.FlyoutPresenterStyle = style;
        flyout.ShouldConstrainToRootBounds = false;

        var mainPanel = new Border
        {
            UseLayoutRounding = true,
            Width = 640,
            Padding = new Thickness(20, 15, 20, 15),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(230, 12, 12, 12)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };

        var rootLayout = new StackPanel { Spacing = 12 };

        rootLayout.Children.Add(new TextBlock
        {
            UseLayoutRounding = true,
            Text = "AUDIO EQUALIZER",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.Gray),
            HorizontalAlignment = HorizontalAlignment.Center,
            CharacterSpacing = 150
        });

        var eqGrid = new Grid { UseLayoutRounding = true, HorizontalAlignment = HorizontalAlignment.Stretch };

        for (int i = 0; i < 10; i++)
        {
            eqGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        string[] labels = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

        for (int i = 0; i < 10; i++)
        {
            int bandIndex = i;
            var bandStack = new StackPanel { UseLayoutRounding = true, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            double savedDb = _currentSettings.Equalizer.BandGains[bandIndex];

            if (double.IsNaN(savedDb) || double.IsInfinity(savedDb)) savedDb = 0.0;
            savedDb = Math.Clamp(savedDb, -30.0, 30.0);

            var slider = new Slider
            {
                UseLayoutRounding = true,
                Orientation = Orientation.Vertical,
                Height = 130,
                Minimum = -30,
                Maximum = 30,
                StepFrequency = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                Value = savedDb
            };

            slider.ValueChanged += (s, e) =>
            {
                audioEngine?.SetEqBand(bandIndex, e.NewValue);
                _currentSettings.Equalizer.BandGains[bandIndex] = e.NewValue;
            };

            var label = new TextBlock
            {
                UseLayoutRounding = true,
                Text = labels[i],
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))
            };

            bandStack.Children.Add(slider);
            bandStack.Children.Add(label);
            Grid.SetColumn(bandStack, i);
            eqGrid.Children.Add(bandStack);
        }

        rootLayout.Children.Add(eqGrid);

        var resetBtn = new Button
        {
            UseLayoutRounding = true,
            Content = "RESET",
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(15, 3, 15, 3),
            Margin = new Thickness(0, 10, 0, 0),
            IsTabStop = false
        };

        resetBtn.Click += (s, e) =>
        {
            for (int i = 0; i < 10; i++)
            {
                audioEngine.SetEqBand(i, 0.0);
            }

            _currentSettings.Equalizer.Reset();
            flyout.Hide();
            ShowEqualizerOverlay(anchor);
        };

        rootLayout.Children.Add(resetBtn);
        mainPanel.Child = rootLayout;
        flyout.Content = mainPanel;
        flyout.ShowAt(anchor);
        flyout.Closed += (x, y) => { SaveSettings(); };
    }
    public void RegisterCanvasVisualizer()
    {
        if (canvas == null) return;

        canvas.Update -= _updateHandler;
        canvas.Draw -= _drawHandler;
        canvas.Update += _updateHandler;
        canvas.Draw += _drawHandler;
    }

    private void InitializeLongRunningBackgroundWorkerTimer()
    {
        Thread thread = null;

        thread = new Thread(async () =>
        {
            var stamp = Stopwatch.GetTimestamp();
            while (!MainTokenSource.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(166.67));

                if (MainTokenSource.IsCancellationRequested)
                {
                    break;
                }

                DispatcherQueue.TryEnqueue(() => BackgroundTimer?.Invoke(TimeSpan.FromSeconds(Stopwatch.GetElapsedTime(stamp).TotalSeconds)));

            }

            thread?.Join();
        });

        backgroundThread = thread;
        thread.Priority = ThreadPriority.BelowNormal;
        thread.IsBackground = true;
        thread.Start();

    }
    private void InitializeLoadingOverlay(Grid parent)
    {
        _loadingOverlay = new Grid
        {
            UseLayoutRounding = true,
            Background = new SolidColorBrush(ColorHelper.FromArgb(180, 0, 0, 0)),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Canvas.SetZIndex(_loadingOverlay, 9999);

        var stack = new StackPanel
        {
            UseLayoutRounding = true,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12
        };

        _loadingRing = new ProgressRing
        {
            UseLayoutRounding = true,
            IsActive = false,
            Width = 150,
            Height = 150,
            Foreground = new SolidColorBrush(Colors.White)
        };

        _loadingText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = "Loading...",
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.White),
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        stack.Children.Add(_loadingRing);
        stack.Children.Add(_loadingText);
        _loadingOverlay.Children.Add(stack);
        parent.Children.Add(_loadingOverlay);
    }
    private void InitializeToolbarLoadingRing(Grid parent)
    {
        _toolbarProgressRing = new ProgressRing
        {
            UseLayoutRounding = true,
            IsActive = false,
            Foreground = new SolidColorBrush(Colors.LimeGreen)
        };

        _toolbarLoadingRing = new Border
        {
            UseLayoutRounding = true,
            Child = _toolbarProgressRing,
            Background = new SolidColorBrush(ColorHelper.FromArgb(160, 18, 18, 18)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };

        Grid.SetRow(_toolbarLoadingRing, 0);
        Grid.SetRowSpan(_toolbarLoadingRing, 2);
        Canvas.SetZIndex(_toolbarLoadingRing, 200);
        parent.Children.Add(_toolbarLoadingRing);
        _borderlessToolbar.SizeChanged += (s, e) => UpdateLoadingRingPosition();
        _toolbarLoadingRing.SizeChanged += (s, e) => UpdateLoadingRingPosition();
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DraggableOverlay))]
    public void AddCustomOverlay(FrameworkElement element)
    {
        // Wrap the element in our helper to ensure all areas are hit-testable and cursors work
        var wrapper = new DraggableOverlay(element)
        {
            UseLayoutRounding = true,
            Width = 150,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        if (_overlayLayer.Children.Contains(wrapper)) return;

        // --- Context Menu Logic ---
        var menu = new MenuFlyout();
        var loopItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Loop", Icon = new SymbolIcon(Symbol.RepeatOne) };
        var deleteItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Remove", Icon = new SymbolIcon(Symbol.Delete) };

        deleteItem.Click += (x, y) =>
        {
            if (overlays.Contains(element)) overlays.Remove(element);
            _overlayLayer.Children.Remove(wrapper);
        };

        if (element is MediaPlayerElement mpe)
        {
            loopItem.Click += (s, e) =>
            {
                if (mpe.MediaPlayer != null)
                    mpe.MediaPlayer.IsLoopingEnabled = !mpe.MediaPlayer.IsLoopingEnabled;
            };
        }
        else { loopItem.IsEnabled = false; }

        var mirrorItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Mirror", Icon = new SymbolIcon(Symbol.TwoPage) };
        mirrorItem.Click += (s, e) =>
        {
            if (element.RenderTransform is not ScaleTransform st)
            {
                element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                element.RenderTransform = new ScaleTransform { ScaleX = -1 };
            }
            else { st.ScaleX = st.ScaleX == 1 ? -1 : 1; }
        };

        var toggleItem = new MenuFlyoutItem { UseLayoutRounding = true, Text = "Disable Overlay", Icon = new SymbolIcon(Symbol.HideBcc) };

        toggleItem.Click += (s, e) =>
        {
            bool isVisible = wrapper.Opacity > 0;
            wrapper.Opacity = isVisible ? 0 : 1;
            wrapper.IsHitTestVisible = !isVisible;
            toggleItem.Text = isVisible ? "Enable Overlay" : "Disable Overlay";
        };

        menu.Items.Add(loopItem);
        menu.Items.Add(mirrorItem);
        menu.Items.Add(new MenuFlyoutSeparator { UseLayoutRounding = true });
        menu.Items.Add(toggleItem);
        menu.Items.Add(new MenuFlyoutSeparator { UseLayoutRounding = true });
        menu.Items.Add(deleteItem);
        wrapper.ContextFlyout = menu;

        // --- 8-Point Interaction Logic ---
        bool isDragging = false;
        bool isResizing = false;
        bool _t = false, _b = false, _l = false, _r = false; // Active edges

        Windows.Foundation.Point lastPoint = new Windows.Foundation.Point(0, 0);
        const double GRIP = 12; // Edge sensitivity

        wrapper.PointerPressed += (s, e) =>
        {
            var pos = e.GetCurrentPoint(wrapper).Position;

            // Detect zones
            _l = pos.X < GRIP;
            _r = pos.X > wrapper.ActualWidth - GRIP;
            _t = pos.Y < GRIP;
            _b = pos.Y > wrapper.ActualHeight - GRIP;

            isResizing = _l || _r || _t || _b;
            isDragging = !isResizing && e.GetCurrentPoint(wrapper).Properties.IsLeftButtonPressed;

            if (isResizing || isDragging)
            {
                lastPoint = e.GetCurrentPoint(_overlayLayer).Position;
                wrapper.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        };

        wrapper.PointerMoved += (s, e) =>
        {
            var currentPoint = e.GetCurrentPoint(_overlayLayer).Position;
            var pos = e.GetCurrentPoint(wrapper).Position;
            double dx = currentPoint.X - lastPoint.X;
            double dy = currentPoint.Y - lastPoint.Y;

            if (isResizing)
            {
                if (_l)
                {
                    double w = Math.Max(50, wrapper.ActualWidth - dx);
                    if (w > 50) { wrapper.Width = w; wrapper.Translation += new System.Numerics.Vector3((float)dx, 0, 0); }
                }
                if (_r) wrapper.Width = Math.Max(50, wrapper.ActualWidth + dx);
                if (_t)
                {
                    double h = Math.Max(50, wrapper.ActualHeight - dy);
                    if (h > 50) { wrapper.Height = h; wrapper.Translation += new System.Numerics.Vector3(0, (float)dy, 0); }
                }
                if (_b) wrapper.Height = Math.Max(50, wrapper.ActualHeight + dy);
            }
            else if (isDragging)
            {
                wrapper.Translation += new System.Numerics.Vector3((float)dx, (float)dy, 0);
            }

            // Cursor Logic
            bool hL = pos.X < GRIP; bool hR = pos.X > wrapper.ActualWidth - GRIP;
            bool hT = pos.Y < GRIP; bool hB = pos.Y > wrapper.ActualHeight - GRIP;

            if ((hL && hT) || (hR && hB)) wrapper.ChangeCursor(InputSystemCursorShape.SizeNorthwestSoutheast);
            else if ((hR && hT) || (hL && hB)) wrapper.ChangeCursor(InputSystemCursorShape.SizeNortheastSouthwest);
            else if (hL || hR) wrapper.ChangeCursor(InputSystemCursorShape.SizeWestEast);
            else if (hT || hB) wrapper.ChangeCursor(InputSystemCursorShape.SizeNorthSouth);
            else wrapper.ChangeCursor(isDragging ? InputSystemCursorShape.SizeAll : InputSystemCursorShape.Arrow);

            lastPoint = currentPoint;
        };

        wrapper.PointerReleased += (s, e) =>
        {
            isDragging = isResizing = false;
            _t = _b = _l = _r = false;
            wrapper.ReleasePointerCapture(e.Pointer);
            wrapper.ChangeCursor(InputSystemCursorShape.Arrow);
        };

        _overlayLayer.Children.Add(wrapper);
    }
    private async Task ShowDiscErrorDialog(string title, string content)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = ShowErrorDialogAsync(title, content);
        });
    }

    public async Task ShowErrorDialogAsync(string title, string content)
    {
        try
        {
            var dialog = new ContentDialog
            {
                UseLayoutRounding = true,
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                RequestedTheme = ElementTheme.Dark
            };

            XamlRoot xr = null;

            if (Content.XamlRoot != null)
                xr = Content.XamlRoot;
            else if (Content is FrameworkElement fe && fe.XamlRoot != null)
                xr = fe.XamlRoot;
            else if (Content?.XamlRoot != null)
                xr = Content.XamlRoot;

            if (xr != null)
                dialog.XamlRoot = xr;

            if (dialog.XamlRoot == null)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(dialog, _hwnd);
            }

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Print("Failed to show dialog: " + ex.Message);
        }
    }
    private void ToggleLiveWallpaper()
    {
        if (_bgWindow == null)
        {
            _bgWindow = new LiveWallpaper(audioEngine);
            _bgWindow.Activate();
        }
        else
        {
            _bgWindow.Shutdown();
            _bgWindow.Close();
            _bgWindow = null;
        }
    }
    private void ToggleInfoOverlay()
    {
        try
        {
            // If overlay is already showing — close it (true toggle)
            if (_infoOverlay != null)
            {
                _infoUpdateTimer?.Stop();
                try
                {
                    if (_player?.Parent is Border b && b.Parent is Grid g)
                        g.Children.Remove(_infoOverlay);
                    else
                        (_player?.Parent as Panel)?.Children.Remove(_infoOverlay);
                }
                catch { }
                _infoOverlay = null;

                // Unhook source change listener
                if (_player?.MediaPlayer != null)
                    _player.MediaPlayer.SourceChanged -= OnInfoOverlaySourceChanged;

                return;
            }

            // No active source — nothing to show
            bool hasSource = _player?.MediaPlayer?.Source != null
                          && _player.MediaPlayer.PlaybackSession != null
                          && _player.MediaPlayer.PlaybackSession.PlaybackState != MediaPlaybackState.None;

            bool isRadio = fileType == FileType.Radio;

            if (!hasSource && !isRadio)
            {
                Log.Print("Media Info: No active source.");
                return;
            }

            BuildInfoOverlay();
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }

    private void BuildInfoOverlay()
    {
        try
        {
            var stack = new StackPanel { UseLayoutRounding = true, Spacing = 4, Margin = new Thickness(15) };

            void AddRow(string label, string val)
            {
                stack.Children.Add(new TextBlock
                {
                    UseLayoutRounding = true,
                    Text = $"{label}: {val}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            stack.Children.Add(new TextBlock
            {
                UseLayoutRounding = true,
                Text = "Media Info",
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.Cyan),
                Margin = new Thickness(0, 0, 0, 5)
            });

            if (fileType == FileType.Radio)
            {
                AddRow("Source", "Internet Radio");
                AddRow("Station", _currentStation?.Name ?? "Unknown");
                AddRow("Bitrate", _currentStation?.Bitrate > 0 ? $"{_currentStation.Bitrate} kbps" : "Unknown");
                AddRow("Country", string.IsNullOrEmpty(_currentStation?.Country) ? "Unknown" : _currentStation.Country);
            }
            else
            {
                var f = _currentThumbnailFile;
                var session = _player?.MediaPlayer?.PlaybackSession;

                string name = f?.Name ?? "Unknown";
                string path = f?.Path ?? string.Empty;

                string size = "Unknown";
                try
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        size = $"{new FileInfo(path).Length / 1024.0 / 1024.0:F2} MB";
                }
                catch { }

                string res = "N/A";
                try
                {
                    int w = (int)(session?.NaturalVideoWidth ?? 0u);
                    int h = (int)(session?.NaturalVideoHeight ?? 0u);
                    res = w > 0 && h > 0 ? $"{w} x {h}" : "Audio only";
                }
                catch { }

                AddRow("Name", name);
                AddRow("Resolution", res);
                AddRow("Size", size);
            }

            _dynamicInfoText = new TextBlock
            {
                UseLayoutRounding = true,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Yellow),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            stack.Children.Add(_dynamicInfoText);

            _infoOverlay = new Border
            {
                UseLayoutRounding = true,
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 70, 20, 0),
                MaxWidth = 350,
                Child = stack
            };

            Canvas.SetZIndex(_infoOverlay, 100);

            _infoOverlay.Unloaded += (a, e) =>
            {
                _infoUpdateTimer?.Stop();
                if (_player?.MediaPlayer != null)
                    _player.MediaPlayer.SourceChanged -= OnInfoOverlaySourceChanged;
            };

            if (_player?.Parent is Border border && border.Parent is Grid container)
            {
                container.Children.Add(_infoOverlay);
                StartInfoUpdateTimer();

                // Hook source changes to rebuild static info
                if (_player?.MediaPlayer != null)
                {
                    _player.MediaPlayer.SourceChanged -= OnInfoOverlaySourceChanged;
                    _player.MediaPlayer.SourceChanged += OnInfoOverlaySourceChanged;
                }
            }
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }

    private void OnInfoOverlaySourceChanged(MediaPlayer sender, object args)
    {
        // Fires on background thread — dispatch to UI
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_infoOverlay == null) return;

                // Tear down and rebuild with new source info
                _infoUpdateTimer?.Stop();
                try
                {
                    if (_player?.Parent is Border b && b.Parent is Grid g)
                        g.Children.Remove(_infoOverlay);
                }
                catch { }
                _infoOverlay = null;

                BuildInfoOverlay();
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        });
    }

}
