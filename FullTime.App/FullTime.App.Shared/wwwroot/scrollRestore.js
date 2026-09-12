// Keeps each page's scroll offset in sessionStorage, keyed by the caller (page path + the params
// that change its content, e.g. the selected date). Blazor Hybrid/Server navigation swaps rendered
// content in place rather than doing a real browser navigation, so there's no built-in scroll
// restoration to lean on - this is what lets "back" land where you left off instead of at the top.
window.fullTimeScroll = {
    _handlers: {},

    attach: function (key) {
        this.detach(key);
        const handler = () => sessionStorage.setItem(key, window.scrollY.toString());
        window.addEventListener('scroll', handler, { passive: true });
        this._handlers[key] = handler;
    },

    detach: function (key) {
        const existing = this._handlers[key];
        if (existing) {
            window.removeEventListener('scroll', existing);
            delete this._handlers[key];
        }
    },

    restore: function (key) {
        const y = sessionStorage.getItem(key);
        if (y) {
            window.scrollTo(0, parseInt(y, 10));
        }
    },
};
