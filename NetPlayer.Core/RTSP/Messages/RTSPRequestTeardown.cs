using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetPlayer.Core.RTSP.Messages
{
    public class RtspRequestTeardown : RtspRequest
    {

        // Constructor
        public RtspRequestTeardown()
        {
            Command = "TEARDOWN * RTSP/1.0";
        }
    }
}
