using Microsoft.Graphics.Canvas;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SSPlayer;

public class PlayerSettings
{
    public int Rotation { get; set; } = 0;
    public bool FlipHorizontal { get; set; } = false;
    public bool RepeatPlaylist { get; set; } = false;
    public bool BorderlessMode { get; set; } = true;
    public bool AutoPlay { get; set; } = true;
    public double PlaybackSpeed { get; set; } = 1.0;
    public double Volume { get; set; } = 50.0;
    public bool RepeatForever { get; set; } = false;
    public double Brightness { get; set; } = 0.5;
    public double Contrast { get; set; } = 0.5;
    public bool EnableFadeIn { get; set; }
    public bool EnableFadeOut { get; set; }
    public double SpeakerBalance { get; set; } = 0;
    public EqualizerSettings Equalizer { get; set; } = new EqualizerSettings();
}
public enum InternalPlayStatus { None = 0, ForcePlay = 1, ForcePause = 2 }
public class PlaylistItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public bool IsAudio { get; set; } = false;

    [System.Text.Json.Serialization.JsonIgnore]
    private BitmapImage? _thumbnail;

    [System.Text.Json.Serialization.JsonIgnore]
    public BitmapImage Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(); }
    }
}
public class DraggableOverlay : ContentControl
{
    public DraggableOverlay(FrameworkElement content)
    {
        Content = content;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public void ChangeCursor(InputSystemCursorShape shape)
    {
        ProtectedCursor = InputSystemCursor.Create(shape);
    }
}

public class AtlasWrapper
{
    public int hashcode { get; set; }
    public List<CanvasRenderTarget> atlases { get; set; } = new List<CanvasRenderTarget>();
    public void Dispose()
    {
        foreach (var itm in atlases)
        {
            itm.Dispose();
        }
    }
}

[DynamicInterfaceCastableImplementation] // keeps it from being trimmed
[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public unsafe interface IMemoryBufferByteAccess
{
    [PreserveSig]
    void GetBuffer(out byte* buffer, out uint capacity);
}

public class EqualizerSettings
{
    public bool IsEnabled { get; set; } = true;
    // Default gains are 0.0 (neutral) for all 10 bands
    public List<double> BandGains { get; set; } = new() { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    public void Reset() => BandGains = new() { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
}

public sealed class CanvasObject : Grid
{
    // This ensures the trimmer sees the property even if it thinks the class is unused
    internal static void _KeepAlive() => _ = new CanvasObject().Cursor;

    public InputCursor? Cursor { get => ProtectedCursor; set => ProtectedCursor = value; }
}