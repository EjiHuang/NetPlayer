using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetPlayer.Core.RTSP.Messages
{
    public class RtspRequestPlay : RtspRequest
    {

        // Constructor
        public RtspRequestPlay()
        {
            Command = "PLAY * RTSP/1.0";
        }
    }
}
