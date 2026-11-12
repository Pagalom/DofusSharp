(() => {
    const selector = 'details.job-filter[open], details.equipment-type-filter[open], details.analytics-menu[open]';
    document.addEventListener('pointerdown', event => {
        for (const disclosure of document.querySelectorAll(selector)) {
            if (!disclosure.contains(event.target)) disclosure.open = false;
        }
    });
    document.addEventListener('toggle', event => {
        const menu = event.target;
        if (!(menu instanceof HTMLDetailsElement) || !menu.matches('details.analytics-menu[open]')) return;
        for (const other of document.querySelectorAll('details.analytics-menu[open]')) {
            if (other !== menu) other.open = false;
        }
    }, true);
    document.addEventListener('click', event => {
        const action = event.target.closest?.('details.analytics-menu button');
        if (action) action.closest('details.analytics-menu').open = false;
    });
    const closingTrades = new WeakSet();
    document.addEventListener('scroll', event => {
        const card = document.querySelector('.trading-card[data-dismiss-on-scroll="true"]');
        if (!card || !card.getClientRects().length || !event.target.contains(card)) return;
        const close = card.querySelector('[data-close-trading-detail]');
        if (!close || closingTrades.has(close)) return;
        closingTrades.add(close);
        close.click();
    }, { capture: true, passive: true });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Tab') return;
        const panel = document.querySelector('.analytics-drawer[role="dialog"]');
        if (!panel) return;
        const controls = [...panel.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), summary, a[href]')]
            .filter(element => element.getClientRects().length > 0);
        const first = controls[0], last = controls.at(-1);
        if (!first) return;
        if (event.shiftKey && document.activeElement === first) { last.focus(); event.preventDefault(); }
        else if (!event.shiftKey && document.activeElement === last) { first.focus(); event.preventDefault(); }
    });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        const focused = document.activeElement?.closest(selector);
        const open = [...document.querySelectorAll(selector)];
        if (open.length === 0) return;
        for (const disclosure of open) disclosure.open = false;
        if (focused) focused.querySelector('summary')?.focus({ preventScroll: true });
        event.preventDefault();
    });
})();
