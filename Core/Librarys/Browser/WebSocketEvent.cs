using Core.Event;
using Core.Models.WebPage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Core.Librarys.Browser
{
    public static class WebSocketEvent
    {
        public static event WebServerEventHandler OnWebLog;

        public static void Invoke(NotifyWeb args_)
        {
            OnWebLog?.Invoke(args_);
        }
    }
}
