using System.Windows;
using System.Windows.Input;
using Wydscop.Services;

namespace Wydscop;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private SearchWindow? _window;
    private GlobalHotkey? _hotkey;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Wydscop.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Log.Reset();
        LegacyMigration.Run(); // first run after the rename from EasyFind: bring settings, recents and pins along

        _window = new SearchWindow();
        _hotkey = new GlobalHotkey(_window, ModifierKeys.Alt, Key.Space);
        _hotkey.Pressed += (_, _) =>
        {
            Log.Write("Hotkey pressed");
            _window.Toggle();
        };
        Log.Write($"Hotkey {_hotkey.DisplayName} registered: {_hotkey.IsRegistered} (Win32 error {_hotkey.ErrorCode})");

        // Pinned last time: come back open, in the same place.
        if (BarPositionStore.LoadPinned())
        {
            _window.SetPinned(true);
            _window.ShowBar();
        }

        bool firstRun = StartupManager.EnsureDefault();

        _tray = new TrayIcon(_hotkey.DisplayName,
            open: () => _window.ShowBar(),
            clearRecent: _window.ClearRecent,
            getTheme: () => _window.BarTheme,
            setTheme: _window.SetTheme,
            exit: ExitApp);

        if (!_hotkey.IsRegistered)
        {
            _tray.ShowMessage("Wydscop",
                $"{_hotkey.DisplayName} is already used by another app. Close that app or change its shortcut, then restart Wydscop.",
                warning: true);
        }
        else if (firstRun)
        {
            _tray.ShowMessage("Wydscop is running", $"Press {_hotkey.DisplayName} to search.");
        }
    }

    private void ExitApp()
    {
        if (_window is not null) _window.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        if (_singleInstance is not null)
        {
            if (_window is not null) _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}
