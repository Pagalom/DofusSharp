window.overlayMotion = {
    attach(root, receiver) {
        if (root.__overlayMotion) return;
        let drag = null;
        const down = e => {
            const handle = e.target.closest('[data-overlay-hit]');
            if (!handle || !root.contains(handle) || e.button !== 0 || e.target.closest('button,input,a,summary')) return;
            e.preventDefault();
            handle.setPointerCapture(e.pointerId);
            const active = drag = { handle, pointer: e.pointerId, latest: null, sending: false, ended: false };
            active.ready = receiver.invokeMethodAsync('StartMotion', Number(handle.dataset.overlayHit), e.clientX, e.clientY, window.devicePixelRatio || 1)
                .then(native => active.native = native === true).catch(() => active.native = false);
        };
        const flush = async active => {
            if (active.sending) return;
            active.sending = true;
            try {
                await active.ready;
                // Windows follows the native cursor at 60 Hz. The fallback keeps only
                // the newest position instead of replaying a backlog of mouse events.
                while (!active.native && active.latest) {
                    const point = active.latest; active.latest = null;
                    await receiver.invokeMethodAsync('MoveMotion', ...point);
                }
                if (active.ended) await receiver.invokeMethodAsync('FinishMotion');
            } catch (_) {} finally { active.sending = false; }
        };
        const move = e => {
            if (!drag || drag.pointer !== e.pointerId) return;
            drag.latest = [e.screenX, e.screenY];
            void flush(drag);
        };
        const up = e => {
            if (!drag || drag.pointer !== e.pointerId) return;
            const active = drag; drag = null;
            active.latest = [e.screenX, e.screenY]; active.ended = true;
            if (active.handle.hasPointerCapture(e.pointerId)) active.handle.releasePointerCapture(e.pointerId);
            void flush(active);
        };
        root.addEventListener('pointerdown', down);
        root.addEventListener('pointermove', move);
        root.addEventListener('pointerup', up);
        root.addEventListener('pointercancel', up);
        root.__overlayMotion = () => {
            if (drag) {
                const active = drag; drag = null; active.ended = true;
                if (active.handle.hasPointerCapture(active.pointer)) active.handle.releasePointerCapture(active.pointer);
                void flush(active);
            }
            root.removeEventListener('pointerdown', down);
            root.removeEventListener('pointermove', move);
            root.removeEventListener('pointerup', up);
            root.removeEventListener('pointercancel', up);
            delete root.__overlayMotion;
        };
    },
    detach(root) { root?.__overlayMotion?.(); }
};
