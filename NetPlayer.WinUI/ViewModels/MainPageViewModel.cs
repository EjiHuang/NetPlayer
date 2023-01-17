using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetPlayer.WinUI.Controls;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace NetPlayer.WinUI.ViewModels
{
    [INotifyPropertyChanged]
    public partial class MainPageViewModel
    {
        public ObservableCollection<string> Urls { get; set; }

        public MediaElement? MediaPlayer { get; set; }

        [ObservableProperty]
        string? url;

        [ObservableProperty]
        bool isDecodeing;

        public MainPageViewModel()
        {
            Urls = new ObservableCollection<string>
            {
                "rtsp://admin:SGZHTF@192.168.50.129:554/h264/ch1/main/av_stream"
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
    }
}
