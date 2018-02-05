﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SocketHttpListener.Net
{
    internal enum EntitySendFormat
    {
        ContentLength = 0, // Content-Length: XXX
        Chunked = 1, // Transfer-Encoding: chunked
    }
}
