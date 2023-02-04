﻿using FFmpeg.AutoGen;
using System.Diagnostics;

namespace NetPlayer.FFmpeg.Encoder
{
    public sealed unsafe class FFmpegStreamEncoder : IDisposable
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private AVFormatContext* _pFormatContext;
        private AVCodecContext* _pEncoderContext;
        private AVStream* _pStream;
        private AVCodecID _codecID;

        private string? _formatName;
        private string _url;

        private bool _isEncoderInitialised = false;

        public FFmpegStreamEncoder(string url)
        {
            // 解析格式
            _formatName = GetFormatType(url);
            if (_formatName == null)
            {
                throw new Exception("Not support this url:" + url);
            }

            _url = url;
        }

        public void InitialiseEncoder(AVCodecID codecID, int width, int height, int frameRate)
        {
            if (!_isEncoderInitialised)
            {
                _isEncoderInitialised = true;

                // 创建输出数据的封装格式
                _pFormatContext = ffmpeg.avformat_alloc_context();
                var pFormatContext = _pFormatContext;
                ffmpeg.avformat_alloc_output_context2(&pFormatContext, null, _formatName, _url).ThrowExceptionIfError();
                if (_pFormatContext == null)
                {
                    throw new Exception("Could not allocate an output context");
                }
                if (pFormatContext->oformat == null)
                {
                    throw new Exception("Could not allocate an output format");
                }

                // 查找对应编码器
                _codecID = codecID;
                var pCodec = ffmpeg.avcodec_find_encoder(codecID);
                if (pCodec == null)
                {
                    throw new ApplicationException($"Codec encoder could not be found for {codecID}.");
                }

                // 基于编码器创建一个新的流
                _pStream = ffmpeg.avformat_new_stream(pFormatContext, pCodec);
                if (_pStream == null)
                {
                    throw new Exception("AVStream not found.");
                }

                // 配置编码器
                _pEncoderContext = ffmpeg.avcodec_alloc_context3(pCodec);
                if (_pEncoderContext == null)
                {
                    throw new ApplicationException("Failed to allocate encoder codec context.");
                }

                _pEncoderContext->width = width;
                _pEncoderContext->height = height;
                _pEncoderContext->time_base.den = frameRate;
                _pEncoderContext->time_base.num = 1;
                _pEncoderContext->framerate.den = 1;
                _pEncoderContext->framerate.num = frameRate;

                _pEncoderContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                _pEncoderContext->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;

                // 设置关键帧间隔
                if (frameRate < 5)
                    _pEncoderContext->gop_size = 1;
                else
                    _pEncoderContext->gop_size = frameRate;

                if (_codecID == AVCodecID.AV_CODEC_ID_H264)
                {
                    ffmpeg.av_opt_set(_pEncoderContext->priv_data, "preset", "ultrafast", 0);
                    ffmpeg.av_opt_set(_pEncoderContext->priv_data, "tune", "zerolatency", 0);
                }
                else if ((_codecID == AVCodecID.AV_CODEC_ID_VP8) || (_codecID == AVCodecID.AV_CODEC_ID_VP9))
                {
                    ffmpeg.av_opt_set(_pEncoderContext->priv_data, "quality", "realtime", 0);
                }

                if ((pFormatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                {
                    _pEncoderContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                }

                ffmpeg.avcodec_open2(_pEncoderContext, pCodec, null).ThrowExceptionIfError();
                ffmpeg.avcodec_parameters_from_context(_pStream->codecpar, _pEncoderContext);

                logger.Debug($"Successfully initialised ffmpeg based image encoder: CodecId:[{codecID}] - {width}:{height} - {frameRate} Fps");

                // 输出一些信息
                ffmpeg.av_dump_format(pFormatContext, 0, _url, 1);

                // 创建并初始化AVIOContext
                if (ffmpeg.avio_open(&pFormatContext->pb, _url, ffmpeg.AVIO_FLAG_WRITE) < 0)
                {
                    Debug.WriteLine("Failed to open output file. It could be a video stream.");
                }

                // 配置选项
                AVDictionary* options = null;
                if (_formatName == "rtsp")
                {
                    ffmpeg.av_dict_set(&options, "rtsp_transport", "tcp", 0);
                    ffmpeg.av_dict_set(&options, "profile", "baseline", 0);
                }

                ffmpeg.avformat_write_header(pFormatContext, &options).ThrowExceptionIfError();
                _pFormatContext = pFormatContext;
            }
        }

        public void TryEncodeNextPacket(AVFrame uncompressedFrame, AVCodecID codecID = AVCodecID.AV_CODEC_ID_H264, int frameRate = 30)
        {
            var width = uncompressedFrame.width;
            var height = uncompressedFrame.height;

            if (!_isEncoderInitialised)
            {
                InitialiseEncoder(codecID, width, height, frameRate);
            }
            else if (_pEncoderContext->width != width || _pEncoderContext->height != height)
            {
                _pEncoderContext->width = width;
                _pEncoderContext->height = height;
            }

            if (uncompressedFrame.format != (int)_pEncoderContext->pix_fmt)
            {
                throw new ArgumentException("Invalid pixel format.", nameof(uncompressedFrame));
            }
            var pPacket = ffmpeg.av_packet_alloc();

            try
            {
                var error = 0;

                // 向输出编码器上下文提供原始视频帧
                ffmpeg.avcodec_send_frame(_pEncoderContext, &uncompressedFrame).ThrowExceptionIfError();

                do
                {
                    // 从输出编码器上下文中读取编码好的数据包
                    error = ffmpeg.avcodec_receive_packet(_pEncoderContext, pPacket);

                    ffmpeg.av_packet_rescale_ts(pPacket, _pEncoderContext->time_base, _pFormatContext->streams[pPacket->stream_index]->time_base);
                    ffmpeg.av_interleaved_write_frame(_pFormatContext, pPacket).ThrowExceptionIfError();

                } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN) || error == ffmpeg.AVERROR(ffmpeg.AVERROR_EOF));
            }
            finally
            {
                ffmpeg.av_packet_unref(pPacket);
                ffmpeg.av_packet_free(&pPacket);
            }
        }

        private string? GetFormatType(string url)
        {
            if (url.StartsWith("rtsp://"))
            {
                return "rtsp";
            }

            if (url.StartsWith("udp://"))
            {
                return "h264";
            }

            if (url.StartsWith("rtp://"))
            {
                return "rtp";
            }

            if (url.StartsWith("rtmp://"))
            {
                return "flv";
            }

            return null;
        }

        public void Dispose()
        {
            try
            {
                var pFormatContext = _pFormatContext;

                ffmpeg.av_write_trailer(pFormatContext);
                ffmpeg.avformat_close_input(&pFormatContext);

                ffmpeg.avcodec_close(_pEncoderContext);
                ffmpeg.av_free(_pEncoderContext);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[VideoStreamEncoder] " + ex.ToString());
            }

            GC.SuppressFinalize(this);
        }
    }
}
