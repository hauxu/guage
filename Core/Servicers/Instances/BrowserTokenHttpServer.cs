using Core.Librarys;

using Core.Librarys.Security;

using Newtonsoft.Json;

using System;

using System.Net;

using System.Text;

using System.Threading;

using System.Threading.Tasks;



namespace Core.Servicers.Instances

{

    /// <summary>

    /// 仅在本机提供浏览器插件读取 WebSocket 令牌的 HTTP 端点。

    /// </summary>

    internal sealed class BrowserTokenHttpServer : IDisposable

    {

        private readonly int _port;

        private HttpListener _listener;

        private CancellationTokenSource _cancellation;



        public bool IsStarted { get; private set; }



        public BrowserTokenHttpServer(int port)

        {

            _port = port;

        }



        public void Start()

        {

            if (IsStarted)

            {

                return;

            }



            try

            {

                _listener = new HttpListener();

                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

                _listener.Start();

                _cancellation = new CancellationTokenSource();

                IsStarted = true;

                Task.Run(() => ListenLoop(_cancellation.Token));

            }

            catch (Exception ex)

            {

                IsStarted = false;

                Logger.Error($"无法启动浏览器插件令牌服务（端口 {_port}），" + ex.Message);

            }

        }



        public void Stop()

        {

            if (!IsStarted)

            {

                return;

            }



            IsStarted = false;

            try

            {

                _cancellation?.Cancel();

                _listener?.Stop();

                _listener?.Close();

            }

            catch (Exception ex)

            {

                Logger.Error(ex.Message);

            }

        }



        private async Task ListenLoop(CancellationToken token)

        {

            while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)

            {

                HttpListenerContext context = null;

                try

                {

                    context = await _listener.GetContextAsync().ConfigureAwait(false);

                    HandleRequest(context);

                }

                catch (ObjectDisposedException)

                {

                    break;

                }

                catch (HttpListenerException)

                {

                    break;

                }

                catch (Exception ex)

                {

                    Logger.Error(ex.Message);

                }

            }

        }



        /// <summary>

        /// 只接受本机回环调用：Host 必须是 127.0.0.1/localhost（挡 DNS rebinding 把外部域名

        /// 解析到回环），且不能是网页发起的跨站请求（Sec-Fetch-Site / Origin 判据）。

        /// 本机进程仍可伪造这些头——那是本地信任模型的边界，不属于本端点的职责。

        /// </summary>

        private static bool IsTrustedCaller(HttpListenerRequest request)

        {

            string host = request.UserHostName ?? string.Empty;

            if (!(host.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase)

                || host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))

            {

                return false;

            }



            string fetchSite = request.Headers["Sec-Fetch-Site"];

            if (!string.IsNullOrEmpty(fetchSite)

                && !string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase)

                && !string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase))

            {

                return false;

            }



            string origin = request.Headers["Origin"];

            return string.IsNullOrEmpty(origin) || WebSocketSecurity.IsExtensionOrigin(origin);

        }



        private static void HandleRequest(HttpListenerContext context)

        {

            if (!IsTrustedCaller(context.Request))

            {

                WriteText(context, 403, "Forbidden", "text/plain; charset=utf-8");

                return;

            }



            string path = context.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();

            if (path == "/browser-ws-token")

            {

                WriteText(context, 200, WebSocketSecurity.GetToken(), "text/plain; charset=utf-8");

                return;

            }



            if (path == "/browser-ws-config")

            {

                var payload = new

                {

                    token = WebSocketSecurity.GetToken(),

                    wsPort = WebSocketSecurity.ActiveWebSocketPort,

                    tokenPort = WebSocketSecurity.ActiveTokenPort

                };

                WriteText(context, 200, JsonConvert.SerializeObject(payload), "application/json; charset=utf-8");

                return;

            }



            WriteText(context, 404, "Not Found", "text/plain; charset=utf-8");

        }



        private static void WriteText(HttpListenerContext context, int statusCode, string body, string contentType)

        {

            //  注意：绝不添加 Access-Control-Allow-Origin。

            //  该令牌端点仅供浏览器扩展读取（扩展拥有 host_permissions，不受 CORS 限制），

            //  任意网页的 fetch 会因同源策略无法读取响应，从而切断“网页窃取令牌”的攻击链。

            byte[] buffer = Encoding.UTF8.GetBytes(body ?? string.Empty);

            context.Response.StatusCode = statusCode;

            context.Response.ContentType = contentType;

            context.Response.ContentLength64 = buffer.Length;

            context.Response.OutputStream.Write(buffer, 0, buffer.Length);

            context.Response.OutputStream.Close();

        }



        public void Dispose()

        {

            Stop();

        }

    }

}
