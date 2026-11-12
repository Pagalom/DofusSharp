const views = new Map();
export function capture(name) {
    views.set(name, { x: window.scrollX, y: window.scrollY, focus: document.activeElement });
}
export function restore(name) {
    const view = views.get(name);
    requestAnimationFrame(() => {
        if (view?.focus?.isConnected && !view.focus.closest('[hidden]'))
            view.focus.focus({ preventScroll: true });
        window.scrollTo({ left: view?.x ?? 0, top: view?.y ?? 0, behavior: 'instant' });
    });
}
export function scrollToTop() { window.scrollTo({ top: 0, behavior: 'instant' }); }
