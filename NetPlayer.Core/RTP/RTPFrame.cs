using SIPSorcery.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetPlayer.Core.RTP
{
    public class RTPFrame
    {
        public uint Timestamp { get; set; }
        public bool HasMarker { get; set; }
        public bool HasBeenProcessed { get; set; }
        public FrameTypesEnum FrameType { get; set; }

        public RTPFrame()
        {
            //RTSPMessage.ParseRTSPMessage();
        }
    }
}
