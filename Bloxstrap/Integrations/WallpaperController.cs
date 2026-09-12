using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations;

public static class WallpaperController
{
    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;
    private static bool _areWallpaperAppsSaved = false;
    private static readonly List<string> _closedWallpaperApps = new();

    // wallpaper restoring is an adaptation of https://gist.github.com/Drarig29/4aa001074826f7da69b5bb73a83ccd39
    private const string DESKTOP_REG_PATH = @"Control Panel\Desktop";
    private const string HISTORY_REG_PATH = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
    private const int HISTORY_MAX_ENTRIES = 5; // this is actually the cap windows has

    private static WallpaperState? _savedState;

    private struct WallpaperState
    {
        public int Style;
        public bool IsTile;
        public string[] History;
        public string Wallpaper;
    }

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
            RestoreWallpaper();
            RestoreWallpaperApps();            
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

        if (_savedState == null)
            BackupState();

        if (!VALID_STYLES.Contains(style.ToLower()))
            style = "Fill";

        App.Logger.WriteLine(
            LOG_IDENT,
            $"Applying wallpaper: {path} | style-{style}"
        );

        bool result = SystemParametersInfo(
            SPI_SETDESKWALLPAPER,
            0,
            path,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
        );

        SetWallpaperStyle(style);
        RestoreHistory(); // dont keep trace

        if (!result)
        {
            App.Logger.WriteLine(
                LOG_IDENT,
                $"SystemParametersInfo failed: {Marshal.GetLastWin32Error()} | path={path}"
            );
        }
    }

     private static void RestoreHistory()
    {
        if (_savedState == null) return;

        var backupState = _savedState.Value;

        using RegistryKey? historyKey = Registry.CurrentUser.OpenSubKey(HISTORY_REG_PATH, true);
        
        for (var i = 0; i < HISTORY_MAX_ENTRIES; i++)
            if (backupState.History[i] != null)
                historyKey?.SetValue($"BackgroundHistoryPath{i}", backupState.History[i], RegistryValueKind.String);
    }

    public static void BackupState()
    {
        var history = new string[HISTORY_MAX_ENTRIES];

        using RegistryKey? historyKey = Registry.CurrentUser.OpenSubKey(HISTORY_REG_PATH, true);
        for (var i = 0; i < history.Length; i++)
            history[i] = historyKey?.GetValue($"BackgroundHistoryPath{i}") as string ?? string.Empty;

        using RegistryKey? wpConfigKey = Registry.CurrentUser.OpenSubKey(DESKTOP_REG_PATH, true);
        _savedState = new WallpaperState
        {
            Style = int.Parse(wpConfigKey?.GetValue("WallpaperStyle") as string ?? "0"),
            IsTile = (wpConfigKey?.GetValue("TileWallpaper") as string ?? "0") == "1",
            History = history,
            Wallpaper = history[0],
        };
    }

    public static void RestoreWallpaper()
    {
        if (_savedState == null) return;

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(DESKTOP_REG_PATH, true);

        ApplyWallpaper(_savedState.Value.Wallpaper);
        key?.SetValue("WallpaperStyle", _savedState.Value.Style.ToString());
        key?.SetValue("TileWallpaper", _savedState.Value.IsTile ? "1" : "0");
        
        RestoreHistory();
        _savedState = null;
    }

    private static void SetWallpaperStyle(string style)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(DESKTOP_REG_PATH, true);

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

        if (_areWallpaperAppsSaved)
            return;

        _areWallpaperAppsSaved = true;

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
        _areWallpaperAppsSaved = false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool SystemParametersInfo(
        int uAction,
        int uParam,
        string lpvParam,
        int fuWinIni
    );
}
