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

    _initPane(paneId) {
        if (!this.panes[paneId]) {
            this.panes[paneId] = {
                pdfDoc: null,
                pages: [],
                annotations: [],
                currentStroke: null,
                filePath: null
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

        // Calculate scale to fit viewer width (minus padding)
        const viewerWidth = viewer.clientWidth - 40; // 20px padding each side
        const firstPage = await pane.pdfDoc.getPage(1);
        const defaultVp = firstPage.getViewport({ scale: 1 });
        const fitScale = Math.min(viewerWidth / defaultVp.width, 3); // cap at 3x
        const scale = Math.max(fitScale, 0.5); // min 0.5x

        for (let i = 0; i < pane.pdfDoc.numPages; i++) {
            const page = i === 0 ? firstPage : await pane.pdfDoc.getPage(i + 1);
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

        // Clicking anywhere in a pane makes it active
        overlay.addEventListener('pointerdown', (e) => {
            self.activePane = paneId;
            const pane = self.panes[paneId];
            if (!pane) return;

            if (self.textInputEl) return;

            if (self.toolMode === 'draw') {
                drawing = true;
                overlay.setPointerCapture(e.pointerId);
                const r = overlay.getBoundingClientRect();
                const x = e.clientX - r.left;
                const y = e.clientY - r.top;
                pane.currentStroke = { pageIdx, points: [{ x, y }], color: self.drawColor, width: self.drawWidth };
            } else if (self.toolMode === 'text') {
                const r = overlay.getBoundingClientRect();
                const x = e.clientX - r.left;
                const y = e.clientY - r.top;
                self._showTextInput(overlay.parentElement, pageIdx, x, y, paneId);
            }
        });

        overlay.addEventListener('pointermove', (e) => {
            const pane = self.panes[paneId];
            if (!drawing || !pane || !pane.currentStroke) return;
            const r = overlay.getBoundingClientRect();
            const x = e.clientX - r.left;
            const y = e.clientY - r.top;
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
        ctx.fillText(ann.text, ann.x, ann.y);
    },

    _showTextInput(wrapper, pageIdx, x, y, paneId) {
        if (this.textInputEl) {
            this.textInputEl.remove();
            this.textInputEl = null;
        }

        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'pcf-text-input';
        input.style.left = x + 'px';
        input.style.top = (y - 18) + 'px';
        input.placeholder = 'Type here...';

        const color = this.drawColor;
        const fontSize = this.fontSize;
        input.style.color = color;
        input.style.fontSize = fontSize + 'px';

        wrapper.appendChild(input);
        this.textInputEl = input;

        input.addEventListener('pointerdown', (e) => e.stopPropagation());

        const self = this;
        let committed = false;
        const commit = () => {
            if (committed) return;
            committed = true;
            const text = input.value.trim();
            if (text) {
                const pane = self.panes[paneId];
                if (pane) {
                    pane.annotations.push({ pageIdx, type: 'text', text, x, y, color: color, fontSize: fontSize });
                    self._redrawOverlay(pageIdx, paneId);
                    self._notifyChange();
                }
            }
            input.remove();
            self.textInputEl = null;
        };

        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') commit();
            if (e.key === 'Escape') { input.remove(); self.textInputEl = null; }
        });
        input.addEventListener('blur', commit);

        requestAnimationFrame(() => input.focus());
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
            // Remove the blank page from DOM and pane.pages
            const pg = pane.pages.pop();
            if (pg && pg.canvas && pg.canvas.parentElement) {
                pg.canvas.parentElement.remove();
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

    addBlankPage(paneId) {
        const pane = this.panes[paneId];
        if (!pane || pane.pages.length === 0) return;

        // Use same dimensions as the last page
        const lastPg = pane.pages[pane.pages.length - 1];
        const w = lastPg.canvas.width;
        const h = lastPg.canvas.height;

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

        viewer.appendChild(wrapper);

        const pageIdx = pane.pages.length;
        pane.pages.push({ canvas, overlay, vp: lastPg.vp, pageIdx, scale: lastPg.scale });
        this._attachEvents(overlay, pageIdx, paneId);

        // Mark as annotation so it gets saved
        pane.annotations.push({ type: 'blank-page', pageIdx });

        // Scroll to the new page
        wrapper.scrollIntoView({ behavior: 'smooth', block: 'end' });

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
