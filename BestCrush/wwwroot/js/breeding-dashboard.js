const listeners = new WeakMap();
export function attach(root, reference) {
    detach(root);
    const dismiss = () => reference.invokeMethodAsync('DismissMount').catch(() => {});
    const pointer = event => {
        if (!root.querySelector('[data-breeding-open]')) return;
        if (!(event.target instanceof Element)) return;
        if (event.target.closest('[data-breeding-open], [data-breeding-mount-tile], .tooltip')) return;
        dismiss();
    };
    const keyboard = event => {
        if (event.key === 'Escape' && root.querySelector('[data-breeding-open]')) {
            const card = root.querySelector('[data-breeding-open]')?.closest('.breeding-enclosure');
            dismiss();
            setTimeout(() => card?.querySelector('.breeding-mount-tile')?.focus(), 0);
        }
    };
    document.addEventListener('pointerdown', pointer, true);
    document.addEventListener('keydown', keyboard);
    listeners.set(root, { pointer, keyboard });
}
export function detach(root) {
    const handlers = listeners.get(root);
    if (!handlers) return;
    document.removeEventListener('pointerdown', handlers.pointer, true);
    document.removeEventListener('keydown', handlers.keyboard);
    listeners.delete(root);
}
export function revealAction(root, id) {
    if (!id || !/^[a-f0-9]{32}$/i.test(id)) return;
    const target = root.querySelector(`[data-breeding-action="${id}"]`);
    if (!target) return;
    for (let parent = target.parentElement; parent && parent !== root; parent = parent.parentElement)
        if (parent instanceof HTMLDetailsElement) parent.open = true;
    target.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'center' });
}
