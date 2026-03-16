// PC Folder PDF viewer with annotation support — multi-pane
window.pcfViewer = {
    panes: {},       // keyed by paneId ('left', 'right')
    activePane: 'left',
    toolMode: null,
    drawColor: '#e11d48',
    drawWidth: 2.5,
    fontSize: 14,
    textInputEl: null,
    dotNetRef: null,
    _pcId: 0,

    _zoomLevels: [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.0, 2.5, 3.0],

    _initPane(paneId) {
        if (!this.panes[paneId]) {
            this.panes[paneId] = {
                pdfDoc: null,
                pages: [],
                annotations: [],
                currentStroke: null,
                filePath: null,
                baseScale: 1,
                zoomLevel: 1.0
            };
        }
        return this.panes[paneId];
    },

    setDotNetRef(ref) { this.dotNetRef = ref; },
    setPcId(pcId) { this._pcId = pcId; },
    setColor(color) { this.drawColor = color; },
    setFontSize(size) { this.fontSize = size; },
    setActivePane(paneId) { this.activePane = paneId; },

    async loadPdf(url, paneId) {
        paneId = paneId || this.activePane;
        const pane = this._initPane(paneId);
        pane.zoomLevel = 1.0; // reset zoom on new PDF

        // Extract filePath from the URL for auto-save
        const u = new URL(url, location.origin);
        pane.filePath = u.searchParams.get('path');

        let viewer = document.getElementById('pcf-viewer-' + paneId);
        // Wait for the viewer element to exist and have a valid width
        for (let attempt = 0; attempt < 50 && (!viewer || viewer.clientWidth < 10); attempt++) {
            await new Promise(r => requestAnimationFrame(r));
            viewer = document.getElementById('pcf-viewer-' + paneId);
        }
        if (!viewer) return;
        viewer.innerHTML = '';
        pane.pages = [];
        pane.annotations = [];
        pane.currentStroke = null;

        const pdfjsLib = window['pdfjs-dist/build/pdf'];
        if (!pdfjsLib) { viewer.innerHTML = '<div style="color:#fff;padding:40px;">PDF.js not loaded</div>'; return; }

        try {
            pane.pdfDoc = await pdfjsLib.getDocument(url).promise;
        } catch (e) {
            viewer.innerHTML = '<div style="color:#fff;padding:40px;">Failed to load PDF: ' + e.message + '</div>';
            return;
        }

        // Scan ALL pages to find the widest one for scale calculation
        const allPages = [];
        let maxNaturalWidth = 0;
        for (let i = 1; i <= pane.pdfDoc.numPages; i++) {
            const page = await pane.pdfDoc.getPage(i);
            const naturalVp = page.getViewport({ scale: 1 });
            if (naturalVp.width > maxNaturalWidth) maxNaturalWidth = naturalVp.width;
            allPages.push(page);
        }

        // Calculate scale to fit the WIDEST page within viewer width
        const viewerWidth = viewer.clientWidth - 40; // 20px padding each side
        const fitScale = Math.min(viewerWidth / maxNaturalWidth, 3); // cap at 3x
        const scale = Math.max(fitScale, 0.5); // min 0.5x
        pane.baseScale = scale;

        for (let i = 0; i < allPages.length; i++) {
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

            const overlay = document.createElement('canvas');
            overlay.className = 'pcf-annotation-canvas';
            overlay.width = vp.width;
            overlay.height = vp.height;
            overlay.style.width = vp.width + 'px';
            overlay.style.height = vp.height + 'px';
            wrapper.appendChild(overlay);

            viewer.appendChild(wrapper);

            const ctx = canvas.getContext('2d');
            await page.render({ canvasContext: ctx, viewport: vp }).promise;

            pane.pages.push({ canvas, overlay, vp, pageIdx: i, scale });
            this._attachEvents(overlay, i, paneId);
        }
    },

    _attachEvents(overlay, pageIdx, paneId) {
        const self = this;
        let drawing = false;

        // Helper: get pointer position in canvas coordinates, accounting for CSS zoom
        function canvasXY(e) {
            const r = overlay.getBoundingClientRect();
            const zoom = (self.panes[paneId] && self.panes[paneId].zoomLevel) || 1;
            return {
                x: (e.clientX - r.left) / zoom,
                y: (e.clientY - r.top) / zoom
            };
        }

        // Clicking anywhere in a pane makes it active
        overlay.addEventListener('pointerdown', (e) => {
            self.activePane = paneId;
            const pane = self.panes[paneId];
            if (!pane) return;

            if (self.textInputEl) return;

            if (self.toolMode === 'draw') {
                drawing = true;
                overlay.setPointerCapture(e.pointerId);
                const { x, y } = canvasXY(e);
                pane.currentStroke = { pageIdx, points: [{ x, y }], color: self.drawColor, width: self.drawWidth };
            } else if (self.toolMode === 'text') {
                const { x, y } = canvasXY(e);
                self._showTextInput(overlay.parentElement, pageIdx, x, y, paneId);
            }
        });

        overlay.addEventListener('dblclick', (e) => {
            const pane = self.panes[paneId];
            if (!pane || self.textInputEl) return;
            const { x, y } = canvasXY(e);
            const hit = self._hitTestText(pane, pageIdx, x, y);
            if (hit) {
                e.preventDefault();
                self._showTextInput(overlay.parentElement, pageIdx, hit.ann.x, hit.ann.y, paneId, hit.ann, hit.idx);
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
    },

    _drawStroke(ctx, stroke) {
        if (stroke.points.length < 2) return;
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
    },

    _drawText(ctx, ann) {
        ctx.font = (ann.fontSize || 14) + 'px Arial';
        ctx.fillStyle = ann.color || '#1e293b';
        const lines = (ann.text || '').split('\n');
        const lineHeight = (ann.fontSize || 14) * 1.3;
        for (let i = 0; i < lines.length; i++) {
            ctx.fillText(lines[i], ann.x, ann.y + i * lineHeight);
        }
    },

    _hitTestText(pane, pageIdx, x, y) {
        // Create a temporary canvas context for measuring text
        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');
        for (let i = pane.annotations.length - 1; i >= 0; i--) {
            const ann = pane.annotations[i];
            if (ann.type !== 'text' || ann.pageIdx !== pageIdx) continue;
            const fontSize = ann.fontSize || 14;
            ctx.font = fontSize + 'px Arial';
            const lines = (ann.text || '').split('\n');
            const lineHeight = fontSize * 1.3;
            const totalHeight = lines.length * lineHeight;
            let maxWidth = 0;
            for (const line of lines) {
                const w = ctx.measureText(line).width;
                if (w > maxWidth) maxWidth = w;
            }
            // Text baseline is at ann.y, so bounding box goes from ann.y - fontSize to ann.y - fontSize + totalHeight
            const top = ann.y - fontSize;
            const bottom = top + totalHeight + 4;
            const left = ann.x - 4;
            const right = ann.x + maxWidth + 4;
            if (x >= left && x <= right && y >= top && y <= bottom) {
                return { ann, idx: i };
            }
        }
        return null;
    },

    _showTextInput(wrapper, pageIdx, x, y, paneId, existingAnn, existingIdx) {
        if (this.textInputEl) {
            this.textInputEl.remove();
            this.textInputEl = null;
        }

        const isEditing = existingAnn != null;
        const color = isEditing ? existingAnn.color : this.drawColor;
        const fontSize = isEditing ? (existingAnn.fontSize || 14) : this.fontSize;

        const input = document.createElement('textarea');
        input.className = 'pcf-text-input';
        input.rows = 1;
        input.style.left = x + 'px';
        input.style.top = (y - 18) + 'px';
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
                        // Update existing annotation
                        pane.annotations[existingIdx].text = text;
                    } else {
                        // Empty text = delete annotation
                        pane.annotations.splice(existingIdx, 1);
                    }
                } else if (text) {
                    // New annotation
                    pane.annotations.push({ pageIdx, type: 'text', text, x, y, color: color, fontSize: fontSize });
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
        for (const paneId in this.panes) {
            const pane = this.panes[paneId];
            for (const pg of pane.pages) {
                pg.overlay.style.cursor = mode === 'draw' ? 'crosshair' : mode === 'text' ? 'text' : 'default';
            }
        }
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

    async getAnnotatedPdf(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        if (!pane) return JSON.stringify([]);

        const finalCanvas = document.createElement('canvas');
        const pages = [];

        for (const pg of pane.pages) {
            const w = pg.canvas.width;
            const h = pg.canvas.height;
            finalCanvas.width = w;
            finalCanvas.height = h;
            const ctx = finalCanvas.getContext('2d');
            ctx.drawImage(pg.canvas, 0, 0);
            ctx.drawImage(pg.overlay, 0, 0);
            const dataUrl = finalCanvas.toDataURL('image/png');
            pages.push({ width: w, height: h, dataUrl });
        }
        return JSON.stringify(pages);
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
        for (const pg of pane.pages) {
            const wrapper = pg.canvas.parentElement;
            if (wrapper) wrapper.style.zoom = level;
        }
        return Math.round(level * 100);
    },

    getZoomPercent(paneId) {
        paneId = paneId || this.activePane;
        const pane = this.panes[paneId];
        return pane ? Math.round((pane.zoomLevel || 1) * 100) : 100;
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

        const finalCanvas = document.createElement('canvas');
        const pagesData = [];
        for (const pg of pane.pages) {
            const w = pg.canvas.width;
            const h = pg.canvas.height;
            finalCanvas.width = w;
            finalCanvas.height = h;
            const ctx = finalCanvas.getContext('2d');
            ctx.drawImage(pg.canvas, 0, 0);
            ctx.drawImage(pg.overlay, 0, 0);
            pagesData.push({ width: w, height: h, dataUrl: finalCanvas.toDataURL('image/png') });
        }

        // Use synchronous XHR to ensure it completes before tab closes
        const formData = new FormData();
        for (let i = 0; i < pagesData.length; i++) {
            const byteStr = atob(pagesData[i].dataUrl.split(',')[1]);
            const arr = new Uint8Array(byteStr.length);
            for (let j = 0; j < byteStr.length; j++) arr[j] = byteStr.charCodeAt(j);
            formData.append('page_' + i, new Blob([arr], { type: 'image/png' }), 'page_' + i + '.png');
        }
        formData.append('pageCount', pagesData.length.toString());
        formData.append('widths', JSON.stringify(pagesData.map(p => p.width)));
        formData.append('heights', JSON.stringify(pagesData.map(p => p.height)));

        const xhr = new XMLHttpRequest();
        xhr.open('POST', '/api/pc-file-save-annotated?pcId=' + this._pcId + '&path=' + encodeURIComponent(pane.filePath), false);
        xhr.send(formData);

        // Clear annotations after saving
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
        if (!pane || pane.annotations.length === 0 || !pane.filePath || !this._pcId) return;

        const pagesJson = await this.getAnnotatedPdf(paneId);
        await window.pcfSaveAnnotatedPdf(this._pcId, pane.filePath, pagesJson);
        pane.annotations = [];
    }
};

// ── Auto-save on tab close / navigation ──
window.addEventListener('beforeunload', function () {
    window.pcfViewer.saveAllSync();
});
