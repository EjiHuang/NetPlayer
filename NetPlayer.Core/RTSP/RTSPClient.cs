using Microsoft.Extensions.Logging;
using NetPlayer.Core.RTP;
using NetPlayer.Core.RTSP.Messages;
using NetPlayer.Core.RTSP.Sdp;
using SIPSorcery.Net;
using System;
using System.Collections.Generic;
using System.IO;
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

        private Uri _videoUri = null;
        private int _videoPayload = -1;
        private int _videoDataChannel = -1;
        private int _videoRtcpChannel = -1;

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
            var dataReceived = e.Message as RtspData;

        }

        private void RtspMessageReceived(object sender, RtspChunkEventArgs e)
        {
            var message = e.Message as RtspResponse;

            logger.Debug("Received RTSP Message " + message.OriginalRequest.ToString());

            if (message.IsOk == false)
            {
                logger.Debug("Got Error in RTSP Reply " + message.ReturnCode + " " + message.ReturnMessage);

                if (message.ReturnCode == 401 && (message.OriginalRequest.Headers.ContainsKey(RtspHeaderNames.Authorization) == true))
                {
                    Stop();
                    return;
                }

                if (message.ReturnCode == 401 && message.Headers.ContainsKey(RtspHeaderNames.WWWAuthenticate))
                {
                    // 处理鉴权信息头 WWW-Authenticate header
                    // 例如：Basic realm="AProxy"
                    // 例如：Digest realm="AXIS_WS_ACCC8E3A0A8F", nonce="000057c3Y810622bff50b36005eb5efeae118626a161bf", stale=FALSE
                    // 例如：Digest realm="IP Camera(21388)", nonce="534407f373af1bdff561b7b4da295354", stale="FALSE"

                    var wwwAuthenticate = message.Headers[RtspHeaderNames.WWWAuthenticate];
                    var authParams = "";

                    if (wwwAuthenticate.StartsWith("basic", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _authType = "Basic";
                        authParams = wwwAuthenticate[5..];
                    }

                    if (wwwAuthenticate.StartsWith("digest", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _authType = "Digest";
                        authParams = wwwAuthenticate[6..];
                    }

                    var items = authParams.Split(new char[] { ',' });

                    foreach (var item in items)
                    {
                        var parts = item.Trim().Split(new char[] { '=' }, 2);
                        if (parts.Length >= 2 && parts[0].Trim().Equals("realm"))
                        {
                            _realm = parts[1].Trim(new char[] { ' ', '\"' });
                        }
                        else if (parts.Length >= 2 && parts[0].Trim().Equals("nonce"))
                        {
                            _nonce = parts[1].Trim(new char[] { ' ', '\"' });
                        }
                    }

                    logger.Debug("WWW Authorize parsed for " + _authType + " " + _realm + " " + _nonce);
                }

                var resendMessage = message.OriginalRequest.Clone() as RtspMessage;
                if (_authType != null)
                {
                    AddAuthorization(resendMessage, _userName, _password, _authType, _realm, _nonce, _url);
                }

                _rtspClient.SendMessage(resendMessage);

                return;
            }

            // 来自OPTIONS请求的应答
            if (message.OriginalRequest != null && message.OriginalRequest is RtspRequestOptions)
            {
                if (message.Headers.ContainsKey(RtspHeaderNames.Public))
                {
                    var parts = message.Headers[RtspHeaderNames.Public].Split(',');
                    foreach (var part in parts)
                    {
                        if (part.Trim().ToUpper().Equals("GET_PARAMETER")) _serverSupportsGetParameter = true;
                        if (part.Trim().ToUpper().Equals("SET_PARAMETER")) _serverSupportsSetParameter = true;
                    }
                }

                // 启动保活时钟
                if (_keepaliveTimer == null)
                {
                    _keepaliveTimer = new Timer();
                    _keepaliveTimer.Elapsed += KeepaliveTimer_Elapsed;
                    _keepaliveTimer.Interval = 20 * 1000;
                    _keepaliveTimer.Enabled = true;

                    // 发送DESCRIBE请求
                    var describeMessage = new RtspRequestDescribe
                    {
                        RtspUri = new Uri(_url)
                    };

                    if (_authType != null)
                    {
                        AddAuthorization(describeMessage, _userName, _password, _authType, _realm, _nonce, _url);
                    }

                    _rtspClient.SendMessage(describeMessage);
                }
                else
                {
                    // 啥也不用做，只有等保活时钟为空是才发送DESCRIBE请求，因为保活过程中会一直发OPTIONS请求
                }
            }

            // 来自DESCRIBE请求的应答
            if (message.OriginalRequest != null && message.OriginalRequest is RtspRequestDescribe)
            {
                if (message.IsOk == false)
                {
                    logger.Debug("Got Error in DESCRIBE Reply " + message.ReturnCode + " " + message.ReturnMessage);
                    return;
                }

                // 开始尝试解析SDP
                logger.Debug(Encoding.UTF8.GetString(message.Data));

                SdpFile sdpData;
                using (var sdpStream = new StreamReader(new MemoryStream(message.Data)))
                {
                    sdpData = SdpFile.Read(sdpStream);
                }

                var nextFreeRtpChannel = 0;
                var nextFreeRtcpChannel = 1;

                for (int x = 0; x < sdpData.Medias.Count; x++)
                {
                    bool audio = sdpData.Medias[x].MediaType == Media.MediaTypes.audio;
                    bool video = sdpData.Medias[x].MediaType == Media.MediaTypes.video;

                    //if (video && video_payload != -1) continue;
                }
            }
        }

        private void KeepaliveTimer_Elapsed(object sender, ElapsedEventArgs e)
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
