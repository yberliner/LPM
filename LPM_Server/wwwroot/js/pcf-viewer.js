// PC Folder PDF viewer with annotation support — multi-pane
window.pcfViewer = {
    panes: {},       // keyed by paneId ('left', 'right')
    activePane: 'left',
    toolMode: null,
    drawColor: '#22c55e',
    drawWidth: 2.5,
    fontSize: 14,
    textInputEl: null,
    dotNetRef: null,
    _pcId: 0,

    _zoomLevels: [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.0, 2.5, 3.0],
    _panMode: false,
    _textSelectMode: false,

    _initPane(paneId) {
        if (!this.panes[paneId]) {
            this.panes[paneId] = {
                pdfDoc: null,
                pages: [],
                annotations: [],
                currentStroke: null,
                filePath: null,
                baseScale: 1,
                zoomLevel: 1.0,
                dualMode: false,
                dualPage: 0,
                dualScale: 1
            };
        }
        return this.panes[paneId];
    },

    setDotNetRef(ref) { this.dotNetRef = ref; },
    setPcId(pcId) { this._pcId = pcId; },
    setColor(color) {
        this.drawColor = color;
        if (this.textInputEl) this.textInputEl.style.color = color;
    },
    setFontSize(size) { this.fontSize = size; },
    setActivePane(paneId) { this.activePane = paneId; },

    // Proactively cancel any in-flight loadPdf for a pane.
    // Call this before changing the pane layout so the stale render
    // detects the mismatch immediately rather than finishing at wrong scale.
    cancelLoad(paneId) {
        const pane = this._initPane(paneId);
        pane.loadGen = (pane.loadGen || 0) + 1;
    },

    async loadPdf(url, paneId) {
        paneId = paneId || this.activePane;
        const pane = this._initPane(paneId);

        // Generation counter — if a newer loadPdf starts while this one is mid-await,
        // the stale call detects the mismatch and exits without touching the DOM.
        pane.loadGen = (pane.loadGen || 0) + 1;
        const myGen = pane.loadGen;

        pane.zoomLevel = 1.0; // reset zoom on new PDF

        // Extract filePath and solo flag from the URL for auto-save
        const u = new URL(url, location.origin);
        pane.filePath = u.searchParams.get('path');
        pane.solo = u.searchParams.get('solo') === 'true';

        // Folder summary files are served via a distinct endpoint — mark read-only
        pane.readOnly = url.includes('/api/pc-file-folder-summary');

        let viewer = document.getElementById('pcf-viewer-' + paneId);
        // Wait for the viewer element to exist AND its width to stabilize across
        // multiple consecutive animation frames. We capture the width here and
        // use it later for scale calculation — never re-read clientWidth after
        // async operations, since the layout could change in between.
        let prevWidth = -1, stableFrames = 0, stableWidth = 0;
        for (let attempt = 0; attempt < 60; attempt++) {
            await new Promise(r => requestAnimationFrame(r));
            if (pane.loadGen !== myGen) return; // superseded
            viewer = document.getElementById('pcf-viewer-' + paneId);
            if (!viewer) continue;
            const w = viewer.clientWidth;
            if (w >= 50 && w === prevWidth) {
                if (++stableFrames >= 4) { stableWidth = w; break; }
            } else { stableFrames = 0; }
            prevWidth = w;
        }
        if (!viewer || pane.loadGen !== myGen || stableWidth < 50) return;

        if (!viewer._panInited) {
            viewer._panInited = true;
            this._initPanOnViewer(viewer);
        }
        viewer.innerHTML = '';
        pane.pages = [];
        pane.annotations = [];
        pane.currentStroke = null;

        // Reset dual mode state when loading a new file
        pane.dualMode  = false;
        pane.dualScale = 1;
        viewer.style.flexDirection  = '';
        viewer.style.alignItems     = '';
        viewer.style.justifyContent = '';
        viewer.style.flexWrap       = '';
        viewer.classList.remove('pcf-dual-mode');

        const pdfjsLib = window['pdfjs-dist/build/pdf'];
        if (!pdfjsLib) { viewer.innerHTML = '<div style="color:#fff;padding:40px;">PDF.js not loaded</div>'; return; }

        let pdfDoc;
        try {
            pdfDoc = await pdfjsLib.getDocument({ url, withCredentials: true }).promise;
        } catch (e) {
            if (pane.loadGen !== myGen) return;
            const msg = e.status ? `HTTP ${e.status} — ${e.message}` : e.message;
            viewer.innerHTML = '<div style="color:#fff;padding:40px;">Failed to load PDF: ' + msg + '</div>';
            console.error('[pcf-viewer] loadPdf error', url, e);
            return;
        }
        if (pane.loadGen !== myGen) return; // superseded while fetching
        pane.pdfDoc = pdfDoc;

        // Scan ALL pages to find the widest one for scale calculation
        const allPages = [];
        let maxNaturalWidth = 0;
        for (let i = 1; i <= pane.pdfDoc.numPages; i++) {
            const page = await pane.pdfDoc.getPage(i);
            if (pane.loadGen !== myGen) return; // superseded
            const naturalVp = page.getViewport({ scale: 1 });
            if (naturalVp.width > maxNaturalWidth) maxNaturalWidth = naturalVp.width;
            allPages.push(page);
        }

        // Calculate scale to fit the WIDEST page within viewer width.
        // Use stableWidth captured in the RAF loop — not viewer.clientWidth here,
        // since async operations above could let layout changes slip through.
        const viewerWidth = stableWidth - 40; // 20px padding each side
        const fitScale = Math.min(viewerWidth / maxNaturalWidth, 3); // cap at 3x
        const scale = fitScale; // always fit to pane width — no minimum (narrow panes must be allowed to go below 0.5)
        pane.baseScale = scale;

        for (let i = 0; i < allPages.length; i++) {
            if (pane.loadGen !== myGen) return; // superseded mid-render
            const page = allPages[i];
            const vp = page.getViewport({ scale });

            const wrapper = document.createElement('div');
            wrapper.className = 'pcf-page-wrapper';
            wrapper.style.width = vp.width + 'px';
            wrapper.style.height = vp.height + 'px';

            const canvas = document.createElement('canvas');
            canvas.width = vp.width;
            canvas.height = vp.height;
            wrapper.appendChild(canvas);

            const textLayerDiv = document.createElement('div');
            textLayerDiv.className = 'pcf-text-layer';
            textLayerDiv.style.width  = vp.width  + 'px';
            textLayerDiv.style.height = vp.height + 'px';
            textLayerDiv.style.setProperty('--scale-factor', vp.scale);
            textLayerDiv.style.pointerEvents = this._textSelectMode ? 'auto' : 'none';
            wrapper.appendChild(textLayerDiv);

            const overlay = document.createElement('canvas');
            overlay.className = 'pcf-annotation-canvas';
            overlay.width = vp.width;
            overlay.height = vp.height;
            overlay.style.width = vp.width + 'px';
            overlay.style.height = vp.height + 'px';
            overlay.style.pointerEvents = this._textSelectMode ? 'none' : '';
            wrapper.appendChild(overlay);

            viewer.appendChild(wrapper);

            const ctx = canvas.getContext('2d');
            await page.render({ canvasContext: ctx, viewport: vp }).promise;
            if (pane.loadGen !== myGen) return; // superseded after render

            // Populate text layer asynchronously — non-blocking, non-critical
            (async () => {
                try {
                    if (pane.loadGen !== myGen) return;
                    const pdfjsLib2 = window['pdfjs-dist/build/pdf'];
                    console.log('[pcf-text] page', i, '— getTextContent start, renderTextLayer type:', typeof pdfjsLib2?.renderTextLayer);
                    const textContent = await page.getTextContent();
                    if (pane.loadGen !== myGen) return;
                    console.log('[pcf-text] page', i, '— items:', textContent?.items?.length, 'sample:', textContent?.items?.[0]?.str);
                    if (!textContent || !textContent.items || !textContent.items.length) {
                        console.log('[pcf-text] page', i, '— no text items, skipping');
                        return;
                    }

                    // Try PDF.js 3.x class-based API (new renderTextLayer)
                    if (pdfjsLib2 && typeof pdfjsLib2.renderTextLayer === 'function') {
                        try {
                            const task = new pdfjsLib2.renderTextLayer({
                                textContentSource: Promise.resolve(textContent),
                                container: textLayerDiv,
                                viewport: vp,
                            });
                            if (task.render) await task.render();
                            else if (task.promise) await task.promise;
                            console.log('[pcf-text] page', i, '— 3.x API spans:', textLayerDiv.childElementCount);
                            if (textLayerDiv.childElementCount > 0) return;
                        } catch(e1) { console.log('[pcf-text] page', i, '— 3.x API error:', e1.message); }
                        // Try PDF.js 2.x function API
                        try {
                            const task = pdfjsLib2.renderTextLayer({
                                textContent: textContent,
                                container: textLayerDiv,
                                viewport: vp,
                                textDivs: []
                            });
                            if (task && task.promise) await task.promise;
                            console.log('[pcf-text] page', i, '— 2.x API spans:', textLayerDiv.childElementCount);
                            if (textLayerDiv.childElementCount > 0) return;
                        } catch(e2) { console.log('[pcf-text] page', i, '— 2.x API error:', e2.message); }
                    }

                    // Fallback: manually position text spans from getTextContent() items
                    console.log('[pcf-text] page', i, '— using manual fallback');
                    const Util = pdfjsLib2 && pdfjsLib2.Util;
                    for (const item of textContent.items) {
                        if (!item.str) continue;
                        const span = document.createElement('span');
                        span.textContent = item.str;
                        let tx;
                        if (Util && Util.transform) {
                            tx = Util.transform(vp.transform, item.transform);
                        } else {
                            const [va,vb,vc,vd,ve,vf] = vp.transform;
                            const [ia,ib,ic,id,ie,ig] = item.transform;
                            tx = [va*ia+vc*ib, vb*ia+vd*ib, va*ic+vc*id, vb*ic+vd*id,
                                  va*ie+vc*ig+ve, vb*ie+vd*ig+vf];
                        }
                        const fontH = Math.hypot(tx[2], tx[3]) || 1;
                        span.style.left = tx[4] + 'px';
                        span.style.top  = (tx[5] - fontH) + 'px';
                        span.style.fontSize = fontH + 'px';
                        span.style.fontFamily = 'sans-serif';
                        const angle = Math.atan2(tx[1], tx[0]);
                        const sx = Math.hypot(tx[0], tx[1]) / fontH;
                        let xf = '';
                        if (Math.abs(sx - 1) > 0.05) xf += `scaleX(${sx.toFixed(3)}) `;
                        if (Math.abs(angle) > 0.01) xf += `rotate(${angle.toFixed(4)}rad)`;
                        if (xf) { span.style.transform = xf.trim(); span.style.transformOrigin = '0% 100%'; }
                        textLayerDiv.appendChild(span);
                    }
                    console.log('[pcf-text] page', i, '— manual spans created:', textLayerDiv.childElementCount);
                } catch(e) { console.log('[pcf-text] page', i, '— ERROR:', e.message); }
            })();

            pane.pages.push({ canvas, overlay, vp, pageIdx: i, scale, srcDoc: pane.pdfDoc, srcPageNum: i + 1 });
            this._attachEvents(overlay, i, paneId);
        }
    },

    _attachEvents(overlay, pageIdx, paneId) {
        const self = this;
        let drawing = false;

        // Helper: get pointer position in canvas coordinates.
        // Uses overlay.width/r.width ratio which accounts for CSS zoom, dualScale,
        // AND any max-width CSS constraint — no divisor guessing needed.
        function canvasXY(e) {
            const r = overlay.getBoundingClientRect();
            return {
                x: (e.clientX - r.left) * (overlay.width  / r.width),
                y: (e.clientY - r.top)  * (overlay.height / r.height)
            };
        }

        // Helper: convert a viewport point (clientX, clientY) to wrapper-relative coords
        // for DOM element positioning (accounting for wrapper CSS zoom).
        function wrapperXY(clientX, clientY) {
            const wr = overlay.parentElement.getBoundingClientRect();
            const wz = parseFloat(overlay.parentElement.style.zoom) || 1;
            return {
                wx: (clientX - wr.left) / wz,
                wy: (clientY - wr.top)  / wz
            };
        }

        // Helper: convert a canvas coordinate to wrapper-relative coords.
        function canvasToWrapperXY(cx, cy) {
            const r  = overlay.getBoundingClientRect();
            const wr = overlay.parentElement.getBoundingClientRect();
            const wz = parseFloat(overlay.parentElement.style.zoom) || 1;
            const vx = r.left + cx * r.width  / overlay.width;
            const vy = r.top  + cy * r.height / overlay.height;
            return {
                wx: (vx - wr.left) / wz,
                wy: (vy - wr.top)  / wz
            };
        }

        // Clicking anywhere in a pane makes it active
        overlay.addEventListener('pointerdown', (e) => {
            self.activePane = paneId;
            const pane = self.panes[paneId];
            if (!pane) return;

            if (self.textInputEl) return;

            // Always allow dragging existing text annotations, regardless of tool mode
            const { x: pdX, y: pdY } = canvasXY(e);
            const textHit = self._hitTestText(pane, pageIdx, pdX, pdY);
            if (textHit) {
                overlay.setPointerCapture(e.pointerId);
                const ann = textHit.ann;
                const startX = pdX, startY = pdY;
                const origX = ann.x, origY = ann.y;
                let moved = false;
                const onMove = (me) => {
                    const { x: mx, y: my } = canvasXY(me);
                    ann.x = origX + (mx - startX);
                    ann.y = origY + (my - startY);
                    self._redrawOverlay(pageIdx, paneId);
                    moved = true;
                };
                const onUp = () => {
                    overlay.removeEventListener('pointermove', onMove);
                    overlay.removeEventListener('pointerup', onUp);
                    if (moved) self._notifyChange();
                };
                overlay.addEventListener('pointermove', onMove);
                overlay.addEventListener('pointerup', onUp);
            } else if (self.toolMode === 'draw' || self.toolMode === 'brush') {
                drawing = true;
                overlay.setPointerCapture(e.pointerId);
                pane.currentStroke = {
                    pageIdx, points: [{ x: pdX, y: pdY }],
                    color: self.drawColor,
                    width: self.toolMode === 'brush' ? Math.max(self.drawWidth * 4, 16) : self.drawWidth,
                    brush: self.toolMode === 'brush'
                };
            } else if (self.toolMode === 'text') {
                const { wx, wy } = wrapperXY(e.clientX, e.clientY);
                self._showTextInput(overlay.parentElement, pageIdx, pdX, pdY, paneId, undefined, undefined, wx, wy);
            }
        });

        overlay.addEventListener('dblclick', (e) => {
            const pane = self.panes[paneId];
            if (!pane || self.textInputEl) return;
            const { x, y } = canvasXY(e);
            const hit = self._hitTestText(pane, pageIdx, x, y);
            if (hit) {
                e.preventDefault();
                const { wx, wy } = canvasToWrapperXY(hit.ann.x, hit.ann.y);
                self._showTextInput(overlay.parentElement, pageIdx, hit.ann.x, hit.ann.y, paneId, hit.ann, hit.idx, wx, wy);
            }
        });

        overlay.addEventListener('pointermove', (e) => {
            const pane = self.panes[paneId];
            if (!drawing || !pane || !pane.currentStroke) return;
            const { x, y } = canvasXY(e);
            pane.currentStroke.points.push({ x, y });
            self._redrawOverlay(pageIdx, paneId);
        });

        overlay.addEventListener('pointerup', () => {
            const pane = self.panes[paneId];
            if (drawing && pane && pane.currentStroke && pane.currentStroke.points.length > 1) {
                pane.annotations.push({ pageIdx, type: 'draw', stroke: pane.currentStroke });
                self._notifyChange();
            }
            drawing = false;
            if (pane) pane.currentStroke = null;
        });
    },

    _redrawOverlay(pageIdx, paneId) {
        const pane = this.panes[paneId];
        if (!pane) return;
        const pg = pane.pages[pageIdx];
        if (!pg) return;
        const ctx = pg.overlay.getContext('2d');
        ctx.clearRect(0, 0, pg.overlay.width, pg.overlay.height);

        for (const ann of pane.annotations) {
            if (ann.pageIdx !== pageIdx) continue;
            if (ann.type === 'draw') this._drawStroke(ctx, ann.stroke);
            if (ann.type === 'text') this._drawText(ctx, ann);
        }

        if (pane.currentStroke && pane.currentStroke.pageIdx === pageIdx) {
            this._drawStroke(ctx, pane.currentStroke);
        }

        this._syncTextSelectLayer(pageIdx, paneId);
    },

    _syncTextSelectLayer(pageIdx, paneId) {
        const pane = this.panes[paneId];
        if (!pane) return;
        const pg = pane.pages[pageIdx];
        if (!pg) return;
        const overlay = pg.overlay;
        const wrapper = overlay.parentElement;
        if (!wrapper) return;

        let layer = wrapper.querySelector('.pcf-text-select-layer');
        if (!layer) {
            layer = document.createElement('div');
            layer.className = 'pcf-text-select-layer';
            layer.style.width  = overlay.width  + 'px';
            layer.style.height = overlay.height + 'px';
            wrapper.appendChild(layer);
        }

        // Remove existing spans for this page index
        layer.querySelectorAll(`[data-pidx="${pageIdx}"]`).forEach(el => el.remove());

        // Rebuild spans for text annotations on this page
        for (const ann of pane.annotations) {
            if (ann.type !== 'text' || ann.pageIdx !== pageIdx) continue;
            const fontSize = ann.fontSize || 14;
            const span = document.createElement('div');
            span.className = 'pcf-text-select-span';
            span.dataset.pidx = pageIdx;
            span.style.left     = ann.x + 'px';
            span.style.top      = (ann.y - fontSize) + 'px';
            span.style.fontSize = fontSize + 'px';
            if (ann.maxWidth) span.style.maxWidth = ann.maxWidth + 'px';
            span.style.pointerEvents = this._textSelectMode ? 'auto' : 'none';
            span.textContent = ann.text;
            layer.appendChild(span);
        }
    },

    _drawStroke(ctx, stroke) {
        if (stroke.points.length < 2) return;
        const prevAlpha = ctx.globalAlpha;
        const prevComposite = ctx.globalCompositeOperation;
        if (stroke.brush) {
            ctx.globalAlpha = 0.35;
            ctx.globalCompositeOperation = 'multiply';
        }
        ctx.beginPath();
        ctx.strokeStyle = stroke.color;
        ctx.lineWidth = stroke.width;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.moveTo(stroke.points[0].x, stroke.points[0].y);
        for (let i = 1; i < stroke.points.length; i++) {
            ctx.lineTo(stroke.points[i].x, stroke.points[i].y);
        }
        ctx.stroke();
        ctx.globalAlpha = prevAlpha;
        ctx.globalCompositeOperation = prevComposite;
    },

    // Word-wrap text respecting explicit \n and maxWidth
    _wrapText(ctx, text, maxWidth) {
        const raw = (text || '').split('\n');
        const lines = [];
        for (const segment of raw) {
            if (!maxWidth) { lines.push(segment); continue; }
            const words = segment.split(' ');
            let cur = '';
            for (const word of words) {
                const test = cur ? cur + ' ' + word : word;
                if (cur && ctx.measureText(test).width > maxWidth) { lines.push(cur); cur = word; }
                else cur = test;
            }
            lines.push(cur);
        }
        return lines;
    },

    _drawText(ctx, ann) {
        ctx.font = (ann.fontSize || 14) + 'px Arial';
        ctx.fillStyle = ann.color || '#1e293b';
        const lineHeight = (ann.fontSize || 14) * 1.3;
        const lines = this._wrapText(ctx, ann.text, ann.maxWidth || 0);
        for (let i = 0; i < lines.length; i++) {
            ctx.fillText(lines[i], ann.x, ann.y + i * lineHeight);
        }
    },

    _hitTestText(pane, pageIdx, x, y) {
        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');
        for (let i = pane.annotations.length - 1; i >= 0; i--) {
            const ann = pane.annotations[i];
            if (ann.type !== 'text' || ann.pageIdx !== pageIdx) continue;
            const fontSize = ann.fontSize || 14;
            ctx.font = fontSize + 'px Arial';
            const lines = this._wrapText(ctx, ann.text, ann.maxWidth || 0);
            const lineHeight = fontSize * 1.3;
            const totalHeight = lines.length * lineHeight;
            let maxW = 0;
            for (const line of lines) { const w = ctx.measureText(line).width; if (w > maxW) maxW = w; }
            const top = ann.y - fontSize;
            const bottom = top + totalHeight + 4;
            const left = ann.x - 4;
            const right = ann.x + maxW + 4;
            if (x >= left && x <= right && y >= top && y <= bottom) return { ann, idx: i };
        }
        return null;
    },

    _showTextInput(wrapper, pageIdx, x, y, paneId, existingAnn, existingIdx, inputX, inputY) {
        if (this.textInputEl) {
            this.textInputEl.remove();
            this.textInputEl = null;
        }

        const isEditing = existingAnn != null;
        const color = this.drawColor;
        const fontSize = isEditing ? (existingAnn.fontSize || 14) : this.fontSize;

        // Use wrapper-relative coords for positioning when provided (correct when
        // CSS zoom / max-width constraint is active); fall back to canvas coords.
        const posX = inputX !== undefined ? inputX : x;
        const posY = inputY !== undefined ? inputY : y;

        const input = document.createElement('textarea');
        input.className = 'pcf-text-input';
        input.rows = 1;
        input.style.left = posX + 'px';
        input.style.top = (posY - fontSize) + 'px';
        input.style.width = '220px';
        input.placeholder = 'Type here... (Shift+Enter for new line)';
        input.style.color = color;
        input.style.fontSize = fontSize + 'px';
        input.style.lineHeight = '1.3';
        input.style.resize = 'none';
        input.style.overflow = 'hidden';

        if (isEditing) {
            input.value = existingAnn.text;
        }

        wrapper.appendChild(input);
        this.textInputEl = input;

        // Auto-grow textarea
        const autoGrow = () => {
            input.style.height = 'auto';
            input.style.height = input.scrollHeight + 'px';
        };
        input.addEventListener('input', autoGrow);
        // Trigger auto-grow for pre-filled text
        if (isEditing) {
            requestAnimationFrame(autoGrow);
        }

        input.addEventListener('pointerdown', (e) => e.stopPropagation());

        const self = this;
        let committed = false;
        const commit = () => {
            if (committed) return;
            committed = true;
            const text = input.value.trim();
            const pane = self.panes[paneId];
            if (pane) {
                if (isEditing) {
                    if (text) {
                        // Update existing annotation — apply current color/fontSize from toolbar
                        pane.annotations[existingIdx].text = text;
                        pane.annotations[existingIdx].color = self.drawColor;
                        pane.annotations[existingIdx].fontSize = self.fontSize;
                        pane.annotations[existingIdx].maxWidth = input.offsetWidth;
                    } else {
                        // Empty text = delete annotation
                        pane.annotations.splice(existingIdx, 1);
                    }
                } else if (text) {
                    // New annotation — store maxWidth so word-wrap matches the textarea
                    pane.annotations.push({ pageIdx, type: 'text', text, x, y, color, fontSize, maxWidth: input.offsetWidth });
                }
                self._redrawOverlay(pageIdx, paneId);
                self._notifyChange();
            }
            input.remove();
            self.textInputEl = null;
        };

        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); commit(); }
            if (e.key === 'Escape') { input.remove(); self.textInputEl = null; }
        });
        input.addEventListener('blur', commit);

        requestAnimationFrame(() => {
            input.focus();
            if (isEditing) {
                input.selectionStart = input.selectionEnd = input.value.length;
            }
        });
    },

    _notifyChange() {
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('OnAnnotationChanged');
        }
    },

    setTool(mode) {
        this.toolMode = mode;
        if (mode) this.setTextSelectMode(false);
        const cursor = mode === 'draw' ? 'crosshair' : mode === 'brush' ? 'crosshair' : mode === 'text' ? 'text' : 'default';
        for (const paneId in this.panes) {
            const pane = this.panes[paneId];
            for (const pg of pane.pages) {
                pg.overlay.style.cursor = cursor;
            }
        }
    },

    setTextSelectMode(enabled) {
        this._textSelectMode = enabled;
        console.log('[pcf-text] setTextSelectMode', enabled, 'panes:', Object.keys(this.panes));
        for (const paneId in this.panes) {
            const viewer = document.getElementById('pcf-viewer-' + paneId);
            console.log('[pcf-text] pane', paneId, 'viewer found:', !!viewer);
            if (!viewer) continue;
            const layers = viewer.querySelectorAll('.pcf-text-layer');
            const overlays = viewer.querySelectorAll('.pcf-annotation-canvas');
            console.log('[pcf-text] text layers:', layers.length, 'overlays:', overlays.length);
            layers.forEach((tl, i) => {
                tl.style.pointerEvents = enabled ? 'auto' : 'none';
                console.log('[pcf-text] layer', i, 'children:', tl.childElementCount, 'pointerEvents->', tl.style.pointerEvents);
            });
            overlays.forEach(ov => {
                ov.style.pointerEvents = enabled ? 'none' : '';
                ov.style.cursor = enabled ? 'auto' : '';
            });
            viewer.querySelectorAll('.pcf-text-select-span').forEach(span => {
                span.style.pointerEvents = enabled ? 'auto' : 'none';
            });
            viewer.style.cursor = enabled ? 'text' : '';
        }
    },

    setWidth(width) {
        this.drawWidth = width;
    },

    undo(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        if (!pane || pane.annotations.length === 0) return;
        const removed = pane.annotations.pop();
        if (removed.type === 'blank-page') {
            // Remove the blank page from DOM and pane.pages by its pageIdx
            const idx = removed.pageIdx;
            if (idx >= 0 && idx < pane.pages.length) {
                const pg = pane.pages[idx];
                if (pg && pg.canvas && pg.canvas.parentElement) {
                    pg.canvas.parentElement.remove();
                }
                pane.pages.splice(idx, 1);
                // Re-index pages
                for (let i = 0; i < pane.pages.length; i++) {
                    pane.pages[i].pageIdx = i;
                }
                // Shift annotation pageIdx values down for pages after the removed one
                for (const ann of pane.annotations) {
                    if (ann.pageIdx > idx) {
                        ann.pageIdx--;
                    }
                }
            }
        } else if (removed.type === 'bg-change') {
            // Restore the previous canvas snapshot
            const pg = pane.pages[removed.pageIdx];
            if (pg && removed.snapshot) {
                const ctx = pg.canvas.getContext('2d');
                ctx.clearRect(0, 0, pg.canvas.width, pg.canvas.height);
                ctx.drawImage(removed.snapshot, 0, 0);
            }
        } else {
            this._redrawOverlay(removed.pageIdx, paneId);
        }
    },

    hasAnnotations(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        return pane ? pane.annotations.length > 0 : false;
    },

    hasAnyAnnotations() {
        for (const paneId in this.panes) {
            if (this.panes[paneId].annotations.length > 0) return true;
        }
        return false;
    },

    _getVisiblePageIdx(paneId) {
        const pane = this.panes[paneId];
        if (!pane || pane.pages.length === 0) return 0;
        const viewer = document.getElementById('pcf-viewer-' + paneId);
        if (!viewer) return 0;

        const viewerRect = viewer.getBoundingClientRect();
        const viewerMid = viewerRect.top + viewerRect.height / 2;
        let bestIdx = 0;
        let bestDist = Infinity;

        for (let i = 0; i < pane.pages.length; i++) {
            const wrapper = pane.pages[i].canvas.parentElement;
            if (!wrapper) continue;
            const r = wrapper.getBoundingClientRect();
            const pageMid = r.top + r.height / 2;
            const dist = Math.abs(pageMid - viewerMid);
            if (dist < bestDist) {
                bestDist = dist;
                bestIdx = i;
            }
        }
        return bestIdx;
    },

    addBlankPage(paneId, position) {
        const pane = this.panes[paneId];
        if (!pane || pane.pages.length === 0) return;

        const visibleIdx = this._getVisiblePageIdx(paneId);
        // insertIdx = the index the new page will occupy
        const insertIdx = position === 'above' ? visibleIdx : visibleIdx + 1;

        // Use same dimensions as the visible page
        const refPg = pane.pages[visibleIdx];
        const w = refPg.canvas.width;
        const h = refPg.canvas.height;

        const viewer = document.getElementById('pcf-viewer-' + paneId);
        if (!viewer) return;

        const wrapper = document.createElement('div');
        wrapper.className = 'pcf-page-wrapper';
        wrapper.style.width = w + 'px';
        wrapper.style.height = h + 'px';

        // White canvas (blank page)
        const canvas = document.createElement('canvas');
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext('2d');
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, w, h);
        wrapper.appendChild(canvas);

        // Annotation overlay
        const overlay = document.createElement('canvas');
        overlay.className = 'pcf-annotation-canvas';
        overlay.width = w;
        overlay.height = h;
        overlay.style.width = w + 'px';
        overlay.style.height = h + 'px';
        wrapper.appendChild(overlay);

        // Apply current zoom level
        if (pane.zoomLevel && pane.zoomLevel !== 1) {
            wrapper.style.zoom = pane.zoomLevel;
        }

        // Insert into DOM at the right position
        if (insertIdx < pane.pages.length) {
            const refWrapper = pane.pages[insertIdx].canvas.parentElement;
            viewer.insertBefore(wrapper, refWrapper);
        } else {
            viewer.appendChild(wrapper);
        }

        // Insert into pane.pages array
        const newPage = { canvas, overlay, vp: refPg.vp, pageIdx: insertIdx, scale: refPg.scale };
        pane.pages.splice(insertIdx, 0, newPage);

        // Re-index all pages and annotations after insertion
        for (let i = 0; i < pane.pages.length; i++) {
            pane.pages[i].pageIdx = i;
        }
        for (const ann of pane.annotations) {
            if (ann.pageIdx >= insertIdx) {
                ann.pageIdx++;
            }
        }

        this._attachEvents(overlay, insertIdx, paneId);

        // Mark as annotation so it gets saved
        pane.annotations.push({ type: 'blank-page', pageIdx: insertIdx });

        // Scroll to the new page
        wrapper.scrollIntoView({ behavior: 'smooth', block: 'center' });

        // Notify Blazor
        if (this.dotNetRef) this.dotNetRef.invokeMethodAsync('OnAnnotationChanged');
    },

    async insertPdfPages(paneId, beforePageIdx, pdfUrl) {
        const pane = this.panes[paneId];
        if (!pane) return 0;
        const viewer = document.getElementById('pcf-viewer-' + paneId);
        if (!viewer) return 0;

        let insertDoc;
        try {
            insertDoc = await pdfjsLib.getDocument(pdfUrl).promise;
        } catch (e) {
            console.error('Failed to load insert PDF:', e);
            return 0;
        }

        const numPages = insertDoc.numPages;
        const refPg = pane.pages[0]; // use first page for reference dimensions
        // Target canvas width = same as the first original page so zoom works uniformly
        const refCanvasWidth = refPg ? refPg.canvas.width : null;

        for (let i = 1; i <= numPages; i++) {
            const page = await insertDoc.getPage(i);
            const naturalVp = page.getViewport({ scale: 1 });
            const insertScale = refCanvasWidth
                ? (refCanvasWidth / naturalVp.width)
                : (pane.baseScale || 1);
            const vp = page.getViewport({ scale: insertScale });

            const wrapper = document.createElement('div');
            wrapper.className = 'pcf-page-wrapper';
            wrapper.style.width = vp.width + 'px';
            wrapper.style.height = vp.height + 'px';

            const canvas = document.createElement('canvas');
            canvas.width = vp.width;
            canvas.height = vp.height;
            const ctx = canvas.getContext('2d');
            await page.render({ canvasContext: ctx, viewport: vp }).promise;
            wrapper.appendChild(canvas);

            const overlay = document.createElement('canvas');
            overlay.className = 'pcf-annotation-canvas';
            overlay.width = vp.width;
            overlay.height = vp.height;
            overlay.style.width = vp.width + 'px';
            overlay.style.height = vp.height + 'px';
            wrapper.appendChild(overlay);

            if (pane.zoomLevel && pane.zoomLevel !== 1) {
                wrapper.style.zoom = pane.zoomLevel;
            }

            const insertIdx = beforePageIdx + (i - 1);
            if (insertIdx < pane.pages.length) {
                const refWrapper = pane.pages[insertIdx].canvas.parentElement;
                viewer.insertBefore(wrapper, refWrapper);
            } else {
                viewer.appendChild(wrapper);
            }

            const newPage = { canvas, overlay, vp, pageIdx: insertIdx, scale: insertScale, srcDoc: insertDoc, srcPageNum: i };
            pane.pages.splice(insertIdx, 0, newPage);

            this._attachEvents(overlay, insertIdx, paneId);
        }

        // Re-index pages and shift annotations
        for (let i = 0; i < pane.pages.length; i++) {
            pane.pages[i].pageIdx = i;
        }
        for (const ann of pane.annotations) {
            if (ann.pageIdx >= beforePageIdx) {
                ann.pageIdx += numPages;
            }
        }

        // Mark each inserted page as annotation so they get saved
        for (let i = 0; i < numPages; i++) {
            pane.annotations.push({ type: 'blank-page', pageIdx: beforePageIdx + i });
        }

        // Scroll to first inserted page
        if (pane.pages[beforePageIdx]) {
            pane.pages[beforePageIdx].canvas.parentElement.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }

        if (this.dotNetRef) this.dotNetRef.invokeMethodAsync('OnAnnotationChanged');
        return numPages;
    },

    // Build annotation data: transparent overlays for draw/text pages,
    // full composites only for bg-changed/blank/inserted pages, nothing for unchanged originals.
    getAnnotationData(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        if (!pane) return JSON.stringify({ totalPages: 0, pages: [] });

        // Collect which page indices have each annotation type
        const bgChangePages = new Set(
            pane.annotations.filter(a => a.type === 'bg-change').map(a => a.pageIdx)
        );
        const overlayPages = new Set(
            pane.annotations.filter(a => a.type === 'draw' || a.type === 'text').map(a => a.pageIdx)
        );

        const pages = [];
        for (let i = 0; i < pane.pages.length; i++) {
            const pg = pane.pages[i];
            // Original pages have srcDoc === pane.pdfDoc; blank/inserted pages have no srcDoc or a different one
            const isOriginal = pg.srcDoc != null && pg.srcDoc === pane.pdfDoc;
            const srcPageIdx = isOriginal ? (pg.srcPageNum - 1) : -1;
            const hasBgChange = bgChangePages.has(i);
            const hasOverlay  = overlayPages.has(i);

            if (!isOriginal || hasBgChange) {
                // Blank/inserted page or bg-changed original: send full composite
                const fc = document.createElement('canvas');
                fc.width = pg.canvas.width; fc.height = pg.canvas.height;
                const fctx = fc.getContext('2d');
                fctx.drawImage(pg.canvas, 0, 0);
                fctx.drawImage(pg.overlay, 0, 0);
                pages.push({
                    action: isOriginal ? 'full_replace' : 'full_new',
                    srcPageIdx,
                    w: fc.width, h: fc.height,
                    dataUrl: fc.toDataURL('image/png')
                });
            } else if (hasOverlay) {
                // Original page with draw/text: send transparent annotation overlay only
                pages.push({
                    action: 'overlay',
                    srcPageIdx,
                    w: pg.overlay.width, h: pg.overlay.height,
                    dataUrl: pg.overlay.toDataURL('image/png')
                });
            } else {
                // Original page with no changes: server keeps as-is
                pages.push({ action: 'original', srcPageIdx });
            }
        }
        return JSON.stringify({ totalPages: pane.pages.length, pages });
    },

    // ── Zoom ──

    zoomIn(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        if (!pane || !pane.pdfDoc) return 100;
        const cur = pane.zoomLevel;
        let next = cur;
        for (const z of this._zoomLevels) {
            if (z > cur + 0.01) { next = z; break; }
        }
        return this._setZoom(paneId, next);
    },

    zoomOut(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        if (!pane || !pane.pdfDoc) return 100;
        const cur = pane.zoomLevel;
        let prev = cur;
        for (let i = this._zoomLevels.length - 1; i >= 0; i--) {
            if (this._zoomLevels[i] < cur - 0.01) { prev = this._zoomLevels[i]; break; }
        }
        return this._setZoom(paneId, prev);
    },

    _setZoom(paneId, level) {
        const pane = this.panes[paneId];
        if (!pane) return 100;
        pane.zoomLevel = level;
        const ds = pane.dualScale || 1;
        for (const pg of pane.pages) {
            const wrapper = pg.canvas.parentElement;
            if (wrapper) wrapper.style.zoom = level * ds;
        }
        return Math.round(level * 100);
    },

    fitPage(paneId) {
        const pane = this.panes[paneId];
        if (!pane || !pane.pages.length) return 100;
        const viewer = document.getElementById('pcf-viewer-' + paneId);
        if (!viewer) return 100;

        // Find the tallest rendered page
        let maxPageH = 0;
        for (const pg of pane.pages) {
            if (pg.canvas.height > maxPageH) maxPageH = pg.canvas.height;
        }
        if (maxPageH === 0) return 100;

        const viewerH = viewer.clientHeight - 40; // 20px padding each side
        // Fit height; also cap at 1.0 so it never exceeds fit-to-width
        const fitLevel = Math.min(1.0, viewerH / maxPageH);
        return this._setZoom(paneId, fitLevel);
    },

    getZoomPercent(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        return pane ? Math.round((pane.zoomLevel || 1) * 100) : 100;
    },

    // ── Pan (drag-to-scroll) mode ──

    setPanMode(enabled) {
        this._panMode = enabled;
        for (const paneId of Object.keys(this.panes)) {
            const viewer = document.getElementById('pcf-viewer-' + paneId);
            if (viewer) viewer.classList.toggle('pcf-pan-mode', enabled);
        }
    },

    _initPanOnViewer(viewer) {
        let dragging = false;
        let startX, startY, scrollL, scrollT;
        const self = this;

        viewer.addEventListener('mousedown', (e) => {
            if (!self._panMode) return;
            dragging = true;
            startX = e.clientX;
            startY = e.clientY;
            scrollL = viewer.scrollLeft;
            scrollT = viewer.scrollTop;
            viewer.classList.add('pcf-panning');
            e.preventDefault();
            e.stopPropagation();
        });

        viewer.addEventListener('mousemove', (e) => {
            if (!dragging || !self._panMode) return;
            viewer.scrollLeft = scrollL - (e.clientX - startX);
            viewer.scrollTop  = scrollT  - (e.clientY - startY);
        });

        const stopDrag = () => {
            if (!dragging) return;
            dragging = false;
            viewer.classList.remove('pcf-panning');
        };
        viewer.addEventListener('mouseup',    stopDrag);
        viewer.addEventListener('mouseleave', stopDrag);
    },

    // ── Extract pages mode ──
    _extractMode: false,
    _extractSelected: {},  // { pageIdx: true/false }

    enterExtractMode(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        if (!pane || pane.pages.length === 0) return;

        this._extractMode = true;
        this._extractSelected = {};

        for (const pg of pane.pages) {
            const wrapper = pg.canvas.parentElement;
            if (!wrapper) continue;

            // Add checkbox overlay
            const cb = document.createElement('label');
            cb.className = 'pcf-extract-cb';
            cb.innerHTML = '<input type="checkbox" /><span></span>';
            cb.style.position = 'absolute';
            cb.style.top = '10px';
            cb.style.left = '10px';
            cb.style.zIndex = '20';
            cb.style.cursor = 'pointer';

            const idx = pg.pageIdx;
            const self = this;
            cb.querySelector('input').addEventListener('change', function () {
                self._extractSelected[idx] = this.checked;
                wrapper.style.outline = this.checked ? '3px solid #3b82f6' : 'none';
                if (self.dotNetRef) self.dotNetRef.invokeMethodAsync('OnExtractSelectionChanged', self.getExtractCount());
            });

            wrapper.style.position = 'relative';
            wrapper.appendChild(cb);
        }
    },

    exitExtractMode(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        if (!pane) return;

        this._extractMode = false;
        this._extractSelected = {};

        for (const pg of pane.pages) {
            const wrapper = pg.canvas.parentElement;
            if (!wrapper) continue;
            wrapper.style.outline = 'none';
            const cb = wrapper.querySelector('.pcf-extract-cb');
            if (cb) cb.remove();
        }
    },

    getExtractCount() {
        let count = 0;
        for (const k in this._extractSelected) {
            if (this._extractSelected[k]) count++;
        }
        return count;
    },

    getSelectedPageIndices() {
        const indices = [];
        for (const k in this._extractSelected) {
            if (this._extractSelected[k]) indices.push(parseInt(k));
        }
        indices.sort((a, b) => a - b);
        return indices;
    },

    // ── Auto-save support ──

    // Save a specific pane synchronously (for beforeunload)
    _saveSync(paneId) {
        const pane = this.panes[paneId];
        if (!pane || pane.annotations.length === 0 || !pane.filePath || !this._pcId) return;

        const meta = JSON.parse(this.getAnnotationData(paneId));
        const formData = new FormData();
        const metaForSend = { totalPages: meta.totalPages, pages: [] };
        let imgIdx = 0;

        for (const p of meta.pages) {
            if (p.action === 'original') {
                metaForSend.pages.push({ action: 'original', srcPageIdx: p.srcPageIdx });
            } else {
                const byteStr = atob(p.dataUrl.split(',')[1]);
                const arr = new Uint8Array(byteStr.length);
                for (let j = 0; j < byteStr.length; j++) arr[j] = byteStr.charCodeAt(j);
                formData.append('img_' + imgIdx, new Blob([arr], { type: 'image/png' }), 'img_' + imgIdx + '.png');
                metaForSend.pages.push({ action: p.action, srcPageIdx: p.srcPageIdx ?? -1, w: p.w, h: p.h, imgIdx });
                imgIdx++;
            }
        }
        formData.append('meta', JSON.stringify(metaForSend));

        const xhr = new XMLHttpRequest();
        const soloParam = pane.solo ? '&solo=true' : '';
        xhr.open('POST', '/api/pc-file-save-annotated?pcId=' + this._pcId + '&path=' + encodeURIComponent(pane.filePath) + soloParam, false);
        xhr.send(formData);

        pane.annotations = [];
    },

    // Save all panes that have annotations (sync, for beforeunload)
    saveAllSync() {
        for (const paneId in this.panes) {
            this._saveSync(paneId);
        }
    },

    // Async save for a pane (used when switching files)
    async savePane(paneId) {
        const pane = this.panes[paneId];
        if (!pane || pane.readOnly || pane.annotations.length === 0 || !pane.filePath || !this._pcId) return;

        const dataJson = this.getAnnotationData(paneId);
        await window.pcfSaveAnnotatedPdf(this._pcId, pane.filePath, dataJson, pane.solo);
        pane.annotations = [];
    },

    // ── Page background color ──

    async setPageBackground(paneId, pageIdx, color) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        if (!pane) return;
        const pg = pane.pages[pageIdx];
        if (!pg) return;

        const w = pg.canvas.width;
        const h = pg.canvas.height;
        const ctx = pg.canvas.getContext('2d');

        // Snapshot current canvas for undo
        const snapshot = document.createElement('canvas');
        snapshot.width = w;
        snapshot.height = h;
        snapshot.getContext('2d').drawImage(pg.canvas, 0, 0);
        pane.annotations.push({ type: 'bg-change', pageIdx, snapshot });

        // Use per-page source doc/page tracking set at render time
        const hasSrc = pg.srcDoc && pg.srcPageNum;

        if (hasSrc) {
            // Render from the exact source doc/page (correct even after insertions)
            const tmpCanvas = document.createElement('canvas');
            tmpCanvas.width = w;
            tmpCanvas.height = h;
            const tmpCtx = tmpCanvas.getContext('2d');
            const page = await pg.srcDoc.getPage(pg.srcPageNum);
            await page.render({ canvasContext: tmpCtx, viewport: pg.vp }).promise;

            ctx.clearRect(0, 0, w, h);

            // Fill background color
            ctx.fillStyle = (color && color !== '' && color !== '#ffffff' && color !== '#FFFFFF') ? color : '#ffffff';
            ctx.fillRect(0, 0, w, h);

            // Draw PDF on top — use multiply blend so white areas take the background color
            if (color && color !== '' && color !== '#ffffff' && color !== '#FFFFFF') {
                ctx.globalCompositeOperation = 'multiply';
            }
            ctx.drawImage(tmpCanvas, 0, 0);
            ctx.globalCompositeOperation = 'source-over';
        } else {
            // Blank page (no source doc) — just fill with the color directly
            ctx.clearRect(0, 0, w, h);
            ctx.fillStyle = (color && color !== '' && color !== '#ffffff' && color !== '#FFFFFF') ? color : '#ffffff';
            ctx.fillRect(0, 0, w, h);
        }

        // Notify Blazor
        if (this.dotNetRef) this.dotNetRef.invokeMethodAsync('OnAnnotationChanged');
    },

    // ── Dual-page (side-by-side) mode ──

    enterDualMode(paneId) {
        const pane = this.panes[paneId];
        if (!pane || !pane.pages.length) return [0, 0];
        const viewer = document.getElementById('pcf-viewer-' + paneId);
        if (!viewer) return [0, 0];

        pane.dualMode = true;
        pane.dualPage = 0;

        // Scale so each page fits half the viewer width AND the viewer height
        const zl             = pane.zoomLevel || 1;
        const renderedWidth  = pane.pages[0].canvas.width  * zl;
        const renderedHeight = pane.pages[0].canvas.height * zl;
        const scaleByWidth   = (viewer.clientWidth  / 2 - 24) / renderedWidth;
        const scaleByHeight  = (viewer.clientHeight - 40)     / renderedHeight;  // 40 = 20px top + 20px bottom padding
        pane.dualScale       = Math.min(1, scaleByWidth, scaleByHeight);

        viewer.style.flexDirection  = 'row';
        viewer.style.alignItems     = 'center';
        viewer.style.justifyContent = 'center';
        viewer.style.flexWrap       = 'nowrap';
        viewer.classList.add('pcf-dual-mode');

        this._applyDualVisibility(paneId);
        return [1, pane.pages.length];
    },

    exitDualMode(paneId) {
        const pane = this.panes[paneId];
        if (!pane) return;
        const viewer = document.getElementById('pcf-viewer-' + paneId);

        pane.dualMode  = false;
        pane.dualScale = 1;

        if (viewer) {
            viewer.style.flexDirection  = '';
            viewer.style.alignItems     = '';
            viewer.style.justifyContent = '';
            viewer.style.flexWrap       = '';
            viewer.classList.remove('pcf-dual-mode');
        }

        const zl = pane.zoomLevel || 1;
        for (const pg of pane.pages) {
            const wrapper = pg.canvas.parentElement;
            if (!wrapper) continue;
            wrapper.style.display = '';
            wrapper.style.zoom    = zl !== 1 ? String(zl) : '';
        }
    },

    dualPageNav(paneId, delta) {
        const pane = this.panes[paneId];
        if (!pane || !pane.dualMode) return [0, 0];
        const total      = pane.pages.length;
        const maxPage    = Math.floor((total - 1) / 2) * 2;   // last even starting index
        pane.dualPage    = Math.max(0, Math.min(pane.dualPage + delta * 2, maxPage));
        this._applyDualVisibility(paneId);
        return [pane.dualPage + 1, total];
    },

    _applyDualVisibility(paneId) {
        const pane = this.panes[paneId];
        if (!pane) return;
        const p0 = pane.dualPage;
        const p1 = p0 + 1;
        const ds = pane.dualScale || 1;
        const zl = pane.zoomLevel || 1;
        for (let i = 0; i < pane.pages.length; i++) {
            const wrapper = pane.pages[i].canvas.parentElement;
            if (!wrapper) continue;
            if (i === p0 || i === p1) {
                wrapper.style.display = '';
                wrapper.style.zoom    = String(ds * zl);
            } else {
                wrapper.style.display = 'none';
            }
        }
    },

    getPageCount(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        return pane && pane.pages ? pane.pages.length : 0;
    },

    attachPageContextMenu(paneId) {
        const viewer = document.getElementById('pcf-viewer-' + paneId);
        if (!viewer || viewer._ctxMenuAttached) return;
        viewer._ctxMenuAttached = true;
        viewer.addEventListener('contextmenu', (e) => {
            // Find which page was right-clicked
            const wrapper = e.target.closest('.pcf-page-wrapper');
            if (!wrapper) return;
            const pane = this.panes[paneId];
            if (!pane) return;
            const idx = Array.from(viewer.querySelectorAll('.pcf-page-wrapper')).indexOf(wrapper);
            if (idx < 0) return;
            e.preventDefault();
            this.activePane = paneId; // activate pane on right-click
            if (this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync('OnPageRightClick', paneId, idx, e.clientX, e.clientY);
            }
        });
    },

    // ── Float Page ──────────────────────────────────────────

    _floatWin: null,

    floatPage(paneId, pageIdx) {
        try {
        const pane = this.panes[paneId];
        if (!pane || !pane.pages.length) { console.warn('[pcfViewer] floatPage: no pages in pane', paneId); return; }
        const pg = pane.pages[Math.min(pageIdx, pane.pages.length - 1)];
        if (!pg) { console.warn('[pcfViewer] floatPage: page not found', pageIdx); return; }

        // Close any existing float first
        this.closeFloat();

        // Composite: page canvas + annotation overlay → single snapshot
        const offscreen = document.createElement('canvas');
        offscreen.width  = pg.canvas.width;
        offscreen.height = pg.canvas.height;
        const ctx = offscreen.getContext('2d');
        ctx.drawImage(pg.canvas,  0, 0);
        ctx.drawImage(pg.overlay, 0, 0);
        const dataUrl = offscreen.toDataURL('image/png');

        // Title: filename + page number
        const fileName = pane.filePath ? pane.filePath.split(/[\\/]/).pop() : 'Document';
        const title    = fileName + ' — Page ' + (pageIdx + 1) + '  (read only)';

        // Size the window so the page image fills it exactly (no black bars).
        // Overhead inside the window:
        //   title bar ≈ 33px (padding 6+6 + line ~20 + 1px border)
        //   body padding: 8px top + 8px bottom = 16px vertical, 8px left + 8px right = 16px horizontal
        //   outer border: 2px × 2 = 4px each axis
        const OVERHEAD_H = 53; // titleBar(33) + bodyPad_V(16) + border_V(4)
        const OVERHEAD_W = 20; // bodyPad_H(16) + border_H(4)
        const pageAspect = pg.canvas.height / pg.canvas.width; // H/W ratio

        const maxW = window.innerWidth  - 40;
        const maxH = window.innerHeight - 80;

        // Base width: one pane's width (or half-viewport for single pane)
        const viewerEl = document.getElementById('pcf-viewer-' + paneId);
        let defW = Math.round(window.innerWidth / 2);
        if (viewerEl) {
            const r = viewerEl.getBoundingClientRect();
            const paneIds = ['left', 'center', 'right'];
            let visiblePanes = 0;
            for (const pid of paneIds) {
                const vEl = document.getElementById('pcf-viewer-' + pid);
                if (vEl && vEl.offsetWidth > 0) visiblePanes++;
            }
            defW = visiblePanes >= 2 ? Math.round(r.width) : Math.round(window.innerWidth / 2);
        }
        defW = Math.max(200, Math.min(defW, maxW));

        // Height = exactly what the image needs to fill the body at this width
        let defH = Math.round(OVERHEAD_H + (defW - OVERHEAD_W) * pageAspect);

        // If that's too tall for the viewport, shrink width until it fits
        if (defH > maxH) {
            defW = Math.round(OVERHEAD_W + (maxH - OVERHEAD_H) / pageAspect);
            defH = maxH;
        }

        defW = Math.max(200, Math.min(defW, maxW));
        defH = Math.max(150, Math.min(defH, maxH));

        // Position: top-right, near the viewer area
        const posX = window.innerWidth  - defW - 20;
        const posY = 60;

        // Build the floating window element
        const win = document.createElement('div');
        win.id = 'pcf-float-win';
        win.style.cssText =
            'position:fixed;z-index:10007;top:' + posY + 'px;left:' + posX + 'px;' +
            'width:' + defW + 'px;height:' + defH + 'px;' +
            'display:flex;flex-direction:column;' +
            'border:2px solid #3b82f6;border-radius:6px;' +
            'background:#1e1e2e;box-shadow:0 8px 32px rgba(0,0,0,.6);' +
            'overflow:hidden;min-width:200px;min-height:150px;';

        // Title bar
        const titleBar = document.createElement('div');
        titleBar.style.cssText =
            'flex-shrink:0;cursor:move;background:#1e293b;color:#e2e8f0;' +
            'padding:6px 10px;display:flex;align-items:center;gap:8px;' +
            'font-size:13px;user-select:none;border-bottom:1px solid #334155;';
        titleBar.innerHTML =
            '<i class="ri-lock-line" style="color:#94a3b8;flex-shrink:0;font-size:14px;"></i>' +
            '<span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="' + title + '">' + title + '</span>' +
            '<button id="pcf-float-close" style="background:transparent;border:none;color:#94a3b8;' +
            'cursor:pointer;font-size:20px;line-height:1;padding:0 2px;" title="Close (Esc)">&times;</button>';

        // Body: scrollable image
        const body = document.createElement('div');
        body.style.cssText =
            'flex:1;overflow:auto;display:flex;justify-content:center;' +
            'align-items:flex-start;padding:8px;background:#0f172a;';

        const img = document.createElement('img');
        img.src = dataUrl;
        img.style.cssText = 'max-width:100%;height:auto;display:block;';
        img.draggable = false;
        body.appendChild(img);

        win.appendChild(titleBar);
        win.appendChild(body);
        document.body.appendChild(win);
        this._floatWin = win;

        // Close button
        win.querySelector('#pcf-float-close').addEventListener('click', () => this.closeFloat());

        // Drag via title bar
        titleBar.addEventListener('mousedown', (e) => {
            if (e.target.closest('button')) return;
            e.preventDefault();
            const sx = e.clientX, sy = e.clientY;
            const ox = parseInt(win.style.left) || 0, oy = parseInt(win.style.top) || 0;
            const onMv = (e) => {
                win.style.left = Math.max(0, ox + e.clientX - sx) + 'px';
                win.style.top  = Math.max(0, oy + e.clientY - sy) + 'px';
            };
            const onUp = () => { document.removeEventListener('mousemove', onMv); document.removeEventListener('mouseup', onUp); };
            document.addEventListener('mousemove', onMv);
            document.addEventListener('mouseup', onUp);
        });

        // Edge + corner resize handles (8 directions)
        const resizeHandles = [
            ['n',  'top:0;left:6px;right:6px;height:5px;cursor:n-resize;'],
            ['s',  'bottom:0;left:6px;right:6px;height:5px;cursor:s-resize;'],
            ['e',  'right:0;top:6px;bottom:6px;width:5px;cursor:e-resize;'],
            ['w',  'left:0;top:6px;bottom:6px;width:5px;cursor:w-resize;'],
            ['ne', 'top:0;right:0;width:10px;height:10px;cursor:ne-resize;'],
            ['nw', 'top:0;left:0;width:10px;height:10px;cursor:nw-resize;'],
            ['se', 'bottom:0;right:0;width:10px;height:10px;cursor:se-resize;'],
            ['sw', 'bottom:0;left:0;width:10px;height:10px;cursor:sw-resize;'],
        ];
        for (const [pos, hStyle] of resizeHandles) {
            const h = document.createElement('div');
            h.style.cssText = 'position:absolute;z-index:2;' + hStyle;
            h.addEventListener('mousedown', (e) => {
                e.preventDefault(); e.stopPropagation();
                const sx = e.clientX, sy = e.clientY;
                const r = win.getBoundingClientRect();
                const ol = r.left, ot = r.top, ow = r.width, oh = r.height;
                const onMv = (e) => {
                    const dx = e.clientX - sx, dy = e.clientY - sy;
                    let nl = ol, nt = ot, nw = ow, nh = oh;
                    if (pos.includes('e')) nw = Math.max(200, ow + dx);
                    if (pos.includes('s')) nh = Math.max(150, oh + dy);
                    if (pos.includes('w')) { nw = Math.max(200, ow - dx); nl = ol + (ow - nw); }
                    if (pos.includes('n')) { nh = Math.max(150, oh - dy); nt = ot + (oh - nh); }
                    win.style.left = nl + 'px'; win.style.top  = nt + 'px';
                    win.style.width = nw + 'px'; win.style.height = nh + 'px';
                };
                const onUp = () => { document.removeEventListener('mousemove', onMv); document.removeEventListener('mouseup', onUp); };
                document.addEventListener('mousemove', onMv);
                document.addEventListener('mouseup', onUp);
            });
            win.appendChild(h);
        }

        } catch (e) { console.error('[pcfViewer] floatPage error:', e); }
    },

    closeFloat() {
        if (this._floatWin) {
            this._floatWin.remove();
            this._floatWin = null;
        }
    }
};

// ── Auto-save on tab close / navigation ──
window.addEventListener('beforeunload', function () {
    window.pcfViewer.saveAllSync();
});

// ── Close float window on Escape ──
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && window.pcfViewer._floatWin) {
        window.pcfViewer.closeFloat();
    }
});
