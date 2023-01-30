// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

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
using NetPlayer.FFmpeg;
using NetPlayer.WinUI.Controls;
using NetPlayer.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NetPlayer.WinUI
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

            this.InitializeComponent();

            FFmpegHelper.RegisterFFmpegBinaries();
            FFmpegHelper.LogConfigure();
            logger.Debug("[FFmpeg.AutoGen] Current directory: " + Environment.CurrentDirectory);
            logger.Debug(string.Format("[FFmpeg.AutoGen] Running in {0}-bit mode.", Environment.Is64BitProcess ? "64" : "32"));
            logger.Debug(string.Format("[FFmpeg.AutoGen] FFmpeg version info: {0}", ffmpeg.av_version_info()));
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<MainPageViewModel>();
            services.AddSingleton<ContentDialogService>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();

            ((FrameworkElement)MainWindow.Content).Loaded += static (_, _) =>
            {
                Current.XamlRoot = Current.MainWindow.XamlRoot;
            };

            MainWindow.Activate();
        }
    }
}
