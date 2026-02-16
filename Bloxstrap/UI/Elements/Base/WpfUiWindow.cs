using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Mvvm.Contracts;
using Wpf.Ui.Mvvm.Services;

namespace Bloxstrap.UI.Elements.Base
{
    public abstract class WpfUiWindow : UiWindow
    {
        private readonly IThemeService _themeService = new ThemeService();

        public WpfUiWindow()
        {
            ApplyTheme();
        }

        private IntPtr hwnd;
        private IntPtr GetWindowHandle(Window window)
        {
            // Make sure the window has been initialized
            if (!window.IsInitialized)
                throw new InvalidOperationException("Window must be initialized first.");

            var helper = new WindowInteropHelper(window);
            return helper.Handle;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        public static bool IsDwmEnabled()
        {
            return DwmIsCompositionEnabled(out bool enabled) == 0 && enabled;
        }
        public static bool SupportsColorBorder()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            Version version = Environment.OSVersion.Version;
            // Windows 11 is version 10.0.22000+
            return (version.Major == 10 && version.Build >= 22000) && IsDwmEnabled();
        }

        public void ApplyTheme()
        {
            const int customThemeIndex = 2; // index for CustomTheme merged dictionary

            _themeService.SetTheme(App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Dark ? ThemeType.Dark : ThemeType.Light);
            _themeService.SetSystemAccent();

            // there doesn't seem to be a way to query the name for merged dictionaries
            var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/UI/Style/{Enum.GetName(App.Settings.Prop.Theme.GetFinal())}.xaml") };
            Application.Current.Resources.MergedDictionaries[customThemeIndex] = dict;

            if (!App.IsProductionBuild && !SupportsColorBorder()) {
                this.BorderBrush = System.Windows.Media.Brushes.Red;
                this.BorderThickness = new Thickness(4);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            if (App.Settings.Prop.WPFSoftwareRender || App.LaunchSettings.NoGPUFlag.Active)
            {
                if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
                    hwndSource.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            }

            base.OnSourceInitialized(e);

            hwnd = GetWindowHandle(this);

            if (!App.IsProductionBuild && SupportsColorBorder())
            {
                uint winColor = 0x0000FF; // Red
                DwmSetWindowAttribute(hwnd, 34, ref winColor, sizeof(uint));
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);
    }
}
