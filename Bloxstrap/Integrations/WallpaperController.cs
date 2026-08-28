using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations;

public static class WallpaperController
{
    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;
    private static string? _originalWallpaper;
    private static string? _originalWallpaperBackup;
    private static object? _originalWallpaperStyle;
    private static object? _originalTileWallpaper;
    private static object? _originalBackgroundType;
    private static bool _wallpaperApps = false;
    private static readonly List<string> _closedWallpaperApps = new();

    // String array of known apps (currently just Wallpaper Engine and Lively Wallpaper, as these are the main 2 everyone uses I believe)
    private static readonly string[] WallpaperProcesses =
    {
        "wallpaper32",
        "wallpaper64",
        "Lively",
        "LivelyUI",
        "Livelywpf",
    };

    private static readonly List<string> VALID_STYLES = new()
    {
        "fill",
        "fit",
        "stretch",
        "tile",
        "center",
        "span",
    };

    public static void SetWallpaper(string wallpaperPath, string? style)
    {
        const string LOG_IDENT = "WallpaperController::SetWallpaper";
        try
        {
            CloseWallpaperApps();
            ApplyWallpaper(wallpaperPath, style ?? "Fill");
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine(
                LOG_IDENT,
                $"Failed to set wallpaper: {ex}"
            );
            RestoreWallpaperApps();
        }
    }

    public static void ResetWallpaper()
    {
        const string LOG_IDENT = "WallpaperController::ResetWallpaper";
        try
        {
            // point Windows back at the real source file whenever it survived, so its wallpaper
            // setting never ends up referencing our own temp copy
            string? restorePath = File.Exists(_originalWallpaper) ? _originalWallpaper : _originalWallpaperBackup;

            if (!string.IsNullOrEmpty(restorePath))
            {
                App.Logger.WriteLine(
                    LOG_IDENT,
                    $"Restoring wallpaper: {restorePath} | style={_originalWallpaperStyle} tile={_originalTileWallpaper} backgroundType={_originalBackgroundType}"
                );

                RestoreWallpaperState();

                bool result = SystemParametersInfo(
                    SPI_SETDESKWALLPAPER,
                    0,
                    restorePath,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
                );

                if (!result)
                {
                    App.Logger.WriteLine(
                        LOG_IDENT,
                        $"SystemParametersInfo failed: {Marshal.GetLastWin32Error()}"
                    );
                }

                RestoreBackgroundType();
            }

            RestoreWallpaperApps();

            _originalWallpaper = null;
            _originalWallpaperBackup = null;
            _originalWallpaperStyle = null;
            _originalTileWallpaper = null;
            _originalBackgroundType = null;
        } catch (Exception ex)
        {
            App.Logger.WriteLine(
                LOG_IDENT,
                $"Failed to reset wallpaper: {ex}"
            );
        }
    }

    private static void ApplyWallpaper(string path, string style = "fill")
    {
        const string LOG_IDENT = "WallpaperController::ApplyWallpaper";

        if (string.IsNullOrEmpty(_originalWallpaper))
        {
            string current = GetCurrentWallpaper();
            if (string.IsNullOrEmpty(current))
            {
                App.Logger.WriteLine(
                    LOG_IDENT,
                    "Failed to get current wallpaper, aborting change"
                );

                return;
            }

            _originalWallpaper = current;
            _originalWallpaperBackup = BackupWallpaperFile(current);

            if (!File.Exists(_originalWallpaper) && string.IsNullOrEmpty(_originalWallpaperBackup))
            {
                App.Logger.WriteLine(
                    LOG_IDENT,
                    $"No restorable copy of the current wallpaper ({current}), aborting change"
                );

                _originalWallpaper = null;
                return;
            }

            SaveWallpaperState();
        }

        if (!VALID_STYLES.Contains(style.ToLower()))
            style = "Fill";

        App.Logger.WriteLine(
            LOG_IDENT,
            $"Applying wallpaper: {path} | style-{style}"
        );

        SetWallpaperStyle(style);

        bool result = SystemParametersInfo(
            SPI_SETDESKWALLPAPER,
            0,
            path,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
        );

        if (!result)
        {
            App.Logger.WriteLine(
                LOG_IDENT,
                $"SystemParametersInfo failed: {Marshal.GetLastWin32Error()} | path={path}"
            );
        }
    }

    // SPI_GETDESKWALLPAPER hands back Themes\TranscodedWallpaper, the cache Windows overwrites the
    // moment a new wallpaper is applied, so the path alone is worthless by the time we restore it
    private static string? BackupWallpaperFile(string path)
    {
        const string LOG_IDENT = "WallpaperController::BackupWallpaperFile";

        try
        {
            if (!File.Exists(path))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Current wallpaper does not exist on disk: {path}");
                return null;
            }

            Directory.CreateDirectory(Paths.Temp);

            string backupPath = Path.Combine(Paths.Temp, "OriginalWallpaper" + Path.GetExtension(path));

            File.Copy(path, backupPath, true);

            App.Logger.WriteLine(LOG_IDENT, $"Backed up {path} to {backupPath}");

            return backupPath;
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine(LOG_IDENT, $"Failed to back up wallpaper: {ex}");
            return null;
        }
    }

    private static string GetCurrentWallpaper()
    {
        const int MAX_PATH = 260;

        var buffer = new StringBuilder(MAX_PATH);

        SystemParametersInfo(
            SPI_GETDESKWALLPAPER,
            MAX_PATH,
            buffer,
            0
        );

        return buffer.ToString();
    }

    private const string DESKTOP_KEY = @"Control Panel\Desktop";
    private const string WALLPAPERS_KEY = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
    private const int BACKGROUND_TYPE_SLIDESHOW = 2;

    private static void SaveWallpaperState()
    {
        using RegistryKey? desktop = Registry.CurrentUser.OpenSubKey(DESKTOP_KEY);
        _originalWallpaperStyle = desktop?.GetValue("WallpaperStyle");
        _originalTileWallpaper = desktop?.GetValue("TileWallpaper");

        using RegistryKey? wallpapers = Registry.CurrentUser.OpenSubKey(WALLPAPERS_KEY);
        _originalBackgroundType = wallpapers?.GetValue("BackgroundType");
    }

    private static void RestoreWallpaperState()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(DESKTOP_KEY, true);

        if (key is null)
            return;

        if (_originalWallpaperStyle is not null)
            key.SetValue("WallpaperStyle", _originalWallpaperStyle);

        if (_originalTileWallpaper is not null)
            key.SetValue("TileWallpaper", _originalTileWallpaper);
    }

    // SPI_SETDESKWALLPAPER forces BackgroundType to 0 (single picture), which permanently turns a
    // slideshow or spotlight desktop into a static image unless we put the original value back.
    private static void RestoreBackgroundType()
    {
        const string LOG_IDENT = "WallpaperController::RestoreBackgroundType";

        if (_originalBackgroundType is not int backgroundType)
            return;

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(WALLPAPERS_KEY, true);

        if (key is null)
            return;

        key.SetValue("BackgroundType", backgroundType, RegistryValueKind.DWord);

        if (backgroundType == BACKGROUND_TYPE_SLIDESHOW)
        {
            App.Logger.WriteLine(
                LOG_IDENT,
                "Restored slideshow desktop; it resumes on the next Windows rotation"
            );
        }
    }

    private static void SetWallpaperStyle(string style)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Control Panel\Desktop",
            true
        );

        switch (style.ToLower())
        {
            case "fill":
                key?.SetValue("WallpaperStyle", "10");
                key?.SetValue("TileWallpaper", "0");
                break;

            case "fit":
                key?.SetValue("WallpaperStyle", "6");
                key?.SetValue("TileWallpaper", "0");
                break;

            case "stretch":
                key?.SetValue("WallpaperStyle", "2");
                key?.SetValue("TileWallpaper", "0");
                break;

            case "tile":
                key?.SetValue("WallpaperStyle", "0");
                key?.SetValue("TileWallpaper", "1");
                break;

            case "center":
                key?.SetValue("WallpaperStyle", "0");
                key?.SetValue("TileWallpaper", "0");
                break;

            case "span":
                key?.SetValue("WallpaperStyle", "22");
                key?.SetValue("TileWallpaper", "0");
                break;
        }
    }

    private static void CloseWallpaperApps()
    {
        const string LOG_IDENT = "WallpaperController::CloseWallpaperApps";

        if (_wallpaperApps)
            return;

        _wallpaperApps = true;

        foreach (string procName in WallpaperProcesses)
        {
            foreach (Process proc in Process.GetProcessesByName(procName))
            {
                try
                {
                    string? exe = null;

                    try
                    {
                        exe = proc.MainModule?.FileName;
                    }
                    catch { }

                    if (!string.IsNullOrWhiteSpace(exe))
                        _closedWallpaperApps.Add(exe);

                    App.Logger.WriteLine(
                        LOG_IDENT,
                        $"Closing wallpaper app: {proc.ProcessName}"
                    );

                    proc.CloseMainWindow();

                    if (!proc.WaitForExit(3000))
                        proc.Kill();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(
                        LOG_IDENT,
                        $"Failed to close wallpaper app: {ex}"
                    );
                }
            }
        }
    }

    private static void RestoreWallpaperApps()
    {
        const string LOG_IDENT = "WallpaperController::RestoreWallpaperApps";
        foreach (string exe in _closedWallpaperApps)
        {
            try
            {
                if (File.Exists(exe))
                {
                    Process.Start(exe);

                    App.Logger.WriteLine(
                        LOG_IDENT,
                        $"Restarted wallpaper app: {exe}"
                    );
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(
                    LOG_IDENT,
                    $"Failed to restart wallpaper app: {ex}"
                );
            }
        }

        _closedWallpaperApps.Clear();
        _wallpaperApps = false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool SystemParametersInfo(
        int uAction,
        int uParam,
        string lpvParam,
        int fuWinIni
    );

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool SystemParametersInfo(
        int uAction,
        int uParam,
        System.Text.StringBuilder lpvParam,
        int fuWinIni
    );
}
