using FFmpeg.AutoGen;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NetPlayer.WinUI.FFmpeg.Utils
{
    public static unsafe class FFmpegHelper
    {
        private static readonly av_log_set_callback_callback FFmpegLogCallbackDelegate = FFmpegLogCallback;

        internal static void RegisterFFmpegBinaries()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var current = Environment.CurrentDirectory;
                var probe = Path.Combine("FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86");

                while (current != null)
                {
                    var ffmpegBinaryPath = Path.Combine(current, probe);

                    if (Directory.Exists(ffmpegBinaryPath))
                    {
                        Debug.WriteLine($"[FFmpeg.AutoGen] FFmpeg binaries found in: {ffmpegBinaryPath}");
                        ffmpeg.RootPath = ffmpegBinaryPath;
                        return;
                    }

                    current = Directory.GetParent(current)?.FullName;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                ffmpeg.RootPath = "/lib/x86_64-linux-gnu/";
            else
                throw new NotSupportedException(); // fell free add support for platform of your choose
        }

        internal static void LogConfigure()
        {
            // 设置ffmpeg日志等级
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_INFO);
            ffmpeg.av_log_set_callback(FFmpegLogCallbackDelegate);
        }

        private unsafe static void FFmpegLogCallback(void* ptr, int level, string format, byte* vl)
        {
            if (level > ffmpeg.av_log_get_level())
                return;

            var lineData = new byte[1024];
            fixed (byte* pLineData = lineData)
            {
                var printPrefix = 1;
                ffmpeg.av_log_format_line(ptr, level, format, vl, pLineData, lineData.Length, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)pLineData);
                Debug.Write(line);
            }
        }

        public static int ThrowExceptionIfError(this int error)
        {
            if (error < 0)
            {
                throw new ApplicationException(av_strerror(error));
            }
            return error;
        }

        public static unsafe string? av_strerror(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message;
        }
    }
}
