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
