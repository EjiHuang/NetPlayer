using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetPlayer.Core.RTSP.Messages
{
    public class RtspRequestPause : RtspRequest
    {

        // Constructor
        public RtspRequestPause()
        {
            Command = "PAUSE * RTSP/1.0";
        }
    }
}
