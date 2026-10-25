using System;

namespace Core.Librarys.Security
{
    public static class WebSocketSecurity
    {
        public const int MaxMessageLength = 65536;

        public static readonly int[] WebSocketPortCandidates = { 8908, 8910, 8912 };
        public static readonly int[] TokenPortCandidates = { 8909, 8911, 8913 };

        private static string _token = string.Empty;
        private static int _connectedClientCount;

        public static int ConnectedClientCount => _connectedClientCount;

        public static event EventHandler ExtensionConnectionChanged;

        public static int ActiveWebSocketPort { get; private set; } = WebSocketPortCandidates[0];
        public static int ActiveTokenPort { get; private set; } = TokenPortCandidates[0];
        public static bool IsServiceRunning { get; private set; }
        public static string LastStartError { get; private set; } = string.Empty;

        public static void SetToken(string token)
        {
            _token = token ?? string.Empty;
        }

        public static string GetToken()
        {
            return _token;
        }

        /// <summary>
        /// 浏览器扩展的 Origin 形如 chrome-extension://&lt;32 位 a-p&gt;（Firefox 是 UUID）。
        /// 只做格式校验、不绑定具体扩展 ID：未打包的开发扩展 ID 也是随机生成的，
        /// 写死 ID 会把本地加载的用户挡在外面。带路径/参数的 Origin 一律拒绝。
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex ExtensionOriginRegex =
            new System.Text.RegularExpressions.Regex(
                @"^(chrome|edge)-extension://[a-p]{32}/?$|^moz-extension://[0-9a-fA-F\-]{36}/?$",
                System.Text.RegularExpressions.RegexOptions.Compiled
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public static bool IsExtensionOrigin(string origin)
        {
            return !string.IsNullOrWhiteSpace(origin) && ExtensionOriginRegex.IsMatch(origin.Trim());
        }

        public static bool ValidateToken(string token)
        {
            return !string.IsNullOrEmpty(_token)
                && !string.IsNullOrEmpty(token)
                && string.Equals(_token, token, StringComparison.Ordinal);
        }

        public static void SetActivePorts(int webSocketPort, int tokenPort)
        {
            ActiveWebSocketPort = webSocketPort;
            ActiveTokenPort = tokenPort;
            IsServiceRunning = true;
            LastStartError = string.Empty;
            ExtensionConnectionChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void MarkStopped(string error = null)
        {
            IsServiceRunning = false;
            if (!string.IsNullOrWhiteSpace(error))
            {
                LastStartError = error;
            }

            ResetClientConnections();
            ExtensionConnectionChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void RegisterClientConnection()
        {
            System.Threading.Interlocked.Increment(ref _connectedClientCount);
            ExtensionConnectionChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void UnregisterClientConnection()
        {
            System.Threading.Interlocked.Decrement(ref _connectedClientCount);
            if (_connectedClientCount < 0)
            {
                _connectedClientCount = 0;
            }

            ExtensionConnectionChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void ResetClientConnections()
        {
            System.Threading.Interlocked.Exchange(ref _connectedClientCount, 0);
            ExtensionConnectionChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
