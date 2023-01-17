// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using Windows.Graphics;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NetPlayer.WinUI
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private const string WindowTitle = "Net Player";
        private const int StartupWidth = 1208;
        private const int StartupHeight = 750;

        public IntPtr Hwnd { get; private set; }
        private AppWindow? _appWindow;
        public XamlRoot XamlRoot => Content.XamlRoot;

        public MainWindow()
        {
            this.InitializeComponent();
            InitializeWindow();
        }

        private void InitializeWindow()
        {
            Title = WindowTitle;

            Hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(Hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.ResizeClient(new SizeInt32(StartupWidth, StartupHeight));
        }
    }
}
