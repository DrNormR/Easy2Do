using Foundation;
using UIKit;
using AudioToolbox;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.iOS;

namespace Easy2Do.iOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the
// User Interface of the application, as well as listening (and optionally responding) to
// application events from iOS.
[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .AfterSetup(_builder =>
            {
                // App services are now fully initialised.

                // Set iOS alarm sound via AudioToolbox vibration.
                App.AlarmService.PlatformSoundAction = static () =>
                {
                    try { SystemSound.Vibrate.PlaySystemSound(); }
                    catch { /* sound is best-effort */ }
                };

                // Save notes when the app enters the background.
                // BeginBackgroundTask gives iOS up to ~5 seconds to finish before suspending.
                UIApplication.Notifications.ObserveDidEnterBackground((_, _) =>
                {
                    var app = UIApplication.SharedApplication;
                    var taskId = app.BeginBackgroundTask(() => { });
                    _ = Task.Run(async () =>
                    {
                        try { await App.SaveAllNotesAsync(); }
                        finally { app.EndBackgroundTask(taskId); }
                    });
                });
            });
    }
}
