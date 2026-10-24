import { api } from './browser-api.js';
import {
    buildNotifyPayload,
    isTrackableUrl,
    resolveIconUrl,
    resolveTitle
} from './security.js';

const FOCUSED_CHECK_MS = 1000;
//  会话状态持久化键：SW 被回收时未结算的 activePage 存入 storage.session，
//  SW 重启后恢复，避免正在访问的页面时长丢失
const ACTIVE_PAGE_STORAGE_KEY = 'activePage';

export class TabTracker {
    /**
     * @param {import('./connection.js').GuageConnection} connection
     */
    constructor(connection) {
        this.connection = connection;
        this.isBrowserFocused = true;
        this.activePage = null;
    }

    async start() {
        //  从 storage.session 恢复上次未结算的会话（SW 被回收时状态不丢）
        try {
            const stored = await api.storage.session.get(ACTIVE_PAGE_STORAGE_KEY);
            const page = stored && stored[ACTIVE_PAGE_STORAGE_KEY];
            if (page && page.url && page.startTime && Date.now() - page.startTime < 3600 * 1000) {
                this.activePage = page;
            }
        } catch {
            //  恢复失败不影响主流程
        }

        api.tabs.onActivated.addListener((event) => {
            this.connection.ensureConnected();
            this.handleTabActivated(event.tabId);
        });

        api.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
            if (!tab.active) {
                return;
            }

            this.connection.ensureConnected();

            if (changeInfo.status === 'complete'
                || changeInfo.title
                || changeInfo.favIconUrl
                || changeInfo.url) {
                this.onActivePage(tab);
            }
        });

        this.startWatchFocus();
    }

    async handleTabActivated(tabId) {
        const tab = await this.getTab(tabId);
        if (tab) {
            this.onActivePage(tab);
        }
    }

    getTab(tabId) {
        return new Promise((resolve) => {
            api.tabs.get(tabId, (tab) => {
                if (api.runtime.lastError) {
                    resolve(null);
                    return;
                }

                resolve(tab);
            });
        });
    }

    isFocused() {
        return new Promise((resolve) => {
            api.windows.getCurrent((window_) => {
                resolve(Boolean(window_?.focused));
            });
        });
    }

    getCurrentTab() {
        return new Promise((resolve, reject) => {
            api.tabs.query({ active: true, currentWindow: true }, (tabs) => {
                if (tabs?.length > 0) {
                    resolve(tabs[0]);
                    return;
                }

                reject(new Error('no active tab'));
            });
        });
    }

    /**
     * 读取页面母图标（apple-touch-icon 等）需要 scripting + 全站 host 权限，
     * 权限代价大于收益，已移除；图标统一取 favIconUrl，缺失时回退 origin/favicon.ico。
     */
    async onActivePage(tab) {
        if (this.connection.sleeping || !tab) {
            return;
        }

        if (!isTrackableUrl(tab.url)) {
            if (this.activePage) {
                this.calDuration();
            }

            this.activePage = null;
            return;
        }

        if (this.activePage?.url === tab.url) {
            await this.refreshActiveMetadata(tab);
            return;
        }

        if (this.activePage?.url) {
            this.calDuration();
        }

        await this.setActive(tab);
    }

    async refreshActiveMetadata(tab) {
        if (!this.activePage) {
            return;
        }

        this.activePage.title = resolveTitle(tab) || this.activePage.title;
        this.activePage.icon = resolveIconUrl(tab) || this.activePage.icon;
    }

    async setActive(tab) {
        const url = tab.url;
        if (!isTrackableUrl(url)) {
            this.activePage = null;
            return;
        }

        this.activePage = {
            url,
            title: resolveTitle(tab),
            icon: resolveIconUrl(tab),
            startTime: Date.now()
        };
        this.persistActivePage();
    }

    persistActivePage() {
        if (!this.activePage) {
            return;
        }

        try {
            api.storage.session.set({ [ACTIVE_PAGE_STORAGE_KEY]: this.activePage });
        } catch {
            //  忽略持久化失败
        }
    }

    calDuration() {
        if (!this.activePage?.url) {
            return;
        }

        const duration = Math.floor((Date.now() - this.activePage.startTime) / 1000);
        //  亚秒级切页（时长为 0）：直接丢弃会话，避免无效上报
        if (duration <= 0) {
            this.activePage = null;
            try {
                api.storage.session.remove(ACTIVE_PAGE_STORAGE_KEY);
            } catch {
                //  忽略清理失败
            }
            return;
        }
        const activeTime = Math.floor(this.activePage.startTime / 1000);
        // 本次结算的唯一标识：断线重发时载荷不变，服务端按 MsgId 幂等去重
        const msgId = (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function')
            ? crypto.randomUUID()
            : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
        const payload = buildNotifyPayload({
            url: this.activePage.url,
            title: this.activePage.title,
            icon: this.activePage.icon,
            duration,
            activeTime,
            msgId
        });

        this.activePage = null;
        try {
            api.storage.session.remove(ACTIVE_PAGE_STORAGE_KEY);
        } catch {
            //  忽略清理失败
        }

        if (payload) {
            this.connection.notifyGuage(payload);
        }
    }

    startWatchFocus() {
        setInterval(async () => {
            this.connection.ensureConnected();
            const focused = await this.isFocused();
            if (focused) {
                if (!this.isBrowserFocused) {
                    this.isBrowserFocused = true;
                    try {
                        const tab = await this.getCurrentTab();
                        await this.onActivePage(tab);
                    } catch {
                        // ignore
                    }
                }

                return;
            }

            if (this.isBrowserFocused) {
                this.isBrowserFocused = false;
                this.calDuration();
            }
        }, FOCUSED_CHECK_MS);
    }

    bindSleepHandlers() {
        this.connection.onSleep = () => this.calDuration();
        this.connection.onWake = async () => {
            try {
                const tab = await this.getCurrentTab();
                await this.setActive(tab);
            } catch {
                // ignore
            }
        };
    }
}
