using FFmpeg.AutoGen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using NetPlayer.Core.RTSP;
using NetPlayer.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NetPlayer.UI
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private const string WindowTitle = "Net Player";
        private const int StartupWidth = 1280;
        private const int StartupHeight = 750;

        public IntPtr Handle { get; }
        public XamlRoot GridXamlRoot => _grid.XamlRoot;

        private readonly MainViewModel _mainViewModel;

        public MainWindow()
        {
            Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            
            Title = WindowTitle;
            App.SetWindowSize(Handle, StartupWidth, StartupHeight);

            this.InitializeComponent();

            _mainViewModel = App.Current.Services.GetRequiredService<MainViewModel>();
            
            
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            if (await mediaPlayer.OpenAsync())
            {
                mediaPlayer.Play();
            }
        }
    }
}
