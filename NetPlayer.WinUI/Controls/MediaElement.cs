// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using FFmpeg.AutoGen;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetPlayer.WinUI.FFmpeg.Core;
using NetPlayer.WinUI.FFmpeg.Core.Decoder;
using NetPlayer.WinUI.Win2D;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NetPlayer.WinUI.Controls
{
    public sealed class MediaElement : Control
    {
        CanvasControl? _canvas;
        CanvasBitmap? _bitmap;

        FFmpegVideoDecoder? _videoDecoder;
        VideoFrameConverter? _videoFrameBGRA32Converter = null;

        public MediaElement()
        {
            DefaultStyleKey = typeof(MediaElement);
        }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _canvas = GetTemplateChild("canvas") as CanvasControl;
            if (_canvas != null)
            {
                _canvas.Draw += CanvasControl_Draw;
            }

            MediaPlayer = this;
        }

        private void CanvasControl_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_bitmap == null)
                return;

            var transform = Win2DHelper.CalcutateImageCenteredTransform(_canvas!.ActualSize, _bitmap.Size);
            transform.Source = _bitmap;
            args.DrawingSession.DrawImage(transform);
        }

        public async Task<bool> OpenAsync(string? url = null)
        {
            if (url != null)
            {
                Url = url;
            }

            if (Url != null)
            {
                url = Url.ToString();
                return await Task.Run(() =>
                {
                    unsafe
                    {
                        _videoDecoder = new FFmpegVideoDecoder(url, null);
                        var ret = _videoDecoder.InitialiseSource(new Dictionary<string, string>
                        {
                            { "timeout", "5000000" },
                            { "fflags", "nobuffer" },
                            { "rtsp_transport", "udp" },
                        });

                        if (ret == false)
                        {
                            _videoDecoder.Dispose();
                            return false;
                        }

                        _videoDecoder.OnVideoFrame += OnVideoFrame;
                        _videoDecoder.OnEndOfFile += OnEndOfFile;
                        _videoDecoder.OnError += OnError;
                        
                        return true;
                    }
                });
            }

            return false;
        }

        private void OnError(string errorMessage)
        {
            Debug.WriteLine("[Media Player] " + errorMessage);
        }

        private void OnEndOfFile()
        {
            Debug.WriteLine("[Media Player] Video decoding has stopped.");
            DispatcherQueue.TryEnqueue(() => { IsDecoding = false; });
        }

        private unsafe void OnVideoFrame(ref AVFrame frame)
        {
            if (_videoDecoder != null)
            {
                var frameRate = (int)_videoDecoder.VideoAverageFrameRate;
                var timestampDuration = (uint)_videoDecoder.VideoFrameSpace;

                var width = frame.width;
                var height = frame.height;

                if (_videoFrameBGRA32Converter == null
                    || _videoFrameBGRA32Converter.SourceWidth != width
                    || _videoFrameBGRA32Converter.SourceHeight != height)
                {
                    _videoFrameBGRA32Converter = new VideoFrameConverter(
                        width, height,
                        (AVPixelFormat)frame.format,
                        width, height,
                        AVPixelFormat.AV_PIX_FMT_BGR0);
                    Debug.WriteLine($"[Media Player] Frame format: [{(AVPixelFormat)frame.format}]");
                }

                var frameBGRA32 = _videoFrameBGRA32Converter.Convert(frame);
                if ((frameBGRA32.width != 0) && (frameBGRA32.height != 0))
                {
                    var rawImage = new RawImage
                    {
                        Width = width,
                        Height = height,
                        Stride = frameBGRA32.linesize[0],
                        Sample = (IntPtr)frameBGRA32.data[0],
                        PixelFormat = VideoPixelFormatsEnum.Bgra
                    };

                    _bitmap = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(), rawImage.GetBuffer(), rawImage.Width, rawImage.Height, DirectXPixelFormat.B8G8R8A8UIntNormalized);
                    _canvas?.Invalidate();
                }
            }
        }

        public void Play()
        {
            if (_videoDecoder != null)
            {
                IsDecoding = _videoDecoder.StartDecode();
                if (IsDecoding)
                {
                    Debug.WriteLine($"[Media Player] Video decoding...");
                }
                else
                {
                    Debug.WriteLine($"[Media Player] Video decoding failed");
                }
            }
        }

        public async Task StopAsync()
        {
            if (_videoDecoder != null)
            {
                await _videoDecoder.CloseAsync();
                _videoDecoder.Dispose();
                IsDecoding = false;
            }
        }

        public bool IsDecoding
        {
            get { return (bool)GetValue(IsDecodingProperty); }
            set { SetValue(IsDecodingProperty, value); }
        }

        public static readonly DependencyProperty IsDecodingProperty =
            DependencyProperty.Register("IsDecoding", typeof(bool), typeof(MediaElement), new PropertyMetadata(false));

        public MediaElement MediaPlayer
        {
            get { return (MediaElement)GetValue(MediaPlayerProperty); }
            set { SetValue(MediaPlayerProperty, value); }
        }

        public static readonly DependencyProperty MediaPlayerProperty =
            DependencyProperty.Register("MediaPlayer", typeof(MediaElement), typeof(MediaElement), new PropertyMetadata(null));

        public string Url
        {
            get => (string)GetValue(UrlProperty);
            set => SetValue(UrlProperty, value);
        }

        public static readonly DependencyProperty UrlProperty =
            DependencyProperty.Register("Url", typeof(string), typeof(MediaElement), new PropertyMetadata(null));

    }
}
