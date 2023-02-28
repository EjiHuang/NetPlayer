using FFmpeg.AutoGen;
using System.Diagnostics;

namespace NetPlayer.FFmpeg.Encoder
{
    public sealed unsafe class FFmpegVideoEncoder : IDisposable
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private AVFormatContext* _pFormatContext;
        private AVCodecContext* _pEncoderContext;
        private AVStream* _pStream;
        private AVCodecID _codecID;

        private string _fileName;
        private bool _isEncoderInitialised = false;
        public bool IsEncoderInitialised => _isEncoderInitialised;

        public FFmpegVideoEncoder(string fileName)
        {
            _fileName = fileName;
        }

        public void InitialiseEncoder(AVCodecID codecID, int width, int height, int frameRate)
        {
            if (!_isEncoderInitialised)
            {
                _isEncoderInitialised = true;

                // 创建输出数据的封装格式
                _pFormatContext = ffmpeg.avformat_alloc_context();
                var pFormatContext = _pFormatContext;
                ffmpeg.avformat_alloc_output_context2(&pFormatContext, null, null, _fileName).ThrowExceptionIfError();
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
                ffmpeg.av_dump_format(pFormatContext, 0, _fileName, 1);

                // 创建并初始化AVIOContext
                if (ffmpeg.avio_open(&pFormatContext->pb, _fileName, ffmpeg.AVIO_FLAG_WRITE) < 0)
                {
                    Debug.WriteLine("Failed to open output file. It could be a video stream.");
                }

                // 配置选项
                ffmpeg.avformat_write_header(pFormatContext, null).ThrowExceptionIfError();
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

                do
                {
                    // 向输出编码器上下文提供原始视频帧
                    ffmpeg.avcodec_send_frame(_pEncoderContext, &uncompressedFrame).ThrowExceptionIfError();

                    // 从输出编码器上下文中读取编码好的数据包
                    error = ffmpeg.avcodec_receive_packet(_pEncoderContext, pPacket);

                } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN) || error == ffmpeg.AVERROR(ffmpeg.AVERROR_EOF));

                ffmpeg.av_packet_rescale_ts(pPacket, _pEncoderContext->time_base, _pFormatContext->streams[pPacket->stream_index]->time_base);
                pPacket->stream_index = _pStream->index;
                ffmpeg.av_interleaved_write_frame(_pFormatContext, pPacket).ThrowExceptionIfError();
            }
            finally
            {
                ffmpeg.av_packet_unref(pPacket);
                ffmpeg.av_packet_free(&pPacket);
            }
        }

        public void Dispose()
        {
            try
            {
                var pFormatContext = _pFormatContext;
                if (pFormatContext != null)
                {
                    ffmpeg.av_write_trailer(pFormatContext);
                    ffmpeg.avformat_close_input(&pFormatContext);
                }

                if (_pEncoderContext != null)
                {
                    ffmpeg.avcodec_close(_pEncoderContext);
                    ffmpeg.av_free(_pEncoderContext);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[VideoStreamEncoder] " + ex.ToString());
            }

            GC.SuppressFinalize(this);
        }
    }
}
