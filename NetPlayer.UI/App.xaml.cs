using FFmpeg.AutoGen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using NetPlayer.UI.FFmpeg.Utils;
using NetPlayer.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NetPlayer.UI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static new App Current => (App)Application.Current;

        public MainWindow MainWindow { get; private set; } = null!;
        public XamlRoot XamlRoot { get; private set; } = null!;
        public IServiceProvider Services { get; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            Services = ConfigureServices();

            InitializeComponent();

            FFmpegBinariesHelper.RegisterFFmpegBinaries();
            FFmpegBinariesHelper.LogConfigure();
            logger.Debug("[FFmpeg.AutoGen] Current directory: " + Environment.CurrentDirectory);
            logger.Debug(string.Format("[FFmpeg.AutoGen] Running in {0}-bit mode.", Environment.Is64BitProcess ? "64" : "32"));
            logger.Debug(string.Format("[FFmpeg.AutoGen] FFmpeg version info: {0}", ffmpeg.av_version_info()));
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<MainViewModel>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();

            // XamlRoot has to be set after Content has loaded
            ((FrameworkElement)MainWindow.Content).Loaded += static (_, _) =>
            {
                Current.XamlRoot = Current.MainWindow.GridXamlRoot;
                logger.Debug("XamlRoot set.");
            };

            MainWindow.Closed += MainWindow_Closed;
            MainWindow.Activate();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            logger.Debug("Window closed.");
        }

        public static float GetWindowScalingFactor(IntPtr hwnd)
        {
            uint dpi = PInvoke.GetDpiForWindow(new HWND(hwnd));
            return dpi / 96f;
        }

        public static void SetWindowSize(IntPtr hwnd, int width, int height)
        {
            float scalingFactor = GetWindowScalingFactor(hwnd);
            width = (int)(width * scalingFactor);
            height = (int)(height * scalingFactor);

            IntPtr HWND_TOP = new(0);
            PInvoke.SetWindowPos(new HWND(hwnd), new HWND(HWND_TOP), 0, 0, width, height, SET_WINDOW_POS_FLAGS.SWP_NOMOVE);
        }
    }
}
