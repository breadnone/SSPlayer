using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SSPlayer;

public static class MetadataEditor
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10) // 10 seconds is plenty for a metadata API
    };

    private static readonly Dictionary<string, byte[]> _coverCache = new();
    private record MetadataPackage(string Title, string Artist, string Album, string Year, string Genre, string HighResUrl, string ThumbUrl);

    public static Flyout CreateFlyout(FrameworkElement anchor, StorageFile file, Action onSaved = null)
    {
        var stack = new StackPanel { UseLayoutRounding = true, Spacing = 8, Width = 300, Padding = new Thickness(5) };
        var currentCover = new Image { UseLayoutRounding = true, Width = 120, Height = 120, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5) };

        var coverScroller = new ScrollViewer
        {
            UseLayoutRounding = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            Height = 85,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 5, 0, 5)
        };

        var coverList = new StackPanel { UseLayoutRounding = true, Orientation = Orientation.Horizontal, Spacing = 8 };
        coverScroller.Content = coverList;

        var autoFillBtn = new Button { UseLayoutRounding = true, Content = "Auto-Fill (Search Web)", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 10 };
        byte[] dataToSave = null;

        var titleInput = new TextBox { UseLayoutRounding = true, Header = "Title", FontSize = 12 };
        var artistInput = new TextBox { UseLayoutRounding = true, Header = "Artist / Producer", FontSize = 12 };
        var albumInput = new TextBox { UseLayoutRounding = true, Header = "Album", FontSize = 12 };
        var yearInput = new TextBox { UseLayoutRounding = true, Header = "Year", FontSize = 12 };
        var genreInput = new TextBox { UseLayoutRounding = true, Header = "Genre", FontSize = 12 };

        var statusLabel = new TextBlock
        {
            UseLayoutRounding = true,
            FontSize = 11,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        var saveBtn = new Button
        {
            UseLayoutRounding = true,
            Content = "Save",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };

        var flyout = new Flyout();
        flyout.ShouldConstrainToRootBounds = false;

        var scroll = new ScrollViewer
        {
            UseLayoutRounding = true,
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = anchor.ActualHeight - 150
        };

        flyout.Content = scroll;

        autoFillBtn.Click += async (s, e) =>
        {
            flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Right });
            var profile = NetworkInformation.GetInternetConnectionProfile();
            var isConnected = profile != null && profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;

            if (!isConnected)
            {
                statusLabel.Text = "No internet connection. Please connect and try again.";
                return;
            }

            MainWindow.main.ToggleNonBlockingLoading(true);
            autoFillBtn.IsEnabled = false;
            statusLabel.Text = "Searching web...";
            coverList.Children.Clear();
            _coverCache.Clear();

            try
            {
                string query = !string.IsNullOrEmpty(titleInput.Text) ? titleInput.Text : file.Name;
                var response = await _httpClient.GetStringAsync($"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&limit=10&entity=song");

                List<MetadataPackage> packages = new();
                using (var doc = JsonDocument.Parse(response))
                {
                    var results = doc.RootElement.GetProperty("results");

                    foreach (var item in results.EnumerateArray())
                    {
                        string tUrl = item.GetProperty("artworkUrl100").GetString();
                        packages.Add(new MetadataPackage(
                            Title: item.TryGetProperty("trackName", out var t) ? t.GetString() : "",
                            Artist: item.TryGetProperty("artistName", out var a) ? a.GetString() : "",
                            Album: item.TryGetProperty("collectionName", out var al) ? al.GetString() : "",
                            Year: item.TryGetProperty("releaseDate", out var rd) ? rd.GetString().Split('-')[0] : "",
                            Genre: item.TryGetProperty("primaryGenreName", out var g) ? g.GetString() : "",
                            HighResUrl: tUrl.Replace("100x100bb", "600x600bb"),
                            ThumbUrl: tUrl
                        ));
                    }
                }

                if (packages.Count > 0)
                {
                    coverScroller.Visibility = Visibility.Visible;
                    flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Right });

                    foreach (var pkg in packages)
                    {
                        var btn = new Button { UseLayoutRounding = true, Padding = new Thickness(2), Background = null };
                        var img = new Image { UseLayoutRounding = true, Source = new BitmapImage(new Uri(pkg.ThumbUrl)), Width = 60, Height = 60, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                        btn.Content = img;

                        btn.Click += async (sb, eb) =>
                        {
                            statusLabel.Text = "Applying selection...";

                            try
                            {
                                if (!_coverCache.TryGetValue(pkg.HighResUrl, out byte[] imgBytes))
                                {
                                    imgBytes = await _httpClient.GetByteArrayAsync(pkg.HighResUrl);
                                    _coverCache[pkg.HighResUrl] = imgBytes;
                                }

                                dataToSave = imgBytes;

                                using var ms = new InMemoryRandomAccessStream();
                                using (var writer = new DataWriter(ms.GetOutputStreamAt(0)))
                                {
                                    writer.WriteBytes(imgBytes);
                                    await writer.StoreAsync();
                                }

                                ms.Seek(0);
                                var bmp = new BitmapImage();
                                await bmp.SetSourceAsync(ms);
                                currentCover.Source = bmp;

                                titleInput.Text = pkg.Title;
                                artistInput.Text = pkg.Artist;
                                albumInput.Text = pkg.Album;
                                yearInput.Text = pkg.Year;
                                genreInput.Text = pkg.Genre;

                                statusLabel.Text = "Selection applied.";
                            }
                            catch (Exception ex) { statusLabel.Text = "Selection Error: " + ex.Message; }
                        };
                        coverList.Children.Add(btn);
                    }
                    statusLabel.Text = "Pick a cover.";
                }
                else { statusLabel.Text = "No results."; }
            }
            catch (Exception ex) { statusLabel.Text = "Search Error: " + ex.Message; }
            finally
            {
                autoFillBtn.IsEnabled = true;
                MainWindow.main.ToggleNonBlockingLoading(false);
            }
        };

        saveBtn.Click += async (s, e) =>
        {
            MainWindow.main.ToggleNonBlockingLoading(true);
            saveBtn.IsEnabled = false;
            statusLabel.Text = "Saving...";

            try
            {
                var m = await file.Properties.GetMusicPropertiesAsync();

                // Update text properties
                m.Title = titleInput.Text ?? "";
                m.Artist = artistInput.Text ?? "";
                m.Album = albumInput.Text ?? "";

                // Validate and set year
                if (uint.TryParse(yearInput.Text, out uint y) && y >= 1000 && y <= 9999)
                {
                    m.Year = y;
                }
                else if (!string.IsNullOrWhiteSpace(yearInput.Text))
                {
                    statusLabel.Text = "Year must be between 1000-9999";
                    saveBtn.IsEnabled = true;
                    return;
                }

                // Update genre
                m.Genre.Clear();
                if (!string.IsNullOrWhiteSpace(genreInput.Text))
                {
                    m.Genre.Add(genreInput.Text);
                }

                // Save music properties first
                await m.SavePropertiesAsync();

                statusLabel.Text = "Updated successfully!";
                onSaved?.Invoke();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error: " + ex.Message;
                System.Diagnostics.Debug.WriteLine($"Save error: {ex}");
            }
            finally
            {
                saveBtn.IsEnabled = true;
                MainWindow.main.ToggleNonBlockingLoading(false);
            }
        };

        stack.Children.Add(currentCover);
        stack.Children.Add(coverScroller);
        var btns = new StackPanel { UseLayoutRounding = true, Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10 };
        btns.Children.Add(autoFillBtn);
        stack.Children.Add(btns);
        stack.Children.Add(titleInput);
        stack.Children.Add(artistInput);
        stack.Children.Add(albumInput);
        stack.Children.Add(yearInput);
        stack.Children.Add(genreInput);
        stack.Children.Add(statusLabel);
        stack.Children.Add(saveBtn);

        flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Right });

        _ = LoadMetadata(file, titleInput, artistInput, albumInput, yearInput, genreInput, currentCover);
        return flyout;
    }

    private static async Task LoadMetadata(StorageFile file, TextBox t, TextBox art, TextBox alb, TextBox yr, TextBox gen, Image img)
    {
        try
        {
            var m = await file.Properties.GetMusicPropertiesAsync();
            t.Text = m.Title ?? ""; art.Text = m.Artist ?? "";
            alb.Text = m.Album ?? ""; yr.Text = m.Year > 0 ? m.Year.ToString() : "";
            gen.Text = m.Genre.FirstOrDefault() ?? "";

            var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.MusicView);
            if (thumb != null) { var bmp = new BitmapImage(); await bmp.SetSourceAsync(thumb); img.Source = bmp; }
        }
        catch { t.Text = file.Name; }
    }

}