﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using System;

namespace MQTTnet.AspNetCore
{
    public static class ConnectionRouteBuilderExtensions
    {
#if NETCOREAPP3_1
        [Obsolete("This class is obsolete and will be removed in a future version. The recommended alternative is to use MapMqtt inside Microsoft.AspNetCore.Builder.UseEndpoints(...).")]
#endif
        public static void MapMqtt(this ConnectionsRouteBuilder connection, PathString path)
        {
            connection.MapConnectionHandler<MqttConnectionHandler>(path, options =>
            {
                options.WebSockets.SubProtocolSelector = MqttSubProtocolSelector.SelectSubProtocol;
            });
        }
    }
}