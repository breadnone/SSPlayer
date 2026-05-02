using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SSPlayer.Logs;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Devices.Printers;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.System;
using Windows.UI;

namespace SSPlayer;

// ─────────────────────────────────────────────────────────────────────────────
//  Model
// ─────────────────────────────────────────────────────────────────────────────

public enum CodecStatus { Supported, NotSupported }
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2257", Justification = "False positive — INotifyPropertyChanged is not IDynamicInterfaceCastable")]
public sealed class CodecInfo
{
    public string DisplayName { get; init; }
    public string Category { get; init; }  // "Video" | "Audio" | "Image"
    public string Description { get; init; }
    public string StoreUrl { get; init; }  // null = no Store package available
    public CodecStatus Status { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Persistence — what we write to codec_check.json
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class CodecCheckRecord
{
    public DateTime LastCheckedUtc { get; set; }  // when we last ran the check
    public DateTime LastShownUtc { get; set; }  // when the dialog was last shown
}

// ─────────────────────────────────────────────────────────────────────────────
//  Checker  (fully static — no instance state)
// ─────────────────────────────────────────────────────────────────────────────

public static class CodecChecker
{
    // How many days between re-showing the dialog while codecs are still missing
    public const int RecheckIntervalDays = 15;

#if DEBUG
    // In debug builds, lower the interval to 0 so the dialog always fires.
    // Set to any small number (e.g. 0) to make testing instant.
    public const int DebugRecheckIntervalDays = 0;
#endif

    private enum ProbeKind { VideoSubtype, AudioSubtype, ImageMime }

    private sealed record CodecEntry(
        string Category,
        string DisplayName,
        string Description,
        string StoreUrl,
        ProbeKind Kind,
        string ProbeValue);

    // ── Catalogue ─────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<CodecEntry> Catalogue = new[]
    {
        // Video
        new CodecEntry("Video", "HEVC / H.265",         "4K · HDR · HEVC streams",
            "ms-windows-store://pdp/?ProductId=9NMZLZ57R3T7",
            ProbeKind.VideoSubtype, "hvc1"),

        new CodecEntry("Video", "H.264 / AVC",          "Most common video codec",
            null, ProbeKind.VideoSubtype, "avc1"),

        new CodecEntry("Video", "AV1",                  "Next-gen open codec · 4K / 8K",
            "ms-windows-store://pdp/?ProductId=9MVZQVXJBQ9V",
            ProbeKind.VideoSubtype, "av01"),

        new CodecEntry("Video", "VP9",                  "WebM · YouTube open codec",
            null, ProbeKind.VideoSubtype, "vp09"),

        new CodecEntry("Video", "VP8",                  "Legacy WebM codec",
            null, ProbeKind.VideoSubtype, "vp80"),

        new CodecEntry("Video", "MPEG-2 Video",         "DVD · Broadcast · VOB files",
            "ms-windows-store://pdp/?ProductId=9N95Q1ZZPMH4",
            ProbeKind.VideoSubtype, "mp2v"),

        new CodecEntry("Video", "MPEG-1 Video",         "Legacy VCD format",
            null, ProbeKind.VideoSubtype, "mpg1"),

        new CodecEntry("Video", "VC-1 / WMV Advanced",  "Windows Media Video 9 Advanced",
            null, ProbeKind.VideoSubtype, "WVC1"),

        new CodecEntry("Video", "DivX / MPEG-4 ASP",    "MPEG-4 Part 2 · legacy .avi",
            null, ProbeKind.VideoSubtype, "DX50"),

        new CodecEntry("Video", "Xvid / MPEG-4 ASP",    "Open-source MPEG-4 Part 2",
            null, ProbeKind.VideoSubtype, "XVID"),

        new CodecEntry("Video", "Theora",                "Ogg video codec",
            null, ProbeKind.VideoSubtype, "theo"),

        // Image
        new CodecEntry("Image", "WebP",                 "Google modern image format",
            null, ProbeKind.ImageMime, "image/webp"),

        new CodecEntry("Image", "HEIF / HEIC",          "Apple · iPhone photos",
            "ms-windows-store://pdp/?ProductId=9PMMSR1CGMPC",
            ProbeKind.ImageMime, "image/heif"),

        new CodecEntry("Image", "AVIF",                 "AV1-based next-gen image",
            null, ProbeKind.ImageMime, "image/avif"),

        new CodecEntry("Image", "JPEG XL (.jxl)",       "Next-gen photo format",
            null, ProbeKind.ImageMime, "image/jxl"),

        // Audio
        new CodecEntry("Audio", "AAC",                  "Advanced Audio Coding · M4A · iTunes",
            null, ProbeKind.AudioSubtype, "mp4a"),

        new CodecEntry("Audio", "MP3",                  "MPEG Layer III",
            null, ProbeKind.AudioSubtype, "mp3a"),

        new CodecEntry("Audio", "Opus",                 "Low-latency voice & music codec",
            null, ProbeKind.AudioSubtype, "Opus"),

        new CodecEntry("Audio", "Vorbis",               "Ogg audio codec",
            null, ProbeKind.AudioSubtype, "vrbs"),

        new CodecEntry("Audio", "FLAC",                 "Free Lossless Audio Codec",
            null, ProbeKind.AudioSubtype, "fLaC"),

        new CodecEntry("Audio", "Dolby AC-3",           "DVD surround sound",
            null, ProbeKind.AudioSubtype, "ac-3"),

        new CodecEntry("Audio", "Dolby E-AC-3 (DD+)",   "Streaming surround",
            null, ProbeKind.AudioSubtype, "ec-3"),

        new CodecEntry("Audio", "DTS Core",             "Cinematic surround audio",
            null, ProbeKind.AudioSubtype, "dtsc"),

        new CodecEntry("Audio", "ALAC",                 "Apple Lossless Audio",
            null, ProbeKind.AudioSubtype, "alac"),

        new CodecEntry("Audio", "WMA",                  "Windows Media Audio",
            null, ProbeKind.AudioSubtype, "WMAP"),

        new CodecEntry("Audio", "MPEG-2 Audio / MP2",   "Broadcast audio · DVD layer",
            null, ProbeKind.AudioSubtype, "mp2a"),
    };

    // ── Known-support tables ──────────────────────────────────────────────────

    private static readonly HashSet<string> InboxVideo = new(StringComparer.OrdinalIgnoreCase)
    {
        "avc1", "H264",
        "WVC1", "WMV3", "WMV2", "WMV1",
        "mp2v", "MPEG2",
        "mpg1",
        "mjpg", "MJPG",
        "DX50", "DIVX", "XVID",
    };

    private static readonly HashSet<string> StoreVideo = new(StringComparer.OrdinalIgnoreCase)
    {
        "hvc1", "hev1",  // HEVC Video Extensions
        "av01", "av1", "AV1", "ivf",   // AV1 Video Extension
        "vp09",           // VP9 Video Extensions
    };

    private static readonly HashSet<string> InboxAudio = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3a", "mp4a", "WMAP", "fLaC", "alac",
        "Opus", "vrbs", "mp2a", "ac-3", "ec-3", "dtsc",
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Probes all codecs concurrently and returns the complete result list.</summary>
    public static async Task<IReadOnlyList<CodecInfo>> CheckAllAsync()
    {
        var tasks = Catalogue.Select(static async entry =>
        {
            var status = entry.Kind switch
            {
                ProbeKind.VideoSubtype => await ProbeVideoAsync(entry.ProbeValue),
                ProbeKind.AudioSubtype => await ProbeAudioAsync(entry.ProbeValue),
                ProbeKind.ImageMime => await ProbeImageAsync(entry.ProbeValue),
                _ => CodecStatus.NotSupported,
            };

            return new CodecInfo
            {
                Category = entry.Category,
                DisplayName = entry.DisplayName,
                Description = entry.Description,
                StoreUrl = entry.StoreUrl,
                Status = status,
            };
        });

        return await Task.WhenAll(tasks);
    }

    // ── Probes ────────────────────────────────────────────────────────────────

    private static async Task<CodecStatus> ProbeVideoAsync(string subtype)
    {
        //if (subtype == "av01") return CodecStatus.Supported;

        await Task.Yield();

        if (InboxVideo.Contains(subtype)) return CodecStatus.Supported;
        if (StoreVideo.Contains(subtype))
        {
            bool isavi1 = subtype.Contains("av01");

            try
            {
                var props = new VideoEncodingProperties { Subtype = subtype };
                var profile = new MediaEncodingProfile { Video = props };

                return profile.Video.Subtype.Equals(subtype, StringComparison.OrdinalIgnoreCase) ? CodecStatus.Supported : CodecStatus.NotSupported;
            }
            catch
            {
                if (!isavi1)
                    return CodecStatus.NotSupported;
                else
                    return CodecStatus.Supported;
            }
        }
        return CodecStatus.NotSupported;
    }
    private const string Av1SubtypeGuid = "{61763031-0000-0010-8000-00AA00389B71}";
    public static async Task<bool> IsAv1Supported(string subtype)
    {
        bool isAv1Installed = false;

        if (subtype.Contains("av01") || subtype.Contains("AV01") || subtype.Contains("av1"))
        {
            var query = new Windows.Media.Core.CodecQuery();
            var codecs = await query.FindAllAsync(CodecKind.Video, CodecCategory.Decoder, subtype);
            // Check if any registered decoder supports the AV1 GUID or the FourCC string
            isAv1Installed = codecs.Any(c => c.Subtypes.Any(s => s.Equals(Av1SubtypeGuid, StringComparison.OrdinalIgnoreCase) || s.Contains("av01") || s.Contains("AV01")));
        }

        return isAv1Installed;
    }
    private static async Task<CodecStatus> ProbeAudioAsync(string subtype)
    {
        await Task.Yield();
        return InboxAudio.Contains(subtype) ? CodecStatus.Supported : CodecStatus.NotSupported;
    }

    private static async Task<CodecStatus> ProbeImageAsync(string mimeType)
    {
        await Task.Yield();
        try
        {
            foreach (var dec in BitmapDecoder.GetDecoderInformationEnumerator())
                foreach (var mime in dec.MimeTypes)
                    if (string.Equals(mime, mimeType, StringComparison.OrdinalIgnoreCase))
                        return CodecStatus.Supported;
        }
        catch { }
        return CodecStatus.NotSupported;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  MainWindow — scheduling, dialog, cards
// ─────────────────────────────────────────────────────────────────────────────

public sealed partial class MainWindow
{
    // Persisted as   %LocalAppData%\SSPlayer\codec_check.json
    // alongside settings.json
    private string CodecCheckRecordPath => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "codec_check.json");

    // ── Scheduling ────────────────────────────────────────────────────────────

    /// <summary>
    /// Call once at the end of <see cref="OnLoaded"/>.
    ///
    /// Logic:
    ///   • First launch ever          → run immediately (no record file yet).
    ///   • Subsequent launches        → run only if ≥ 5 days have passed since
    ///                                  the last time the dialog was shown.
    ///   • All codecs now installed   → update the record, stay silent.
    ///
    /// DEBUG builds also expose <see cref="Debug_ForceCodecCheckNow"/> to
    /// trigger the dialog instantly from any test menu or keyboard shortcut.
    /// </summary>
    private void TriggerCodecCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // ── Read persisted record ─────────────────────────────────
                var record = LoadCodecCheckRecord();

                int intervalDays =
#if DEBUG
                    CodecChecker.DebugRecheckIntervalDays;
#else
                    CodecChecker.RecheckIntervalDays;
#endif
                // Not due yet?
                double daysSinceLastShown = (DateTime.UtcNow - record.LastShownUtc).TotalDays;
                bool isDue = record.LastShownUtc == DateTime.MinValue   // first ever run
                          || daysSinceLastShown >= intervalDays;

                if (!isDue)
                {
                    double daysLeft = intervalDays - daysSinceLastShown;
                    Log.Print($"Codec check not due — next in {daysLeft:F1} day(s).");
                    return;
                }

                // ── Probe ─────────────────────────────────────────────────
                var results = await CodecChecker.CheckAllAsync();
                record.LastCheckedUtc = DateTime.UtcNow;

                var missing = results
                    .Where(static c => c.Status == CodecStatus.NotSupported && c.StoreUrl != null)
                    .ToList();

                if (missing.Count == 0)
                {
                    // All good — update check timestamp but don't show dialog
                    record.LastShownUtc = DateTime.UtcNow;
                    SaveCodecCheckRecord(record);
                    Log.Print("Codec check: all relevant codecs present — no dialog needed.");
                    return;
                }

                // Update LastShownUtc now, before showing — so even a force-quit
                // won't cause a double-show on the very next launch.
                record.LastShownUtc = DateTime.UtcNow;
                SaveCodecCheckRecord(record);

                Log.Print($"Codec check: {missing.Count} missing — showing dialog.");

                DispatcherQueue.TryEnqueue(async () =>
                    await ShowCodecDialogAsync(results, missing));
            }
            catch (Exception ex) { Log.Print($"TriggerCodecCheck: {ex.Message}"); }
        });
    }

#if DEBUG
    /// <summary>
    /// DEBUG ONLY — Wipes the saved record and immediately runs the codec
    /// check, forcing the dialog to appear regardless of the normal schedule.
    /// Wire this to a debug menu item or a keyboard shortcut like Ctrl+Shift+K.
    /// </summary>
    public void Debug_ForceCodecCheckNow()
    {
        try
        {
            // Delete the record so TriggerCodecCheck sees a brand-new install
            if (File.Exists(CodecCheckRecordPath))
                File.Delete(CodecCheckRecordPath);

            Log.Print("[DEBUG] Codec check record cleared — forcing check now.");
        }
        catch (Exception ex) { Log.Print($"[DEBUG] Could not clear record: {ex.Message}"); }

        TriggerCodecCheck();
    }
#endif

    private CodecCheckRecord LoadCodecCheckRecord()
    {
        try
        {
            if (File.Exists(CodecCheckRecordPath))
            {
                var rec = JsonSerializer.Deserialize(File.ReadAllText(CodecCheckRecordPath), AppJsonContext.Default.CodecCheckRecord);
                if (rec != null) return rec;
            }
        }
        catch (Exception ex) { Log.Print($"LoadCodecCheckRecord: {ex.Message}"); }
        return new CodecCheckRecord();
    }

    private void SaveCodecCheckRecord(CodecCheckRecord record)
    {
        try
        {
            File.WriteAllText(CodecCheckRecordPath, JsonSerializer.Serialize(record, AppJsonContext.Default.CodecCheckRecord));
        }
        catch (Exception ex) { Log.Print($"SaveCodecCheckRecord: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Dialog  — no cancel / opt-out button, just "Done"
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ShowCodecDialogAsync(
        IReadOnlyList<CodecInfo> all,
        IReadOnlyList<CodecInfo> missing)
    {
        try
        {
            int okCount = all.Count(static c => c.Status == CodecStatus.Supported);
            int missingCount = missing.Count;

            // ── Summary banner ────────────────────────────────────────────
            var summaryGrid = new Grid { UseLayoutRounding = true,Margin = new Thickness(0, 0, 0, 0) };
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var summaryText = new StackPanel { UseLayoutRounding = true, Spacing = 3 };
            summaryText.Children.Add(new TextBlock
            {
                UseLayoutRounding = true,
                Text = "Some codec extensions are not installed.",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            summaryText.Children.Add(new TextBlock
            {
                UseLayoutRounding = true,
                Text = "Codecs are maintained and published by Microsoft via Microsot Store.",
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
            });

            var badgeStack = new StackPanel
            {
                UseLayoutRounding = true,
                Spacing = 5,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };

            badgeStack.Children.Add(MakeBadge($"❌  {missingCount} missing", Color.FromArgb(210, 180, 50, 50)));
            badgeStack.Children.Add(MakeBadge($"✅  {okCount} installed", Color.FromArgb(210, 40, 130, 70)));
            Grid.SetColumn(summaryText, 0);
            Grid.SetColumn(badgeStack, 1);
            summaryGrid.Children.Add(summaryText);
            summaryGrid.Children.Add(badgeStack);

            var summaryBar = new Border
            {
                UseLayoutRounding = true,
                Background = new SolidColorBrush(Color.FromArgb(28, 255, 160, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 160, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 14),
                Child = summaryGrid,
            };

            // ── Codec cards, grouped by category ─────────────────────────
            var cardStack = new StackPanel { UseLayoutRounding = true, Spacing = 6 };

            foreach (var group in missing.GroupBy(static c => c.Category).OrderBy(static g => g.Key switch { "Video" => 0, "Image" => 1, "Audio" => 2, _ => 3 }))
            {
                cardStack.Children.Add(new TextBlock
                {
                    UseLayoutRounding = true,
                    Text = group.Key.ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Opacity = 0.4,
                    Margin = new Thickness(2, 8, 0, 2),
                    CharacterSpacing = 100,
                });

                foreach (var codec in group)
                {
                    cardStack.Children.Add(BuildCodecCard(codec));
                }
            }

            // ── "Remind me in 5 days" footer note ────────────────────────
            cardStack.Children.Add(new TextBlock
            {
                UseLayoutRounding = true,
                Text = $"SSPlayer will remind you again in {CodecChecker.RecheckIntervalDays} days if codecs are still missing.",
                FontSize = 11,
                Opacity = 0.4,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 14, 2, 2),
            });

            // ── Scroll wrapper ────────────────────────────────────────────
            var outerStack = new StackPanel { UseLayoutRounding = true, Spacing = 0 };
            outerStack.Children.Add(summaryBar);
            outerStack.Children.Add(cardStack);

            double windowH = Content.XamlRoot.Size.Height;
            double dialogChrome = 20;   // ContentDialog title bar / button bar height
            double verticalGap = 20;    // 10px top + 10px bottom

            var scroll = new ScrollViewer
            {
                UseLayoutRounding = true,
                Content = outerStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxWidth = Content.XamlRoot.Size.Width - (Content.XamlRoot.Size.Width / 1.7),
                MaxHeight = windowH - dialogChrome - verticalGap,
                MinWidth = Content.XamlRoot.Size.Width - (Content.XamlRoot.Size.Width / 1.7),
                MinHeight = windowH - dialogChrome - verticalGap
            };

            var smallButtonStyle = new Style(typeof(Button));
            smallButtonStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 120.0));
            smallButtonStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));

            var dialog = new ContentDialog
            {
                UseLayoutRounding = true,
                Title = "Missing Codecs",
                Content = scroll,
                CloseButtonText = "Continue →",
                CloseButtonStyle = smallButtonStyle,
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex) { Log.Print($"ShowCodecDialogAsync: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Card — entire surface clickable via Windows.System.Launcher
    // ─────────────────────────────────────────────────────────────────────────

    private static Border BuildCodecCard(CodecInfo codec)
    {
        string catIcon = codec.Category switch
        {
            "Video" => "🎬",
            "Audio" => "🎵",
            "Image" => "🖼️",
            _ => "📦",
        };

        var iconText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = catIcon,
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var textStack = new StackPanel { UseLayoutRounding = true, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

        textStack.Children.Add(new TextBlock
        {
            UseLayoutRounding = true,
            Text = codec.DisplayName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });

        textStack.Children.Add(new TextBlock
        {
            UseLayoutRounding = true,
            Text = codec.Description,
            FontSize = 11,
            Opacity = 0.55,
        });

        var chip = new Border
        {
            UseLayoutRounding = true,
            Background = new SolidColorBrush(Color.FromArgb(190, 0, 95, 210)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(130, 70, 150, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(11, 4, 11, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                UseLayoutRounding = true,
                Text = "Get  →",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
            },
        };

        var row = new Grid { UseLayoutRounding = true };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(iconText, 0);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(chip, 2);
        row.Children.Add(iconText);
        row.Children.Add(textStack);
        row.Children.Add(chip);

        // Default / hover / pressed brush pairs
        var bgDefault = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
        var bdDefault = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        var bgHover = new SolidColorBrush(Color.FromArgb(40, 0, 120, 215));
        var bdHover = new SolidColorBrush(Color.FromArgb(130, 0, 140, 255));
        var bgPressed = new SolidColorBrush(Color.FromArgb(65, 0, 100, 200));

        var card = new Border
        {
            UseLayoutRounding = true,
            Background = bgDefault,
            BorderBrush = bdDefault,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 11, 14, 11),
            Child = row,
        };

        card.PointerEntered += (_, _) => { card.Background = bgHover; card.BorderBrush = bdHover; };
        card.PointerExited += (_, _) => { card.Background = bgDefault; card.BorderBrush = bdDefault; };
        card.PointerPressed += (_, _) => card.Background = bgPressed;
        card.PointerReleased += (_, _) => card.Background = bgHover;

        // Windows.System.Launcher.LaunchUriAsync — built-in WinRT API that
        // opens the Microsoft Store app directly to the product page.
        card.Tapped += async (_, _) =>
        {
            try
            {
                if (codec.StoreUrl != null)
                    await Launcher.LaunchUriAsync(new Uri(codec.StoreUrl));
            }
            catch (Exception ex) { Log.Print($"Store launch ({codec.DisplayName}): {ex.Message}"); }
        };

        return card;
    }

    // ── Badge helper ──────────────────────────────────────────────────────────

    private static Border MakeBadge(string text, Color bg) => new()
    {
        UseLayoutRounding = true,
        Background = new SolidColorBrush(bg),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10, 3, 10, 3),
        Child = new TextBlock
        {
            UseLayoutRounding = true,
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
        },
    };
}
