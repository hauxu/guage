import {
    buildLocalTokenUrl,
    buildLocalWebSocketUrl,
    MIN_NOTIFY_INTERVAL_MS
} from './security.js';

const TEN_SECONDS_MS = 10 * 1000;
const RENOTIFY_MS = 10 * 1000;
const RECONNECTFAIL_SLEEP = 5;
const MAX_RECONNECT_FAILS = 60;

const WS_PORTS = [8908, 8910, 8912];
const TOKEN_PORTS = [8909, 8911, 8913];

export class GuageConnection {
    constructor() {
        this.webSocket = null;
        this.wsToken = '';
        this.wsPort = WS_PORTS[0];
        this.tokenPort = TOKEN_PORTS[0];
        this.isConnected = false;
        this.isSleep = false;
        this.reconnectFail = 0;
        this.autoReConnectIntervalId = null;
        this.keepAliveIntervalId = null;
        this.notifyFailList = [];
        this.lastNotifyAt = 0;
        this.connecting = false;
    }

    async init() {
        await this.connect();
        this.startRenotify();
    }

    async loadConnectionConfig() {
        for (let i = 0; i < TOKEN_PORTS.length; i++) {
            const port = TOKEN_PORTS[i];
            try {
                const response = await fetch(buildLocalTokenUrl(port, '/browser-ws-config'));
                if (response.ok) {
                    const cfg = await response.json();
                    this.wsToken = (cfg.token || '').trim();
                    this.wsPort = cfg.wsPort || WS_PORTS[Math.min(i, WS_PORTS.length - 1)];
                    this.tokenPort = cfg.tokenPort || port;
                    return true;
                }
            } catch (error) {
                console.warn(`[guage] 无法读取连接配置（端口 ${port}）`, error);
            }

            try {
                const response = await fetch(buildLocalTokenUrl(port, '/browser-ws-token'));
                if (response.ok) {
                    this.wsToken = (await response.text()).trim();
                    this.wsPort = WS_PORTS[Math.min(i, WS_PORTS.length - 1)];
                    this.tokenPort = port;
                    return true;
                }
            } catch (error) {
                console.warn(`[guage] 无法读取 WebSocket 令牌（端口 ${port}）`, error);
            }
        }

        this.wsToken = '';
        return false;
    }

    get sleeping() {
        return this.isSleep;
    }

    canConnect() {
        const state = this.webSocket?.readyState;
        return !this.connecting
            && state !== WebSocket.OPEN
            && state !== WebSocket.CONNECTING;
    }

    ensureConnected() {
        if (!this.isConnected && this.canConnect()) {
            this.connect();
        }
    }

    async handleTokenRotation() {
        const oldToken = this.wsToken;
        await this.loadConnectionConfig();
        if (this.wsToken && this.wsToken === oldToken) {
            // 令牌未变化：说明是服务端竞态或旧令牌，避免陷入无效的重连循环
            console.warn('[guage] 令牌未变化，跳过重连');
            return;
        }

        console.warn('[guage] 令牌已更新，正在重新连接...');
        await this.reconnectWithNewToken();
    }

    async reconnectWithNewToken() {
        const oldSocket = this.webSocket;
        this.webSocket = null;
        this.isConnected = false;
        this.stopKeepAlive();

        if (oldSocket) {
            oldSocket.onclose = null;
            oldSocket.onerror = null;
            oldSocket.onmessage = null;
            try {
                oldSocket.close();
            } catch {
                // ignore
            }
        }

        clearInterval(this.autoReConnectIntervalId);
        await this.connect();
    }

    async connect() {
        if (!this.canConnect()) {
            return;
        }

        this.connecting = true;
        try {
            await this.loadConnectionConfig();
            const wsUrl = buildLocalWebSocketUrl(this.wsPort);

            const oldSocket = this.webSocket;
            this.webSocket = null;
            if (oldSocket) {
                oldSocket.onclose = null;
                oldSocket.onerror = null;
                try {
                    oldSocket.close();
                } catch {
                    // ignore
                }
            }

            const socket = new WebSocket(wsUrl);
            this.webSocket = socket;

            socket.onopen = () => {
                if (this.webSocket !== socket) {
                    return;
                }

                this.isConnected = true;
                this.isSleep = false;
                this.reconnectFail = 0;
                clearInterval(this.autoReConnectIntervalId);
                this.startKeepAlive();
                console.log(`[guage] 已连接 ${wsUrl}`);
            };

            socket.onmessage = (event) => {
                if (this.webSocket !== socket) {
                    return;
                }

                const data = event.data;
                if (data === 'error:invalid_token' || data === 'token_rotated') {
                    this.handleTokenRotation();
                    return;
                }

                if (data === 'sleep') {
                    this.isSleep = true;
                    this.onSleep?.();
                    console.log('[guage] 进入睡眠');
                    return;
                }

                if (data === 'wake') {
                    this.isSleep = false;
                    this.onWake?.();
                    console.log('[guage] 已唤醒');
                }
            };

            socket.onclose = () => {
                if (this.webSocket !== socket) {
                    return;
                }

                this.isConnected = false;
                this.stopKeepAlive();
                console.warn('[guage] 与客户端失去连接');
                this.webSocket = null;
                this.startAutoReConnect();
            };

            socket.onerror = () => {
                // onclose 会跟进重连
            };
        } finally {
            this.connecting = false;
        }
    }

    startAutoReConnect() {
        clearInterval(this.autoReConnectIntervalId);
        this.autoReConnectIntervalId = setInterval(() => {
            if (!this.isConnected && this.canConnect()) {
                if (this.reconnectFail >= MAX_RECONNECT_FAILS) {
                    // 达到重连上限后停止无休止重试，等待下一次页面事件触发 ensureConnected
                    return;
                }

                console.log('[guage] 尝试重新连接...');
                this.connect();
                this.reconnectFail++;
                if (this.reconnectFail >= RECONNECTFAIL_SLEEP && !this.isSleep) {
                    this.isSleep = true;
                }
            }
        }, TEN_SECONDS_MS);
    }

    startKeepAlive() {
        this.stopKeepAlive();
        this.keepAliveIntervalId = setInterval(() => {
            if (this.isConnected && this.webSocket?.readyState === WebSocket.OPEN) {
                this.webSocket.send('ping');
            } else {
                this.stopKeepAlive();
            }
        }, TEN_SECONDS_MS);
    }

    stopKeepAlive() {
        clearInterval(this.keepAliveIntervalId);
        this.keepAliveIntervalId = null;
    }

    startRenotify() {
        setInterval(async () => {
            if (this.isConnected && this.webSocket && this.notifyFailList.length > 0) {
                const item = this.notifyFailList.shift();
                await this.notifyGuage(item);
            }
        }, RENOTIFY_MS);
    }

    async notifyGuage(payload) {
        if (!payload) {
            return false;
        }

        const now = Date.now();
        if (now - this.lastNotifyAt < MIN_NOTIFY_INTERVAL_MS) {
            await new Promise((resolve) => setTimeout(resolve, MIN_NOTIFY_INTERVAL_MS));
        }

        if (this.isConnected && this.webSocket?.readyState === WebSocket.OPEN) {
            if (!this.wsToken) {
                await this.loadConnectionConfig();
            }

            if (this.wsToken) {
                payload.Token = this.wsToken;
            }

            this.webSocket.send(JSON.stringify(payload));
            this.lastNotifyAt = Date.now();
            return true;
        }

        this.notifyFailList.push(payload);
        return false;
    }
}
