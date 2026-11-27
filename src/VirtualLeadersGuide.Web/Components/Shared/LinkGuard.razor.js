// Listens on window in the capture phase so it runs before Blazor's own link handling, which leaves an
// event alone once it is defaultPrevented. The marker's attribute is read per click, so no interop is
// needed when the page goes from clean to unsaved.
export function watch(marker, handler) {
    function onClick(event) {
        if (marker.dataset.linkGuard !== 'on' || event.defaultPrevented || event.button !== 0
            || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
            return;
        }

        const link = event.target.closest?.('a[href]');
        if (!link || link.hasAttribute('download') || (link.target && link.target !== '_self')
            || link.origin !== location.origin) {
            return;
        }

        if (link.hash && link.pathname === location.pathname && link.search === location.search) {
            return;
        }

        event.preventDefault();
        handler.invokeMethodAsync('Follow', link.href);
    }

    window.addEventListener('click', onClick, true);
    return { dispose: () => window.removeEventListener('click', onClick, true) };
}
