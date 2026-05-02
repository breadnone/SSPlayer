using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using Windows.Foundation;

namespace SSPlayer;
public class LiveWallpaper : Window
{
    private CanvasAnimatedControl _canvas;
    private AudioEngine _engine;
    private bool _initialized = false;
    static LiveWallpaper wallpaper;
    private TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedUpdateEventArgs> _updateHandler;
    private TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedDrawEventArgs> _drawHandler;
    // Add field
    private IntPtr _hWnd = IntPtr.Zero;


    public static void DestroyLiveWallpaper()
    {
        wallpaper?.Shutdown();
        wallpaper?.Close();
        wallpaper = null;
    }
    public LiveWallpaper(AudioEngine engine)
    {
        if (wallpaper != null)
        {
            Activated -= OnFirstActivated;
            wallpaper.Close();
        }

        wallpaper = this;
        _engine = engine;
        Activated += OnFirstActivated;

        _canvas = new CanvasAnimatedControl
        {
            UseLayoutRounding = true,
            ClearColor = Colors.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        SizeChanged += (sender, args) =>
        {
            _canvas.Width = args.Size.Width;
            _canvas.Height = args.Size.Height;
        };

        _updateHandler = (s, e) => _engine?.Update(s, e);
        _drawHandler = (s, e) => _engine?.Draw(s, e);
        _canvas.Update += _updateHandler;
        _canvas.Draw += _drawHandler;
        Content = _canvas;
    }
    public void Shutdown()
    {
        if (_canvas != null)
        {
            _canvas.Update -= _updateHandler;
            _canvas.Draw -= _drawHandler;
            _canvas.Paused = true;
            _canvas.RemoveFromVisualTree();
            _canvas = null;
        }

        _updateHandler = null;
        _drawHandler = null;
        _engine = null;

        if (_hWnd != IntPtr.Zero)
        {
            Win32.SetParent(_hWnd, IntPtr.Zero);

            Win32.EnumWindows((topHwnd, lParam) =>
            {
                IntPtr shellView = Win32.FindWindowEx(topHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellView != IntPtr.Zero)
                {
                    IntPtr workerW = Win32.FindWindowEx(IntPtr.Zero, topHwnd, "WorkerW", null);
                    if (workerW != IntPtr.Zero)
                        Win32.ShowWindow(workerW, 0);
                }
                return true;
            }, IntPtr.Zero);

            IntPtr progman = Win32.FindWindow("Progman", null);
            
            if (progman != IntPtr.Zero)
                Win32.InvalidateRect(progman, IntPtr.Zero, true);

            Win32.SystemParametersInfo(0x0014, 0, null, 0x03);

            _hWnd = IntPtr.Zero;
        }
    }
    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _hWnd = hWnd;

        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        const int GWL_EXSTYLE = -20;
        const uint WS_EX_TOOLWINDOW = 0x00000080;
        const uint WS_EX_NOACTIVATE = 0x08000000;
        uint newStyle = 0x80000000 | 0x10000000 | 0x04000000;
        Win32.SetWindowLongPtr(hWnd, -16, new IntPtr(newStyle));
        uint newExStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        Win32.SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(newExStyle));
        SetupWallpaperLayer(hWnd);
    }
    private void SetupWallpaperLayer(IntPtr hWnd)
    {
        IntPtr progman = Win32.FindWindow("Progman", null);
        IntPtr result = IntPtr.Zero;
        Win32.SendMessageTimeout(progman, 0x052C, new IntPtr(0), IntPtr.Zero, Win32.SendMessageTimeoutFlags.SMTO_NORMAL, 1000, out result);
        IntPtr workerW = IntPtr.Zero;

        Win32.EnumWindows((topHwnd, lParam) =>
        {
            IntPtr shellView = Win32.FindWindowEx(topHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (shellView != IntPtr.Zero)
            {
                workerW = Win32.FindWindowEx(IntPtr.Zero, topHwnd, "WorkerW", null);
            }

            return true;
        }, IntPtr.Zero);

        if (workerW == IntPtr.Zero) workerW = progman;

        if (workerW != IntPtr.Zero)
        {
            Win32.SetParent(hWnd, workerW);
            var display = DisplayArea.Primary;
            int width = display.OuterBounds.Width;
            int height = display.OuterBounds.Height;
            const uint SWP_NOZORDER = 0x0004;
            const uint SWP_NOACTIVATE = 0x0010;
            const uint SWP_SHOWWINDOW = 0x0040;
            Win32.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }
}
