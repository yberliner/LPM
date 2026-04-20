// Signature pad for purchase signing
window.SignaturePad = {
    _pads: {},

    init: function (canvasId, dotNetRef) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const rect = canvas.getBoundingClientRect();
        canvas.width = rect.width;
        canvas.height = rect.height;

        const state = {
            drawing: false,
            lastX: 0,
            lastY: 0,
            hasContent: false,
            dotNetRef: dotNetRef
        };

        ctx.strokeStyle = '#1e3a5f';
        ctx.lineWidth = 2;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';

        function getPos(e) {
            const r = canvas.getBoundingClientRect();
            if (e.touches && e.touches.length > 0) {
                return { x: e.touches[0].clientX - r.left, y: e.touches[0].clientY - r.top };
            }
            return { x: e.clientX - r.left, y: e.clientY - r.top };
        }

        function onStart(e) {
            e.preventDefault();
            state.drawing = true;
            const pos = getPos(e);
            state.lastX = pos.x;
            state.lastY = pos.y;
        }

        function onMove(e) {
            if (!state.drawing) return;
            e.preventDefault();
            const pos = getPos(e);
            ctx.beginPath();
            ctx.moveTo(state.lastX, state.lastY);
            ctx.lineTo(pos.x, pos.y);
            ctx.stroke();
            state.lastX = pos.x;
            state.lastY = pos.y;
            if (!state.hasContent) {
                state.hasContent = true;
                if (state.dotNetRef) {
                    state.dotNetRef.invokeMethodAsync('OnSignatureDrawn');
                }
            }
        }

        function onEnd(e) {
            if (state.drawing) {
                state.drawing = false;
            }
        }

        canvas.addEventListener('mousedown', onStart);
        canvas.addEventListener('mousemove', onMove);
        canvas.addEventListener('mouseup', onEnd);
        canvas.addEventListener('mouseleave', onEnd);
        canvas.addEventListener('touchstart', onStart, { passive: false });
        canvas.addEventListener('touchmove', onMove, { passive: false });
        canvas.addEventListener('touchend', onEnd);

        const listeners = [
            { type: 'mousedown',  fn: onStart, opts: undefined },
            { type: 'mousemove',  fn: onMove,  opts: undefined },
            { type: 'mouseup',    fn: onEnd,   opts: undefined },
            { type: 'mouseleave', fn: onEnd,   opts: undefined },
            { type: 'touchstart', fn: onStart, opts: { passive: false } },
            { type: 'touchmove',  fn: onMove,  opts: { passive: false } },
            { type: 'touchend',   fn: onEnd,   opts: undefined },
        ];

        this._pads[canvasId] = { canvas, ctx, state, listeners };
    },

    clear: function (canvasId) {
        const pad = this._pads[canvasId];
        if (!pad) return;
        pad.ctx.clearRect(0, 0, pad.canvas.width, pad.canvas.height);
        pad.state.hasContent = false;
    },

    getDataUrl: function (canvasId) {
        const pad = this._pads[canvasId];
        if (!pad || !pad.state.hasContent) return null;
        return pad.canvas.toDataURL('image/png');
    },

    drawImage: function (canvasId, dataUrl) {
        const pad = this._pads[canvasId];
        if (!pad) return;
        const img = new Image();
        img.onload = function () {
            pad.ctx.drawImage(img, 0, 0, pad.canvas.width, pad.canvas.height);
            pad.state.hasContent = true;
        };
        img.src = dataUrl;
    },

    dispose: function (canvasId) {
        const pad = this._pads[canvasId];
        if (pad) {
            pad.listeners.forEach(({ type, fn, opts }) =>
                pad.canvas.removeEventListener(type, fn, opts)
            );
        }
        delete this._pads[canvasId];
    }
};

// Purchase receipt — server generates PDF, browser downloads directly
window.PrintPurchase = {
    printReceipt: async function (pcName, dateStr, items, notes, signatureDataUrl) {
        try {
            const res = await fetch('/api/purchase-receipt/pdf', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    pcName: pcName,
                    dateStr: dateStr,
                    items: items,
                    notes: notes,
                    signatureDataUrl: signatureDataUrl
                })
            });
            if (!res.ok) {
                alert('Failed to generate purchase receipt PDF.');
                return;
            }
            const blob = await res.blob();
            const url = URL.createObjectURL(blob);
            const safePc = (pcName || 'PC').replace(/[\\/:*?"<>|]/g, '_');
            const safeDate = (dateStr || '').replace(/\//g, '-');
            const a = document.createElement('a');
            a.href = url;
            a.download = 'Purchase Receipt - ' + safePc + ' - ' + safeDate + '.pdf';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
        } catch (err) {
            console.error('PrintPurchase failed:', err);
            alert('Failed to download purchase receipt: ' + err.message);
        }
    }
};
