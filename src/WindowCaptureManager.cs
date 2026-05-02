using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace SSPlayer;

public class WindowCaptureManager : IDisposable
{
    private GraphicsCaptureItem _captureItem;
    private Direct3D11CaptureFramePool _framePool;
    private GraphicsCaptureSession _session;
    private CanvasDevice _canvasDevice;

    private CanvasBitmap _currentFrame;
    private readonly object _frameLock = new object();
    private Windows.Graphics.SizeInt32 _lastSize;
    public CaptureViewWindow window { get; private set; }
    private const float AspectRatio = 16f / 9f;
    public bool IsCapturing { get; private set; }
    /// <summary>
    /// Opens the System Picker to select a window and starts the capture.
    /// </summary>
    public async Task<bool> StartPickerCaptureAsync(MainWindow window, CanvasDevice canvasDevice)
    {
        try
        {
            // 1. Get the Microsoft.UI.WindowId from your window
            Microsoft.UI.WindowId muiWindowId = window.AppWindow.Id;

            // 2. Map it to Windows.UI.WindowId (the API expects this one)
            Windows.UI.WindowId wuiWindowId = new Windows.UI.WindowId()
            {
                Value = muiWindowId.Value
            };

            _captureItem = GraphicsCaptureItem.TryCreateFromWindowId(wuiWindowId);

            // 3. CRITICAL: Check for 0x0 size (common when window/video isn't ready)
            if (_captureItem.Size.Width == 0 || _captureItem.Size.Height == 0)
            {
                await window.ShowErrorDialogAsync("Zero Width/Height", "Invalid rect, must be dirtied for valid size.");
                Debug.WriteLine("Capture failed: Source has no dimensions. Load a video first.");
                return false;
            }

            _canvasDevice = CanvasDevice.GetSharedDevice();
            _lastSize = _captureItem.Size;
            _captureItem.Closed += OnCaptureItemClosed;

            _framePool = Direct3D11CaptureFramePool.Create(
                _canvasDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _lastSize);

            _framePool.FrameArrived += OnFrameArrived;
            _session = _framePool.CreateCaptureSession(_captureItem);

            ApplyExtendedCaptureProperties(_session);

            _session.StartCapture();
            IsCapturing = true;

            this.window = new CaptureViewWindow(this, canvasDevice);
            this.window.Activate();

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Capture Picker failed: {ex.Message}");
            return false;
        }
    }
    public static void MoveToDisplay(Window window, int displayIndex)
    {
        // 1. Get the AppWindow for your Window instance
        var appWindow = window.AppWindow;

        // 2. Get all available display areas
        var displayAreas = DisplayArea.FindAll();

        // Safety check: ensure the index exists
        if (displayIndex < 0 || displayIndex >= displayAreas.Count) return;

        // 3. Get the target display's bounds (WorkArea is better than OuterBounds 
        // because it accounts for the Taskbar)
        RectInt32 workArea = displayAreas[displayIndex].WorkArea;

        // 4. Move the window to the top-left of that display
        // You can also adjust width/height here if you want it fullscreened
        appWindow.MoveAndResize(new RectInt32(
            workArea.X,
            workArea.Y,
            appWindow.Size.Width,
            appWindow.Size.Height
        ));
    }
    private void ApplyExtendedCaptureProperties(GraphicsCaptureSession session)
    {
        // IsCursorCaptureEnabled was added in SDK 19041 (Version 2004)
        if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
        {
            session.IsCursorCaptureEnabled = false;
        }

        // IsBorderRequired was added in SDK 20348 (Version 21H1 / Server 2022)
        if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            session.IsBorderRequired = false;
        }
    }

    public void StopCapture()
    {
        IsCapturing = false;
        _session?.Dispose();
        _framePool?.Dispose();
        _captureItem = null;

        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
        }
    }
    public void DrawLatestFrame(CanvasDrawingSession ds, Rect destinationRect)
    {
        CanvasBitmap frameToDraw = null;

        lock (_frameLock)
        {
            // Verify the bitmap and its device are still healthy
            if (_currentFrame != null && !_currentFrame.Device.IsDeviceLost())
            {
                frameToDraw = _currentFrame;
            }
        }

        if (frameToDraw != null)
        {
            try
            {
                ds.DrawImage(frameToDraw, destinationRect);
            }
            catch { /* Ignore transient render errors during window state jumps */ }
        }
    }
    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();

        if (sender == null || frame == null) return;

        if (Interlocked.CompareExchange(ref _resizingState, 0, 0) == 1)
        {
            frame?.Dispose();
            return;
        }

        if (_canvasDevice != null && _canvasDevice.IsDeviceLost())
        {
            HandleDeviceLost();
            return;
        }

        try
        {
            var size = frame.ContentSize;

            // 1. Guard against invalid dimensions during transition
            if (size.Width <= 0 || size.Height <= 0) return;

            // 2. Handle the Resize/Maximize jump
            if (size.Width != _lastSize.Width || size.Height != _lastSize.Height)
            {
                _lastSize = size;

                // Recreate the pool for the new size
                sender.Recreate(
                    _canvasDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _lastSize);

                // CRITICAL: Return immediately. The current 'frame.Surface' 
                // is technically part of the old pool state.
                frame.Dispose();
                return;
            }

            // 3. Thread-safe Bitmap Creation
            // We use a local variable to ensure we don't hold the lock longer than needed
            CanvasBitmap bitmap = CanvasBitmap.CreateFromDirect3D11Surface(_canvasDevice, frame.Surface);

            lock (_frameLock)
            {
                _currentFrame?.Dispose();
                _currentFrame = bitmap;
            }
        }
        catch (Exception ex)
        {
            // Catch DXGI_ERROR_DEVICE_REMOVED or ObjectDisposedException
            Debug.WriteLine($"Capture Error: {ex.Message}");
        }
    }
    private void HandleDeviceLost()
    {
        Debug.WriteLine("Device lost detected. Re-initializing engine...");

        // 1. Clean up the old broken mess
        StopCapture();

        // 2. Get a fresh device
        _canvasDevice = CanvasDevice.GetSharedDevice();

        // 3. Restart the capture session with the NEW device
        // You'll need to re-run your setup logic here
        _ = StartPickerCaptureAsync(null, _canvasDevice);
    }
    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args) => StopCapture();
    private IntPtr WindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        return HandleSizingMessage(hWnd, uMsg, wParam, lParam);
    }
    public void ToggleFullscreen()
    {
        if (window == null) return;

        var appWin = window.AppWindow;

        if (appWin.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            appWin.SetPresenter(AppWindowPresenterKind.Overlapped);
        }
        else
        {
            appWin.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
    }
    private int _resizingState = 0; // 0 = idle, 1 = resizing

    public IntPtr HandleSizingMessage(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_LBUTTONDBLCLK = 0x0203;
        const uint WM_ENTERSIZEMOVE = 0x0231;
        const uint WM_EXITSIZEMOVE = 0x0232;
        const uint WM_SIZING = 0x0214;

        // --- NEW: Double Click Toggle ---
        if (uMsg == WM_LBUTTONDBLCLK)
        {
            var appWin = window.AppWindow;
            if (appWin.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                appWin.SetPresenter(AppWindowPresenterKind.Overlapped);
            else
                appWin.SetPresenter(AppWindowPresenterKind.FullScreen);
            return IntPtr.Zero;
        }

        // --- NEW: Memory Barrier for Thread Safety ---
        if (uMsg == WM_ENTERSIZEMOVE)
        {
            Interlocked.Exchange(ref _resizingState, 1);
        }
        else if (uMsg == WM_EXITSIZEMOVE)
        {
            Interlocked.Exchange(ref _resizingState, 0);
            ForcePoolRecreation();
        }
        // --- YOUR ORIGINAL ASPECT RATIO LOGIC (UNTOUCHED) ---
        else if (uMsg == WM_SIZING)
        {
            Win32.RECT rect = Marshal.PtrToStructure<Win32.RECT>(lParam);
            float ratio = 16f / 9f;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width < 160 || height < 90) return Win32.DefSubclassProc(hWnd, uMsg, wParam, lParam);

            int edge = wParam.ToInt32();
            if (edge == 1 || edge == 2) // WMSZ_LEFT or WMSZ_RIGHT
            {
                rect.Bottom = rect.Top + (int)(width / ratio);
            }
            else if (edge == 3 || edge == 6) // WMSZ_TOP or WMSZ_BOTTOM
            {
                rect.Right = rect.Left + (int)(height * ratio);
            }

            Marshal.StructureToPtr(rect, lParam, false);
        }

        return Win32.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
    private void ForcePoolRecreation()
    {
        if (_captureItem != null && _framePool != null)
        {
            // Re-read the actual size from the capture item
            var finalSize = _captureItem.Size;
            if (finalSize.Width > 0 && finalSize.Height > 0)
            {
                _lastSize = finalSize;
                _framePool.Recreate(_canvasDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _lastSize);
            }
        }
    }
    int wasDisposed = 0;
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref wasDisposed, 1, 0) != 0)
        {
            return;
        }

        StopCapture();
    }
}

public class CaptureViewWindow : Window
{
    private CanvasAnimatedControl _canvas;
    private WindowCaptureManager _manager;
    private IntPtr _hwnd = IntPtr.Zero;
    private const float AspectRatio = 16f / 9f;
    private Win32.SUBCLASSPROC _subclassProc;
    public readonly string WindowTitle = "SSPlayer-mirror";
    public IntPtr HWND => _hwnd;
    public CaptureViewWindow(WindowCaptureManager manager, CanvasDevice device)
    {
        Title = WindowTitle;
        _manager = manager;

        _canvas = new CanvasAnimatedControl
        {
            CustomDevice = device,
            ClearColor = Microsoft.UI.Colors.Black
        };

        _canvas.Draw += OnDraw;
        Content = _canvas;
        AppWindow.Title = WindowTitle;
        Activated += OnWindowActivated;

        Closed += (s, e) =>
        {
            _canvas.Paused = true;
            _canvas.RemoveFromVisualTree();
            _canvas = null;
            _manager.Dispose();
        };
        _canvas.DoubleTapped += (s, e) =>
        {
            var appWin = AppWindow;
            if (appWin.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                appWin.SetPresenter(AppWindowPresenterKind.Overlapped);
            else
                appWin.SetPresenter(AppWindowPresenterKind.FullScreen);
        };

        _canvas.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var menuState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
                
                if (menuState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                {
                    e.Handled = true;
                    // Accessing the manager's toggle function
                    _manager.ToggleFullscreen();
                }
            }
        };
        _subclassProc = new Win32.SUBCLASSPROC(WindowProc);
    }
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_hwnd != IntPtr.Zero) return;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        if (_hwnd != IntPtr.Zero)
        {
            Win32.SetWindowSubclass(_hwnd, _subclassProc, (uint)_hwnd.ToInt64(), IntPtr.Zero);
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
                          IntPtr uIdSubclass, IntPtr dwRefData)
    {
        return _manager.HandleSizingMessage(hWnd, uMsg, wParam, lParam);
    }
    private void OnDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (_manager == null || !_manager.IsCapturing) return;

        // Use the actual current control size
        var destRect = new Rect(0, 0, sender.Size.Width, sender.Size.Height);

        // Pass the drawing session to the manager
        _manager.DrawLatestFrame(args.DrawingSession, destRect);
    }

}