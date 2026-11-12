export function observe(element, receiver) {
    let disposed = false, previous = 0, frame = 0;
    const measure = () => {
        cancelAnimationFrame(frame);
        frame = requestAnimationFrame(() => {
            if (disposed || !element.isConnected) return;
            const width = element.clientWidth;
            if (width <= 0 || Math.abs(width-previous) < 1) return;
            previous = width;
            receiver.invokeMethodAsync('ResizeChart', width).catch(() => {});
        });
    };
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    measure();
    return { dispose() { disposed = true; cancelAnimationFrame(frame); observer.disconnect(); } };
}
