// Entry point: registers dayjs plugins (classic-script globals), boots the store, mounts the app.

import { createApp } from './vue.esm-browser.prod.js';
import { App } from './components.js';
import { initStore } from './store.js';

if (window.dayjs) {
    if (window.dayjs_plugin_relativeTime) window.dayjs.extend(window.dayjs_plugin_relativeTime);
    if (window.dayjs_plugin_duration) window.dayjs.extend(window.dayjs_plugin_duration);
    if (window.dayjs_plugin_localizedFormat) window.dayjs.extend(window.dayjs_plugin_localizedFormat);
}

initStore();
createApp(App).mount('#app');
