// DragDropTouch — minimal polyfill translating touch events into HTML5
// drag/drop events so Blazor @ondragstart/@ondragover/@ondrop handlers
// work on tablets and phones. No external deps. Auto-initializes on load.
(function () {
    'use strict';
    if (!('ontouchstart' in window)) return;

    const MOVE_THRESHOLD = 5;   // px before a pending long-press is cancelled
    const HOLD_DELAY     = 300; // ms long-press to start drag
    const DRAG_OPACITY   = 0.7;

    let dragSrc      = null;
    let dragImg      = null;
    let pendingDrag  = null;
    let startPos     = null;
    let lastTarget   = null;
    const data       = new Map();

    function makeDataTransfer() {
        return {
            effectAllowed: 'all',
            dropEffect:    'move',
            types: [],
            files: [],
            setData:      (k, v) => data.set(k, v),
            getData:      k      => data.get(k) || '',
            clearData:    ()     => data.clear(),
            setDragImage: ()     => {}
        };
    }

    function dispatch(type, target, touch) {
        let evt;
        try {
            evt = new DragEvent(type, { bubbles: true, cancelable: true });
        } catch (_) {
            evt = new Event(type, { bubbles: true, cancelable: true });
        }
        // Blazor's event bridge reads these standard fields. Each assignment is wrapped
        // because some browsers mark DragEvent coordinate properties as read-only.
        ['clientX','clientY','pageX','pageY','screenX','screenY'].forEach(k => {
            try { Object.defineProperty(evt, k, { value: touch[k], configurable: true }); } catch (_) {}
        });
        try { Object.defineProperty(evt, 'dataTransfer', { value: makeDataTransfer(), configurable: true }); } catch (_) {}
        target.dispatchEvent(evt);
        return evt;
    }

    function elementUnder(x, y) {
        // Hide drag image so it doesn't intercept the hit test.
        let prevDisplay = null;
        if (dragImg) { prevDisplay = dragImg.style.display; dragImg.style.display = 'none'; }
        const el = document.elementFromPoint(x, y);
        if (dragImg) { dragImg.style.display = prevDisplay || ''; }
        return el;
    }

    function startDrag(src, touch) {
        dragSrc = src;
        const evt = dispatch('dragstart', src, touch);
        if (evt.defaultPrevented) { dragSrc = null; return false; }

        // Floating clone as a visual drag indicator.
        const rect = src.getBoundingClientRect();
        dragImg = src.cloneNode(true);
        dragImg.style.position      = 'fixed';
        dragImg.style.pointerEvents = 'none';
        dragImg.style.opacity       = DRAG_OPACITY;
        dragImg.style.zIndex        = '10000';
        dragImg.style.width         = rect.width  + 'px';
        dragImg.style.height        = rect.height + 'px';
        dragImg.style.left          = (touch.clientX - rect.width  / 2) + 'px';
        dragImg.style.top           = (touch.clientY - rect.height / 2) + 'px';
        dragImg.style.boxShadow     = '0 6px 18px rgba(0,0,0,.35)';
        dragImg.style.transform     = 'scale(1.02)';
        document.body.appendChild(dragImg);
        return true;
    }

    function onTouchStart(e) {
        if (e.touches.length !== 1) return;
        const t = e.touches[0];
        const src = t.target.closest && t.target.closest('[draggable="true"]');
        if (!src) return;
        startPos = { x: t.clientX, y: t.clientY };
        if (pendingDrag) clearTimeout(pendingDrag);
        pendingDrag = setTimeout(() => {
            pendingDrag = null;
            startDrag(src, t);
        }, HOLD_DELAY);
    }

    function onTouchMove(e) {
        if (!pendingDrag && !dragSrc) return;
        const t = e.touches[0];

        if (pendingDrag) {
            const dx = Math.abs(t.clientX - startPos.x);
            const dy = Math.abs(t.clientY - startPos.y);
            if (dx > MOVE_THRESHOLD || dy > MOVE_THRESHOLD) {
                clearTimeout(pendingDrag);
                pendingDrag = null;
            }
            return; // let scroll proceed
        }

        // Active drag — stop the page from scrolling under the finger.
        e.preventDefault();
        if (dragImg) {
            dragImg.style.left = (t.clientX - dragImg.offsetWidth  / 2) + 'px';
            dragImg.style.top  = (t.clientY - dragImg.offsetHeight / 2) + 'px';
        }
        const target = elementUnder(t.clientX, t.clientY);
        if (target !== lastTarget) {
            if (lastTarget) dispatch('dragleave', lastTarget, t);
            if (target)     dispatch('dragenter', target,     t);
            lastTarget = target;
        }
        if (target) dispatch('dragover', target, t);
    }

    function onTouchEnd(e) {
        if (pendingDrag) { clearTimeout(pendingDrag); pendingDrag = null; }
        if (!dragSrc) return;
        const t = e.changedTouches[0];
        e.preventDefault();
        const target = elementUnder(t.clientX, t.clientY);
        if (target) dispatch('drop', target, t);
        dispatch('dragend', dragSrc, t);
        if (dragImg) { dragImg.remove(); dragImg = null; }
        dragSrc = null; lastTarget = null; startPos = null;
        data.clear();
    }

    function onTouchCancel() {
        if (pendingDrag) { clearTimeout(pendingDrag); pendingDrag = null; }
        if (dragImg) { dragImg.remove(); dragImg = null; }
        dragSrc = null; lastTarget = null; startPos = null;
        data.clear();
    }

    document.addEventListener('touchstart',  onTouchStart,  { passive: true  });
    document.addEventListener('touchmove',   onTouchMove,   { passive: false });
    document.addEventListener('touchend',    onTouchEnd,    { passive: false });
    document.addEventListener('touchcancel', onTouchCancel);
})();
