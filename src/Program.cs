using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Threading;


namespace SSPlayer;
public class Program
{
    private static App _app; // Store reference

    private static DispatcherQueue? _dispatcherQueue;

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var keyInstance = AppInstance.FindOrRegisterForKey("SSPlayer_Unique_Key");

        if (keyInstance.IsCurrent)
        {
            keyInstance.Activated += OnActivated;
            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread(); // capture it here
                _app = new App();
            });
        }
        else
        {
            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            keyInstance.RedirectActivationToAsync(activationArgs).AsTask().GetAwaiter().GetResult();
        }

        return 0;
    }

    private static void OnActivated(object sender, AppActivationArguments args)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            _app.HandleActivation(args);
        });
    }
}

