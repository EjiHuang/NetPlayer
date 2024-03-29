// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using CommunityToolkit.WinUI.UI.Converters;
using FFmpeg.AutoGen;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using NetPlayer.FFmpeg.Converter;
using NetPlayer.FFmpeg.Decoder;
using NetPlayer.FFmpeg.Encoder;
using NetPlayer.FFmpeg.Util;
using NetPlayer.WinUI.Win2D;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NetPlayer.WinUI.Controls
{
    public enum RtspTransport
    {
        TCP, UDP
    }

    public sealed class MediaElement : Control
    {
        private static readonly object _lockobj = new object();
        private AsyncAutoResetEvent _autoResetEventForSavePng = new(false);

        private CanvasControl? _canvas;
        private CanvasBitmap? _bitmap;

        private FFmpegVideoDecoder? _videoDecoder;
        private VideoFrameConverter? _videoFrameBGRA32Converter = null;

        private VideoFrameConverter? _encoderPixelConverter = null;

        private readonly ConcurrentQueue<AVFrame> _encodeVideoStreamQueue = new();
        private CancellationTokenSource? _ctsForPush;
        private FFmpegStreamEncoder? _streamEncoder;
        private Task? _pushTask;

        private readonly ConcurrentQueue<AVFrame> _encodeVideoFileQueue = new();
        private CancellationTokenSource? _ctsForRecord;
        private FFmpegVideoEncoder? _videoEncoder;

        private bool _savePngRequest;
        private string? _savePngPath;

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

            ControlDataBind();
        }

        private void CanvasControl_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_bitmap == null)
                return;

            var transform = Win2DHelper.CalcutateImageCenteredTransform(_canvas!.ActualSize, _bitmap.Size);
            transform.Source = _bitmap;
            args.DrawingSession.DrawImage(transform);
        }

        private void ControlDataBind()
        {
            var tblFpsCurrent = GetTemplateChild("tblFpsCurrent") as TextBlock;
            var tblVideoBitRate = GetTemplateChild("tblVideoBitRate") as TextBlock;

            tblFpsCurrent?.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath(nameof(FpsCurrent)),
                Source = this,
                Mode = BindingMode.OneWay,
                Converter = new StringFormatConverter(),
                ConverterParameter = "{0:f2}"
            });

            tblVideoBitRate?.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath(nameof(VideoBitRate)),
                Source = this,
                Mode = BindingMode.OneWay,
                Converter = new StringFormatConverter(),
                ConverterParameter = "{0:f1} Kbps"
            });
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

                        var opts = new Dictionary<string, string>();

                        if (url.ToLower().StartsWith("rtmp"))
                        {
                            //opts.Add("probesize", "1024");
                            opts.Add("fflags", "nobuffer");
                            opts.Add("rw_timeout", "5000000");
                        }

                        if (url.ToLower().StartsWith("udp"))
                        {
                            opts.Add("timeout", "5000000");
                        }


                        if (url.ToLower().StartsWith("rtsp"))
                        {
                            opts.Add("fflags", "nobuffer");
                            opts.Add("timeout", "5000000");
                            opts.Add("rtsp_flags", "prefer_tcp");
                            //opts.Add("rtsp_transport", "udp");
                        }

                        // 是否设置缓冲区大小?
                        //opts.Add("buffer_size", 1024);

                        var ret = _videoDecoder.InitialiseSource(opts);

                        if (ret == false)
                        {
                            _videoDecoder.Dispose();
                            return false;
                        }

                        _videoDecoder.OnVideoFrame += OnVideoFrame;
                        _videoDecoder.OnCurStats += OnCurStats;
                        _videoDecoder.OnEndOfFile += OnEndOfFile;
                        _videoDecoder.OnRestartVideo += OnRestartVideo;
                        _videoDecoder.OnError += OnError;

                        return true;
                    }
                });
            }

            return false;
        }

        public async Task<bool> SavePngAsync(string filePath)
        {
            _savePngPath = filePath;
            _savePngRequest = true;

            return IsDecoding && await _autoResetEventForSavePng.WaitAsync(TimeSpan.FromMilliseconds(500));
        }

        private void OnCurStats(DecodeStats curStats)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (IsShowStats)
                {
                    FpsCurrent = curStats.FpsCurrent;
                    VideoBitRate = curStats.VideoBitRate;
                }
            });
        }

        public void Record(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            var partNum = 0;

            if (ext != null && ext == ".ts")
            {
                _ctsForRecord = new CancellationTokenSource();
                IsRecording = true;

                Task.Run(() =>
                {
                    try
                    {
                        _videoEncoder = new FFmpegVideoEncoder(filePath);
                        var frameNumber = 0;
                        var frameRate = 0;
                        var curTicks = DateTime.UtcNow.Ticks;

                        while (_ctsForRecord != null && !_ctsForRecord.IsCancellationRequested)
                        {
                            if (_encodeVideoFileQueue.TryDequeue(out var frame))
                            {
                                var width = frame.width;
                                var height = frame.height;
                                var srcPixelFormat = AVPixelFormat.AV_PIX_FMT_BGR0;
                                var dstPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;  // For H264

                                if (_videoDecoder != null && frameRate == 0)
                                {
                                    frameRate = _videoDecoder.GetFrameRate();
                                    if (frameRate == 0 || frameRate == 90000)
                                    {
                                        frameRate = _videoDecoder.GetFrameRateFromCollector();
                                        if (frameRate == 0)
                                        {
                                            Thread.Sleep(1);
                                            continue;
                                        }
                                    }

                                    Debug.WriteLine("[Media Player] " + $"Encode frame rate = {frameRate}.");
                                }

                                if (_encoderPixelConverter == null || _encoderPixelConverter.SourceWidth != width || _encoderPixelConverter.SourceHeight != height)
                                {
                                    _encoderPixelConverter = new VideoFrameConverter(width, height, srcPixelFormat, width, height, dstPixelFormat);
                                }

                                lock (_lockobj)
                                {
                                    var convertedFrame = _encoderPixelConverter.Convert(frame);

                                    if (DateTime.UtcNow.Ticks - curTicks > 10000000.0)
                                    {
                                        Debug.WriteLine($"[Media Player] Steaming timeout {(DateTime.UtcNow.Ticks - curTicks) / 10000000.0}s, rebuilding the steam encoder.");

                                        frameNumber = 0;

                                        var fileName = Path.GetFileName(filePath);
                                        if (_videoEncoder.IsEncoderInitialised == false)
                                        {
                                            // Do nothing.
                                        }
                                        else if (fileName.Contains($"-part{partNum}"))
                                        {
                                            filePath = Path.Combine(Path.GetDirectoryName(filePath)!, fileName.Replace($"-part{partNum}", $"-part{++partNum}"));
                                        }
                                        else
                                        {
                                            filePath = filePath.Insert(filePath.LastIndexOf(".ts"), $"-part{++partNum}");
                                        }

                                        // Dispose
                                        _videoEncoder?.Dispose();
                                        _videoEncoder = new FFmpegVideoEncoder(filePath);
                                    }

                                    convertedFrame.pts = frameNumber;
                                    curTicks = DateTime.UtcNow.Ticks;

                                    _videoEncoder.TryEncodeNextPacket(convertedFrame, AVCodecID.AV_CODEC_ID_H264, frameRate: frameRate);
                                }

                                frameNumber++;
                            }
                            else
                            {
                                Thread.Sleep(1);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[Media Player] " + ex.ToString());
                    }

                    // Dispose
                    _videoEncoder?.Dispose();
                    _videoEncoder = null;

                    _encodeVideoFileQueue?.Clear();

                    Debug.WriteLine("[Media Player] " + "End of record.");
                    DispatcherQueue?.TryEnqueue(() => { IsRecording = false; });

                }, _ctsForRecord.Token);
            }
        }

        public void StopRecord()
        {
            try
            {
                _ctsForRecord?.Cancel();
                _ctsForRecord = null;

                IsRecording = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Media Player] " + ex.ToString());
            }
        }

        public async Task PushAsync(string url)
        {
            if (IsStreaming == true && url != _streamEncoder?.Url)
            {
                await StopPushAsync();
            }

            if (IsStreaming == true && url == _streamEncoder?.Url)
            {
                return;
            }

            _ctsForPush = new CancellationTokenSource();
            IsStreaming = true;

            _pushTask = Task.Run(() =>
            {
            Repeat:
                var re = false;

                try
                {
                    unsafe
                    {
                        _streamEncoder = new FFmpegStreamEncoder(url);
                    }
                    var frameNumber = 0;
                    var frameRate = 0;
                    var curTicks = DateTime.UtcNow.Ticks;

                    while (_ctsForPush != null && !_ctsForPush.IsCancellationRequested)
                    {
                        if (_encodeVideoStreamQueue != null && _encodeVideoStreamQueue.TryDequeue(out var frame))
                        {
                            var width = frame.width;
                            var height = frame.height;
                            
                            var srcPixelFormat = AVPixelFormat.AV_PIX_FMT_BGR0;
                            var dstPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;  // For H264

                            if (_videoDecoder != null && frameRate == 0)
                            {
                                frameRate = _videoDecoder.GetFrameRate();
                                if (frameRate == 0 || frameRate == 90000)
                                {
                                    frameRate = _videoDecoder.GetFrameRateFromCollector();
                                    if (frameRate == 0)
                                    {
                                        Thread.Sleep(1);
                                        continue;
                                    }
                                }

                                Debug.WriteLine("[Media Player] " + $"Encode frame rate = {frameRate}.");
                            }

                            if (_encoderPixelConverter == null || _encoderPixelConverter.SourceWidth != width || _encoderPixelConverter.SourceHeight != height)
                            {
                                _encoderPixelConverter = new VideoFrameConverter(width, height, srcPixelFormat, width, height, dstPixelFormat);
                            }

                            lock (_lockobj)
                            {
                                var convertedFrame = _encoderPixelConverter.Convert(frame);

                                if (DateTime.UtcNow.Ticks - curTicks > 10000000.0)
                                {
                                    Debug.WriteLine($"[Media Player] Steaming timeout {(DateTime.UtcNow.Ticks - curTicks) / 10000000.0}s, rebuilding the steam encoder.");

                                    frameNumber = 0;

                                    // Dispose
                                    _streamEncoder?.Dispose();
                                    unsafe
                                    {
                                        _streamEncoder = new FFmpegStreamEncoder(url);
                                    }
                                }

                                convertedFrame.pts = frameNumber;
                                curTicks = DateTime.UtcNow.Ticks;

                                unsafe
                                {
                                    if (_videoDecoder != null)
                                    {
                                        _streamEncoder.TryEncodeNextPacket(convertedFrame, AVCodecID.AV_CODEC_ID_H264, frameRate: frameRate);
                                    }
                                }
                            }

                            frameNumber++;
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Media Player] " + ex.ToString());

                    if (ex.Message == "Error number -10053 occurred")
                    {
                        // Try to re-push
                        re = true;
                    }
                    else
                    {
                        _encodeVideoStreamQueue?.Clear();
                        DispatcherQueue?.TryEnqueue(() => { IsStreaming = false; });

                        return;
                    }
                }

                // Dispose
                _streamEncoder?.Dispose();
                _streamEncoder = null;

                _encodeVideoStreamQueue?.Clear();

                if (re)
                {
                    Debug.WriteLine("[Media Player] " + "The Steaming is abnormal, and the stream will be pushed again after 2 seconds.");

                    Thread.Sleep(2000);
                    re = false;

                    goto Repeat;
                }

                Debug.WriteLine("[Media Player] " + "End of streaming.");
                DispatcherQueue?.TryEnqueue(() => { IsStreaming = false; });

            }, _ctsForPush.Token);
        }

        public async Task StopPushAsync()
        {
            try
            {
                _ctsForPush?.Cancel();
                _ctsForPush = null;

                if (_pushTask != null)
                    await _pushTask;
                IsStreaming = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Media Player] " + ex.ToString());
            }
        }

        private void OnError(string errorMessage)
        {
            Debug.WriteLine("[Media Player] " + errorMessage);
        }

        private void OnEndOfFile()
        {
            Debug.WriteLine("[Media Player] Video decoding has stopped.");
            DispatcherQueue?.TryEnqueue(() => { IsDecoding = false; });
        }

        private void OnRestartVideo()
        {
            Debug.WriteLine("[Media Player] Restarted video.");
            DispatcherQueue?.TryEnqueue(() => { IsDecoding = true; });
        }

        private unsafe void OnVideoFrame(ref AVFrame frame)
        {
            if (_videoDecoder != null)
            {
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

                    // Save png
                    if (_savePngRequest && !string.IsNullOrEmpty(_savePngPath))
                    {
                        _savePngRequest = false;

                        _videoDecoder.SavePng(frameBGRA32, _savePngPath);

                        _autoResetEventForSavePng.Set();
                    }

                    // 如果启动了编码器，则将转换好的视频帧用于视频编码
                    if ((_ctsForRecord != null && !_ctsForRecord.IsCancellationRequested))
                    {
                        _encodeVideoFileQueue.Enqueue(frameBGRA32);
                    }
                    if ((_ctsForPush != null && !_ctsForPush.IsCancellationRequested))
                    {
                        _encodeVideoStreamQueue.Enqueue(frameBGRA32);
                    }
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

        public double VideoBitRate
        {
            get { return (double)GetValue(VideoBitRateProperty); }
            set { SetValue(VideoBitRateProperty, value); }
        }

        public static readonly DependencyProperty VideoBitRateProperty =
            DependencyProperty.Register("VideoBitRate", typeof(double), typeof(MediaElement), new PropertyMetadata(0));

        private double FpsCurrent
        {
            get { return (double)GetValue(FpsCurrentProperty); }
            set { SetValue(FpsCurrentProperty, value); }
        }

        private static readonly DependencyProperty FpsCurrentProperty =
            DependencyProperty.Register("FpsCurrent", typeof(double), typeof(MediaElement), new PropertyMetadata(0));

        public bool IsShowStats
        {
            get { return (bool)GetValue(IsShowStatsProperty); }
            set { SetValue(IsShowStatsProperty, value); }
        }

        public static readonly DependencyProperty IsShowStatsProperty =
            DependencyProperty.Register("IsShowStats", typeof(bool), typeof(MediaElement), new PropertyMetadata(false));

        public bool IsRecording
        {
            get { return (bool)GetValue(IsRecordingProperty); }
            set { SetValue(IsRecordingProperty, value); }
        }

        public static readonly DependencyProperty IsRecordingProperty =
            DependencyProperty.Register("IsRecording", typeof(bool), typeof(MediaElement), new PropertyMetadata(false));

        public bool IsStreaming
        {
            get { return (bool)GetValue(IsStreamingProperty); }
            set { SetValue(IsStreamingProperty, value); }
        }

        public static readonly DependencyProperty IsStreamingProperty =
            DependencyProperty.Register("IsStreaming", typeof(bool), typeof(MediaElement), new PropertyMetadata(false));

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
