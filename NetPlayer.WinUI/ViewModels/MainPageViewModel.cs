using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NetPlayer.WinUI.Controls;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

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
        bool isStreaming;

        public MainPageViewModel()
        {
            _contentDialogService = App.Current.Services.GetRequiredService<ContentDialogService>();

            Urls = new ObservableCollection<string>
            {
                "rtsp://admin:SGZHTF@192.168.50.129:554/h264/ch1/main/av_stream",
                "rtsp://admin:admin12345@192.168.1.239:554/h264/ch1/main/av_stream"
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
        private async void Streaming()
        {
            if (MediaPlayer != null)
            {
                if (!isStreaming)
                {
                    var url = await _contentDialogService.InputStringDialog("Streaming url:", "rtmp://127.0.0.1/live/stream0");
                    if (!string.IsNullOrEmpty(url))
                    {
                        MediaPlayer.Push(url);
                    }
                }
                else
                {
                    MediaPlayer?.StopPush();
                }
            }
        }
    }
}
