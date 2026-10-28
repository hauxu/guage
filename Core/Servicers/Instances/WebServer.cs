using Core.Servicers.Interfaces;
using Core.Librarys;
using Core.Librarys.Security;
using System;
using System.Net;
using System.Net.Sockets;
using WebSocketSharp.Server;

namespace Core.Servicers.Instances
{
    public class WebServer : IWebServer, IDisposable
    {
        private WebSocketServer _webSocket;
        private BrowserTokenHttpServer _tokenHttpServer;
        private bool _isStart;

        public bool IsRunning => _isStart && WebSocketSecurity.IsServiceRunning;

        public int ConnectedExtensionCount => WebSocketSecurity.ConnectedClientCount;

        public string LastStartError => WebSocketSecurity.LastStartError;

        public event EventHandler ExtensionConnectionChanged
        {
            add => WebSocketSecurity.ExtensionConnectionChanged += value;
            remove => WebSocketSecurity.ExtensionConnectionChanged -= value;
        }

        public void Start()
        {
            if (_isStart)
            {
                return;
            }

            WebSocketSecurity.MarkStopped();

            for (int i = 0; i < WebSocketSecurity.TokenPortCandidates.Length; i++)
            {
                int tokenPort = WebSocketSecurity.TokenPortCandidates[i];
                int webSocketPort = WebSocketSecurity.WebSocketPortCandidates[
                    Math.Min(i, WebSocketSecurity.WebSocketPortCandidates.Length - 1)];

                if (!IsPortAvailable(webSocketPort) || !IsPortAvailable(tokenPort))
                {
                    continue;
                }

                BrowserTokenHttpServer tokenServer = null;
                WebSocketServer webSocketServer = null;
                try
                {
                    tokenServer = new BrowserTokenHttpServer(tokenPort);
                    tokenServer.Start();
                    if (!tokenServer.IsStarted)
                    {
                        tokenServer.Dispose();
                        continue;
                    }

                    webSocketServer = new WebSocketServer(IPAddress.Loopback, webSocketPort, false);
                    webSocketServer.AddWebSocketService<GuageWebSocketBehavior>("/guage-web");
                    webSocketServer.Start();

                    _tokenHttpServer = tokenServer;
                    _webSocket = webSocketServer;
                    WebSocketSecurity.SetActivePorts(webSocketPort, tokenPort);
                    _isStart = true;
                    Logger.Info($"浏览器统计服务已启动：WebSocket 127.0.0.1:{webSocketPort}，令牌 127.0.0.1:{tokenPort}");
                    return;
                }
                catch (Exception ex)
                {
                    try
                    {
                        webSocketServer?.Stop();
                    }
                    catch
                    {
                    }

                    tokenServer?.Dispose();
                    Logger.Error($"浏览器服务端口 {webSocketPort}/{tokenPort} 启动失败：" + ex.Message);
                }
            }

            string error = "无法启动浏览器统计服务。本机端口 8908/8909（或备用 8910/8911、8912/8913）可能被其他程序占用，网站统计将不可用。";
            WebSocketSecurity.MarkStopped(error);
            Logger.Error(error);
        }

        public void Stop()
        {
            if (!_isStart)
            {
                return;
            }

            _tokenHttpServer?.Stop();
            _webSocket?.Stop();
            _tokenHttpServer?.Dispose();
            _tokenHttpServer = null;
            _webSocket = null;
            _isStart = false;
            WebSocketSecurity.MarkStopped();
        }

        public void SendMsg(string msg_)
        {
            try
            {
                if (!_isStart || _webSocket == null)
                {
                    return;
                }

                _webSocket.WebSocketServices.Broadcast(msg_);
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
        }

        private static bool IsPortAvailable(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    listener?.Stop();
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
