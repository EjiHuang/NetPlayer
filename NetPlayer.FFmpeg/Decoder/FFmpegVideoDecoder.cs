/*
 https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg/blob/master/src/FFmpegVideoDecoder.cs
 */

using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NetPlayer.FFmpeg.Decoder
{
    public class FFmpegVideoDecoder : IDisposable
    {
        public const int MIN_SLEEP_MILLISECONDS = 15;
        public const int DEFAULT_VIDEO_FRAME_RATE = 30;

        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        unsafe private AVInputFormat* _inputFormat = null;
        unsafe private AVFormatContext* _fmtCtx = null;
        unsafe private AVCodecContext* _vidDecCtx = null;

        private int _videoStreamIndex;
        private double _videoTimebase;
        private double _videoAvgFrameRate;
        private int _maxVideoFrameSpace;

        private string _sourceUrl;
        private bool _repeat;
        private bool _isInitialised;
        private bool _isCamera;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _isDisposed;
        private Task? _sourceTask;

        private Task? _infoTask;
        private long _framesDisplayed;
        private long _videoBytes;
        public delegate void OnCurrentStatsDelegate(DecodeStats curStats);
        public event OnCurrentStatsDelegate? OnCurStats;

        public delegate void OnFrameDelegate(ref AVFrame frame);
        public event OnFrameDelegate? OnVideoFrame;

        public event Action? OnEndOfFile;

        public delegate void SourceErrorDelegate(string errorMessage);
        public event SourceErrorDelegate? OnError;

        public double VideoAverageFrameRate
        {
            get => _videoAvgFrameRate;
        }

        public double VideoFrameSpace
        {
            get => _maxVideoFrameSpace;
        }

        public unsafe FFmpegVideoDecoder(string url, AVInputFormat* inputFormat, bool repeat = false, bool isCamera = false)
        {
            _sourceUrl = url;

            _inputFormat = inputFormat;

            _repeat = repeat;

            _isCamera = isCamera;

            _isDisposed = false;
        }

        private void RaiseError(String err)
        {
            Dispose();
            OnError?.Invoke(err);
        }

        public unsafe Boolean InitialiseSource(Dictionary<string, string>? decoderOptions = null)
        {
            if (!_isInitialised)
            {
                _isInitialised = true;
                _isDisposed = false;

                _fmtCtx = ffmpeg.avformat_alloc_context();
                _fmtCtx->flags = ffmpeg.AVFMT_FLAG_NONBLOCK;

                AVDictionary* options = null;

                if (decoderOptions != null)
                {
                    foreach (String key in decoderOptions.Keys)
                    {
                        if (ffmpeg.av_dict_set(&options, key, decoderOptions[key], 0) < 0)
                            logger.Warn($"Cannot set option [{key}]=[{decoderOptions[key]}]");
                    }
                }

                var pFormatContext = _fmtCtx;
                if (ffmpeg.avformat_open_input(&pFormatContext, _sourceUrl, _inputFormat, &options) < 0)
                {
                    ffmpeg.avformat_free_context(pFormatContext);
                    _fmtCtx = null;

                    RaiseError("Cannot open source");
                    return false;
                }

                if (ffmpeg.avformat_find_stream_info(_fmtCtx, null) < 0)
                {
                    RaiseError("Cannot get info from stream");
                    return false;
                }

                ffmpeg.av_dump_format(_fmtCtx, 0, _sourceUrl, 0);

                // Set up video decoder.
                AVCodec* vidCodec = null;
                _videoStreamIndex = ffmpeg.av_find_best_stream(_fmtCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &vidCodec, 0);
                if (_videoStreamIndex < 0)
                {
                    RaiseError("Cannot get video stream using specified codec");
                    return false;
                }

                logger.Debug($"FFmpeg file source decoder [{ffmpeg.avcodec_get_name(vidCodec->id)}] video codec for stream [{_videoStreamIndex}] - url:[{_sourceUrl}].");
                _vidDecCtx = ffmpeg.avcodec_alloc_context3(vidCodec);
                if (_vidDecCtx == null)
                {
                    RaiseError("Cannot create video context");
                    return false;
                }

                if (ffmpeg.avcodec_parameters_to_context(_vidDecCtx, _fmtCtx->streams[_videoStreamIndex]->codecpar) < 0)
                {
                    var pCodecContext = _vidDecCtx;
                    ffmpeg.avcodec_free_context(&pCodecContext);
                    _vidDecCtx = null;

                    RaiseError("Cannot set parameters in this context");
                    return false;
                }

                if (ffmpeg.avcodec_open2(_vidDecCtx, vidCodec, null) < 0)
                {
                    var pCodecContext = _vidDecCtx;
                    ffmpeg.avcodec_free_context(&pCodecContext);
                    _vidDecCtx = null;

                    RaiseError("Cannot open Codec context");
                    return false;
                }

                _videoTimebase = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->time_base);
                if (Double.IsNaN(_videoTimebase) || (_videoTimebase <= 0))
                    _videoTimebase = 0.001;

                _videoAvgFrameRate = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->avg_frame_rate);
                if (_videoAvgFrameRate <= 0)
                {
                    _videoAvgFrameRate = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->r_frame_rate);
                }

                if (Double.IsNaN(_videoAvgFrameRate) || (_videoAvgFrameRate <= 0))
                    _videoAvgFrameRate = 2;

                _maxVideoFrameSpace = (int)(_videoAvgFrameRate > 0 ? 1000 / _videoAvgFrameRate : 1000 / DEFAULT_VIDEO_FRAME_RATE);
            }
            return true;
        }

        public Boolean StartDecode()
        {
            if (!_isStarted)
            {
                _isClosed = false;

                if (InitialiseSource())
                {
                    _isStarted = true;
                    _sourceTask = Task.Run(RunDecodeLoop);

                    if (_infoTask == null)
                    {
                        _infoTask = Task.Run(DecodeInfoLoop);
                    }
                }
            }
            return _isStarted;
        }

        public Boolean Pause()
        {
            if (!_isClosed)
            {
                _isPaused = true;
            }
            return _isPaused;
        }

        public Boolean Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;
            }
            return !_isPaused;
        }

        public async Task CloseAsync()
        {
            _isClosed = true;

            if (_sourceTask != null)
            {
                // The decode loop should finish very quickly one the close is signaled.
                // Wait for it to complete in case the native objects need to be cleaned up.
                await _sourceTask;
            }
        }

        private void RunDecodeLoop()
        {
            bool needToRestartVideo = false;
            unsafe
            {
                AVPacket* pkt = null;
                AVFrame* avFrame = ffmpeg.av_frame_alloc();

                int eagain = ffmpeg.AVERROR(ffmpeg.EAGAIN);
                int error;

                bool canContinue = true;
                bool managePacket = true;

                double firts_dpts = 0;

                _framesDisplayed = 0;
                _videoBytes = 0;

                try
                {
                    // Decode loop.
                    pkt = ffmpeg.av_packet_alloc();

                Repeat:

                    DateTime startTime = DateTime.Now;

                    while (!_isClosed && !_isPaused && canContinue)
                    {
                        error = ffmpeg.av_read_frame(_fmtCtx, pkt);
                        if (error < 0)
                        {
                            managePacket = false;
                            if (error == eagain)
                                ffmpeg.av_packet_unref(pkt);
                            else
                                canContinue = false;
                        }
                        else
                            managePacket = true;

                        if (managePacket)
                        {
                            if (pkt->stream_index == _videoStreamIndex)
                            {
                                if (ffmpeg.avcodec_send_packet(_vidDecCtx, pkt) < 0)
                                {
                                    RaiseError("Cannot suplly packet to decoder");
                                    return;
                                }

                                int recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, avFrame);
                                while (recvRes >= 0)
                                {
                                    //Debug.WriteLine($"video number samples {avFrame->nb_samples}, pts={avFrame->pts}, dts={(int)(_videoTimebase * avFrame->pts * 1000)}, width {avFrame->width}, height {avFrame->height}.");

                                    _framesDisplayed++;
                                    _videoBytes += pkt->size;

                                    OnVideoFrame?.Invoke(ref *avFrame);

                                    if (!_isCamera)
                                    {
                                        double dpts = 0;
                                        if (avFrame->pts != ffmpeg.AV_NOPTS_VALUE)
                                        {
                                            dpts = _videoTimebase * avFrame->pts;
                                            if (firts_dpts == 0)
                                                firts_dpts = dpts;

                                            dpts -= firts_dpts;
                                        }

                                        //Debug.WriteLine($"Decoded video frame {avFrame->width}x{avFrame->height}, ts {avFrame->best_effort_timestamp}, delta {avFrame->best_effort_timestamp - prevVidTs}, dpts {dpts}.");

                                        int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                                        if (sleep > MIN_SLEEP_MILLISECONDS)
                                        {
                                            ffmpeg.av_usleep((uint)(Math.Min(_maxVideoFrameSpace, sleep) * 1000));
                                        }
                                    }

                                    recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, avFrame);
                                }

                                if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                                {
                                    RaiseError("Cannot receive more frame");
                                    return;
                                }
                            }

                            ffmpeg.av_packet_unref(pkt);
                        }
                    }

                    if (_isPaused && !_isClosed)
                    {
                        ffmpeg.av_usleep((uint)(MIN_SLEEP_MILLISECONDS * 1000));
                        goto Repeat;
                    }
                    else
                    {
                        logger.Debug($"FFmpeg end of file for source {_sourceUrl}.");

                        if (!_isClosed && _repeat)
                        {
                            if (ffmpeg.avio_seek(_fmtCtx->pb, 0, ffmpeg.AVIO_SEEKABLE_NORMAL) < 0)
                            {
                                RaiseError("Cannot go to the beginning of the stream");
                                return;
                            }

                            if (ffmpeg.avformat_seek_file(_fmtCtx, _videoStreamIndex, 0, 0, _fmtCtx->streams[_videoStreamIndex]->duration, ffmpeg.AVSEEK_FLAG_ANY) < 0)
                            {
                                // We can't easily go back to the beginning of the file ...
                                canContinue = false;
                                needToRestartVideo = true;
                            }
                            else
                            {
                                canContinue = true;
                                goto Repeat;
                            }
                        }
                        else
                        {
                            OnEndOfFile?.Invoke();
                        }
                    }
                }
                finally
                {
                    ffmpeg.av_frame_unref(avFrame);
                    ffmpeg.av_free(avFrame);

                    ffmpeg.av_packet_unref(pkt);
                    ffmpeg.av_free(pkt);
                }
            }

            if (needToRestartVideo)
            {
                Dispose();
                Task.Run(() => StartDecode());
            }
        }

        private void DecodeInfoLoop()
        {
            int curLoop = 0;
            int interval = 100;
            int secondLoops = 1000 / interval;
            long prevTicks = DateTime.UtcNow.Ticks;
            double curSecond = 0;
            DecodeStats curStats = new();

            do
            {
                try
                {
                    if (_isStarted == false)
                    {
                        Thread.Sleep(interval);
                        continue;
                    }

                    curLoop++;
                    if (curLoop == secondLoops)
                    {
                        var curTicks = DateTime.UtcNow.Ticks;
                        curSecond = (curTicks - prevTicks) / 10000000.0;
                        prevTicks = curTicks;
                    }

                    lock (this)
                    {
                        // Get every second info
                        if (curLoop == secondLoops)
                        {
                            curStats.VideoBitRate = (_videoBytes - curStats.VideoBytes) * 8 / 1000.0;
                            curStats.VideoBytes = _videoBytes;

                            curStats.FpsCurrent = (_framesDisplayed - curStats.FramesDisplayed) / curSecond;
                            curStats.FramesDisplayed = _framesDisplayed;

                            OnCurStats?.Invoke(curStats);
                        }
                    }

                    if (curLoop == secondLoops)
                        curLoop = 0;

                    Thread.Sleep(interval);
                }
                catch
                {
                    curLoop = 0;
                }
            } while (true);
        }

        public unsafe int GetFrameRate()
        {
            int frameRate = (int)ffmpeg.av_q2d(_vidDecCtx->framerate);

            if (frameRate > 0)
            {
                return frameRate;
            }

            if (_videoAvgFrameRate > 2)
            {
                frameRate = (int)_videoAvgFrameRate;
            }
            else
            {
                frameRate = (int)ffmpeg.av_q2d(ffmpeg.av_guess_frame_rate(null, _fmtCtx->streams[_videoStreamIndex], null));
            }

            return frameRate;
        }

        public void Dispose()
        {
            if (_isInitialised && !_isDisposed)
            {
                _isClosed = true;
                _isDisposed = true;
                _isInitialised = false;
                _isStarted = false;

                logger.Debug("Disposing of FFmpegVideoDecoder.");
                unsafe
                {
                    try
                    {
                        if (_vidDecCtx != null)
                        {
                            var pCodecContext = _vidDecCtx;
                            ffmpeg.avcodec_close(pCodecContext);
                            ffmpeg.avcodec_free_context(&pCodecContext);
                            _vidDecCtx = null;
                        }
                    }
                    catch { }

                    try
                    {
                        if (_fmtCtx != null)
                        {
                            var pFormatContext = _fmtCtx;
                            ffmpeg.avformat_close_input(&pFormatContext);
                            ffmpeg.avformat_free_context(pFormatContext);
                            _fmtCtx = null;
                        }
                    }
                    catch { }
                }
            }
        }
    }

    public struct DecodeStats
    {
        public long FramesDisplayed { get; set; }
        public long VideoBytes { get; set; }
        public double FpsCurrent { get; set; }
        public double VideoBitRate { get; set; }
    }
}
