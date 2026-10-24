/** 与 更矩 服务端 WebSocketSecurity / UrlSecurity 保持一致的上限。 */
export const MAX_MESSAGE_LENGTH = 65536;
export const MAX_TITLE_LENGTH = 500;
export const MAX_URL_LENGTH = 2048;
export const MIN_NOTIFY_INTERVAL_MS = 250;

const BLOCKED_SCHEMES = new Set([
    'chrome:',
    'chrome-extension:',
    'chrome-devtools:',
    'edge:',
    'extension:',
    'devtools:',
    'about:',
    'data:',
    'javascript:',
    'file:',
    'blob:',
    'view-source:',
    'vbscript:'
]);

const LOCAL_HOSTS = new Set(['localhost', '127.0.0.1', '0.0.0.0', '[::1]']);

/**
 * 是否允许统计的页面 URL（仅 http/https）。
 */
export function isTrackableUrl(url) {
    if (!url || typeof url !== 'string') {
        return false;
    }

    const trimmed = url.trim();
    if (!trimmed || trimmed.length > MAX_URL_LENGTH) {
        return false;
    }

    let parsed;
    try {
        parsed = new URL(trimmed);
    } catch {
        return false;
    }

    const scheme = parsed.protocol.toLowerCase();
    if (scheme !== 'http:' && scheme !== 'https:') {
        return false;
    }

    if (BLOCKED_SCHEMES.has(scheme)) {
        return false;
    }

    return true;
}

/**
 * 仅允许连接本机 更矩 WebSocket / 令牌服务。
 */
export function isAllowedLocalPort(port) {
    return port === 8908 || port === 8910 || port === 8912
        || port === 8909 || port === 8911 || port === 8913;
}

export function buildLocalWebSocketUrl(port) {
    if (!isAllowedLocalPort(port)) {
        throw new Error('非法 WebSocket 端口');
    }

    return `ws://127.0.0.1:${port}/GuageWebSentry`;
}

export function buildLocalTokenUrl(port, path) {
    if (!isAllowedLocalPort(port)) {
        throw new Error('非法令牌服务端口');
    }

    const safePath = path.startsWith('/') ? path : `/${path}`;
    return `http://127.0.0.1:${port}${safePath}`;
}

export function sanitizeTitle(title) {
    if (!title || typeof title !== 'string') {
        return '';
    }

    return title
        .replace(/[\u0000-\u001f\u007f]/g, '')
        .trim()
        .slice(0, MAX_TITLE_LENGTH);
}

function normalizeHost(hostname) {
    return (hostname || '').replace(/^\[|\]$/g, '').toLowerCase();
}

function isLocalHost(hostname) {
    return LOCAL_HOSTS.has(normalizeHost(hostname));
}

/**
 * favicon 安全校验：公网 URL 或与本页同域。
 */
export function isSafeIconUrl(iconUrl, pageUrl) {
    if (!iconUrl || typeof iconUrl !== 'string') {
        return false;
    }

    let icon;
    let page;
    try {
        icon = new URL(iconUrl.trim());
        page = new URL(pageUrl.trim());
    } catch {
        return false;
    }

    if (icon.protocol !== 'http:' && icon.protocol !== 'https:') {
        return false;
    }

    if (!isLocalHost(icon.hostname)) {
        return true;
    }

    return normalizeHost(icon.hostname) === normalizeHost(page.hostname)
        && icon.port === page.port;
}

export function resolveIconUrl(tab) {
    if (!tab?.url || !isTrackableUrl(tab.url)) {
        return '';
    }

    if (tab.favIconUrl && isSafeIconUrl(tab.favIconUrl, tab.url)) {
        return tab.favIconUrl;
    }

    try {
        const page = new URL(tab.url);
        return `${page.origin}/favicon.ico`;
    } catch {
        return '';
    }
}

export function resolveTitle(tab) {
    return sanitizeTitle(tab?.title || '');
}

/**
 * 构造上报载荷并做长度校验。
 * msgId：本次结算会话的唯一标识，断线重发时保持不变，服务端据此幂等去重。
 */
export function buildNotifyPayload({ url, title, icon, duration, activeTime, token, msgId }) {
    if (!isTrackableUrl(url)) {
        return null;
    }

    const payload = {
        Url: url.trim(),
        Title: sanitizeTitle(title),
        Icon: typeof icon === 'string' ? icon.trim() : '',
        Duration: Math.max(0, Math.min(Number(duration) || 0, 86400)),
        ActiveTime: Math.max(0, Number(activeTime) || 0)
    };

    if (token) {
        payload.Token = token;
    }

    if (msgId) {
        payload.MsgId = typeof msgId === 'string' ? msgId.slice(0, 64) : '';
    }

    if (payload.Icon && !isSafeIconUrl(payload.Icon, payload.Url)) {
        payload.Icon = resolveIconUrl({ url: payload.Url, favIconUrl: '' });
    }

    const json = JSON.stringify(payload);
    if (json.length > MAX_MESSAGE_LENGTH) {
        payload.Title = payload.Title.slice(0, Math.max(0, MAX_TITLE_LENGTH - 64));
        const retry = JSON.stringify(payload);
        if (retry.length > MAX_MESSAGE_LENGTH) {
            return null;
        }
    }

    return payload;
}
