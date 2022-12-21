using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetPlayer.Core.RTSP
{
    public static class RtspUtils
    {
        /// <summary>
        /// Registers the URI.
        /// </summary>
        public static void RegisterUri()
        {
            if (!UriParser.IsKnownScheme("rtsp"))
                UriParser.Register(new HttpStyleUriParser(), "rtsp", 554);
        }
    }
}
