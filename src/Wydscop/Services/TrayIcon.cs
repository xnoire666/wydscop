using Forms = System.Windows.Forms;

namespace Wydscop.Services;

/// <summary>
/// The notification-area icon and its right-click menu.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _startupItem;

    public TrayIcon(string hotkeyName, Action open, Action clearRecent,
        Func<BarTheme> getTheme, Action<BarTheme> setTheme, Action exit)
    {
        // Theme ▸ Light (soft gray) / Dark / Match Windows — the current one is checked.
        var themeMenu = new Forms.ToolStripMenuItem("Theme");
        foreach (var (label, mode) in new[] { ("Light", BarTheme.Light), ("Dark", BarTheme.Dark), ("Match Windows", BarTheme.System) })
        {
            var item = new Forms.ToolStripMenuItem(label) { Tag = mode };
            item.Click += (_, _) => setTheme(mode);
            themeMenu.DropDownItems.Add(item);
        }
        themeMenu.DropDownOpening += (_, _) =>
        {
            foreach (Forms.ToolStripMenuItem item in themeMenu.DropDownItems)
                item.Checked = (BarTheme)item.Tag! == getTheme();
        };

        _startupItem = new Forms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled,
        };
        _startupItem.CheckedChanged += (_, _) => StartupManager.SetEnabled(_startupItem.Checked);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem($"Open search ({hotkeyName})", null, (_, _) => open()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Reset search bar position and size", null, (_, _) => BarPositionStore.Clear()));
        menu.Items.Add(themeMenu);
        menu.Items.Add(new Forms.ToolStripMenuItem("Clear recent items", null, (_, _) => clearRecent()));
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => exit()));
        menu.Opening += (_, _) => _startupItem.Checked = StartupManager.IsEnabled;

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = $"Wydscop ({hotkeyName})",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) open();
        };
    }

    /// <summary>
    /// The Wydscop icon from the embedded .ico, picked at the tray's small-icon size so it
    /// stays sharp at 125% / 150% scaling instead of being shrunk from a big frame.
    /// </summary>
    private static System.Drawing.Icon LoadIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Wydscop.ico");
        return stream is null
            ? System.Drawing.SystemIcons.Application
            : new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void ShowMessage(string title, string text, bool warning = false)
    {
        _icon.ShowBalloonTip(4000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
