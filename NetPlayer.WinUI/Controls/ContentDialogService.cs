using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.Foundation;

namespace NetPlayer.WinUI.Controls
{
    public sealed class ContentDialogService
    {
        private XamlRoot _xamlRoot => App.Current.XamlRoot;

        public IAsyncOperation<ContentDialogResult> ShowMessageDialog(string title, string? message = null)
        {
            return new ContentDialog()
            {
                XamlRoot = _xamlRoot,
                Title = title,
                Content = message,
                CloseButtonText = "Ok",
            }.ShowAsync();
        }

        public IAsyncOperation<ContentDialogResult> ShowYesNoMessageDialog(string title, string? message = null, Action? onYesClick = null, Action? onNoClick = null)
        {
            return new ContentDialog()
            {
                XamlRoot = _xamlRoot,
                Title = title,
                Content = message,
                PrimaryButtonText = "Yes",
                PrimaryButtonCommand = onYesClick is null ? null : new RelayCommand(onYesClick),
                SecondaryButtonText = "No",
                SecondaryButtonCommand = onNoClick is null ? null : new RelayCommand(onNoClick),
            }.ShowAsync();
        }

        public async Task<string> InputStringDialog(string title, string inputText, Action? onOkClick = null, Action? onCancelClick = null)
        {
            var inputTextBox = new TextBox
            {
                AcceptsReturn = false,
                Height = 32,
                Text = inputText,
                SelectionStart = inputText.Length
            };

            var dialog = new ContentDialog()
            {
                XamlRoot = _xamlRoot,
                Title = title,
                Content = inputTextBox,
                PrimaryButtonText = "Ok",
                PrimaryButtonCommand = onOkClick is null ? null : new RelayCommand(onOkClick),
                SecondaryButtonText = "Cancel",
                SecondaryButtonCommand = onCancelClick is null ? null : new RelayCommand(onCancelClick),
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                return inputTextBox.Text;
            }

            return string.Empty;
        }
    }
}
