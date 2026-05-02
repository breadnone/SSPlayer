
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SSPlayer;
using SSPlayer.Logs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PlayerSettings))]
[JsonSerializable(typeof(EqualizerSettings))]
[JsonSerializable(typeof(List<double>))]
[JsonSerializable(typeof(List<PlaylistItem>))]
[JsonSerializable(typeof(PlaylistItem))]
[JsonSerializable(typeof(ObservableCollection<PlaylistItem>))]
[JsonSerializable(typeof(CodecCheckRecord))]
internal partial class AppJsonContext : JsonSerializerContext { }

namespace SSPlayer
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2257", Justification = "False positive — INotifyPropertyChanged is not IDynamicInterfaceCastable")]
    public sealed partial class MainWindow
    {
        private Dictionary<int, SoftwareBitmapSource> _thumbnailSources = new();
        private List<FrameworkElement> overlays = new();
        private DispatcherTimer _infoUpdateTimer;
        private TextBlock _dynamicInfoText;
        private LiveWallpaper _bgWindow;
        private CanvasAnimatedControl canvas;
        private TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedUpdateEventArgs> _updateHandler;
        private TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedDrawEventArgs> _drawHandler;
        private List<AtlasWrapper> _gpuAtlases = new(4);
        private int _totalFrameCount = 0;
        private int _framesPerAtlas = 0;

        private void CleanupThumbnailState()
        {
            IsThumbnailReady = false;
            _currentFrameIdx = 0;
            _totalFrameCount = 0;
            _framesPerAtlas = 0;
            _canvasImageSource = null;
        }

        public bool IsThumbnailReady { get; private set; }
        private CanvasDevice canvasDevice;
        private CanvasImageSource _canvasImageSource;
        private int _currentFrameIdx = 0;
        private Microsoft.UI.Xaml.Shapes.Rectangle _thumbnailRect;
        CancellationTokenSource activeThumbnailWorker;
        private void StopAndClearThumbnailStrip()
        {
            try
            {
                // Cancel in-flight worker
                activeThumbnailWorker?.Cancel();
                activeThumbnailWorker?.Dispose();
                activeThumbnailWorker = null;
            }
            catch { }

            // Dispose all GPU atlases and free VRAM
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    foreach (var atm in _gpuAtlases)
                    {
                        try { atm?.Dispose(); } catch { }
                    }

                    _gpuAtlases.Clear();

                    if (_thumbnailRect != null)
                        _thumbnailRect.Fill = null;

                    CleanupThumbnailState();
                    ToggleNonBlockingLoading(false);
                }
                catch { }
            });
        }

        private void StartThumbnailStripGeneration(StorageFile f, Action onCompleted)
        {
            // Cancel and fully dispose previous worker before starting new one
            activeThumbnailWorker?.Cancel();
            activeThumbnailWorker?.Dispose();
            activeThumbnailWorker = null;

            // Dispose stale atlases from previous file immediately
            foreach (var atm in _gpuAtlases)
            {
                try { atm?.Dispose(); } catch { }
            }

            _gpuAtlases.Clear();
            CleanupThumbnailState();
            activeThumbnailWorker = CancellationTokenSource.CreateLinkedTokenSource(MainTokenSource.Token);
            var token = activeThumbnailWorker.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;
                    await ProcessThumbnailStripAsync(f, onCompleted, token);
                }
                catch (Exception ex)
                {
                    Log.Print($"Thumbnail strip failed or was cancelled: {ex.Message}");
                }
            }, token);
        }
        private async Task ProcessThumbnailStripAsync(StorageFile f, Action onCompleted, CancellationToken token)
        {
            // D3D hard limits — stay well under the minimum guaranteed cap (8192px)
            // to be safe on older integrated GPUs. 8192 is the DirectX 10 minimum.
            const int MAX_ATLAS_WIDTH = 8192;
            // Memory budget per atlas in bytes: 32 MB = comfortable VRAM budget
            // THUMB_W * THUMB_H * 4 bytes (BGRA) per frame
            const long MAX_ATLAS_BYTES = 32 * 1024 * 1024;

            if (token.IsCancellationRequested)
            {
                CleanupThumbnailState();
            }

            using var previewPlayer = new MediaPlayer();
            previewPlayer.IsVideoFrameServerEnabled = true;
            previewPlayer.IsMuted = true;
            previewPlayer.Source = MediaSource.CreateFromStorageFile(f);

            var openTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            previewPlayer.MediaOpened += OnMediaOpened;
            void OnMediaOpened(MediaPlayer s, object _) => openTcs.TrySetResult(true);

            try { await openTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), token); }
            catch { return; }
            finally { previewPlayer.MediaOpened -= OnMediaOpened; }

            if (token.IsCancellationRequested)
            {
                CleanupThumbnailState();
                return;
            }

            double naturalW = previewPlayer.PlaybackSession.NaturalVideoWidth;
            double naturalH = previewPlayer.PlaybackSession.NaturalVideoHeight;
            if (naturalW <= 0)
            {
                CleanupThumbnailState();
                return;
            }
            double videoRatio = naturalW / naturalH;
            double targetRatio = (double)THUMB_W / THUMB_H;
            double scale = videoRatio > targetRatio ? (double)THUMB_W / naturalW : (double)THUMB_H / naturalH;
            int fitW = (int)(naturalW * scale);
            int fitH = (int)(naturalH * scale);
            int offsetX = (THUMB_W - fitW) / 2;
            int offsetY = (THUMB_H - fitH) / 2;

            // ── Compute how many frames we can safely fit ───────────────────
            long bytesPerFrame = THUMB_W * THUMB_H * 4L;
            int maxByWidth = MAX_ATLAS_WIDTH / THUMB_W;                 // texture width cap
            int maxByMemory = (int)(MAX_ATLAS_BYTES / bytesPerFrame);    // memory cap
            int framesPerAtlas = Math.Max(1, Math.Min(maxByWidth, maxByMemory));

            // Total desired frames — cap at a reasonable number so very long videos
            // don't spin up hundreds of atlases. 200 frames = ~5 atlases of 40 = plenty.
            int desiredTotal = STRIP_FRAME_COUNT;   // your existing constant (40)
                                                    // For very long videos (> 2hr), you may want more resolution:
                                                    // desiredTotal = Math.Min(200, STRIP_FRAME_COUNT);
            int totalFrames = desiredTotal;
            int atlasCount = (int)Math.Ceiling((double)totalFrames / framesPerAtlas);

            long durationTicks = previewPlayer.PlaybackSession.NaturalDuration.Ticks;

            // ── Allocate all atlases upfront ────────────────────────────────
            var at = new AtlasWrapper();
            at.hashcode = f.GetHashCode();
            CanvasRenderTarget thumbTarget = null;

            try
            {
                for (int a = 0; a < atlasCount; a++)
                {
                    int framesInThisAtlas = (a < atlasCount - 1) ? framesPerAtlas : totalFrames - (framesPerAtlas * (atlasCount - 1));
                    at.atlases.Add(new CanvasRenderTarget(canvasDevice, THUMB_W * framesInThisAtlas, THUMB_H, 96));
                }

                thumbTarget = new CanvasRenderTarget(canvasDevice, THUMB_W, THUMB_H, 96);
            }
            catch
            {
                at.Dispose();
                thumbTarget?.Dispose();
                return;
            }

            using var frameReadyLock = new SemaphoreSlim(0, 1);

            TypedEventHandler<MediaPlayer, object> handler = (s, e) =>
            {
                if (frameReadyLock.CurrentCount == 0) frameReadyLock.Release();
            };

            previewPlayer.VideoFrameAvailable += handler;

            try
            {
                for (int i = 0; i < totalFrames; i++)
                {
                    if (token.IsCancellationRequested) break;

                    previewPlayer.PlaybackSession.Position = TimeSpan.FromTicks((long)(durationTicks * ((double)i / totalFrames)));

                    if (!await frameReadyLock.WaitAsync(TimeSpan.FromMilliseconds(500), token))
                        continue;

                    if (token.IsCancellationRequested) break;

                    previewPlayer.CopyFrameToVideoSurface(thumbTarget, new Rect(0, 0, THUMB_W, THUMB_H));
                    int atlasIdx = i / framesPerAtlas;
                    int localIdx = i % framesPerAtlas;
                    var atlas = at.atlases[atlasIdx];

                    using var ds = atlas.CreateDrawingSession();
                    ds.FillRectangle(localIdx * THUMB_W, 0, THUMB_W, THUMB_H, Colors.Black);
                    ds.DrawImage(thumbTarget, new Rect(localIdx * THUMB_W + offsetX, offsetY, fitW, fitH), new Rect(0, 0, THUMB_W, THUMB_H));
                }
            }
            catch (Exception drw1)
            {
                this.IsSafeOrThrow(drw1);
                Log.Print("Warning : Thumbnail generation was failed." + drw1.Message);
            }
            finally
            {
                thumbTarget.Dispose();
                previewPlayer.VideoFrameAvailable -= handler;
                previewPlayer.Pause();
                previewPlayer.Source = null;
            }

            if (token.IsCancellationRequested)
            {
                CleanupThumbnailState();
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (token.IsCancellationRequested)
                {
                    CleanupThumbnailState();
                    return;
                }

                _gpuAtlases.Add(at);
                _totalFrameCount = totalFrames;
                _framesPerAtlas = framesPerAtlas;

                _canvasImageSource = new CanvasImageSource(canvasDevice, THUMB_W, THUMB_H, 96);
                var brush = new ImageBrush { ImageSource = _canvasImageSource, Stretch = Stretch.Fill };
                if (_thumbnailRect != null) _thumbnailRect.Fill = brush;

                IsThumbnailReady = true;
                onCompleted?.Invoke();
            });
        }

        public void ShowThumbnailPreviewAtPosition(int hascode, double ratio)
        {
            if (hascode == -1) return;

            if (!IsThumbnailReady || _gpuAtlases.Count == 0 || _canvasImageSource == null) return;

            int frameIdx = Math.Clamp((int)Math.Floor(ratio * _totalFrameCount), 0, _totalFrameCount - 1);

            if (_currentFrameIdx == frameIdx) return;
            _currentFrameIdx = frameIdx;

            int atlasIdx = frameIdx / _framesPerAtlas;
            int localIdx = frameIdx % _framesPerAtlas;

            if (atlasIdx >= _gpuAtlases.Count) return;

            try
            {
                var atlas = _gpuAtlases.Find(x => x.hashcode == hascode);
                if (atlas == null) return;
                using var ds = _canvasImageSource.CreateDrawingSession(Colors.Transparent);
                ds.DrawImage(atlas.atlases[atlasIdx], new Rect(0, 0, THUMB_W, THUMB_H), new Rect(localIdx * THUMB_W, 0, THUMB_W, THUMB_H));
            }
            catch (Exception drw0)
            {
                Log.Print("Error: Getting thumbnail preview was failed. " + drw0.Message);
            }
        }

        private CancellationTokenSource _thumbLoaderCts;
        private void StartLazyThumbnailLoading()
        {
            _thumbLoaderCts?.Cancel();
            _thumbLoaderCts = CancellationTokenSource.CreateLinkedTokenSource(MainTokenSource.Token);
            var token = _thumbLoaderCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(800, token);

                    foreach (var item in _playlistCollection.ToList())
                    {
                        if (token.IsCancellationRequested) return;
                        if (item.Thumbnail != null) continue;
                        string ext = System.IO.Path.GetExtension(item.Path).ToLowerInvariant();
                        bool isAudio = new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".ogg", ".opus" }.Contains(ext);
                        if (isAudio || !File.Exists(item.Path)) continue;
                        await LoadThumbnailAsync(item);
                        await Task.Delay(200, token);
                    }
                }
                catch (Exception ex)
                {
                    this.IsSafeOrThrow(ex);
                }
            }, token);
        }

        private void InitializeThumbnailPopup()
        {
            _thumbnailRect = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                UseLayoutRounding = true,
                Width = THUMB_W,
                Height = THUMB_H
            };

            var border = new Border
            {
                UseLayoutRounding = true,
                Child = _thumbnailRect,
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Colors.DimGray),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Colors.Black),
                Padding = new Thickness(0)
            };

            _thumbnailPopup = new Popup
            {
                UseLayoutRounding = true,
                Child = border,
                IsOpen = false,
                IsLightDismissEnabled = false
            };

            if (!rootGrid.Children.Contains(_thumbnailPopup))
                rootGrid.Children.Add(_thumbnailPopup);
        }
    }
}
