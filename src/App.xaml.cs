using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.IO;
using Windows.ApplicationModel.Activation;

namespace SSPlayer;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            HandleActivation(activationArgs);
        }
        catch (Exception ex)
        {
            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSPlayer");
            Directory.CreateDirectory(appFolder);
            File.WriteAllText(Path.Combine(appFolder, "crash.txt"), $"=== OnLaunched {DateTime.Now} ===\n{ex}");
        }
    }

    // Single method handles BOTH first launch and redirected activations
    public void HandleActivation(AppActivationArguments args)
    {
        if (_window is null) return;

        if (args.Kind == ExtendedActivationKind.File && args.Data is IFileActivatedEventArgs fileArgs && fileArgs.Files.Count > 0)
        {
            _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () => await _window.OpenWithFilesAsync(fileArgs.Files));
        }

        // Always bring window to front on redirect
        _window.DispatcherQueue.TryEnqueue(() => _window.Activate());
    }
}