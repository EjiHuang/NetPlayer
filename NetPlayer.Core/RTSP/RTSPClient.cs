using Microsoft.Extensions.Logging;
using NetPlayer.Core.RTP;
using NetPlayer.Core.RTSP.Messages;
using SIPSorcery.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Windows.Management;

namespace NetPlayer.Core.RTSP
{
    public enum RtpTransport
    {
        Udp,
        Tcp,
        Multicast,
        Unknown
    };

    public enum MediaRequest
    {
        VideoOnly,
        AudioOnly,
        VideoAndAudio
    }

    public enum RtspStatus
    {
        WaitingToConnect,
        Connecting,
        ConnectFailed,
        Connected
    }

    public class RTSPClient
    {
        private const int RTSP_PORT = 554;
        private const int MAX_FRAMES_QUEUE_LENGTH = 1000;
        private const int RTP_KEEP_ALIVE_INTERVAL = 30;
        private const int RTP_TIMEOUT_SECONDS = 15;
        private const int BANDWIDTH_CALCULATION_SECONDS = 5;

        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private string _url;
        private string _hostName;
        private int _port;
        private string _userName;
        private string _password;
        private string _session = null;
        private string _authType = null;
        private string _realm = null;
        private string _nonce = null;

        private bool _clientWantsVideo;
        private bool _clientWantsAudio;

        private RtspTcpTransport _rtspSocket = null;
        private volatile RtspStatus _rtspSocketStatus = RtspStatus.WaitingToConnect;
        private RtspListener _rtspClient = null;
        private RtpTransport _rtpTransport = RtpTransport.Unknown;

        private UDPSocket _videoUdpPair = null;
        private UDPSocket _audioUdpPair = null;

        private int _cseq = 1;
        private TcpClient _rtspConnection;
        private NetworkStream _rtspStream;
        private RTPSession _rtpSession;
        private int _rtpPayloadHeaderLength;
        private List<RTPFrame> _frames = new List<RTPFrame>();

        private bool _serverSupportsGetParameter = false;
        private bool _serverSupportsSetParameter = false;
        private Timer _keepaliveTimer = null;

        public RTSPClient()
        {

        }

        public void Connect(string url, RtpTransport rtpTransport, MediaRequest mediaRequest = MediaRequest.VideoAndAudio)
        {
            RtspUtils.RegisterUri();

            logger.Debug("Connecting to " + url);
            _url = url;

            try
            {
                var uri = new Uri(_url);
                _hostName = uri.Host;
                _port = uri.Port;

                if (uri.UserInfo.Length > 0)
                {
                    _userName = uri.UserInfo.Split(new char[] { ':' })[0];
                    _password = uri.UserInfo.Split(new char[] { ':' })[1];
                    _url = uri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo, UriFormat.UriEscaped);
                }
            }
            catch
            {
                _userName = null;
                _password = null;
            }

            _clientWantsVideo = false;
            _clientWantsAudio = false;
            if (mediaRequest == MediaRequest.VideoOnly || mediaRequest == MediaRequest.VideoAndAudio)
            {
                _clientWantsVideo = true;
            }
            if (mediaRequest == MediaRequest.AudioOnly || mediaRequest == MediaRequest.VideoAndAudio)
            {
                _clientWantsAudio = true;
            }

            // 与RTSP服务器建立连接，这里主要是基于TCP协议
            _rtspSocketStatus = RtspStatus.Connecting;
            try
            {
                _rtspSocket = new RtspTcpTransport(_hostName, _port);
            }
            catch
            {
                _rtspSocketStatus = RtspStatus.ConnectFailed;
                logger.Warn("Error - did not connect");
                return;
            }

            if (_rtspSocket.Connected == false)
            {
                _rtspSocketStatus = RtspStatus.ConnectFailed;
                logger.Warn("Error - did not connect");
                return;
            }

            _rtspSocketStatus = RtspStatus.Connected;

            _rtspClient = new RtspListener(_rtspSocket);
            _rtspClient.AutoReconnect = false;
            _rtspClient.MessageReceived += RtspMessageReceived;
            _rtspClient.DataReceived += RtpDataReceived;
            _rtspClient.Start();

            // 检测RTP传输方式，目前只需要针对UDP做特殊处理
            _rtpTransport = rtpTransport;
            if (_rtpTransport == RtpTransport.Udp)
            {
                _videoUdpPair = new UDPSocket(50000, 51000);
                _videoUdpPair.DataReceived += RtpDataReceived;
                _videoUdpPair.Start();
                _audioUdpPair = new UDPSocket(50000, 51000);
                _audioUdpPair.DataReceived += RtpDataReceived;
                _audioUdpPair.Start();
            }

            // 首先发送OPTIONS请求
            _rtspClient.SendMessage(new RtspRequestOptions
            {
                RtspUri = new Uri(_url)
            });
        }

        public void Pause()
        {
            if (_rtspClient != null)
            {
                var pauseMessage = new RtspRequestPause
                {
                    RtspUri= new Uri(_url),
                    Session=_session
                };

                if (_authType != null)
                {
                    AddAuthorization(pauseMessage, _userName, _password, _authType, _realm, _nonce, _url);
                }

                _rtspClient.SendMessage(pauseMessage);
            }
        }

        public void Play()
        {
            if (_rtspClient != null)
            {
                var playMessage = new RtspRequestPlay
                {
                    RtspUri = new Uri(_url),
                    Session = _session
                };

                if (_authType != null)
                {
                    AddAuthorization(playMessage, _userName, _password, _authType, _realm, _nonce, _url);
                }

                _rtspClient.SendMessage(playMessage);
            }
        }

        public void Stop()
        {
            if (_rtspClient != null)
            {
                var teardownMessage = new RtspRequestTeardown
                {
                    RtspUri = new Uri(_url),
                    Session = _session
                };

                if (_authType != null)
                {
                    AddAuthorization(teardownMessage, _userName, _password, _authType, _realm, _nonce, _url);
                }

                _rtspClient.SendMessage(teardownMessage);
            }

            // 关闭保活时钟
            _keepaliveTimer?.Stop();

            // 关闭RTP UDP传输管道
            _videoUdpPair?.Stop();
            _audioUdpPair?.Stop();

            // 关闭RTSP客户端会话
            _rtspClient?.Stop();
        }

        private void RtpDataReceived(object sender, RtspChunkEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void RtspMessageReceived(object sender, RtspChunkEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void AddAuthorization(RtspMessage message, string userName, string password, string authType, string realm, string nonce, string url)
        {
            if (userName == null || userName.Length == 0) return;
            if (password == null || password.Length == 0) return;
            if (realm == null || realm.Length == 0) return;
            if (authType.Equals("Digest") && (nonce == null || nonce.Length == 0)) return;

            if (authType.Equals("Basic"))
            {
                byte[] credentials = System.Text.Encoding.UTF8.GetBytes(userName + ":" + password);
                string credentialsBase64 = Convert.ToBase64String(credentials);
                string basicAuthorization = "Basic " + credentialsBase64;

                message.Headers.Add(RtspHeaderNames.Authorization, basicAuthorization);

                return;
            }
            else if (authType.Equals("Digest"))
            {
                string method = message.Method;

                MD5 md5 = System.Security.Cryptography.MD5.Create();
                string hashA1 = CalculateMD5Hash(md5, userName + ":" + realm + ":" + password);
                string hashA2 = CalculateMD5Hash(md5, method + ":" + url);
                string response = CalculateMD5Hash(md5, hashA1 + ":" + nonce + ":" + hashA2);

                const string quote = "\"";
                string digest_authorization = "Digest username=" + quote + userName + quote + ", "
                    + "realm=" + quote + realm + quote + ", "
                    + "nonce=" + quote + nonce + quote + ", "
                    + "uri=" + quote + url + quote + ", "
                    + "response=" + quote + response + quote;

                message.Headers.Add(RtspHeaderNames.Authorization, digest_authorization);

                return;
            }
        }

        private static string CalculateMD5Hash(MD5 md5Session, string input)
        {
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hash = md5Session.ComputeHash(inputBytes);

            var output = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                output.Append(hash[i].ToString("x2"));
            }

            return output.ToString();
        }
    }
}
