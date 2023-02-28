// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
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
            SubClassDelegate = new SUBCLASSPROC(WindowSubClass);
            bool bReturn = SetWindowSubclass(Hwnd, SubClassDelegate, 0, 0);

            var windowId = Win32Interop.GetWindowIdFromWindow(Hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.ResizeClient(new SizeInt32(StartupWidth, StartupHeight));
        }

        private int WindowSubClass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, uint dwRefData)
        {
            switch (uMsg)
            {
                case WM_CLOSE:
                    {
                        Quit();
                        return 0;
                    }
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private async void Quit()
        {
            await mainPage.DisposeAsync();

            Close();
        }

        public delegate int SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, uint dwRefData);

        [DllImport("Comctl32.dll", SetLastError = true)]
        public static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, uint dwRefData);

        [DllImport("Comctl32.dll", SetLastError = true)]
        public static extern int DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        public const int WM_CLOSE = 0x0010;

        private SUBCLASSPROC? SubClassDelegate;
    }
}
