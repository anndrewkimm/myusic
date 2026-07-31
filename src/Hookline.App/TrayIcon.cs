using System.Drawing;
using Forms = System.Windows.Forms;

namespace Hookline.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _disposed;

    public TrayIcon(
        Action open,
        Action importAudioFile,
        Action importFromUrl,
        Action mixClips,
        Action openLibrary,
        Action exit
    )
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(importAudioFile);
        ArgumentNullException.ThrowIfNull(importFromUrl);
        ArgumentNullException.ThrowIfNull(mixClips);
        ArgumentNullException.ThrowIfNull(openLibrary);
        ArgumentNullException.ThrowIfNull(exit);

        _menu = new Forms.ContextMenuStrip();
        var openItem = new Forms.ToolStripMenuItem(
            AppStrings.TrayOpen
        );
        openItem.Click += (_, _) => open();
        var importItem = new Forms.ToolStripMenuItem(
            AppStrings.TrayImport
        );
        importItem.Click += (_, _) => importAudioFile();
        var importFromUrlItem = new Forms.ToolStripMenuItem(
            AppStrings.TrayImportFromUrl
        );
        importFromUrlItem.Click += (_, _) => importFromUrl();
        var mixItem = new Forms.ToolStripMenuItem(
            AppStrings.TrayMix
        );
        mixItem.Click += (_, _) => mixClips();
        var libraryItem = new Forms.ToolStripMenuItem(
            AppStrings.TrayLibrary
        );
        libraryItem.Click += (_, _) => openLibrary();
        var exitItem = new Forms.ToolStripMenuItem(
            AppStrings.TrayExit
        );
        exitItem.Click += (_, _) => exit();
        _menu.Items.Add(openItem);
        _menu.Items.Add(importItem);
        _menu.Items.Add(importFromUrlItem);
        _menu.Items.Add(mixItem);
        _menu.Items.Add(libraryItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = AppStrings.TrayTooltip,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                open();
            }
        };
    }

    public void ShowInfo(string message) =>
        ShowBalloon(message, Forms.ToolTipIcon.Info);

    public void ShowError(string message) =>
        ShowBalloon(message, Forms.ToolTipIcon.Error);

    public void ShowMenu()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _menu.Show(Forms.Cursor.Position);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _disposed = true;
    }

    private void ShowBalloon(
        string message,
        Forms.ToolTipIcon icon
    )
    {
        _notifyIcon.BalloonTipTitle = AppStrings.TrayStatusTitle;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(4_000);
    }
}
