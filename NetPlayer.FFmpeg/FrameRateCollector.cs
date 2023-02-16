/*
 https://github.com/ugcs/ugcs-video-transmitter/blob/master/src/VideoTransmitter/VideoTools/FramerateCollector.cs
 */

using FFmpeg.AutoGen;
using System.Diagnostics;

namespace NetPlayer.FFmpeg
{
    public sealed class FrameRateCollector
    {
        private Stopwatch _stopwatch = new Stopwatch();
        private int _framesReceived;
        private int _framesToCollect;
        public AVRational? FrameRate { get; private set; }

        public FrameRateCollector(int framesToCollect)
        {
            _framesToCollect = framesToCollect;
        }

        public void FrameReceived()
        {
            if (_framesReceived % _framesToCollect == 0)
            {
                if (_framesReceived != 0)
                {
                    if (_stopwatch.Elapsed.TotalSeconds > 0)
                        FrameRate = new AVRational { num = _framesReceived * 1000, den = (int)_stopwatch.Elapsed.TotalMilliseconds };
                    else
                        FrameRate = new AVRational { num = _framesReceived, den = 1 };

                    //Debug.WriteLine($"{nameof(FrameRateCollector)}: Framerate: {FrameRate.Value.num}/{FrameRate.Value.den}");
                }

                _stopwatch.Restart();
                _framesReceived = 0;
            }

            _framesReceived++;
        }
    }
}
