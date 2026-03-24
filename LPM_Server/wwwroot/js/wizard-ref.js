// ── Wizard Reference Float Window ──
window.wizardRef = {
    _win: null,
    _loadGen: 0,

    // Returns a status string back to C# for server-side logging
    async openFloat(url, title) {
        this.closeFloat();

        const W = Math.min(window.innerWidth - 40, 620);
        const H = Math.min(window.innerHeight - 80, 860);
        const X = Math.max(0, window.innerWidth - W - 20);
        const Y = 60;

        const win = document.createElement('div');
        win.id = 'wiz-ref-float';
        win.style.cssText =
            'position:fixed;z-index:10007;top:' + Y + 'px;left:' + X + 'px;' +
            'width:' + W + 'px;height:' + H + 'px;' +
            'display:flex;flex-direction:column;' +
            'border:2px solid #3b82f6;border-radius:6px;' +
            'background:#1e1e2e;box-shadow:0 8px 32px rgba(0,0,0,.6);' +
            'overflow:hidden;min-width:200px;min-height:150px;';

        // Title bar
        const bar = document.createElement('div');
        bar.style.cssText =
            'flex-shrink:0;cursor:move;background:#1e293b;color:#e2e8f0;' +
            'padding:6px 10px;display:flex;align-items:center;gap:8px;' +
            'font-size:13px;user-select:none;border-bottom:1px solid #334155;';
        const safeTitle = title.replace(/</g, '&lt;').replace(/>/g, '&gt;');
        bar.innerHTML =
            '<i class="ri-file-pdf-2-line" style="color:#94a3b8;flex-shrink:0;font-size:14px;"></i>' +
            '<span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="' + safeTitle + '">' +
              safeTitle + '  <span style="color:#64748b;font-size:11px;">(read only)</span>' +
            '</span>' +
            '<button id="wiz-ref-close" style="background:transparent;border:none;color:#94a3b8;cursor:pointer;font-size:20px;line-height:1;padding:0 4px;" title="Close">&times;</button>';

        // Body — scrollable canvas container
        const body = document.createElement('div');
        body.style.cssText = 'flex:1;overflow-y:auto;overflow-x:hidden;background:#525659;padding:12px 0;display:flex;flex-direction:column;align-items:center;gap:8px;';

        const spinner = document.createElement('div');
        spinner.style.cssText = 'color:#94a3b8;font-size:.85rem;display:flex;align-items:center;gap:8px;margin-top:40px;';
        spinner.innerHTML = '<i class="ri-loader-4-line" style="animation:spin 1s linear infinite;"></i>Loading\u2026';
        body.appendChild(spinner);

        win.appendChild(bar);
        win.appendChild(body);
        document.body.appendChild(win);
        this._win = win;

        win.querySelector('#wiz-ref-close').addEventListener('click', () => this.closeFloat());

        // Drag
        bar.addEventListener('mousedown', (e) => {
            if (e.target.closest('button')) return;
            e.preventDefault();
            const sx = e.clientX, sy = e.clientY;
            const ox = parseInt(win.style.left) || 0, oy = parseInt(win.style.top) || 0;
            const mv = (e) => {
                win.style.left = Math.max(0, ox + e.clientX - sx) + 'px';
                win.style.top  = Math.max(0, oy + e.clientY - sy) + 'px';
            };
            const up = () => { document.removeEventListener('mousemove', mv); document.removeEventListener('mouseup', up); };
            document.addEventListener('mousemove', mv);
            document.addEventListener('mouseup', up);
        });

        // 8-direction resize
        [
            ['n',  'top:0;left:6px;right:6px;height:5px;cursor:n-resize;'],
            ['s',  'bottom:0;left:6px;right:6px;height:5px;cursor:s-resize;'],
            ['e',  'right:0;top:6px;bottom:6px;width:5px;cursor:e-resize;'],
            ['w',  'left:0;top:6px;bottom:6px;width:5px;cursor:w-resize;'],
            ['ne', 'top:0;right:0;width:10px;height:10px;cursor:ne-resize;'],
            ['nw', 'top:0;left:0;width:10px;height:10px;cursor:nw-resize;'],
            ['se', 'bottom:0;right:0;width:10px;height:10px;cursor:se-resize;'],
            ['sw', 'bottom:0;left:0;width:10px;height:10px;cursor:sw-resize;'],
        ].forEach(([pos, hs]) => {
            const h = document.createElement('div');
            h.style.cssText = 'position:absolute;z-index:2;' + hs;
            h.addEventListener('mousedown', (e) => {
                e.preventDefault(); e.stopPropagation();
                const sx = e.clientX, sy = e.clientY;
                const r = win.getBoundingClientRect();
                const ol = r.left, ot = r.top, ow = r.width, oh = r.height;
                const mv = (e) => {
                    const dx = e.clientX - sx, dy = e.clientY - sy;
                    let nl = ol, nt = ot, nw = ow, nh = oh;
                    if (pos.includes('e')) nw = Math.max(200, ow + dx);
                    if (pos.includes('s')) nh = Math.max(150, oh + dy);
                    if (pos.includes('w')) { nw = Math.max(200, ow - dx); nl = ol + (ow - nw); }
                    if (pos.includes('n')) { nh = Math.max(150, oh - dy); nt = ot + (oh - nh); }
                    win.style.left = nl + 'px'; win.style.top = nt + 'px';
                    win.style.width = nw + 'px'; win.style.height = nh + 'px';
                };
                const up = () => { document.removeEventListener('mousemove', mv); document.removeEventListener('mouseup', up); };
                document.addEventListener('mousemove', mv);
                document.addEventListener('mouseup', up);
            });
            win.appendChild(h);
        });

        // Fetch → ArrayBuffer → pdf.js canvas rendering
        const myGen = ++this._loadGen;
        try {
            const resp = await fetch(url, { credentials: 'include' });
            const status = resp.status;
            if (!resp.ok) {
                spinner.innerHTML = '<span style="color:#ef4444;">HTTP ' + status + '</span>';
                return 'fetch-error:status=' + status;
            }
            const arrayBuffer = await resp.arrayBuffer();
            const byteLen = arrayBuffer.byteLength;

            if (myGen !== this._loadGen) return 'cancelled';

            const pdfjsLib = window['pdfjs-dist/build/pdf'];
            if (!pdfjsLib) {
                spinner.innerHTML = '<span style="color:#ef4444;">pdf.js not loaded</span>';
                return 'error:pdfjs-not-loaded';
            }

            let pdfDoc;
            try {
                pdfDoc = await pdfjsLib.getDocument({ data: arrayBuffer }).promise;
            } catch(pdfErr) {
                if (myGen !== this._loadGen) return 'cancelled';
                spinner.innerHTML = '<span style="color:#ef4444;">PDF error: ' + pdfErr.message + '</span>';
                return 'pdf-error:' + pdfErr.message;
            }
            if (myGen !== this._loadGen) return 'cancelled';

            // Measure available width from the body container
            const bodyWidth = body.clientWidth || (W - 24);
            const availWidth = Math.max(100, bodyWidth - 24);

            // Scan all pages for widest to compute consistent scale
            const allPages = [];
            let maxNaturalWidth = 0;
            for (let i = 1; i <= pdfDoc.numPages; i++) {
                const page = await pdfDoc.getPage(i);
                if (myGen !== this._loadGen) return 'cancelled';
                const vp0 = page.getViewport({ scale: 1 });
                if (vp0.width > maxNaturalWidth) maxNaturalWidth = vp0.width;
                allPages.push(page);
            }
            const scale = Math.min(availWidth / maxNaturalWidth, 3);

            // Remove spinner
            if (body.contains(spinner)) body.removeChild(spinner);

            for (let i = 0; i < allPages.length; i++) {
                if (myGen !== this._loadGen) return 'cancelled';
                const page = allPages[i];
                const vp = page.getViewport({ scale });

                const wrapper = document.createElement('div');
                wrapper.style.cssText = 'position:relative;flex-shrink:0;width:' + vp.width + 'px;height:' + vp.height + 'px;';

                const canvas = document.createElement('canvas');
                canvas.width = vp.width;
                canvas.height = vp.height;
                canvas.style.cssText = 'display:block;';
                wrapper.appendChild(canvas);

                // Text layer for copy/paste
                const textDiv = document.createElement('div');
                textDiv.style.cssText =
                    'position:absolute;top:0;left:0;' +
                    'width:' + vp.width + 'px;height:' + vp.height + 'px;' +
                    'overflow:hidden;line-height:1;' +
                    'pointer-events:auto;cursor:text;' +
                    'user-select:text;-webkit-user-select:text;' +
                    '-webkit-text-size-adjust:none;text-size-adjust:none;forced-color-adjust:none;';
                textDiv.style.setProperty('--scale-factor', vp.scale);
                wrapper.appendChild(textDiv);

                body.appendChild(wrapper);

                const ctx = canvas.getContext('2d');
                await page.render({ canvasContext: ctx, viewport: vp }).promise;
                if (myGen !== this._loadGen) return 'cancelled';

                // Populate text layer asynchronously (non-blocking)
                (async () => {
                    try {
                        const pdfjsLib2 = window['pdfjs-dist/build/pdf'];
                        const textContent = await page.getTextContent();
                        if (myGen !== this._loadGen) return;
                        if (!textContent || !textContent.items || !textContent.items.length) return;

                        // Try pdf.js 3.x API
                        if (pdfjsLib2 && typeof pdfjsLib2.renderTextLayer === 'function') {
                            try {
                                const task = new pdfjsLib2.renderTextLayer({
                                    textContentSource: Promise.resolve(textContent),
                                    container: textDiv,
                                    viewport: vp,
                                });
                                if (task.render) await task.render();
                                else if (task.promise) await task.promise;
                                if (textDiv.childElementCount > 0) return;
                            } catch(e1) { /* fall through */ }
                            try {
                                const task = pdfjsLib2.renderTextLayer({
                                    textContent,
                                    container: textDiv,
                                    viewport: vp,
                                    textDivs: []
                                });
                                if (task && task.promise) await task.promise;
                                if (textDiv.childElementCount > 0) return;
                            } catch(e2) { /* fall through */ }
                        }

                        // Manual fallback: position spans from text items
                        const Util = pdfjsLib2 && pdfjsLib2.Util;
                        for (const item of textContent.items) {
                            if (!item.str) continue;
                            const span = document.createElement('span');
                            span.textContent = item.str;
                            let tx;
                            if (Util && item.transform) {
                                tx = Util.transform(vp.transform, item.transform);
                            } else if (item.transform) {
                                tx = item.transform;
                            }
                            if (tx) {
                                const angle = Math.atan2(tx[1], tx[0]);
                                const scaleX = Math.sqrt(tx[0]*tx[0] + tx[1]*tx[1]);
                                const scaleY = Math.sqrt(tx[2]*tx[2] + tx[3]*tx[3]);
                                span.style.cssText =
                                    'color:transparent;position:absolute;white-space:pre;cursor:text;transform-origin:0% 0%;' +
                                    'left:' + tx[4] + 'px;top:' + (vp.height - tx[5]) + 'px;' +
                                    'font-size:' + (item.height || scaleY) + 'px;' +
                                    'transform:scaleX(' + (item.width / (span.textContent.length * scaleX) || 1) + ')rotate(' + angle + 'rad);';
                            } else {
                                span.style.cssText = 'color:transparent;position:absolute;white-space:pre;cursor:text;';
                            }
                            textDiv.appendChild(span);
                        }
                    } catch(e) { /* text layer is non-critical */ }
                })();
            }

            return 'ok:status=' + status + ':pages=' + pdfDoc.numPages + ':bytes=' + byteLen;
        } catch(err) {
            if (body.contains(spinner)) {
                spinner.innerHTML = '<span style="color:#ef4444;">Error: ' + err.message + '</span>';
            }
            return 'exception:' + err.message;
        }
    },

    closeFloat() {
        this._loadGen++; // cancel any in-flight render
        if (this._win) { this._win.remove(); this._win = null; }
    }
};

// Inject styles once (spinner keyframe + text layer selection highlight)
(function() {
    if (document.getElementById('wiz-ref-style')) return;
    const s = document.createElement('style');
    s.id = 'wiz-ref-style';
    s.textContent =
        '@keyframes spin { from { transform:rotate(0deg); } to { transform:rotate(360deg); } }' +
        '#wiz-ref-float div[style*="user-select:text"] span { color:transparent;position:absolute;white-space:pre;cursor:text;transform-origin:0% 0%; }' +
        '#wiz-ref-float div[style*="user-select:text"] ::selection { background:rgba(0,102,255,0.28);color:transparent; }';
    document.head.appendChild(s);
})();
