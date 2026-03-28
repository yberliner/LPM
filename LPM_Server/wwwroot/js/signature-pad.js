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

// Print purchase receipt — structured data, fits exactly 1 A4 page
window.PrintPurchase = {
    printReceipt: function (pcName, dateStr, items, notes, signatureDataUrl) {
        var html = [];
        html.push('<!DOCTYPE html><html><head><title>Purchase Receipt</title>');
        html.push('<style>');
        html.push('@page { size: A4 portrait; margin: 20mm 15mm; }');
        html.push('* { box-sizing: border-box; margin: 0; padding: 0; }');
        html.push('body { font-family: Arial, Helvetica, sans-serif; color: #1e293b; max-width: 100%; overflow: hidden; }');
        html.push('.receipt { max-height: 257mm; overflow: hidden; }');
        html.push('.header { text-align: center; border-bottom: 3px solid #1e3a5f; padding-bottom: 12px; margin-bottom: 16px; }');
        html.push('.header h1 { font-size: 28px; color: #1e3a5f; margin-bottom: 4px; }');
        html.push('.header .subtitle { font-size: 13px; color: #64748b; }');
        html.push('.meta { display: flex; justify-content: space-between; margin-bottom: 16px; font-size: 13px; color: #475569; }');
        html.push('table { width: 100%; border-collapse: collapse; margin-bottom: 14px; }');
        html.push('th { background: #f1f5f9; color: #334155; font-size: 11px; text-transform: uppercase; letter-spacing: 0.05em; padding: 8px 10px; text-align: left; border-bottom: 2px solid #cbd5e1; }');
        html.push('td { padding: 7px 10px; font-size: 13px; border-bottom: 1px solid #e2e8f0; }');
        html.push('.total-row td { font-weight: 700; border-top: 2px solid #1e3a5f; background: #f8fafc; }');
        html.push('.type-badge { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 11px; font-weight: 600; }');
        html.push('.type-auditing { background: #f0fdf4; color: #15803d; }');
        html.push('.type-course { background: #e0f2fe; color: #0369a1; }');
        html.push('.notes-section { margin-bottom: 14px; }');
        html.push('.notes-label { font-size: 11px; text-transform: uppercase; letter-spacing: 0.05em; color: #64748b; font-weight: 700; margin-bottom: 4px; }');
        html.push('.notes-box { border: 1px solid #e2e8f0; border-radius: 6px; padding: 8px 12px; min-height: 36px; font-size: 13px; color: #334155; background: #f8fafc; }');
        html.push('.sig-section { margin-top: 16px; text-align: center; }');
        html.push('.sig-label { font-size: 11px; text-transform: uppercase; letter-spacing: 0.05em; color: #64748b; font-weight: 700; margin-bottom: 6px; }');
        html.push('.sig-img { max-width: 280px; max-height: 90px; border-bottom: 2px solid #1e3a5f; padding-bottom: 4px; }');
        html.push('.sig-name { font-size: 12px; color: #64748b; margin-top: 4px; }');
        html.push('.footer { margin-top: 20px; text-align: center; font-size: 10px; color: #94a3b8; border-top: 1px solid #e2e8f0; padding-top: 8px; }');
        html.push('</style></head><body>');
        html.push('<div class="receipt">');

        // Header
        html.push('<div class="header">');
        html.push('<h1>' + escHtml(pcName) + '</h1>');
        html.push('<div class="subtitle">Purchase Receipt</div>');
        html.push('</div>');

        // Meta
        html.push('<div class="meta">');
        html.push('<span><strong>Date:</strong> ' + escHtml(dateStr) + '</span>');
        html.push('</div>');

        // Items table
        if (items && items.length > 0) {
            html.push('<table>');
            html.push('<thead><tr>');
            html.push('<th>Type</th><th style="text-align:center;">Hours</th><th style="text-align:right;">Amount</th><th>Registrar</th><th>Referral</th>');
            html.push('</tr></thead><tbody>');

            var totalHrs = 0, totalAmt = 0;
            for (var i = 0; i < items.length; i++) {
                var it = items[i];
                var typeBadge = it.itemType === 'Course'
                    ? '<span class="type-badge type-course">' + escHtml(it.courseName || 'Course') + '</span>'
                    : '<span class="type-badge type-auditing">Auditing</span>';
                var hrs = it.itemType === 'Auditing' ? it.hoursBought : 0;
                totalHrs += hrs;
                totalAmt += (it.amountPaid || 0);
                html.push('<tr>');
                html.push('<td>' + typeBadge + '</td>');
                html.push('<td style="text-align:center;font-weight:600;">' + (hrs > 0 ? hrs + 'h' : '\u2014') + '</td>');
                html.push('<td style="text-align:right;font-weight:600;">' + (it.amountPaid > 0 ? '\u20AA' + it.amountPaid : '\u2014') + '</td>');
                html.push('<td style="color:#64748b;">' + escHtml(it.registrarName || '') + '</td>');
                html.push('<td style="color:#64748b;">' + escHtml(it.referralName || '') + '</td>');
                html.push('</tr>');
            }

            html.push('<tr class="total-row">');
            html.push('<td>Total</td>');
            html.push('<td style="text-align:center;">' + (totalHrs > 0 ? totalHrs + 'h' : '\u2014') + '</td>');
            html.push('<td style="text-align:right;">' + (totalAmt > 0 ? '\u20AA' + totalAmt : '\u2014') + '</td>');
            html.push('<td colspan="2"></td>');
            html.push('</tr>');

            html.push('</tbody></table>');
        }

        // Notes
        if (notes) {
            html.push('<div class="notes-section">');
            html.push('<div class="notes-label">Notes</div>');
            html.push('<div class="notes-box">' + escHtml(notes) + '</div>');
            html.push('</div>');
        }

        // Signature
        if (signatureDataUrl) {
            html.push('<div class="sig-section">');
            html.push('<div class="sig-label">Buyer Signature</div>');
            html.push('<img class="sig-img" src="' + signatureDataUrl + '" />');
            html.push('<div class="sig-name">' + escHtml(pcName) + '</div>');
            html.push('</div>');
        }

        html.push('<div class="footer">Generated on ' + new Date().toLocaleDateString() + '</div>');
        html.push('</div>'); // .receipt
        html.push('</body></html>');

        var printWindow = window.open('', '_blank', 'width=800,height=1000');
        printWindow.document.write(html.join(''));
        printWindow.document.close();
        printWindow.focus();
        setTimeout(function () { printWindow.print(); }, 300);
    }
};

function escHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
