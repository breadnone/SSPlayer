using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;
using static SSPlayer.MainWindow;

namespace SSPlayer;

public class ShortcutManager
{
    private readonly MainWindow _window;
    private InputKeyboardSource _inputSource;
    public static ShortcutManager Initialize(MainWindow window) => new ShortcutManager(window);
    public ShortcutManager(MainWindow window)
    {
        _window = window;
        window.Activated += OnWindowActivated;
        window.Closed += OnWindowClosed;
    }
    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        _window.Activated -= OnWindowActivated;
        Init();
    }
    private void Init()
    {
        if (_inputSource != null) return;
        var inputSource = InputKeyboardSource.GetForIsland(_window.Content.XamlRoot.ContentIsland);

        if (inputSource != null)
        {
            inputSource.KeyDown += OnKeyDown;
            inputSource.KeyUp += OnKeyUp;
        }
    }

    private void OnKeyDown(InputKeyboardSource sender, KeyEventArgs args)
    {
        var key = args.VirtualKey;

        if (args.VirtualKey == VirtualKey.Menu)
        {
            _altPressed = true;
            return;
        }

        if (args.VirtualKey == VirtualKey.Enter && _altPressed)
        {
            args.Handled = true;
            _altPressed = false;
            _window.ToggleFullscreen();
            return;
        }

        switch (key)
        {
            case (VirtualKey)179: // MediaPlayPause 
            case VirtualKey.Execute:
                args.Handled = true;
                HandlePlayPauseToggle();
                return;

            case (VirtualKey)176: // MediaNextTrack
            case VirtualKey.GoForward:
                args.Handled = true;
                _ = _window.SkipNext();
                return;

            case (VirtualKey)177: // MediaPreviousTrack
            case VirtualKey.GoBack:
                args.Handled = true;
                _ = _window.SkipPrevious();
                return;

            case (VirtualKey)178: // MediaStop
                args.Handled = true;
                _window.PauseMedia(PlayerPlayState.Paused);
                return;
        }

        var focused = FocusManager.GetFocusedElement(_window.Content.XamlRoot);

        if (!(focused is Microsoft.UI.Xaml.Controls.TextBox))
        {
            switch (key)
            {
                // Space for Play/Pause
                case VirtualKey.Space:
                    args.Handled = true;
                    HandlePlayPauseToggle();
                    break;

                // Left/Right Arrows for Seeking (e.g., 5 seconds)
                case VirtualKey.Left:
                    args.Handled = true;
                    _window.SeekBackward(TimeSpan.FromSeconds(5));
                    break;

                case VirtualKey.Right:
                    args.Handled = true;
                    _window.SeekForward(TimeSpan.FromSeconds(5));
                    break;

                // Up/Down Arrows for Volume
                case VirtualKey.Up:
                    args.Handled = true;
                    _window.UpdateVolume(5); // Increase 5%
                    break;
                case VirtualKey.Down:
                    args.Handled = true;
                    _window.UpdateVolume(-5); // Decrease 5%
                    break;
                // M Key for Mute
                case VirtualKey.M:
                    args.Handled = true;
                    var isMuted = _window.ToggleMute();
                    _window.ShowPlayPauseStatus(isMuted ? "\uE74F" : "\uE994", 1.4);
                    break;
                case VirtualKey.P:
                    args.Handled = true;
                    _ = _window.SkipPrevious();
                    break;
                case VirtualKey.N:
                    args.Handled = true;
                    _ = _window.SkipNext();
                    break;
            }
        }
    }
    private void HandlePlayPauseToggle()
    {
        if (_window.PlayState == PlayerPlayState.Playing)
        {
            _window.PauseMedia(PlayerPlayState.Paused);
            _window.ShowPlayPauseStatus("\uF8AE", 1.4);
        }
        else if (_window.PlayState == PlayerPlayState.Paused)
        {
            _window.PlayMedia();
            _window.ShowPlayPauseStatus("\uF5B0", 1.4);
        }
    }
    private bool _altPressed = false;
    private void OnKeyUp(InputKeyboardSource sender, KeyEventArgs args)
    {
        if (args.VirtualKey == VirtualKey.Menu)
            _altPressed = false;
    }
    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        // Unsubscribe from window events
        _window.Activated -= OnWindowActivated;
        _window.Closed -= OnWindowClosed;

        // Unsubscribe from keyboard events and null the source
        if (_inputSource != null)
        {
            _inputSource.KeyDown -= OnKeyDown;
            _inputSource.KeyUp -= OnKeyUp;
            _inputSource = null;
        }
    }
}