// Page-first vertical scroll for a nested scrollable container, fully symmetric:
//
//   DOWN — page scrolls until the container's TOP hits the viewport TOP.
//          Then the container scrolls internally until its internal bottom.
//          Then the page scrolls again.
//
//   UP   — page scrolls until the container's BOTTOM hits the viewport BOTTOM.
//          Then the container scrolls internally (up) until its internal top.
//          Then the page scrolls again.
//
// Horizontal wheel events are passed through so the matrix's horizontal scroll
// still works.

(function () {
    if (window.lpmAttachMatrixScroll) return;

    const attached = new WeakSet();

    window.lpmAttachMatrixScroll = function (containerId) {
        const el = document.getElementById(containerId);
        if (!el || attached.has(el)) return;
        attached.add(el);

        el.addEventListener('wheel', function (e) {
            // Ignore mostly-horizontal wheel events
            if (Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;

            const dy = e.deltaY;
            if (dy === 0) return;

            // Trackpad detection — pixel-mode events with small per-tick deltas.
            // For trackpads we must let the browser's native scroll chain run: manually
            // preventDefault-ing + scrollBy-ing on every micro-event interrupts the OS
            // momentum accumulation and makes scrolling feel stuck on Mac touchpads.
            // Mouse wheels (even high-DPI) produce per-tick deltas well above this
            // threshold, or use deltaMode !== 0 (line/page), so they still get the
            // custom page-first UX below.
            if (e.deltaMode === 0 && Math.abs(dy) < 50) return;

            const rect = el.getBoundingClientRect();
            const vh   = window.innerHeight;
            const atInternalTop    = el.scrollTop <= 0;
            const atInternalBottom = Math.ceil(el.scrollTop + el.clientHeight) >= el.scrollHeight;

            if (dy > 0) {
                // DOWN
                if (rect.top > 0) {
                    // Matrix top still below viewport top → scroll page
                    e.preventDefault();
                    window.scrollBy({ top: dy, behavior: 'auto' });
                } else if (atInternalBottom) {
                    // Matrix exhausted internally → continue page scroll
                    e.preventDefault();
                    window.scrollBy({ top: dy, behavior: 'auto' });
                }
                // else: matrix scrolls internally (default)
            } else {
                // UP — mirror of DOWN with top↔bottom
                if (rect.bottom < vh) {
                    // Matrix bottom still above viewport bottom → scroll page
                    e.preventDefault();
                    window.scrollBy({ top: dy, behavior: 'auto' });
                } else if (atInternalTop) {
                    // Matrix at internal top → continue page scroll
                    e.preventDefault();
                    window.scrollBy({ top: dy, behavior: 'auto' });
                }
                // else: matrix scrolls internally (default — reveals earlier rows)
            }
        }, { passive: false });
    };
})();
