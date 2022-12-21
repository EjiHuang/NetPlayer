using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetPlayer.Core.RTSP.Messages
{
    public class RtspRequestDescribe : RtspRequest
    {

        // constructor

        public RtspRequestDescribe()
        {
            Command = "DESCRIBE * RTSP/1.0";
        }
    }
}
