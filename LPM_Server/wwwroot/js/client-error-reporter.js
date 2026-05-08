// Client-side global error reporter — POSTs uncaught JS errors and unhandled promise
// rejections to the server so they show up in the same log file as server errors.
//
// Filters applied to keep noise low:
//  • Origin filter: skip errors whose source isn't from this app's origin (cuts ~all
//    browser-extension noise like password managers, ad-blockers, translators, etc.)
//  • 2-second throttle: outer gate; prevents a runaway looping error from spamming
//  • Message-hash dedup: same error logged at most once per DEDUP_MS (10 min default).
//    Memory bounded by pruning entries older than DEDUP_MS on every report attempt.
//  • Self-protection: the reporter never throws — fetch failures are swallowed silently
//    so a network blip can't recursively trigger more errors.
(function () {
    if (window.__lpmErrorReporterInstalled) return;
    window.__lpmErrorReporterInstalled = true;

    var lastSent = 0;
    var THROTTLE_MS = 2000;
    var DEDUP_MS    = 10 * 60 * 1000;     // 10 min — same error logs at most once per window
    var seen        = Object.create(null); // key (first 200 chars of message) → last-seen timestamp

    // Known-benign error patterns — these are framework-internal and produce no user-visible
    // effect. Filtering them at the source keeps the log focused on actionable issues.
    // To add another known-noise pattern: append a substring (case-sensitive) to this list.
    var BENIGN_PATTERNS = [
        // Blazor Server disposal race: a JS listener fires after its DotNetObjectReference
        // was disposed (e.g. component unmounted on page navigation). Harmless — Blazor
        // catches it and the missed event would have been a no-op anyway.
        'There is no tracked object with id',
        'DotNetObjectReference instance was already disposed'
    ];

    function shouldSkip(filename) {
        // Empty filename usually means a third-party / extension. Errors with a real
        // filename from another origin (chrome-extension://, https://cdn.other.com, etc.)
        // are also not ours to fix.
        if (!filename) return true;
        try {
            return !filename.startsWith(location.origin);
        } catch (e) {
            return true;
        }
    }

    function pruneSeen(now) {
        for (var k in seen) {
            if (now - seen[k] > DEDUP_MS) delete seen[k];
        }
    }

    function isBenign(message) {
        if (!message) return false;
        for (var i = 0; i < BENIGN_PATTERNS.length; i++) {
            if (message.indexOf(BENIGN_PATTERNS[i]) !== -1) return true;
        }
        return false;
    }

    function report(payload) {
        // Drop known-benign framework noise BEFORE throttle/dedup — saves the throttle slot
        // for actionable errors and keeps these out of the log entirely.
        if (isBenign(payload.message)) return;

        var now = Date.now();
        // Outer throttle: prevents a thrashing loop from doing work even before the dedup check.
        if (now - lastSent < THROTTLE_MS) return;

        // Dedup by *normalized* message — collapse runs of digits to "N" so the SAME error
        // logged with different per-event values (scroll positions, byte offsets, line numbers)
        // hashes to the SAME key. Without this, "BytePositionInLine: 3" and "BytePositionInLine: 5"
        // count as distinct errors and the dedup window doesn't catch repeats.
        var key = String(payload.message || '')
            .slice(0, 200)
            .replace(/\d+/g, 'N');
        pruneSeen(now);
        if (seen[key] && now - seen[key] < DEDUP_MS) return;
        seen[key] = now;

        lastSent = now;

        try {
            fetch('/api/client-error', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(Object.assign({
                    url: location.href,
                    userAgent: navigator.userAgent,
                    when: new Date().toISOString()
                }, payload)),
                keepalive: true   // best-effort delivery even during page unload
            }).catch(function () { /* swallow — never let the reporter itself throw */ });
        } catch (e) { /* swallow */ }
    }

    window.addEventListener('error', function (e) {
        if (shouldSkip(e.filename)) return;
        report({
            kind:    'error',
            message: e.message,
            source:  e.filename,
            line:    e.lineno,
            col:     e.colno,
            stack:   (e.error && e.error.stack) ? String(e.error.stack).slice(0, 4000) : null
        });
    });

    window.addEventListener('unhandledrejection', function (e) {
        var reason = e.reason;
        var stack  = (reason && reason.stack) ? String(reason.stack).slice(0, 4000) : null;
        // Skip rejections we can't attribute to our own scripts (no stack, or stack
        // doesn't reference our origin)
        if (!stack || stack.indexOf(location.origin) === -1) return;
        report({
            kind:    'unhandledrejection',
            message: reason ? String(reason.message || reason) : 'unknown',
            stack:   stack
        });
    });
})();
