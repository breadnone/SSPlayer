using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;


namespace SSPlayer.Logs;

public static class Log
{
    public static MainWindow main => MainWindow.main;
    [Conditional("DEBUG")]
    public static void Print(string message)
    {
#if DEBUG

        NativeLogOverlay.Log(message);
#endif
    }

    [Conditional("DEBUG")]
    public static void PrintError(Exception log)
    {
#if DEBUG
        NativeLogOverlay.Log($"Error : {log.Message}");

#endif
    }
    public static void ThrowIfException(Exception ex, Func<bool> condition)
    {
        if (condition.Invoke())
        {
            throw new Exception(ex.Message);
        }
    }
}

public static class Error
{
    [StackTraceHidden]
    [DebuggerStepThrough]
    public static void ThrowIfNull<T>(this T target, [CallerArgumentExpression("target")] string paramName = "") where T : class
    {
        if (target == null)
            throw new Exception($"Error: '{paramName}' is Null.");
    }

    [StackTraceHidden]
    [DebuggerStepThrough]
    public static void ThrowIfDefault<T>(this T target, [CallerArgumentExpression("target")] string paramName = "") where T : struct
    {
        if (EqualityComparer<T>.Default.Equals(target, default))
        {
            throw new Exception($"Error: '{paramName}' ({typeof(T).Name}) is in its default/zero state.");
        }
    }

    [StackTraceHidden]
    [DebuggerStepThrough]
    public static void ThrowIfNullOrEmpty(this string target, [CallerArgumentExpression("target")] string paramName = "")
    {
        if (string.IsNullOrEmpty(target))
            throw new Exception($"Error: String '{paramName}' is Null or Empty.");
    }

    [StackTraceHidden]
    [DebuggerStepThrough]
    public static void ThrowIfNotEqual<T>(this T a, T b, [CallerArgumentExpression("a")] string aName = "", [CallerArgumentExpression("b")] string bName = "") where T : class
    {
        if (!ReferenceEquals(a, b))
        {
            throw new Exception($"Error: '{aName}' is not equal to '{bName}'.");
        }
    }
    [StackTraceHidden]
    [DebuggerStepThrough]
    public static bool IsSafeOrThrow<T>(this T sender, Exception ex, [CallerArgumentExpression("sender")] string aName = "") where T : class
    {
        if (ex is TaskCanceledException || ex is OperationCanceledException)
        {
            return true;
        }

        throw new Exception("Error : " + sender.GetType().Name + " - " + ex.Message);
    }
    [StackTraceHidden]
    [DebuggerStepThrough]
    public static bool IsNotSafe<T>(this T sender, Exception ex, [CallerArgumentExpression("sender")] string aName = "") where T : class
    {
        if (ex is not TaskCanceledException && ex is not OperationCanceledException)
        {
            return true;
        }

        throw new Exception("Error - sender : " + sender.GetType().Name + ex.Message);
    }
    public static bool IsCancelled(this CancellationTokenSource target)
    {
        return target.IsCancellationRequested;
    }
    public static bool IsCancelled(this CancellationToken token)
    {
        return token.IsCancellationRequested;
    }
}
public static class ModalDialog
{
    // Icon Constants
    private const uint MB_ICONERROR = 0x00000010;       // Red "X" stop sign
    private const uint MB_ICONWARNING = 0x00000030;     // Yellow triangle "!"
    private const uint MB_ICONINFORMATION = 0x00000040; // Blue "i" circle
    private const uint MB_ICONQUESTION = 0x00000020;    // Blue "?" 

    private const uint MB_OK = 0x00000000;

    public static void ShowError<T>(this T target, string title, string content, [CallerFilePath] string callerPath = "", [CallerMemberName] string methodName = "")
    {
        string className = System.IO.Path.GetFileNameWithoutExtension(callerPath);
        string debugInfo = $"[Called by: {className}.{methodName}]\n\n";
        MessageBox(IntPtr.Zero, debugInfo + content, title, MB_OK | MB_ICONERROR);
    }
    public static void ShowWarning<T>(this T target, string title, string content, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
    {
        string className = System.IO.Path.GetFileNameWithoutExtension(filePath);
        string enhancedContent = $"[Source: {className}.{memberName}]\n\n{content}";
        MessageBox(IntPtr.Zero, enhancedContent, title, MB_OK | MB_ICONWARNING);
    }
    public static void ShowInfo<T>(this T target, string title, string content, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
    {
        string className = System.IO.Path.GetFileNameWithoutExtension(filePath);
        string enhancedContent = $"[Source: {className}.{memberName}]\n\n{content}";
        MessageBox(IntPtr.Zero, enhancedContent, title, MB_OK | MB_ICONINFORMATION);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
public static class NativeLogOverlay
{
    // Window Styles
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VSCROLL = 0x00200000;

    // Extended Styles for DWM Overlay
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080; // Hides from Taskbar
    private const uint WS_EX_TRANSPARENT = 0x00000020; // Optional: Makes window click-through

    // Edit Control Styles
    private const uint ES_MULTILINE = 0x0004;
    private const uint ES_AUTOVSCROLL = 0x0040;
    private const uint ES_READONLY = 0x0800;

    // Win32 Messages
    private const uint WM_SETTEXT = 0x000C;
    private const uint EM_LINESCROLL = 0x00B6;
    private const int LWA_ALPHA = 0x2;

    // SetWindowPos Flags
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static IntPtr _hwnd;
    private static IntPtr _editHwnd;
    private static readonly Queue<string> _logLines = new Queue<string>();
    private const int MAX_LINES = 50;

    #region P/Invoke
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessage")]
    private static extern IntPtr SendMessageScroll(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    #endregion
    [Conditional("DEBUG")]
    /// <summary>Initializes the overlay at the top of the DWM chain.</summary>
    public static void Initialize(string title = "Debug Mode", int width = 400, int height = 250, byte opacity = 180)
    {
        if (_hwnd != IntPtr.Zero) return;

        // Position: Bottom Right
        int x = GetSystemMetrics(0) - width - 20;
        int y = GetSystemMetrics(1) - height - 60;

        // Create with TopMost and Layered styles
        _hwnd = CreateWindowEx(
            WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TOOLWINDOW,
            "Static", title, WS_POPUP | WS_VISIBLE,
            x, y, width, height,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        // Child Edit Control for the text
        _editHwnd = CreateWindowEx(
            0, "Edit", "",
            WS_CHILD | WS_VISIBLE | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL | ES_READONLY,
            0, 0, width, height,
            _hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        // Apply transparency
        SetLayeredWindowAttributes(_hwnd, 0, opacity, LWA_ALPHA);

        // Force to the very top of the Z-Order (DWM Overlay)
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }
    [Conditional("DEBUG")]
    public static void Log(string message)
    {
        if (_editHwnd == IntPtr.Zero) return;

        _logLines.Enqueue($"{DateTime.Now:HH:mm:ss} > {message}");

        while (_logLines.Count > MAX_LINES) _logLines.Dequeue();

        string fullText = string.Join(Environment.NewLine, _logLines);
        SendMessage(_editHwnd, WM_SETTEXT, IntPtr.Zero, fullText);
        SendMessageScroll(_editHwnd, EM_LINESCROLL, IntPtr.Zero, (IntPtr)MAX_LINES);
    }
}
