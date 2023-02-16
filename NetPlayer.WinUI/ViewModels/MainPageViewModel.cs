using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NetPlayer.WinUI.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NetPlayer.WinUI.ViewModels
{
    [INotifyPropertyChanged]
    public partial class MainPageViewModel
    {
        private readonly ContentDialogService _contentDialogService;

        public ObservableCollection<string> Urls { get; set; }

        public MediaElement? MediaPlayer { get; set; }

        [ObservableProperty]
        string? url;

        [ObservableProperty]
        bool isDecodeing;

        [ObservableProperty]
        bool isRecording;

        [ObservableProperty]
        bool isStreaming;

        [ObservableProperty]
        RtspTransport rtspTransport;

        public MainPageViewModel()
        {
            _contentDialogService = App.Current.Services.GetRequiredService<ContentDialogService>();

            Urls = new ObservableCollection<string>
            {
                "rtmp://127.0.0.1/live/stream1",
                "rtsp://admin:admin12345@192.168.1.239:554/h264/ch1/main/av_stream",
                "rtsp://192.168.1.100:554/stream0",
                "rtsp://admin:SGZHTF@192.168.50.129:554/h264/ch1/main/av_stream",
                "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mp4"
            };
        }

        [RelayCommand]
        private async Task PlayAsync()
        {
            if (MediaPlayer != null)
            {
                if (isDecodeing == false)
                {
                    if (await MediaPlayer.OpenAsync())
                    {
                        MediaPlayer.Play();
                    }
                }
                else
                {
                    await MediaPlayer.StopAsync();
                }
            }
        }

        [RelayCommand]
        private async Task SavePngAsync()
        {
            if (MediaPlayer != null)
            {
                var savePicker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.VideosLibrary,
                    SuggestedFileName = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss")
                };
                savePicker.FileTypeChoices.Add("PNG", new List<string> { ".png" });
                InitializeWithWindow.Initialize(savePicker, App.Current.MainWindow.Hwnd);

                var file = await savePicker.PickSaveFileAsync();
                if (file == null)
                {
                    return;
                }

                var result = await MediaPlayer.SavePngAsync(file.Path);

                Debug.WriteLineIf(result, $"Saved frame to {file.Path}.");

                //var result = await MediaPlayer.SavePngAsync(@"c:/1.png");
            }
        }

        [RelayCommand]
        private async void Streaming()
        {
            if (MediaPlayer != null)
            {
                if (!isStreaming)
                {
                    var url = await _contentDialogService.InputStringDialog("Streaming url:", "rtmp://127.0.0.1/live/stream0");
                    
                    if (!string.IsNullOrEmpty(url))
                    {
                        await MediaPlayer.PushAsync(url);
                    }
                }
                else
                {
                    await MediaPlayer.StopPushAsync();
                }
            }
        }

        [RelayCommand]
        private async void Record()
        {
            if (MediaPlayer != null)
            {
                if (!isRecording)
                {
                    var savePicker = new FileSavePicker
                    {
                        SuggestedStartLocation = PickerLocationId.VideosLibrary,
                        SuggestedFileName = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss")
                    };
                    savePicker.FileTypeChoices.Add("MPEG-TS", new List<string> { ".ts" });
                    InitializeWithWindow.Initialize(savePicker, App.Current.MainWindow.Hwnd);

                    var file = await savePicker.PickSaveFileAsync();
                    if (file == null)
                    {
                        return;
                    }

                    if (!string.IsNullOrEmpty(url))
                    {
                        MediaPlayer.Record(file.Path);
                    }

                    //MediaPlayer.Record(@"C:\Users\EjiHuang\Desktop\h265.ts");
                }
                else
                {
                    MediaPlayer.StopRecord();
                }
            }
        }
    }
}
