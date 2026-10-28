import { GuageConnection } from './lib/connection.js';
import { TabTracker } from './lib/tab-tracker.js';

const connection = new GuageConnection();
const tracker = new TabTracker(connection);

tracker.bindSleepHandlers();
tracker.start();
connection.init();

console.log('[网页统计] 已加载');
