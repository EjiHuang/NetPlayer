﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetPlayer.Core.RTSP.Sdp
{
    public class Bandwidth
    {
        public Bandwidth()
        {
        }

        internal static Bandwidth Parse(string value)
        {
            //TODO really parse.
            return new Bandwidth();
        }
    }
}
