/*
 https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg/blob/master/src/FfmpegInit.cs
 */

using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;

namespace NetPlayer.WinUI.FFmpeg.Core
{
    public static class FFmpegConvert
    {
        public static AVCodecID? GetAVCodecID(VideoCodecsEnum videoCodec)
        {
            AVCodecID? avCodecID = null;
            switch (videoCodec)
            {
                case VideoCodecsEnum.VP8:
                    avCodecID = AVCodecID.AV_CODEC_ID_VP8;
                    break;
                case VideoCodecsEnum.H264:
                    avCodecID = AVCodecID.AV_CODEC_ID_H264;
                    break;
            }

            return avCodecID;
        }
    }
}
