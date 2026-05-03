using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using Windows.Foundation;
using Windows.UI;

namespace SSPlayer;

public class TimelineControl : Grid
{
    public TextBlock StartTimeText { get; private set; }
    public TextBlock EndTimeText { get; private set; }
    public TextBlock TitleLabel { get; private set; }
    TextBlock _floatingLabel;
    public event EventHandler<double> StartRatioChanged;
    public event EventHandler<double> EndRatioChanged;
    public event EventHandler<double> ValueChanged;
    private readonly bool _isDualHandle;
    private readonly Grid _trackArea;
    private readonly Rectangle _trackRange;
    private readonly FrameworkElement _startHandle;
    private readonly FrameworkElement _endHandle;
    private TimeSpan _duration;
    private double _startRatio = 0.0;
    private double _endRatio = 1.0;
    private FrameworkElement _active;
    private double _dragOffsetX;
    private const double PAD = 8.0;
    private const double MIN_GAP = 24.0;
    public double Minimum { get; set; } = 0;
    public double Maximum { get; set; } = 100;
    public double Value => Minimum + (_startRatio * (Maximum - Minimum));
    public void SetValues(double start, double end = -1)
    {
        if (Maximum <= Minimum) return;

        _startRatio = Math.Clamp((start - Minimum) / (Maximum - Minimum), 0, 1);

        if (_isDualHandle && end != -1)
        {
            _endRatio = Math.Clamp((end - Minimum) / (Maximum - Minimum), 0, 1);
        }

        Render();
    }
    public TimelineControl(string title = "", bool isDualHandle = true)
    {
        _isDualHandle = isDualHandle;
        UseLayoutRounding = true;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var timesGrid = new Grid { UseLayoutRounding = true, Margin = new Thickness(0, 0, 0, 6) };
        timesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        timesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        StartTimeText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = "00:00.00",
            FontSize = 13,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 59, 158, 255)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        TitleLabel = new TextBlock
        {
            UseLayoutRounding = true,
            Text = title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.WhiteSmoke),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8
        };

        EndTimeText = new TextBlock
        {
            UseLayoutRounding = true,
            Text = "00:00.00",
            FontSize = 13,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 74, 222, 128)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(StartTimeText, 0);
        Grid.SetColumn(TitleLabel, 1);
        Grid.SetColumn(EndTimeText, 2);

        timesGrid.Children.Add(StartTimeText);
        timesGrid.Children.Add(TitleLabel);
        timesGrid.Children.Add(EndTimeText);
        Grid.SetRow(timesGrid, 0);
        Children.Add(timesGrid);

        _trackArea = new Grid { UseLayoutRounding = true, Height = 56, Background = new SolidColorBrush(Colors.Transparent) };

        var trackBg = new Rectangle
        {
            UseLayoutRounding = true,
            Height = 4,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Bottom,  // ← was Center
            Margin = new Thickness(PAD, 0, PAD, 12)
        };

        _trackArea.Children.Add(trackBg);

        _trackRange = new Rectangle
        {
            UseLayoutRounding = true,
            Height = 4,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(90, 90, 160, 255)),
            VerticalAlignment = VerticalAlignment.Bottom, // ← was Center
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _trackArea.Children.Add(_trackRange);
        _startHandle = CreateHandle(ColorHelper.FromArgb(255, 59, 158, 255));
        _trackArea.Children.Add(_startHandle);

        if (isDualHandle)
        {
            _endHandle = CreateHandle(ColorHelper.FromArgb(255, 74, 222, 128));
            _trackArea.Children.Add(_endHandle);
        }
        // Floating label — only used in single handle mode
        if (!isDualHandle)
        {
            _floatingLabel = new TextBlock
            {
                UseLayoutRounding = true,
                Text = "0:00.00",
                FontSize = 11,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(220, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
                IsHitTestVisible = false
            };

            _trackArea.Children.Add(_floatingLabel);
        }

        Grid.SetRow(_trackArea, 1);
        Children.Add(_trackArea);
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(_startHandle, true);
        if (_endHandle != null) Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(_endHandle, true);

        _trackArea.PointerPressed += OnPointerPressed;
        _trackArea.PointerMoved += OnPointerMoved;
        _trackArea.PointerReleased += OnPointerReleased;
        _trackArea.PointerCaptureLost += (s, e) => { _active = null; };
        SizeChanged += (s, e) => Render();
    }

    public void Restore(double startRatio, double endRatio, TimeSpan duration)
    {
        _duration = duration;
        _startRatio = Math.Clamp(startRatio, 0, 1);
        _endRatio = Math.Clamp(endRatio > 0 ? endRatio : 1.0, 0, 1);
        Render();
    }

    public void SetDuration(TimeSpan duration) { _duration = duration; Render(); }
    private double TrackWidth => Math.Max(1, _trackArea.ActualWidth - PAD * 2);
    private double RatioToX(double ratio) => PAD + ratio * TrackWidth;
    private double XToRatio(double x) => Math.Clamp((x - PAD) / TrackWidth, 0, 1);
    private void Render()
    {
        // Labels always update regardless of layout readiness
        if (_isDualHandle)
        {
            UpdateLabel(StartTimeText, _startRatio);
            UpdateLabel(EndTimeText, _endRatio);
        }

        if (_trackArea.ActualWidth == 0) return;

        double sx = RatioToX(_startRatio);
        double ex = RatioToX(_endRatio);

        _startHandle.Margin = new Thickness(sx - 14, 0, 0, 0);
        if (_endHandle != null) _endHandle.Margin = new Thickness(ex - 14, 0, 0, 0);

        _trackRange.Margin = new Thickness(sx, 0, 0, 0);
        _trackRange.Width = Math.Max(0, ex - sx);

        // Single handle — float label above the thumb, clamped to track bounds
        if (!_isDualHandle && _floatingLabel != null)
        {
            UpdateLabel(_floatingLabel, _startRatio);
            double labelW = _floatingLabel.ActualWidth > 0 ? _floatingLabel.ActualWidth : 40;
            double clampedLeft = Math.Clamp(sx - labelW / 2, PAD, _trackArea.ActualWidth - PAD - labelW);
            _floatingLabel.Margin = new Thickness(clampedLeft, 0, 0, 0);
        }
    }
    private void UpdateLabel(TextBlock tb, double ratio)
    {
        double totalSecs = _duration.TotalSeconds > 0 ? _duration.TotalSeconds : 0;
        double secs = totalSecs * Math.Clamp(ratio, 0, 1);
        int m = (int)(secs / 60);
        double s = secs % 60;
        tb.Text = $"{m}:{s:00.00}";
    }
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(_trackArea).Position.X;
        double sx = RatioToX(_startRatio);
        double ex = _isDualHandle ? RatioToX(_endRatio) : sx;
        double dStart = Math.Abs(pt - sx);
        double dEnd = _isDualHandle ? Math.Abs(pt - ex) : double.MaxValue;

        if (dStart < 32 || dEnd < 32)
        {
            _active = (dStart <= dEnd) ? _startHandle : _endHandle;
            _dragOffsetX = pt - (_active == _startHandle ? sx : ex);
            _trackArea.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_active == null) return;

        double pt = e.GetCurrentPoint(_trackArea).Position.X;
        double ratio = XToRatio(pt - _dragOffsetX);
        double minGap = MIN_GAP / TrackWidth;

        if (_active == _startHandle)
        {
            _startRatio = Math.Clamp(ratio, 0, _isDualHandle ? _endRatio - minGap : 1);
            StartRatioChanged?.Invoke(this, _startRatio);
            ValueChanged?.Invoke(this, this.Value);
        }
        else
        {
            _endRatio = Math.Clamp(ratio, _startRatio + minGap, 1);
            EndRatioChanged?.Invoke(this, _endRatio);
        }

        Render();
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _active = null;
        _trackArea.ReleasePointerCapture(e.Pointer);
    }

    private static FrameworkElement CreateHandle(Color color)
    {
        var stack = new StackPanel
        {
            UseLayoutRounding = true,
            Width = 28,
            Height = 56,         
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Transparent),
            Spacing = 3,
            Padding = new Thickness(0, 22, 0, 0) 
        };
        stack.Children.Add(new Rectangle
        {
            UseLayoutRounding = true,
            Width = 3,
            Height = 16,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.Children.Add(new Ellipse
        {
            UseLayoutRounding = true,
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        return stack;
    }
}