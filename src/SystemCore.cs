using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using SSPlayer.Logs;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
using static SSPlayer.Win32;

namespace SSPlayer;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2257", Justification = "False positive — INotifyPropertyChanged is not IDynamicInterfaceCastable")]
public sealed partial class MainWindow
{
    private void SaveSettings()
    {
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_currentSettings, AppJsonContext.Default.PlayerSettings));
        }
        catch (Exception ex)
        {
            this.IsSafeOrThrow(ex);
            Log.Print($"SaveSettings failed: {ex.Message} | Path: {_settingsPath}");
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _currentSettings = new PlayerSettings();
                SaveSettings();
                return;
            }

            var loaded = JsonSerializer.Deserialize(File.ReadAllText(_settingsPath), AppJsonContext.Default.PlayerSettings);

            if (loaded == null)
            {
                _currentSettings = new PlayerSettings();
                SaveSettings();
                return;
            }

            loaded.Equalizer ??= new EqualizerSettings();

            if (loaded.Equalizer.BandGains == null || loaded.Equalizer.BandGains.Count != 10)
                loaded.Equalizer.BandGains = new List<double> { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            _currentSettings = loaded;
        }
        catch (Exception ex)
        {
            this.IsSafeOrThrow(ex);
            Log.Print($"LoadSettings failed: {ex.Message}");
            _currentSettings = new PlayerSettings();
            SaveSettings();
        }
    }
    int screenStateIsUpdating;
    public void ToggleFullscreen()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (Interlocked.CompareExchange(ref screenStateIsUpdating, 1, 0) != 0) return;
        if (m_AppWindow == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        uint disabled = 1;
                        DwmSetWindowAttribute(_hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disabled, sizeof(uint));
                        SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);
                        SetLayeredWindowAttributes(_hwnd, 0, 0, LWA_ALPHA);
                    }
                    catch (Exception ex) { this.IsSafeOrThrow(ex); }
                });

                await Task.Delay(165);

                if (MainTokenSource.IsCancellationRequested)
                {
                    return;
                }

                if (m_AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                {
                    var tsx0 = new TaskCompletionSource<bool>();

                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        m_AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                        ExtendsContentIntoTitleBar = true;
                        uint round = DWMWCP_ROUND;
                        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(uint));
                        await Task.Delay(15);
                        if (MainTokenSource.IsCancelled()) return;
                        tsx0.TrySetResult(true);
                    });

                    await tsx0.Task.WaitAsync(MainTokenSource.Token);

                    if (MainTokenSource.IsCancelled())
                    {
                        return;
                    }

                    var tsx1 = new TaskCompletionSource<bool>();

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        SnapToAspectRatio();
                        CenterAppWindow();

                        if (MainTokenSource.IsCancelled())
                        {
                            return;
                        }

                        DispatcherQueue.TryEnqueue(() => EnableBorderlessInteractions(rootGrid));
                        tsx1.TrySetResult(true);
                    });

                    await tsx1.Task.WaitAsync(MainTokenSource.Token);
                    if (MainTokenSource.IsCancelled()) return;
                }
                else
                {
                    var tsc2 = new TaskCompletionSource<bool>();

                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            DisableBorderlessInteractions(rootGrid);
                            ExtendsContentIntoTitleBar = false;
                            uint noRound = DWMWCP_DONOTROUND;
                            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(uint));
                            m_AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                            tsc2.TrySetResult(true);
                            await Task.Delay(10, MainTokenSource.Token);
                        }
                        catch (Exception ex) { this.IsSafeOrThrow(ex); }
                    });

                    await tsc2.Task.WaitAsync(MainTokenSource.Token);

                    if (MainTokenSource.IsCancellationRequested)
                    {
                        return;
                    }
                }

                SnapToAspectRatio();
                await FadeInWindow(200);
                if (MainTokenSource.Token.IsCancellationRequested) return;

                DispatcherQueue.TryEnqueue(() =>
                {
                    ResetInactivityTimer();
                    Volatile.Write(ref screenStateIsUpdating, 0);
                    TrySetAcrylicBackdrop();
                });
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        });
    }
    private async Task FadeInWindow(int durationMs)
    {
        try
        {
            int steps = (int)double.Max(1, durationMs / 16.66);
            var delays = TimeSpan.FromMilliseconds(16.66);

            for (int i = 0; i <= steps; i++)
            {
                byte alpha = (byte)Math.Round(255.0 * i / steps);
                DispatcherQueue.TryEnqueue(() => SetLayeredWindowAttributes(_hwnd, 0, alpha, LWA_ALPHA));

                await Task.Delay(delays, MainTokenSource.Token);

                if (MainTokenSource.IsCancellationRequested)
                {
                    return;
                }
            }

            var tsc0 = new TaskCompletionSource<bool>();

            DispatcherQueue.TryEnqueue(() =>
            {
                SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
                tsc0.TrySetResult(true);
            });

            await tsc0.Task.WaitAsync(MainTokenSource.Token);
            await Task.Delay(17);
            var tsc1 = new TaskCompletionSource<bool>();

            DispatcherQueue.TryEnqueue(() =>
            {
                SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) & ~WS_EX_LAYERED);
                uint enabled = 0;
                DwmSetWindowAttribute(_hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref enabled, sizeof(uint));
                tsc1.TrySetResult(true);
            });

            await tsc1.Task.WaitAsync(MainTokenSource.Token);
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private void SnapToAspectRatio()
    {
        if (m_AppWindow == null || m_AppWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped) return;
        var p = m_AppWindow.Presenter as OverlappedPresenter;
        if (p == null || p.State != OverlappedPresenterState.Restored) return;

        GetWindowRect(_hwnd, out var wr);
        GetClientRect(_hwnd, out var cr);
        int cw = (wr.Right - wr.Left) - (cr.Right - cr.Left);
        int ch = (wr.Bottom - wr.Top) - (cr.Bottom - cr.Top);
        int tw = Math.Max(wr.Right - wr.Left, OVERLAY_WIDTH + TOTAL_HORIZONTAL_GAP + cw) - cw;

        // Ensure dimensions are even numbers — video decoders require 2-pixel alignment
        if (tw % 2 != 0) tw++;
        int th = (int)(tw / _targetAspectRatio);
        if (th % 2 != 0) th++;

        // Clamp to minimum decoder-safe size
        tw = Math.Max(tw, 32);
        th = Math.Max(th, 32);

        m_AppWindow.Resize(new SizeInt32(tw + cw, th + ch));
    }

    private void DisableBorderlessInteractions(UIElement rootElement)
    {
        _isDraggingWindow = false;

        if (_borderlessPressedHandler != null)
        {
            rootElement.PointerPressed -= _borderlessPressedHandler;
            _borderlessPressedHandler = null;
        }
        if (_borderlessMovedHandler != null)
        {
            rootElement.PointerMoved -= _borderlessMovedHandler;
            _borderlessMovedHandler = null;
        }
        if (_borderlessReleasedHandler != null)
        {
            rootElement.PointerReleased -= _borderlessReleasedHandler;
            _borderlessReleasedHandler = null;
        }
    }

    public void ToggleBlockingLoading(bool enable, string message, bool skipAnimation)
    {
        if (skipAnimation)
        {
            Log.Print($"End of BlockingLoading: {message}");
            IsLoading = enable;
            return;
        }

        if (enable)
        {
            Log.Print($"Start of BlockingLoading: {message}");
            IsLoading = true;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                Canvas.SetZIndex(_loadingOverlay, 999);
                _loadingOverlay.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
                _loadingOverlay.IsHitTestVisible = true;
                _loadingRing.IsActive = enable;
                _loadingText.Text = message; // Update the label

                if (!enable)
                {
                    Log.Print("Loading End");
                    IsLoading = false;
                }
            }
            catch (Exception ex) { this.IsSafeOrThrow(ex); }
        });
    }

    private async void JumpToTimeWithDialog()
    {
        TextBox inputField = new TextBox
        {
            UseLayoutRounding = true,
            PlaceholderText = "e.g. 1:30 or 00:45:10",
            Margin = new Thickness(0, 10, 0, 0)
        };

        ContentDialog dialog = new ContentDialog
        {
            UseLayoutRounding = true,
            Title = "Jump to Time",
            Content = new StackPanel
            {
                UseLayoutRounding = true,
                Children = {
                new TextBlock {
                    UseLayoutRounding = true,
                    Text = "Enter time (mm:ss or hh:mm:ss):",
                    FontSize = 12,
                    Opacity = 0.7
                },
                inputField
            }
            },
            PrimaryButtonText = "Go",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.Content.XamlRoot
        };

        // 3. Handle Result
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            string input = inputField.Text.Trim();

            if (TryParseTime(input, out TimeSpan targetTime))
            {
                var session = _player.MediaPlayer.PlaybackSession;

                // Bounds check
                if (targetTime < TimeSpan.Zero) targetTime = TimeSpan.Zero;
                if (targetTime > session.NaturalDuration) targetTime = session.NaturalDuration;

                session.Position = targetTime;
                Log.Print($"Jumped to time: {targetTime}");
            }
            else
            {
                Log.Print("Invalid time format.");
            }
        }
    }
    public async Task EditMetadataNative(StorageFile file, string title, string artist, string album, int tracknumber, int year)
    {
        var properties = await file.Properties.GetMusicPropertiesAsync();
        properties.Title = title;
        properties.Artist = artist;
        properties.Album = album;
        properties.TrackNumber = (uint)tracknumber;
        properties.Year = (uint)year;

        await properties.SavePropertiesAsync();
    }
    public async Task<List<(string text, string output)>> GetMetadata(StorageFile file)
    {
        var lis = new List<(string name, string output)>();
        var properties = await file.Properties.GetMusicPropertiesAsync();
        lis.Add(("title", properties.Title));
        lis.Add(("artist", properties.Artist));
        lis.Add(("album", properties.Album));
        lis.Add(("tracknumber", properties.TrackNumber.ToString()));
        lis.Add(("year", properties.Year.ToString()));
        return lis;
    }
    private bool TryParseTime(string input, out TimeSpan result)
    {
        result = TimeSpan.Zero;

        if (double.TryParse(input, out double totalSeconds))
        {
            result = TimeSpan.FromSeconds(totalSeconds);
            return true;
        }

        // Handle mm:ss or hh:mm:ss
        string[] parts = input.Split(':');

        try
        {
            if (parts.Length == 2) // mm:ss
            {
                int min = int.Parse(parts[0]);
                int sec = int.Parse(parts[1]);
                result = new TimeSpan(0, min, sec);
                return true;
            }
            else if (parts.Length == 3) // hh:mm:ss
            {
                int hr = int.Parse(parts[0]);
                int min = int.Parse(parts[1]);
                int sec = int.Parse(parts[2]);
                result = new TimeSpan(hr, min, sec);
                return true;
            }
        }
        catch { }

        return false;
    }
    private async void JumpToFrameWithDialog()
    {
        TextBox inputField = new TextBox
        {
            UseLayoutRounding = true,
            PlaceholderText = "Enter frame number...",
            Margin = new Thickness(0, 10, 0, 0),
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } }
        };

        ContentDialog dialog = new ContentDialog
        {
            UseLayoutRounding = true,
            Title = "Jump to Frame",
            Content = inputField,
            PrimaryButtonText = "Jump",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && int.TryParse(inputField.Text, out int frameNumber))
        {
            PerformFrameJump(frameNumber);
        }
    }
    private void PerformFrameJump(int frameNumber)
    {
        var session = _player.MediaPlayer.PlaybackSession;
        if (session == null || session.NaturalDuration == TimeSpan.Zero) return;

        double fps = 30.0; // Default fallback

        try
        {
            if (_player.Source is MediaPlaybackItem item)
            {
                var props = item.VideoTracks.FirstOrDefault()?.GetEncodingProperties();
                if (props != null && props.FrameRate.Denominator != 0)
                {
                    fps = (double)props.FrameRate.Numerator / props.FrameRate.Denominator;
                }
            }
        }
        catch { }

        // Calculate: (Frame / FPS) = Seconds. Convert to Ticks for high precision.
        // 1 second = 10,000,000 ticks.
        long targetTicks = (long)((frameNumber / fps) * 10000000);
        TimeSpan targetPosition = TimeSpan.FromTicks(targetTicks);

        // Bounds checking
        if (targetPosition < TimeSpan.Zero) targetPosition = TimeSpan.Zero;
        if (targetPosition > session.NaturalDuration) targetPosition = session.NaturalDuration;

        session.Position = targetPosition;
        Log.Print($"Jumped to frame {frameNumber} at {targetPosition}");
    }
    public void SetVolume(float value)
    {
        _volBtnIcon.Glyph = value <= 0 ? "\uE74F" : "\uE767";
        audioEngine?.SetVolume(value);
    }
    public bool ToggleMute()
    {
        bool muted = false;

        if (audioEngine.GetVolume() > 0)
        {
            prevvolvalue = audioEngine.GetVolume();
            SetVolume(0);
            muted = true;
        }
        else
        {
            if (prevvolvalue <= 0)
            {
                prevvolvalue = 50;
            }

            SetVolume((float)prevvolvalue * 100f);
        }

        return muted;
    }

    public void UpdateVolume(double value)
    {
        _currentSettings?.Volume = value;
        audioEngine?.SetVolume(value);

        if (fileType == FileType.Radio && _player?.MediaPlayer != null)
        {
            try { _player.MediaPlayer.Volume = value / 100.0; }
            catch { }
        }

        if (_volumeSlider != null)
        {
            _volumeSlider.Value = value;
        }

        if (_volumeOverlay != null)
        {
            _volumeOverlay.Opacity = 1;
            _volumeOverlayBar.Value = value;
            _volumeOverlayText.Text = $"{(int)value}%";
            _volumeOverlayTimer = TimeSpan.Zero;
            volumetickstop = false;
        }

        SaveSettings();
    }

    public async Task ExtractFrameAsJpegAsync(MediaComposition c, TimeSpan t, StorageFile f, BitmapPropertySet p)
    {
        try
        {
            using var s = await c.GetThumbnailAsync(t, 0, 0, VideoFramePrecision.NearestFrame);
            if (MainTokenSource.IsCancelled()) return;

            var dec = await BitmapDecoder.CreateAsync(s);
            if (MainTokenSource.IsCancelled()) return;
            using var bm = await dec.GetSoftwareBitmapAsync();
            if (MainTokenSource.IsCancelled()) return;

            using var os = await f.OpenAsync(FileAccessMode.ReadWrite);
            if (MainTokenSource.IsCancelled()) return;

            var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, os, p);
            if (MainTokenSource.IsCancelled()) return;
            enc.SetSoftwareBitmap(bm);
            await enc.FlushAsync();
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }

    public async Task ExportVideoSegmentToGif(StorageFile sourceFile, StorageFile outputFile, TimeSpan start, TimeSpan end, int fps, int scaledWidth = 480, int compressionLevel = 1)
    {
        try
        {
            if (sourceFile == null) throw new ArgumentNullException(nameof(sourceFile));
            if (outputFile == null) throw new ArgumentNullException(nameof(outputFile));
            if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));

            var clip = await MediaClip.CreateFromFileAsync(sourceFile);
            var composition = new MediaComposition();
            // clamp start/end to clip bounds
            var originalDuration = clip.OriginalDuration;
            if (start < TimeSpan.Zero) start = TimeSpan.Zero;
            if (end < TimeSpan.Zero) end = originalDuration;
            if (start > originalDuration) start = TimeSpan.Zero;
            if (end > originalDuration) end = originalDuration;
            if (end <= start) end = originalDuration;

            clip.TrimTimeFromStart = start;
            clip.TrimTimeFromEnd = originalDuration - end;
            composition.Clips.Add(clip);

            if (composition.Duration == TimeSpan.Zero)
            {
                return;
            }

            double msPerFrame = 1000.0 / fps;
            ushort delayValue = (ushort)Math.Max(3, Math.Round(msPerFrame / 10.0));
            TimeSpan frameInterval = TimeSpan.FromMilliseconds(msPerFrame);
            TimeSpan currentTime = TimeSpan.Zero;

            using (var outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.GifEncoderId, outputStream);
                var containerProperties = new BitmapPropertySet();
                containerProperties.Add("/appext/Application", new BitmapTypedValue(Encoding.ASCII.GetBytes("NETSCAPE2.0"), PropertyType.UInt8Array));
                containerProperties.Add("/appext/Data", new BitmapTypedValue(new byte[] { 0x03, 0x01, 0x00, 0x00 }, PropertyType.UInt8Array));
                await encoder.BitmapContainerProperties.SetPropertiesAsync(containerProperties);

                bool isFirstFrame = true;
                var duration = composition.Duration;

                while (currentTime < duration)
                {
                    try
                    {
                        var requestTime = currentTime >= duration ? TimeSpan.FromTicks(duration.Ticks - 1) : currentTime;

                        using (var thumbStream = await composition.GetThumbnailAsync(requestTime, scaledWidth, 0, VideoFramePrecision.NearestFrame))
                        {
                            var decoder = await BitmapDecoder.CreateAsync(thumbStream);
                            using (var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied))
                            {
                                int width = softwareBitmap.PixelWidth;
                                int height = softwareBitmap.PixelHeight;
                                byte[] pixels = new byte[4 * width * height];
                                softwareBitmap.CopyToBuffer(pixels.AsBuffer());

                                if (compressionLevel > 0)
                                {
                                    var compression = 3 + compressionLevel;
                                    // Aggressive posterization to 3 bits per channel
                                    for (int j = 0; j < pixels.Length; j += 4)
                                    {
                                        pixels[j] = (byte)((pixels[j] >> compression) << compression);
                                        pixels[j + 1] = (byte)((pixels[j + 1] >> compression) << compression);
                                        pixels[j + 2] = (byte)((pixels[j + 2] >> compression) << compression);
                                    }
                                }

                                var processedBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
                                processedBitmap.CopyFromBuffer(pixels.AsBuffer());

                                var frameProperties = new BitmapPropertySet();
                                frameProperties.Add("/grctle/Delay", new BitmapTypedValue(delayValue, PropertyType.UInt16));
                                frameProperties.Add("/grctle/Disposal", new BitmapTypedValue((uint)1, PropertyType.UInt32));

                                if (compressionLevel > 0)
                                {
                                    frameProperties.Add("Dither", new BitmapTypedValue(false, PropertyType.Boolean));
                                }

                                if (!isFirstFrame)
                                {
                                    await encoder.GoToNextFrameAsync();
                                }

                                await encoder.BitmapProperties.SetPropertiesAsync(frameProperties);
                                encoder.SetSoftwareBitmap(processedBitmap);
                                isFirstFrame = false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Frame extraction failed at {currentTime}: {ex}");
                        break;
                    }

                    currentTime += frameInterval;
                }

                await encoder.FlushAsync();
            }
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private async Task ExportMarkerRangeAsAudio(TimeSpan start, TimeSpan end, string format)
    {
        try
        {
            var semiTransBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 15, 15, 15));
            var textBrush = new SolidColorBrush(Colors.WhiteSmoke);

            var bitrateCombo = new ComboBox
            {
                UseLayoutRounding = true,
                Header = "Select Bitrate",
                ItemsSource = new string[] { "320 kbps (High)", "192 kbps (Standard)", "128 kbps (Basic)" },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = textBrush
            };

            var panel = new StackPanel { UseLayoutRounding = true, Spacing = 8, Padding = new Thickness(0, 8, 0, 0) };

            panel.Children.Add(new TextBlock
            {
                UseLayoutRounding = true,
                Text = $"Audio Range: {start:mm\\:ss\\.ff} → {end:mm\\:ss\\.ff}",
                FontSize = 12,
                Foreground = textBrush
            });

            panel.Children.Add(bitrateCombo);

            var dialog = new ContentDialog
            {
                UseLayoutRounding = true,
                Title = $"Export {format.ToUpper()} Audio",
                Content = panel,
                PrimaryButtonText = "Export",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Background = semiTransBrush
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var savePicker = new FileSavePicker();
            InitializeWithWindow.Initialize(savePicker, _hwnd);
            string ext = $".{format.ToLower()}";
            savePicker.FileTypeChoices.Add($"{format.ToUpper()} Audio", new List<string> { ext });
            savePicker.SuggestedFileName = $"AudioClip_{DateTime.Now:mm-ss}";

            var outputFile = await savePicker.PickSaveFileAsync();
            if (outputFile == null) return;

            uint selectedBitrate = bitrateCombo.SelectedIndex switch
            {
                0 => 320000,
                1 => 192000,
                _ => 128000
            };

            MediaEncodingProfile profile = format.ToLower() switch
            {
                "mp3" => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High),
                "wav" => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
                "flac" => MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High),
                "m4a" => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High),
                _ => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High)
            };

            profile.Audio.Bitrate = selectedBitrate;
            profile.Audio.SampleRate = 48000;
            ToggleNonBlockingLoading(true);

            try
            {
                bool success = await audioEngine.ExportAudioAs(outputFile, profile, (isError) =>
                {
                }, start, end);

                if (!success) throw new Exception("Audio Engine failed to finalize the file.");
            }
            catch (Exception ex)
            {
                try
                {
                    this.IsSafeOrThrow(ex);
                    await ShowSimpleDialog("Export Failed", ex.Message);
                }
                catch (Exception ex0) { this.IsSafeOrThrow(ex0); }
            }
            finally
            {
                ToggleNonBlockingLoading(false);
            }
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private async Task ExportMarkerRangeAsMp4(TimeSpan start, TimeSpan end)
    {
        try
        {
            var sourceFile = GetCurrentStorageFile();

            if (sourceFile == null) return;

            var semiTransBrush = new SolidColorBrush(Color.FromArgb(160, 15, 15, 15));
            var textBrush = new SolidColorBrush(Colors.WhiteSmoke);

            var qualityCombo = new ComboBox
            {
                UseLayoutRounding = true,
                Header = "Select Quality",
                ItemsSource = new string[] { "High (1080p)", "Standard (720p)", "Mobile (480p)" },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = textBrush
            };

            var panel = new StackPanel { UseLayoutRounding = true, Spacing = 8, Padding = new Thickness(0, 8, 0, 0) };

            panel.Children.Add(new TextBlock
            {
                UseLayoutRounding = true,
                Text = $"Range: {start:mm\\:ss\\.ff} → {end:mm\\:ss\\.ff}",
                FontSize = 12,
                Foreground = textBrush
            });

            panel.Children.Add(qualityCombo);

            var dialog = new ContentDialog
            {
                UseLayoutRounding = true,
                Title = "Export Video Clip",
                Content = panel,
                PrimaryButtonText = "Export",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Background = semiTransBrush
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var savePicker = new FileSavePicker();
            InitializeWithWindow.Initialize(savePicker, _hwnd);
            savePicker.FileTypeChoices.Add("MP4 Video", new List<string> { ".mp4" });
            savePicker.SuggestedFileName = $"Clip_{DateTime.Now:mm-ss}";

            var outputFile = await savePicker.PickSaveFileAsync();
            if (outputFile == null) return;

            var quality = qualityCombo.SelectedIndex switch
            {
                1 => VideoEncodingQuality.HD720p,
                2 => VideoEncodingQuality.Wvga,
                _ => VideoEncodingQuality.HD1080p
            };

            ToggleNonBlockingLoading(true);
            await ProcessClipExport(outputFile, start, end, quality);
        }
        catch (Exception ex)
        {
            await ShowSimpleDialog("Export Failed", ex.Message);
        }
        finally
        {
            ToggleNonBlockingLoading(false);
        }
    }
    private async Task ExportMarkerRangeAsGif(TextBlock startText, TextBlock endText)
    {
        try
        {
            var sourceFile = GetCurrentStorageFile();
            if (sourceFile == null) return;

            string[] formats = { @"hh\:mm\:ss\.ff", @"mm\:ss\.ff", @"m\:ss\.ff" };
            bool startParsed = TimeSpan.TryParseExact(startText.Text, formats, null, out var start);
            bool endParsed = TimeSpan.TryParseExact(endText.Text, formats, null, out var end);

            if (!startParsed || !endParsed)
            {
                await ShowSimpleDialog("Error", $"Could not read timestamps. Format should be mm:ss.ff");
                return;
            }

            if (end <= start)
            {
                await ShowSimpleDialog("Invalid Range", "End point must be after start point.");
                return;
            }

            // Use the same consistent colors as your other overlays
            var semiTransBrush = new SolidColorBrush(Color.FromArgb(160, 15, 15, 15));
            var textBrush = new SolidColorBrush(Colors.WhiteSmoke);

            // --- DROPDOWN SETUP ---
            var fpsOptions = new string[] { "10 FPS", "15 FPS", "24 FPS", "30 FPS" };
            int[] fpsValues = { 10, 15, 24, 30 };

            ComboBox fpsCombo = new ComboBox
            {
                UseLayoutRounding = true,
                Header = "Frames Per Second",
                ItemsSource = fpsOptions,
                SelectedIndex = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = textBrush
            };

            var resOptions = new string[] { "240p", "360p", "480p", "720p" };
            int[] resValues = { 240, 360, 480, 720 };

            ComboBox resCombo = new ComboBox
            {
                UseLayoutRounding = true,
                Header = "Resolution",
                ItemsSource = resOptions,
                SelectedIndex = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = textBrush
            };

            var compOptions = new string[] { "0 (None)", "1 (Low)", "2 (Medium)", "3 (High)" };

            ComboBox compCombo = new ComboBox
            {
                UseLayoutRounding = true,
                Header = "Compression Level",
                ItemsSource = compOptions,
                SelectedIndex = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = textBrush
            };

            StackPanel panel = new StackPanel { UseLayoutRounding = true, Spacing = 12, Padding = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(fpsCombo);
            panel.Children.Add(resCombo);
            panel.Children.Add(compCombo);

            ContentDialog dialog = new ContentDialog
            {
                UseLayoutRounding = true,
                Title = "Export GIF",
                Content = panel,
                PrimaryButtonText = "Export",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
                // Consistency styling
                RequestedTheme = ElementTheme.Dark,
                Background = semiTransBrush,
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            int selectedFps = fpsValues[fpsCombo.SelectedIndex < 0 ? 1 : fpsCombo.SelectedIndex];
            int selectedRes = resValues[resCombo.SelectedIndex < 0 ? 2 : resCombo.SelectedIndex];
            int selectedComp = compCombo.SelectedIndex < 0 ? 1 : compCombo.SelectedIndex;

            var savePicker = new FileSavePicker();
            InitializeWithWindow.Initialize(savePicker, _hwnd);
            savePicker.FileTypeChoices.Add("GIF Image", new List<string> { ".gif" });
            string safeStart = startText.Text.Replace(":", "-").Replace(".", "-");
            string safeEnd = endText.Text.Replace(":", "-").Replace(".", "-");
            savePicker.SuggestedFileName = $"Clip_{safeStart}_{safeEnd}";
            var outputFile = await savePicker.PickSaveFileAsync();

            if (outputFile == null || MainTokenSource.IsCancelled()) return;

            ToggleNonBlockingLoading(true);
            // Offload heavy processing to background thread
            await Task.Run(async () =>
            {
                try
                {
                    await ExportVideoSegmentToGif(sourceFile, outputFile, start, end, selectedFps, selectedRes, selectedComp);
                }
                catch { await ShowSimpleDialog("Error", "GIF export was failed."); }
            });

            if (MainTokenSource.IsCancelled()) return;

            await ShowSimpleDialog("Success", "GIF exported successfully.");
        }
        catch (Exception ex)
        {
            await ShowSimpleDialog("Export Failed", ex.Message);
        }
        finally
        {
            ToggleNonBlockingLoading(false);
        }
    }
    private void UpdateMarkerTime(TextBlock target, double ratio, TimeSpan duration)
    {
        double totalSecs = duration.TotalSeconds > 0 ? duration.TotalSeconds : 0;
        var time = TimeSpan.FromSeconds(totalSecs * Math.Clamp(ratio, 0, 1));
        target.Text = time.ToString(@"mm\:ss\.ff");
    }
    private void SeekToPercentage(double p)
    {
        if (_player.MediaPlayer?.PlaybackSession == null || timelineController == null) return;

        var session = _player.MediaPlayer.PlaybackSession;
        if (session.NaturalDuration.TotalSeconds <= 0) return;

        TimeSpan targetPos = TimeSpan.FromSeconds(Math.Clamp(p, 0, 1) * session.NaturalDuration.TotalSeconds);
        SeekUnified(targetPos);
    }
    private void OnSliderThumbnailMoving(object sender, PointerRoutedEventArgs e)
    {
        if (storageFile == null) return;

        if (!IsThumbnailReady)
        {
            if (_thumbnailPopup.IsOpen) _thumbnailPopup.IsOpen = false;
            return;
        }

        var sliderPos = e.GetCurrentPoint(_progressSlider).Position;
        double ratio = Math.Clamp(sliderPos.X / _progressSlider.ActualWidth, 0, 1);

        ShowThumbnailPreviewAtPosition(storageFile != null ? storageFile.GetHashCode() : -1, ratio);
        var rootPos = e.GetCurrentPoint(rootGrid).Position;
        var sliderTransform = _progressSlider.TransformToVisual(rootGrid);
        var sliderRootPos = sliderTransform.TransformPoint(new Point(0, 0));
        _thumbnailPopup.HorizontalOffset = Math.Clamp(rootPos.X - (THUMB_W / 2), 0, rootGrid.ActualWidth - THUMB_W);
        _thumbnailPopup.VerticalOffset = sliderRootPos.Y - THUMB_H;

        if (!_thumbnailPopup.IsOpen)
        {
            _thumbnailPopup.IsOpen = true;
        }
    }
    private async void OnMediaPlayerClicked(object sender, PointerRoutedEventArgs e)
    {
        if (fileType == FileType.Radio) return;
        if (fileType != FileType.Video && fileType != FileType.Audio) return;

        var properties = e.GetCurrentPoint(sender as UIElement).Properties;

        if (properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased)
        {
            if (fileType == FileType.Audio)
            {
                var _lastPressPoint = e.GetCurrentPoint(sender as UIElement).Position;
                contextMenu.ShowAt(_visualizerContainer, _lastPressPoint);
            }

            return;
        }

        if (_player.MediaPlayer == null || _player.Source == null || _player.Visibility == Visibility.Collapsed) return;
        if (Interlocked.CompareExchange(ref isProcessingClick, 1, 0) != 0) return;

        try
        {
            if (MainTokenSource.IsCancellationRequested) return;

            if (PlayState == PlayerPlayState.Playing)
            {
                PauseMedia(PlayerPlayState.Paused);
                ShowPlayPauseStatus("\uF8AE", 1.4);
            }
            else if (PlayState == PlayerPlayState.Paused)
            {
                PlayMedia();
                ShowPlayPauseStatus("\uF5B0", 1.4);
            }
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref isProcessingClick, 0);
        }
    }

    private void UpdateScrollingText(string title)
    {
        if (string.IsNullOrEmpty(title) || _topNowPlayingBar == null) return;

        title = FormatDisplayTitle(title);

        if (string.IsNullOrWhiteSpace(title))
        {
            _topNowPlayingBar.Opacity = 0;
            _nowPlayingStoryboard?.Stop();
            return;
        }

        if (_topNowPlayingBar.ActualHeight <= 0)
        {
            _topNowPlayingBar.Opacity = 1;
            _topNowPlayingBar.Loaded += (s, e) => UpdateScrollingText(title);
            return;
        }

        _nowPlayingStoryboard?.Stop();
        _nowPlayingStoryboard?.Children.Clear();

        if (_topNowPlayingBar.Child is not Canvas canvas) return;
        canvas.Children.Clear();

        string cleanTitle = System.IO.Path.GetFileNameWithoutExtension(title);
        var probe = new TextBlock { UseLayoutRounding = true, FontSize = 14, FontStyle = Windows.UI.Text.FontStyle.Italic };
        probe.Inlines.Add(new Run { Text = $"  {cleanTitle}  " });
        probe.Inlines.Add(new Run { Text = "\uE768", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontStyle = Windows.UI.Text.FontStyle.Normal, FontSize = 12 });
        probe.Inlines.Add(new Run { Text = "  " });
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double unitWidth = probe.DesiredSize.Width;
        double centerY = (_topNowPlayingBar.ActualHeight - probe.DesiredSize.Height) / 2;
        int copies = (int)Math.Ceiling(280 / unitWidth) + 1;

        for (int i = 0; i < copies; i++)
        {
            var tb = new TextBlock
            {
                UseLayoutRounding = true,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.WhiteSmoke),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            };

            tb.Inlines.Add(new Run { Text = $"  {cleanTitle}  " });
            tb.Inlines.Add(new Run { Text = "\uE768", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontStyle = Windows.UI.Text.FontStyle.Normal, FontSize = 12 });
            tb.Inlines.Add(new Run { Text = "  " });

            Canvas.SetLeft(tb, i * unitWidth);
            Canvas.SetTop(tb, centerY);
            canvas.Children.Add(tb);
        }

        // Direct Canvas Animation
        var transform = new TranslateTransform();
        canvas.RenderTransform = transform;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = -unitWidth,
            Duration = new Duration(TimeSpan.FromSeconds(unitWidth / 60)), // Adjusted speed
            RepeatBehavior = RepeatBehavior.Forever
        };

        Storyboard.SetTarget(anim, transform);
        Storyboard.SetTargetProperty(anim, "X");

        _nowPlayingStoryboard?.Children.Add(anim);
        _topNowPlayingBar.Opacity = 1;
        _nowPlayingStoryboard?.Begin();
    }
    private void EnableBorderlessInteractions(UIElement rootElement)
    {
        // Always clean up previous handlers first
        DisableBorderlessInteractions(rootElement);

        // Only register drag handlers when actually in borderless mode
        if (m_AppWindow?.Presenter is not OverlappedPresenter op || op.HasTitleBar)
            return;

        _borderlessPressedHandler = (s, e) =>
        {
            if (e.Handled) return;

            var point = e.GetCurrentPoint(rootElement);
            if (point.Properties.IsLeftButtonPressed)
            {
                Point clientPos = point.Position;
                PointInt32 windowPos = m_AppWindow.Position;
                double screenXDIP = windowPos.X + clientPos.X;
                double screenYDIP = windowPos.Y + clientPos.Y;

                int screenX = (int)Math.Round(screenXDIP);
                int screenY = (int)Math.Round(screenYDIP);

                GetWindowRect(_hwnd, out RECT windowRect);

                bool onResizeBorder =
                    screenX <= windowRect.Left + RESIZE_BORDER_THICKNESS ||
                    screenX >= windowRect.Right - RESIZE_BORDER_THICKNESS ||
                    screenY <= windowRect.Top + RESIZE_BORDER_THICKNESS ||
                    screenY >= windowRect.Bottom - RESIZE_BORDER_THICKNESS;

                if (onResizeBorder)
                {
                    e.Handled = true;
                    return;
                }

                _isDraggingWindow = true;
                rootElement.CapturePointer(e.Pointer);
                _dragStartMouseScreenPos = new Point(screenXDIP, screenYDIP);
                _dragStartWindowPos = new Point(windowPos.X, windowPos.Y);
                e.Handled = true;
            }
        };

        _borderlessMovedHandler = (s, e) =>
        {
            if (_isDraggingWindow)
            {
                Point clientMousePos = e.GetCurrentPoint(null).Position;
                Point currentScreenMousePos = new Point(m_AppWindow.Position.X + clientMousePos.X, m_AppWindow.Position.Y + clientMousePos.Y);

                double deltaX = currentScreenMousePos.X - _dragStartMouseScreenPos.X;
                double deltaY = currentScreenMousePos.Y - _dragStartMouseScreenPos.Y;

                int newX = (int)(_dragStartWindowPos.X + deltaX);
                int newY = (int)(_dragStartWindowPos.Y + deltaY);

                m_AppWindow.Move(new PointInt32 { X = newX, Y = newY });
                e.Handled = true;
            }
        };

        _borderlessReleasedHandler = (s, e) =>
        {
            if (_isDraggingWindow)
            {
                _isDraggingWindow = false;
                rootElement.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        };

        rootElement.PointerPressed += _borderlessPressedHandler;
        rootElement.PointerMoved += _borderlessMovedHandler;
        rootElement.PointerReleased += _borderlessReleasedHandler;
    }

    private async void OnOpenSingleFile(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = new FileOpenPicker();
            InitializeWithWindow.Initialize(p, _hwnd);
            foreach (var ex in _supportedExtensions) p.FileTypeFilter.Add(ex);
            var f = await p.PickSingleFileAsync();
            if (MainTokenSource.IsCancellationRequested) return;

            var au = await f.Properties.GetMusicPropertiesAsync();
            if (MainTokenSource.IsCancelled()) return;
            var vid = await f.Properties.GetVideoPropertiesAsync();
            if (MainTokenSource.IsCancelled()) return;
            var img = await f.Properties.GetImagePropertiesAsync();
            if (MainTokenSource.IsCancelled()) return;

            if (au == null && vid == null && img == null)
            {
                return;
            }

            if (f != null)
            {
                await AddToPlaylist(f, clearPlayList: true);
                if (MainTokenSource.IsCancelled()) return;
                await PlayItemByPath(f.Path, InternalPlayStatus.None);
            }
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }

    private async Task LoadMultimediaFile(StorageFile f, bool trackIndex = true)
    {
        try
        {
            if (_radioPlayer != null)
            {
                _radioPlayer.Pause();
                _radioPlayer.Source = null;
                _radioPlayer.Dispose();
                _radioPlayer = null;
            }

            if (f == null) return;

            if (_emptyStateText != null)
                _emptyStateText.Visibility = Visibility.Collapsed;

            if (PlayState == PlayerPlayState.Playing || PlayState == PlayerPlayState.Paused)
            {
                PauseMedia(PlayerPlayState.Stop);
            }
            else
            {
                PlayState = PlayerPlayState.Stop;
            }

            if (IsImage(f))
            {
                if (canvas != null)
                {
                    canvas.Paused = true;
                }

                _visualizerContainer.Visibility = Visibility.Collapsed;
                audioEngine?.EnableDisableVisualizer(false);
                SetLiveWallpaper(false);

                fileType = FileType.Image;
                _player.Visibility = Visibility.Collapsed;
                _animatedImageHost.Visibility = Visibility.Visible;
                _player.MediaPlayer.SystemMediaTransportControls.DisplayUpdater.Update();
                PauseMedia(PlayerPlayState.Stop);

                var bitmap = new BitmapImage();
                using (var stream = await f.OpenReadAsync())
                    await bitmap.SetSourceAsync(stream);

                if (MainTokenSource.IsCancellationRequested) return;

                _animatedImageHost.Source = bitmap;
                _durationText.Text = "AN";
                _progressSlider.Value = 0;
                _progressSlider.IsEnabled = false;
                return;
            }

            _animatedImageHost.Visibility = Visibility.Collapsed;

            if (_player.Visibility != Visibility.Visible)
            {
                _player.Visibility = Visibility.Visible;
            }

            _player.UpdateLayout();
            Unsafe.SkipInit(out MediaClip clip);

            try
            {
                var vclip = await MediaClip.CreateFromFileAsync(f);
                clip = vclip;
                if (vclip == null) return;
            }
            catch (Exception ex0)
            {
                this.IsSafeOrThrow(ex0);
                return;
            }
            var ms = MediaSource.CreateFromStorageFile(f);
            var duration = clip.OriginalDuration;
            fileType = IsVideo(f) ? FileType.Video : FileType.Unknown;
            fileType = IsAudio(f) ? FileType.Audio : fileType;
            _progressSlider.IsEnabled = fileType == FileType.Video || fileType == FileType.Audio ? true : false;

            if (fileType != FileType.Unknown)
            {
                try
                {
                    var folder = await f.GetParentAsync();

                    if (MainTokenSource.IsCancelled()) return;

                    if (folder != null)
                    {
                        var srr = Path.GetFileNameWithoutExtension(f.Name) + ".srt";

                        if (File.Exists(srr))
                        {
                            var srt = await folder.GetFileAsync(srr);
                            var tts = TimedTextSource.CreateFromUri(new Uri(srt.Path));
                            ms.ExternalTimedTextSources.Add(tts);

                            tts.Resolved += (s, args) =>
                            {
                                if (args.Error != null) return;
                                for (uint i = 0; i < (uint)args.Tracks.Count; i++)
                                {
                                    args.Tracks[(int)i].PlaybackItem.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.PlatformPresented);
                                }
                            };
                        }
                    }
                }
                //DONT RETURN HERE if error out!!!
                catch (FileNotFoundException) { /* no SRT, that's fine */ }
                catch (Exception ex1) { this.IsSafeOrThrow(ex1); return; }
            }

            if (trackIndex)
            {
                await AddToPlaylist(f, duration);
                _currentPlaylistIndex = _playlistCollection.IndexOf(_playlistCollection.FirstOrDefault(x => x.Path == f.Path));
            }

            var playbackItem = new MediaPlaybackItem(ms);

            SetupMediaPlayerEvents();

            if (fileType != FileType.Unknown)
            {
                storageFile = f;
                _player.MediaPlayer.Source = playbackItem;
                _player.MediaPlayer.IsMuted = true;
                _player.MediaPlayer.IsLoopingEnabled = false;
                _currentThumbnailFile = f;

                if (fileType == FileType.Video)
                {
                    _loadSrtMenuItem.IsEnabled = true;
                    ToggleNonBlockingLoading(true);
                    StartThumbnailStripGeneration(f, () => ToggleNonBlockingLoading(false));
                }
                else
                {
                    _loadSrtMenuItem.IsEnabled = false;
                    StopAndClearThumbnailStrip();
                }

                var audioTask = audioEngine.SetupAudioEngine(f, _player, _volumeSlider.Value, _currentSettings.Equalizer);
                PauseMedia(PlayerPlayState.Paused);

                var audioIsReady = await audioTask;
                if (!audioIsReady) return;

                SeekUnified(TimeSpan.Zero);
                UpdateScrollingText(f.Name);

                if (_currentSettings.AutoPlay)
                    PlayMedia();
                else
                    PauseMedia(PlayerPlayState.Paused);
            }

            if (fileType == FileType.Audio)
            {
                ToggleNonBlockingLoading(false);
                _visualizerContainer.Visibility = Visibility.Visible;
                ShowVisualizerOverlay(true);
                audioEngine?.EnableDisableVisualizer(true);
                SetLiveWallpaper(liveWallpaperIsOn);
            }
            else
            {
                if (canvas != null)
                {
                    canvas.Paused = true;
                }

                _visualizerContainer.Visibility = Visibility.Collapsed;
                audioEngine?.EnableDisableVisualizer(false);
                SetLiveWallpaper(false);
            }
        }
        catch (Exception exx) { this.IsSafeOrThrow(exx); }
    }

    public async void PlayMedia()
    {
        try
        {
            if (fileType == FileType.Radio)
            {
                if (RadioPlayer != null)
                {
                    RadioPlayer.Play();
                    PlayState = PlayerPlayState.Playing;
                }
                return;
            }

            if (IsLoading) { Log.Print("loading is in progress."); return; }
            var prevstate = (int)PlayState;
            var oldpos = timelineController.Position;

            if (PlayState == PlayerPlayState.Playing)
            {
                timelineController.Start();
                audioEngine.Start();
                SeekUnified(TimeSpan.Zero);
                return;
            }

            if (PlayState == PlayerPlayState.Paused)
            {
                PlayState = PlayerPlayState.Playing;
                timelineController.Resume();
                audioEngine.Start();
                audioEngine.Seek(timelineController.Position);
                return;
            }

            audioEngine.ToggleMute(true);
            PlayState = PlayerPlayState.Playing;
            timelineController.Start();
            audioEngine.Start();

            if (oldpos > TimeSpan.FromMilliseconds(500) && prevstate == 0)
                SeekUnified(oldpos);
            else
                SeekUnified(TimeSpan.Zero);

            audioEngine.ToggleMute(false);
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    public void PauseMedia(PlayerPlayState state)
    {
        try
        {
            // ── Radio: just pause/stop the radio player ──
            if (fileType == FileType.Radio)
            {
                if (RadioPlayer != null)
                {
                    if (state == PlayerPlayState.Stop)
                    {
                        RadioPlayer.Pause();
                        RadioPlayer.Source = null;  // stop buffering entirely
                    }
                    else
                    {
                        RadioPlayer.Pause();        // pause keeps the connection
                    }
                }
                PlayState = state;
                return;
            }

            if (PlayState == PlayerPlayState.Paused || PlayState == PlayerPlayState.Stop) return;

            PlayState = state;
            audioEngine.ToggleMute(true);
            timelineController.Pause();
            audioEngine.Pause();

            if (state == PlayerPlayState.Stop)
                SeekUnified(TimeSpan.Zero);
            else
                SeekUnified(timelineController.Position);

            audioEngine.ToggleMute(false);
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private async void LoadSrt(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsLoading) return;

            if (_player.Source is not MediaPlaybackItem item) return;
            var p = new FileOpenPicker();
            InitializeWithWindow.Initialize(p, _hwnd);
            p.FileTypeFilter.Add(".srt");
            var f = await p.PickSingleFileAsync();
            if (MainTokenSource.IsCancelled()) return;

            if (f != null)
            {
                var tts = TimedTextSource.CreateFromUri(new Uri(f.Path));
                tts.Resolved += (s, args) => { if (args.Error == null) this.DispatcherQueue.TryEnqueue(() => { for (uint i = 0; i < (uint)args.Tracks.Count; i++) item.TimedMetadataTracks.SetPresentationMode(i, Windows.Media.Playback.TimedMetadataTrackPresentationMode.PlatformPresented); }); };
                item.Source.ExternalTimedTextSources.Add(tts);
            }
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    public void ToggleAlwaysOnTop(bool state)
    {
        IntPtr hWndInsertAfter = state ? HWND_TOPMOST : HWND_NOTOPMOST;
        SetWindowPos(_hwnd, hWndInsertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    private void SetLiveWallpaper(bool state)
    {
        if (!state)
        {
            LiveWallpaper.DestroyLiveWallpaper();
        }
        else
        {
            _bgWindow?.Shutdown();
            _bgWindow?.Close();
            _bgWindow = new LiveWallpaper(audioEngine);
            _bgWindow.Activate();
        }

        liveWallpaperIsOn = state;
    }

    private async void OnFileDraggingOver(object sender, DragEventArgs evt)
    {
        evt.Handled = true;
        evt.AcceptedOperation = DataPackageOperation.Copy;
    }
    private async void OnFileDropped(object sender, DragEventArgs evt)
    {
        evt.Handled = true;

        if (evt.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await evt.DataView.GetStorageItemsAsync();
            StorageFile firstFile = null;

            foreach (var item in items)
            {
                if (item is StorageFile file && _supportedExtensions.Contains(file.FileType.ToLower()))
                {
                    await AddToPlaylist(file);
                    firstFile ??= file;
                }
            }

            // Always load the first file — AutoPlay decision is inside LoadVideoFile
            if (firstFile != null)
                await PlayItemByPath(firstFile.Path, InternalPlayStatus.None);
        }
    }
    /// <summary>
    /// Zooms the media player. 1.0 is normal, 0.5 is half size, 3.0 is triple size.
    /// </summary>
    public void SetZoom(double zoomFactor)
    {
        // Clamp the value to ensure it stays within a reasonable range if needed
        // based on your 0.5 to 3.0 requirement.
        double scale = Math.Clamp(zoomFactor, 0.5, 3.0);

        // Ensure the player scales from the center
        _player.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

        if (_player.RenderTransform is ScaleTransform existingScale)
        {
            existingScale.ScaleX = scale;
            existingScale.ScaleY = scale;
        }
        else
        {
            _player.RenderTransform = new ScaleTransform
            {
                ScaleX = scale,
                ScaleY = scale
            };
        }

        Log.Print($"Zoom set to: {scale:F1}x");
    }

    private StorageFile GetCurrentStorageFile()
    {
        if (_player.Source is MediaPlaybackItem item)
        {
            if (item.Source.CustomProperties.TryGetValue("SourceFile", out object fileObj))
            {
                return fileObj as StorageFile;
            }
        }

        return _currentThumbnailFile;
    }
    private void StartInfoUpdateTimer()
    {
        try
        {
            _infoUpdateTimer?.Stop();
            _infoUpdateTimer = new DispatcherTimer();
            _infoUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);

            _infoUpdateTimer.Tick += (s, e) =>
            {
                try
                {
                    if (_infoOverlay == null)
                    {
                        _infoUpdateTimer?.Stop();
                        return;
                    }

                    if (_dynamicInfoText == null) return;

                    // Radio — show live status only
                    if (fileType == FileType.Radio)
                    {
                        _dynamicInfoText.Text = PlayState == PlayerPlayState.Playing
                            ? "● Live"
                            : "■ Stopped";
                        return;
                    }

                    var session = _player?.MediaPlayer?.PlaybackSession;
                    if (session == null)
                    {
                        _dynamicInfoText.Text = "No session";
                        return;
                    }

                    double fps = 30.0;
                    try
                    {
                        if (_player?.Source is MediaPlaybackItem item)
                        {
                            var props = item.VideoTracks.FirstOrDefault()?.GetEncodingProperties();
                            if (props != null && props.FrameRate.Denominator != 0)
                                fps = (double)props.FrameRate.Numerator / props.FrameRate.Denominator;
                        }
                    }
                    catch { }

                    double currentSec = 0, totalSec = 0;
                    try
                    {
                        currentSec = session.Position.TotalSeconds;
                        totalSec = session.NaturalDuration.TotalSeconds;
                    }
                    catch { }

                    long currentFrame = (long)(currentSec * fps);
                    long totalFrames = (long)(totalSec * fps);

                    _dynamicInfoText.Text =
                        $"Elapsed:  {session.Position:hh\\:mm\\:ss}\n" +
                        $"Duration: {session.NaturalDuration:hh\\:mm\\:ss}\n" +
                        $"FPS:      {fps:F2}\n" +
                        $"Frame:    {currentFrame} / {totalFrames}";
                }
                catch (Exception ex) { this.IsSafeOrThrow(ex); }
            };

            _infoUpdateTimer.Start();
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private async Task ProcessClipExport(StorageFile outputFile, TimeSpan start, TimeSpan end, VideoEncodingQuality quality)
    {
        var sourceFile = GetCurrentStorageFile();
        if (sourceFile == null) return;

        var clip = await MediaClip.CreateFromFileAsync(sourceFile);

        if (end > clip.OriginalDuration) end = clip.OriginalDuration;

        clip.TrimTimeFromStart = start;
        clip.TrimTimeFromEnd = clip.OriginalDuration - end;

        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        var profile = MediaEncodingProfile.CreateMp4(quality);
        await composition.RenderToFileAsync(outputFile, MediaTrimmingPreference.Precise, profile);
    }
    private async Task PlayItemByPath(string path, InternalPlayStatus forceState, bool isCd = false)
    {
        try
        {
            if (path.StartsWith("radio://", StringComparison.OrdinalIgnoreCase))
            {
                if (PlayState == PlayerPlayState.Playing || PlayState == PlayerPlayState.Paused)
                    PauseMedia(PlayerPlayState.Stop);
                _player.MediaPlayer.Source = null;
                bool played = await TryPlayRadioByPath(path);
                if (!played)
                    Log.Print($"Radio station not found in map: {path}");
                fileType = FileType.Audio;
                return;
            }

            if (fileType == FileType.Radio)
            {
                fileType = FileType.Unknown;
                _player.MediaPlayer.Source = null;
            }

            if (GetCurrentStorageFile()?.Path == path)
            {
                if (forceState == InternalPlayStatus.ForcePlay)
                {
                    PlayMedia();
                    return;
                }
                if (!_currentSettings.AutoPlay)
                    PauseMedia(PlayerPlayState.Stop);
                else
                    PlayMedia();
                return;
            }

            int idx = _playlistCollection.IndexOf(_playlistCollection.FirstOrDefault(x => x.Path == path));
            if (idx >= 0) _currentPlaylistIndex = idx;

            bool isCdda = path.StartsWith("cdda:", StringComparison.OrdinalIgnoreCase);

            StorageFile file = null;
            try
            {
                string filePath = path;
                if (isCdda)
                {
                    var parts = path.Substring("cdda:".Length).Split('/');
                    if (parts.Length < 2 || !int.TryParse(parts[1], out int trackNum)) return;
                    filePath = System.IO.Path.Combine(parts[0] + "\\", $"Track{trackNum:D2}.cda");
                }

                file = await StorageFile.GetFileFromPathAsync(filePath);
            }
            catch (Exception ex)
            {
                Log.Print($"PlayItemByPath: file not accessible: {path} | {ex.Message}");
                return;
            }

            if (file == null || MainTokenSource.IsCancellationRequested) return;

            await LoadMultimediaFile(file, trackIndex: false);
            if (MainTokenSource.IsCancellationRequested) return;

            RefreshPlaylistUI();

            if (_currentPlaylistIndex < _playlistCollection.Count)
                _playlistView.ScrollIntoView(_playlistCollection[_currentPlaylistIndex]);

            if (!_currentSettings.AutoPlay && PlayState != PlayerPlayState.Playing)
            {
                if (forceState == InternalPlayStatus.ForcePlay && PlayState != PlayerPlayState.Playing)
                    PlayMedia();
            }
        }
        catch (Exception ex) { this.IsSafeOrThrow(ex); }
    }
    private static string GetFalToken(string path) =>
    Convert.ToBase64String(
        System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(path)))
    .Replace("/", "_").Replace("+", "-").Replace("=", "");
}
