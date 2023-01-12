// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using FFmpeg.AutoGen;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NetPlayer.UI.Utils;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;

namespace NetPlayer.UI.Controls
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
            Debug.WriteLine("[Media Player] End of file.");
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
            _videoDecoder?.StartDecode();
            Debug.WriteLine($"[Media Player] Video Starting...");
        }

        public string Url
        {
            get => (string)GetValue(UrlProperty);
            set => SetValue(UrlProperty, value);
        }

        public static readonly DependencyProperty UrlProperty =
            DependencyProperty.Register("Url", typeof(string), typeof(MediaElement), new PropertyMetadata(null));

    }
}
