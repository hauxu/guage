using Core.Event;
using Core.Librarys.Browser;
using Core.Librarys.Security;
using Core.Models.WebPage;
using Newtonsoft.Json;
using System;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Core.Servicers.Instances
{
    internal sealed class GuageWebSocketBehavior : WebSocketBehavior
    {
        public GuageWebSocketBehavior()
        {
            //  握手 Origin 白名单：仅允许浏览器扩展（或未携带 Origin 的本机客户端）连接，
            //  拒绝任意网页发起的 WebSocket 连接（网页无法伪造 chrome-extension:// 来源）。
            OriginValidator = IsAllowedOrigin;
        }

        private static bool IsAllowedOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin))
            {
                //  本机非浏览器客户端（无 Origin 头）
                return true;
            }

            return origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("edge-extension://", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase);
        }

        protected override void OnOpen()
        {
            WebSocketSecurity.RegisterClientConnection();
            base.OnOpen();
        }

        protected override void OnClose(CloseEventArgs e)
        {
            WebSocketSecurity.UnregisterClientConnection();
            base.OnClose(e);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(e.Data))
                {
                    return;
                }

                if (string.Equals(e.Data, "ping", StringComparison.Ordinal))
                {
                    return;
                }

                if (e.Data.Length > WebSocketSecurity.MaxMessageLength)
                {
                    Core.Librarys.Logger.Error("WebSocket 消息过长，已拒绝。");
                    return;
                }

                NotifyWeb log = JsonConvert.DeserializeObject<NotifyWeb>(e.Data);
                if (log == null)
                {
                    return;
                }

                if (!WebSocketSecurity.ValidateToken(log.Token))
                {
                    Send("error:invalid_token");
                    Core.Librarys.Logger.Error("WebSocket 令牌无效，已拒绝消息。请确认浏览器插件为最新版，或重启 更矩 后重试。");
                    return;
                }

                //  MsgId 长度防御：超长标识不参与幂等去重
                if (!string.IsNullOrEmpty(log.MsgId) && log.MsgId.Length > 64)
                {
                    log.MsgId = null;
                }

                if (!UrlSecurity.TryCreateSafeHttpUri(log.Url, out _))
                {
                    Core.Librarys.Logger.Error("WebSocket 收到非法 URL，已拒绝。");
                    return;
                }

                WebSocketEvent.Invoke(log);
            }
            catch (JsonException ex)
            {
                Core.Librarys.Logger.Error("WebSocket 消息解析失败：" + ex.Message);
            }
            catch (Exception ex)
            {
                Core.Librarys.Logger.Error(ex.ToString());
            }
        }
    }
}
