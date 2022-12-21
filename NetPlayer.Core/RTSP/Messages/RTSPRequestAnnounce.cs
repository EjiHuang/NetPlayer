using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetPlayer.Core.RTSP.Messages
{
    public class RtspRequestAnnounce : RtspRequest
    {
        // constructor

        public RtspRequestAnnounce()
        {
            Command = "ANNOUNCE * RTSP/1.0";
        }
    }
}