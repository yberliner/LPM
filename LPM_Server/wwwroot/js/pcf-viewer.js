// PC Folder PDF viewer with annotation support
window.pcfViewer = {
    pdfDoc: null,
    pages: [],
    toolMode: null,
    annotations: [],
    currentStroke: null,
    drawColor: '#e11d48',
    drawWidth: 2.5,
    fontSize: 14,
    textInputEl: null,
    dotNetRef: null,

    setDotNetRef(ref) { this.dotNetRef = ref; },

    setColor(color) { this.drawColor = color; },
    setFontSize(size) { this.fontSize = size; },

    async loadPdf(url) {
        const viewer = document.getElementById('pcf-viewer');
        if (!viewer) return;
        viewer.innerHTML = '';
        this.pages = [];
        this.annotations = [];
        this.currentStroke = null;

        const pdfjsLib = window['pdfjs-dist/build/pdf'];
        if (!pdfjsLib) { viewer.innerHTML = '<div style="color:#fff;padding:40px;">PDF.js not loaded</div>'; return; }

        try {
            this.pdfDoc = await pdfjsLib.getDocument(url).promise;
        } catch (e) {
            viewer.innerHTML = '<div style="color:#fff;padding:40px;">Failed to load PDF: ' + e.message + '</div>';
            return;
        }

        for (let i = 0; i < this.pdfDoc.numPages; i++) {
            const page = await this.pdfDoc.getPage(i + 1);
            const scale = 1.5;
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

            this.pages.push({ canvas, overlay, vp, pageIdx: i });
            this._attachEvents(overlay, i);
        }
    },

    _attachEvents(overlay, pageIdx) {
        const self = this;
        let drawing = false;

        overlay.addEventListener('pointerdown', (e) => {
            // Ignore if a text input is active
            if (self.textInputEl) return;

            if (self.toolMode === 'draw') {
                drawing = true;
                overlay.setPointerCapture(e.pointerId);
                const r = overlay.getBoundingClientRect();
                const x = e.clientX - r.left;
                const y = e.clientY - r.top;
                self.currentStroke = { pageIdx, points: [{ x, y }], color: self.drawColor, width: self.drawWidth };
            } else if (self.toolMode === 'text') {
                const r = overlay.getBoundingClientRect();
                const x = e.clientX - r.left;
                const y = e.clientY - r.top;
                self._showTextInput(overlay.parentElement, pageIdx, x, y);
            }
        });

        overlay.addEventListener('pointermove', (e) => {
            if (!drawing || !self.currentStroke) return;
            const r = overlay.getBoundingClientRect();
            const x = e.clientX - r.left;
            const y = e.clientY - r.top;
            self.currentStroke.points.push({ x, y });
            self._redrawOverlay(pageIdx);
        });

        overlay.addEventListener('pointerup', () => {
            if (drawing && self.currentStroke && self.currentStroke.points.length > 1) {
                self.annotations.push({ pageIdx, type: 'draw', stroke: self.currentStroke });
                self._notifyChange();
            }
            drawing = false;
            self.currentStroke = null;
        });
    },

    _redrawOverlay(pageIdx) {
        const pg = this.pages[pageIdx];
        if (!pg) return;
        const ctx = pg.overlay.getContext('2d');
        ctx.clearRect(0, 0, pg.overlay.width, pg.overlay.height);

        for (const ann of this.annotations) {
            if (ann.pageIdx !== pageIdx) continue;
            if (ann.type === 'draw') this._drawStroke(ctx, ann.stroke);
            if (ann.type === 'text') this._drawText(ctx, ann);
        }

        if (this.currentStroke && this.currentStroke.pageIdx === pageIdx) {
            this._drawStroke(ctx, this.currentStroke);
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

    _showTextInput(wrapper, pageIdx, x, y) {
        // Remove existing
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

        // Prevent clicks on the input from bubbling to the overlay
        input.addEventListener('pointerdown', (e) => e.stopPropagation());

        const self = this;
        let committed = false;
        const commit = () => {
            if (committed) return;
            committed = true;
            const text = input.value.trim();
            if (text) {
                self.annotations.push({ pageIdx, type: 'text', text, x, y, color: color, fontSize: fontSize });
                self._redrawOverlay(pageIdx);
                self._notifyChange();
            }
            input.remove();
            self.textInputEl = null;
        };

        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') commit();
            if (e.key === 'Escape') { input.remove(); self.textInputEl = null; }
        });
        input.addEventListener('blur', commit);

        // Focus after a tiny delay to avoid immediate blur
        requestAnimationFrame(() => input.focus());
    },

    _notifyChange() {
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('OnAnnotationChanged');
        }
    },

    setTool(mode) {
        this.toolMode = mode;
        for (const pg of this.pages) {
            pg.overlay.style.cursor = mode === 'draw' ? 'crosshair' : mode === 'text' ? 'text' : 'default';
        }
    },

    undo() {
        if (this.annotations.length === 0) return;
        const removed = this.annotations.pop();
        this._redrawOverlay(removed.pageIdx);
    },

    hasAnnotations() {
        return this.annotations.length > 0;
    },

    async getAnnotatedPdf() {
        const finalCanvas = document.createElement('canvas');
        const pages = [];

        for (const pg of this.pages) {
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
    }
};
