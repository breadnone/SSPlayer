using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SSPlayer.Logs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace SSPlayer;

/// <summary>Represents a single community radio station fetched from Radio Browser.</summary>
public class RadioStation
{
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public int Bitrate { get; set; }
    public string FaviconUrl { get; set; } = string.Empty;
    public string StationUuid { get; set; } = string.Empty;

    public bool IsResolved { get; set; }

    public string Subtitle =>
        string.Join("  •  ", new[]
        {
                string.IsNullOrWhiteSpace(Country) ? null : Country,
                string.IsNullOrWhiteSpace(Codec)   ? null : Codec,
                Bitrate > 0                         ? $"{Bitrate} kbps" : null
        }.Where(x => x != null));
}

public sealed partial class MainWindow
{
    // ── Fields ──────────────────────────────────────────────────────────

    private Border _radioPanelHost;
    private readonly ObservableCollection<RadioStation> _radioCollection = new();
    private ListView _radioListView;
    private TextBox _radioSearchBox;
    private TextBlock _radioStatusText;
    private ProgressRing _radioLoadRing;
    private RadioStation _currentStation;
    private CancellationTokenSource _radioLoadCts;
    private StackPanel _radioTagBar;
    private string _activeTagFilter = string.Empty;
    private List<RadioStation> _allStations = new();
    private ComboBox _radioSourceCombo; // Added for server selection

    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private static readonly string[] ApiHosts =
    {
            "de1.api.radio-browser.info",
            "nl1.api.radio-browser.info",
            "at1.api.radio-browser.info",
    };



    public void ShowInternetRadioPanel()
    {
        BuildRadioPanel();

        if (_allStations.Count == 0)
            _ = LoadRadioStationsAsync(_activeTagFilter);
    }

    private void BuildRadioPanel()
    {
        radioPanel.Children.Clear();
        var panel = new Grid { UseLayoutRounding = true, RequestedTheme = ElementTheme.Dark };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _radioLoadRing = new ProgressRing
        {
            UseLayoutRounding = true,
            IsActive = false,
            Width = 18,
            Height = 18,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 100, 200, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };

        _radioSourceCombo = new ComboBox
        {
            UseLayoutRounding = true,
            Width = 130,
            FontSize = 11,
            Height = 28,
            Padding = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Items = { "All Servers", "Radio Browser", "SomaFM", "TuneIn" },
            SelectedIndex = 0,
            VerticalAlignment = VerticalAlignment.Center
        };

        _radioSourceCombo.SelectionChanged += async (s, e) =>
        {
            await RefreshInternetRadioServers();
        };

        var refreshBtn = MakeIconButton("\uE72C", "Refresh");

        refreshBtn.Click += async (s, e) =>
        {
            _allStations.Clear();
            _radioCollection.Clear();
            await LoadRadioStationsAsync(_activeTagFilter);
        };

        var headerStack = new StackPanel
        {
            UseLayoutRounding = true,
            Orientation = Orientation.Horizontal,
            Padding = new Thickness(14, 10, 14, 8),
            Spacing = 8,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };

        headerStack.Children.Add(_radioSourceCombo);
        headerStack.Children.Add(_radioLoadRing);
        headerStack.Children.Add(refreshBtn);
        Grid.SetRow(headerStack, 0);
        panel.Children.Add(headerStack);

        _radioSearchBox = new TextBox
        {
            UseLayoutRounding = true,
            PlaceholderText = "Search stations, countries, genres…",
            Margin = new Thickness(12, 0, 12, 8),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(ColorHelper.FromArgb(60, 255, 255, 255)),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };

        _radioSearchBox.TextChanged += (s, e) => ApplyRadioFilter(_radioSearchBox.Text);
        Grid.SetRow(_radioSearchBox, 1);
        panel.Children.Add(_radioSearchBox);

        _radioTagBar = new StackPanel { UseLayoutRounding = true, Orientation = Orientation.Horizontal, Spacing = 6 };
        string[] tags = { "All", "Pop", "Rock", "Jazz", "Classical", "Electronic", "Hip-Hop", "Country", "Reggae", "News", "Talk", "Ambient", "Lofi" };
        foreach (var t in tags) _radioTagBar.Children.Add(MakeTagPill(t));

        var tagScroll = new ScrollViewer
        {
            UseLayoutRounding = true,
            Height = 70,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Margin = new Thickness(12, 0, 12, 8),
            Content = _radioTagBar
        };

        Grid.SetRow(tagScroll, 2);
        panel.Children.Add(tagScroll);

        _radioListView = new ListView
        {
            UseLayoutRounding = true,
            ItemsSource = _radioCollection,
            SelectionMode = ListViewSelectionMode.Single,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            ItemTemplate = BuildRadioItemTemplate()
        };
        _radioListView.DoubleTapped += async (s, e) =>
        {
            e.Handled = true;
            if (_radioListView.SelectedItem is RadioStation st)
                await PlayRadioStation(st);
        };
        _radioListView.RightTapped += (s, e) =>
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is RadioStation st)
                BuildRadioContextMenu(st).ShowAt(
                    (FrameworkElement)e.OriginalSource,
                    e.GetPosition((FrameworkElement)e.OriginalSource));
        };
        Grid.SetRow(_radioListView, 3);
        panel.Children.Add(_radioListView);

        _radioStatusText = new TextBlock
        {
            Text = "Select a station to start streaming",
            FontSize = 11,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(160, 255, 255, 255)),
            Padding = new Thickness(14, 8, 14, 10),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var statusBar = new Border
        {
            UseLayoutRounding = true,
            Background = new SolidColorBrush(ColorHelper.FromArgb(80, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(30, 255, 255, 255)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _radioStatusText
        };

        Grid.SetRow(statusBar, 4);
        panel.Children.Add(statusBar);
        radioPanel.Children.Clear();
        radioPanel.Children.Add(panel);
    }
    public async Task RefreshInternetRadioServers()
    {
        _allStations.Clear();
        _radioCollection.Clear();
        await LoadRadioStationsAsync(_activeTagFilter);
    }
    private async Task LoadRadioStationsAsync(string tag = "")
    {
        _radioLoadCts?.Cancel();
        _radioLoadCts = CancellationTokenSource.CreateLinkedTokenSource(MainTokenSource.Token);
        var token = _radioLoadCts.Token;

        SetRadioLoading(true, "Fetching stations...");
        _radioCollection.Clear();

        string selectedSource = _radioSourceCombo?.SelectedItem?.ToString() ?? "All Servers";
        List<RadioStation> fetched = new List<RadioStation>();

        _http.DefaultRequestHeaders.Remove("User-Agent");
        _http.DefaultRequestHeaders.Add("User-Agent", "SSPlayer/1.0 (WinUI3 Media Player)");

        // 1. Radio Browser
        if (selectedSource == "All Servers" || selectedSource == "Radio Browser")
        {
            string tagParam = string.IsNullOrWhiteSpace(tag) || tag.Equals("All", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $"&tag={Uri.EscapeDataString(tag.ToLower())}";

            string path = $"/json/stations/search?limit=2000&order=votes&reverse=true&hidebroken=true{tagParam}";

            foreach (var host in ApiHosts)
            {
                if (token.IsCancellationRequested) break;
                try
                {
                    string url = $"https://{host}{path}";
                    var json = await _http.GetStringAsync(url, token);
                    var rbStations = ParseStationsJson(json);
                    if (rbStations?.Count > 0)
                    {
                        fetched.AddRange(rbStations);
                        break;
                    }
                }
                catch { }
            }
        }

        // 2. SomaFM
        if (!token.IsCancellationRequested && (selectedSource == "All Servers" || selectedSource == "SomaFM"))
        {
            try
            {
                var somaJson = await _http.GetStringAsync("https://somafm.com/channels.json", token);
                var somaStations = ParseSomaFmJson(somaJson);
                if (!string.IsNullOrWhiteSpace(tag) && !tag.Equals("All", StringComparison.OrdinalIgnoreCase))
                    somaStations = somaStations.Where(s => s.Tags.Contains(tag.ToLower())).ToList();
                fetched.AddRange(somaStations);
            }
            catch { }
        }

        // 3. TuneIn (RadioTime Community Search)
        if (!token.IsCancellationRequested && (selectedSource == "All Servers" || selectedSource == "TuneIn"))
        {
            try
            {
                string query = string.IsNullOrWhiteSpace(tag) || tag.Equals("All", StringComparison.OrdinalIgnoreCase) ? "top" : tag;
                var tuneInJson = await _http.GetStringAsync($"https://opml.radiotime.com/Search.ashx?query={query}&render=json", token);
                fetched.AddRange(ParseTuneInJson(tuneInJson));
            }
            catch { }
        }

        if (token.IsCancellationRequested) { SetRadioLoading(false); return; }

        if (fetched.Count == 0)
        {
            SetRadioLoading(false, "No stations found. Check connection or filter.");
            return;
        }

        _allStations = fetched;
        ApplyRadioFilter(_radioSearchBox?.Text ?? string.Empty);
        SetRadioLoading(false, $"{fetched.Count} stations loaded");
    }

    private static List<RadioStation> ParseStationsJson(string json)
    {
        var list = new List<RadioStation>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string url = el.TryGetProperty("url_resolved", out var ur) ? ur.GetString() : null;
                if (string.IsNullOrWhiteSpace(url))
                    url = el.TryGetProperty("url", out var u) ? u.GetString() : null;

                if (string.IsNullOrWhiteSpace(url)) continue;

                list.Add(new RadioStation
                {
                    Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    StreamUrl = url,
                    Country = el.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
                    Tags = el.TryGetProperty("tags", out var t) ? t.GetString() ?? "" : "",
                    Codec = el.TryGetProperty("codec", out var co) ? co.GetString() ?? "" : "",
                    Bitrate = el.TryGetProperty("bitrate", out var br) && br.TryGetInt32(out int b) ? b : 0,
                    FaviconUrl = el.TryGetProperty("favicon", out var fv) ? fv.GetString() ?? "" : "",
                    StationUuid = el.TryGetProperty("stationuuid", out var id) ? id.GetString() ?? Guid.NewGuid().ToString() : string.Empty,
                    IsResolved = true
                });
            }
        }
        catch (Exception ex) { Log.Print($"Radio Browser JSON error: {ex.Message}"); }
        return list;
    }

    private static List<RadioStation> ParseSomaFmJson(string json)
    {
        var list = new List<RadioStation>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("channels", out var channels))
            {
                foreach (var el in channels.EnumerateArray())
                {
                    string id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() : "";
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    list.Add(new RadioStation
                    {
                        Name = (el.TryGetProperty("title", out var n) ? n.GetString() : "") + " (SomaFM)",
                        StreamUrl = $"https://ice1.somafm.com/{id}-128-mp3",
                        Country = "USA",
                        Tags = el.TryGetProperty("genre", out var t) ? t.GetString()?.Replace("|", ",") ?? "" : "",
                        Codec = "MP3",
                        Bitrate = 128,
                        FaviconUrl = el.TryGetProperty("image", out var img) ? img.GetString() ?? "" : "",
                        StationUuid = $"somafm-{id}",
                        IsResolved = true
                    });
                }
            }
        }
        catch (Exception ex) { Log.Print($"SomaFM JSON error: {ex.Message}"); }
        return list;
    }

    private static List<RadioStation> ParseTuneInJson(string json)
    {
        var list = new List<RadioStation>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("body", out var body))
            {
                foreach (var el in body.EnumerateArray())
                {
                    if (el.TryGetProperty("type", out var type) && type.GetString() == "audio")
                    {
                        list.Add(new RadioStation
                        {
                            Name = el.TryGetProperty("text", out var n) ? n.GetString() + " (TuneIn)" : "TuneIn Station",
                            StreamUrl = el.TryGetProperty("URL", out var u) ? u.GetString() : "",
                            Tags = el.TryGetProperty("subtext", out var st) ? st.GetString() : "Streaming",
                            Codec = "AAC/MP3",
                            Country = "Global",
                            StationUuid = Guid.NewGuid().ToString(),
                            IsResolved = true
                        });
                    }
                }
            }
        }
        catch (Exception ex) { Log.Print($"TuneIn JSON error: {ex.Message}"); }
        return list;
    }
    // Add this field to InternetRadio.cs partial class
    private MediaPlayer _radioPlayer;
    // Make _radioPlayer accessible to the main control methods
    public MediaPlayer RadioPlayer => _radioPlayer;
    public bool IsRadioPlaying => _radioPlayer?.CurrentState == MediaPlayerState.Playing;
    private async Task PlayRadioStation(RadioStation station)
    {
        if (string.IsNullOrWhiteSpace(station?.StreamUrl)) return;

        try
        {
            SetRadioLoading(true, $"Connecting to {station.Name}…");

            // Stop the main player — don't touch its AudioGraph wiring
            MainWindow.main.PauseMedia(PlayerPlayState.Stop);

            // Dispose previous radio player cleanly
            if (_radioPlayer != null)
            {
                _radioPlayer.Pause();
                _radioPlayer.Source = null;
                _radioPlayer.Dispose();
                _radioPlayer = null;
            }

            // Fresh MediaPlayer — completely outside the AudioGraph pipeline
            _radioPlayer = new MediaPlayer
            {
                RealTimePlayback = true,
                IsMuted = false,
                Volume = _currentSettings.Volume / 100.0,
                // No TimelineController — radio plays on its own clock
            };

            var uri = new Uri(station.StreamUrl);
            var mediaSource = MediaSource.CreateFromUri(uri);
            var playbackItem = new MediaPlaybackItem(mediaSource);

            _radioPlayer.Source = playbackItem;
            _radioPlayer.Play();

            // Tell the UI we're in radio mode (disables seek bar, etc.)
            SwitchToRadioMode();

            _currentStation = station;
            SetRadioLoading(false, $"▶  {station.Name}");
            UpdateNowPlayingText(station.Name);
            AddRadioToPlaylist(station);
        }
        catch (Exception ex)
        {
            Log.Print($"Radio playback error: {ex.Message}");
            SetRadioLoading(false, "Connection Failed");
        }
    }
    public void SetRadioVolume(double volume)
    {
        if (_radioPlayer != null)
            _radioPlayer.Volume = volume;
    }
    private void AddRadioToPlaylist(RadioStation station)
    {
        string identity = string.IsNullOrEmpty(station.StationUuid) ? station.StreamUrl : $"radio://{station.StationUuid}";
        if (_playlistCollection.Any(p => p.Path == identity)) return;

        var item = new PlaylistItem
        {
            Title = $"📻 {station.Name}",
            Path = identity,
            Duration = TimeSpan.Zero,
            IsAudio = true
        };

        _playlistCollection.Add(item);
        _radioUrlMap[identity] = station.StreamUrl;
    }

    private readonly Dictionary<string, string> _radioUrlMap = new();

    public async Task<bool> TryPlayRadioByPath(string path)
    {
        if (!_radioUrlMap.TryGetValue(path, out string streamUrl)) return false;

        var item = _playlistCollection.FirstOrDefault(p => p.Path == path);
        string name = item?.Title?.Replace("📻 ", "").Trim() ?? path;
        var station = new RadioStation { Name = name, StreamUrl = streamUrl };
        await PlayRadioStation(station);
        return true;
    }

    private void ApplyRadioFilter(string query)
    {
        _radioCollection.Clear();
        var source = _allStations;

        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.ToLower();
            source = source.Where(s =>
                s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Country.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Tags.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Codec.Contains(q, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        foreach (var st in source)
            _radioCollection.Add(st);
    }

    private Button MakeTagPill(string tag)
    {
        bool isAll = tag.Equals("All", StringComparison.OrdinalIgnoreCase);
        var btn = new Button
        {
            Content = tag,
            FontSize = 10,
            Padding = new Thickness(10, 3, 10, 3),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(isAll
                ? ColorHelper.FromArgb(120, 100, 180, 255)
                : ColorHelper.FromArgb(60, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0)
        };

        btn.Click += async (s, e) =>
        {
            _activeTagFilter = isAll ? string.Empty : tag;
            foreach (var child in _radioTagBar.Children.OfType<Button>())
            {
                bool active = child.Content?.ToString() == tag;
                child.Background = new SolidColorBrush(active
                    ? ColorHelper.FromArgb(120, 100, 180, 255)
                    : ColorHelper.FromArgb(60, 255, 255, 255));
            }

            if (isAll || _allStations.Count > 0)
            {
                if (isAll) ApplyRadioFilter(_radioSearchBox?.Text ?? "");
                else
                {
                    _radioCollection.Clear();
                    string tagLower = tag.ToLower();
                    foreach (var st in _allStations.Where(s => s.Tags.Contains(tagLower, StringComparison.OrdinalIgnoreCase)))
                        _radioCollection.Add(st);
                }
            }
            else await LoadRadioStationsAsync(_activeTagFilter);
        };
        return btn;
    }

    private DataTemplate BuildRadioItemTemplate()
    {
        const string xaml = @"
<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
  <Grid Padding=""10,8"" Background=""Transparent"">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width=""36""/>
      <ColumnDefinition Width=""*""/>
      <ColumnDefinition Width=""Auto""/>
    </Grid.ColumnDefinitions>
    <Border Grid.Column=""0"" Width=""32"" Height=""32"" CornerRadius=""6""
            Background=""#1AFFFFFF"" VerticalAlignment=""Center"">
      <TextBlock Text=""&#x1F4FB;"" FontSize=""16""
                 HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
    </Border>
    <StackPanel Grid.Column=""1"" VerticalAlignment=""Center"" Margin=""8,0,8,0"">
      <TextBlock Text=""{Binding Name}"" Foreground=""White"" FontSize=""12""
                 TextTrimming=""CharacterEllipsis"" FontWeight=""SemiBold""/>
      <TextBlock Text=""{Binding Subtitle}"" Foreground=""White"" FontSize=""10""
                 Opacity=""0.55"" TextTrimming=""CharacterEllipsis""/>
    </StackPanel>
    <Border Grid.Column=""2"" CornerRadius=""3"" Padding=""4,1""
            Background=""#20FFFFFF"" VerticalAlignment=""Center"">
      <TextBlock Text=""{Binding Codec}"" FontSize=""9"" Foreground=""White"" Opacity=""0.6""/>
    </Border>
  </Grid>
</DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private MenuFlyout BuildRadioContextMenu(RadioStation station)
    {
        var menu = new MenuFlyout();
        var playItem = new MenuFlyoutItem { Text = "▶  Play", Icon = new SymbolIcon(Symbol.Play) };
        playItem.Click += async (s, e) => await PlayRadioStation(station);
        menu.Items.Add(playItem);

        var addItem = new MenuFlyoutItem { Text = "Add to Playlist", Icon = new SymbolIcon(Symbol.Add) };
        addItem.Click += (s, e) => AddRadioToPlaylist(station);
        menu.Items.Add(addItem);

        menu.Items.Add(new MenuFlyoutSeparator());
        var copyUrl = new MenuFlyoutItem { Text = "Copy Stream URL", Icon = new SymbolIcon(Symbol.Copy) };
        copyUrl.Click += (s, e) =>
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(station.StreamUrl);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        };
        menu.Items.Add(copyUrl);
        return menu;
    }

    private void SetRadioLoading(bool loading, string message = "")
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_radioLoadRing != null) _radioLoadRing.IsActive = loading;
            if (_radioStatusText != null && !string.IsNullOrEmpty(message))
                _radioStatusText.Text = message;
        });
    }

    private static Button MakeIconButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 13, Foreground = new SolidColorBrush(Colors.White) },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    private void UpdateNowPlayingText(string name)
    {
        try { Title = $"📻 {name}  —  SSPlayer"; } catch { }
    }
}