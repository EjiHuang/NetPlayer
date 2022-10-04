using Microsoft.Extensions.Logging;
using NetPlayer.Core.RTP;
using SIPSorcery.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NetPlayer.Core.RTSP
{
    public class RTSPClient
    {
        private const int RTSP_PORT = 554;
        private const int MAX_FRAMES_QUEUE_LENGTH = 1000;
        private const int RTP_KEEP_ALIVE_INTERVAL = 30;
        private const int RTP_TIMEOUT_SECONDS = 15;
        private const int BANDWIDTH_CALCULATION_SECONDS = 5;

        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private string _url;
        private int _cseq = 1;
        private TcpClient _rtspConnection;
        private NetworkStream _rtspStream;
        private RTPSession _rtpSession;
        private int _rtpPayloadHeaderLength;
        private List<RTPFrame> _frames = new List<RTPFrame>();

        public RTSPClient()
        {
            
        }
    }
}
