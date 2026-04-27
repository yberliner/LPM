// Statistics page — hover breakdown tooltip.
// Attaches to any element with data-stat-metric / data-stat-staff-id / data-stat-start / data-stat-end.
// Fetches per-cell detail on hover, renders a floating card with entrance/exit animation,
// and caches results per (metric, staff, start, end) for the page session.
(function () {
    if (window.__lpmStatsTooltipInstalled) return;
    window.__lpmStatsTooltipInstalled = true;

    const cache = new Map();
    let currentTip = null;
    let currentCell = null;
    let showTimer = null;
    let hideTimer = null;
    let tipHover = false;
    let dotNetRef = null;  // DotNetObjectReference<Statistics> from Blazor — set by register()

    // Public API for Blazor to register / unregister the .NET callback target.
    window.lpmStatsTooltip = {
        register(ref) { dotNetRef = ref; },
        unregister() { dotNetRef = null; }
    };

    const LABEL = {
        audit:  { title: 'Auditing breakdown',     badgeClass: 'stat-chip-audit'  },
        csolo:  { title: 'Solo CS breakdown',      badgeClass: 'stat-chip-csolo'  },
        cs:     { title: 'CS breakdown',           badgeClass: 'stat-chip-cs'     },
        effort: { title: 'Extra Effort breakdown', badgeClass: 'stat-chip-effort' },
        total:  { title: 'Total time calculation', badgeClass: 'stat-chip-total'  },
    };

    function pad2(n) { return n < 10 ? '0' + n : '' + n; }

    // Format seconds as h:mm — hour is NOT zero-padded, minutes are. Seconds intentionally dropped.
    function fmt(sec) {
        sec = sec | 0;
        if (sec < 0) sec = 0;
        const h = (sec / 3600) | 0;
        const m = ((sec % 3600) / 60) | 0;
        return h + ':' + pad2(m);
    }

    function fmtDate(iso) {
        // "YYYY-MM-DD" → "DD/MM/YYYY" dd/mm/yyyy
        if (!iso || iso.length < 10) return iso || '';
        return iso.substring(8, 10) + '/' + iso.substring(5, 7) + '/' + iso.substring(0, 4);
    }

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    function fetchDetail(metric, staffId, start, end) {
        const key = metric + '|' + staffId + '|' + start + '|' + end;
        if (cache.has(key)) return cache.get(key);
        const p = fetch('/api/stats-cell-detail?metric=' + encodeURIComponent(metric)
                + '&staffId=' + encodeURIComponent(staffId)
                + '&start=' + encodeURIComponent(start)
                + '&end=' + encodeURIComponent(end))
            .then(r => r.ok ? r.json() : Promise.reject(r.status))
            .catch(err => { cache.delete(key); throw err; });
        cache.set(key, p);
        return p;
    }

    function buildRangeLabel(start, end) {
        if (start === end) return fmtDate(start);
        return fmtDate(start) + ' – ' + fmtDate(end);
    }

    function buildTotalHtml(staffName, audit, csolo, cs, effort, total) {
        const meta = LABEL.total;
        const row = (cls, label, sec, op, note) => `
            <div class="stat-formula-row ${cls}">
              <span class="stat-formula-label">
                <span class="stat-formula-dot d-${cls.replace('fr-','')}"></span>
                ${esc(label)}
                ${op ? `<span class="stat-formula-op ${op === '+' ? 'op-plus' : 'op-excl'}">${op === '+' ? '+' : op}</span>` : ''}
                ${note ? `<span class="stat-formula-note">${esc(note)}</span>` : ''}
              </span>
              <span class="stat-formula-value">${fmt(sec)}</span>
            </div>`;
        return `
          <div class="stat-tooltip-inner">
            <div class="stat-tooltip-header ${meta.badgeClass}">
              <div class="stat-tooltip-title">${esc(meta.title)}</div>
              <div class="stat-tooltip-subtitle">${esc(staffName)}</div>
            </div>
            <div class="stat-tooltip-body">
              <div class="stat-formula">
                ${row('fr-audit',  'Auditing',     audit,  audit  > 0 ? '+' : '')}
                ${row('fr-csolo',  'CS Solo',      csolo,  csolo  > 0 ? '+' : '')}
                ${row('fr-effort', 'Extra Effort', effort, effort > 0 ? '+' : '')}
                ${row('fr-cs',     'CS',           cs,     'not counted', 'informational only')}
              </div>
              <div class="stat-formula-divider"></div>
              <div class="stat-formula">
                <div class="stat-formula-sum">
                  <span class="stat-formula-sum-label">Total</span>
                  <span class="stat-formula-sum-value">${fmt(total)}</span>
                </div>
              </div>
            </div>
          </div>`;
    }

    function buildTipHtml(metric, staffName, rangeLabel, data) {
        const meta = LABEL[metric] || { title: 'Breakdown', badgeClass: '' };
        const rows = (data.items || []).map((it, i) => {
            const sid = (it.sessionId != null && it.sessionId > 0) ? it.sessionId : 0;
            const clickAttrs = sid > 0
                ? ` class="stat-tt-clickable" data-session-id="${sid}" title="Open session ${sid} in Session Manager"`
                : '';
            return `
            <tr${clickAttrs}>
                <td class="stat-tt-idx">${i + 1}</td>
                <td class="stat-tt-pc">${esc(it.pcName)}</td>
                <td class="stat-tt-date">${fmtDate(it.date)}</td>
                <td class="stat-tt-dur">${fmt(it.seconds)}</td>
            </tr>`;
        }).join('');
        const empty = (data.items && data.items.length)
            ? ''
            : `<tr><td colspan="4" class="stat-tt-empty">No matching records in this range.</td></tr>`;
        return `
          <div class="stat-tooltip-inner">
            <div class="stat-tooltip-header ${meta.badgeClass}">
              <div class="stat-tooltip-title">${esc(meta.title)}</div>
              <div class="stat-tooltip-subtitle">${esc(staffName)} &middot; ${esc(rangeLabel)}</div>
            </div>
            <div class="stat-tooltip-body">
              <table class="stat-tooltip-table">
                <thead>
                  <tr>
                    <th class="stat-tt-idx">#</th>
                    <th class="stat-tt-pc">PC</th>
                    <th class="stat-tt-date">Date</th>
                    <th class="stat-tt-dur">Duration</th>
                  </tr>
                </thead>
                <tbody>${rows}${empty}</tbody>
              </table>
            </div>
            <div class="stat-tooltip-footer">
              <span class="stat-tt-count">${(data.items || []).length} item${(data.items || []).length === 1 ? '' : 's'}</span>
              <span class="stat-tt-sum">Σ ${fmt(data.totalSec || 0)}</span>
            </div>
          </div>`;
    }

    function positionTip(tip, cell) {
        const rect = cell.getBoundingClientRect();
        const tipRect = tip.getBoundingClientRect();
        const vw = window.innerWidth, vh = window.innerHeight;
        const margin = 12;

        // Horizontal: center on cell, clamp to viewport.
        let left = rect.left + rect.width / 2 - tipRect.width / 2;
        left = Math.max(margin, Math.min(left, vw - tipRect.width - margin));

        // Vertical: prefer below; flip above if not enough room.
        let top = rect.bottom + 10;
        let placement = 'below';
        if (top + tipRect.height > vh - margin) {
            const aboveTop = rect.top - tipRect.height - 10;
            if (aboveTop >= margin) { top = aboveTop; placement = 'above'; }
        }

        tip.style.left = left + 'px';
        tip.style.top = top + 'px';
        tip.classList.remove('stat-tooltip-below', 'stat-tooltip-above');
        tip.classList.add('stat-tooltip-' + placement);

        // Arrow horizontal offset relative to tip.
        const arrowX = rect.left + rect.width / 2 - left;
        tip.style.setProperty('--stat-tip-arrow-x', Math.max(16, Math.min(arrowX, tipRect.width - 16)) + 'px');
    }

    function destroyTip(tip) {
        if (!tip || tip.parentNode == null) return;
        tip.classList.remove('stat-tooltip-show');
        tip.classList.add('stat-tooltip-hide');
        setTimeout(() => { if (tip.parentNode) tip.parentNode.removeChild(tip); }, 160);
    }

    function hideCurrent() {
        if (currentCell) currentCell.classList.remove('stat-cell-active');
        currentCell = null;
        if (currentTip) { destroyTip(currentTip); currentTip = null; }
    }

    function showTotal(cell, staffName) {
        const ds = cell.dataset;
        const audit  = parseInt(ds.statAudit  || '0', 10) || 0;
        const csolo  = parseInt(ds.statCsolo  || '0', 10) || 0;
        const cs     = parseInt(ds.statCs     || '0', 10) || 0;
        const effort = parseInt(ds.statEffort || '0', 10) || 0;
        const total  = parseInt(ds.statTotal  || '0', 10) || (audit + csolo + effort);

        if (currentTip) { destroyTip(currentTip); currentTip = null; }
        if (currentCell) currentCell.classList.remove('stat-cell-active');

        const tip = document.createElement('div');
        tip.className = 'stat-tooltip';
        tip.innerHTML = buildTotalHtml(staffName, audit, csolo, cs, effort, total);
        tip.addEventListener('mouseenter', () => { tipHover = true; clearTimeout(hideTimer); });
        tip.addEventListener('mouseleave', () => { tipHover = false; scheduleHide(); });
        document.body.appendChild(tip);
        currentTip = tip;
        currentCell = cell;
        cell.classList.add('stat-cell-active');

        positionTip(tip, cell);
        requestAnimationFrame(() => tip.classList.add('stat-tooltip-show'));
    }

    function showFor(cell) {
        const ds = cell.dataset;
        const metric = ds.statMetric;
        const staffId = ds.statStaffId;
        const start = ds.statStart;
        const end = ds.statEnd;
        const staffName = ds.statStaffName || '';
        if (!metric) return;

        // Total cell: client-side formula, no fetch, no staff-id / date range.
        if (metric === 'total') {
            showTotal(cell, staffName);
            return;
        }

        if (!staffId || !start || !end) return;

        // Skip cells that display '–' (no data) — avoid empty tooltip.
        if (!cell.querySelector('.badge')) return;

        // Replace any existing tooltip immediately.
        if (currentTip) { destroyTip(currentTip); currentTip = null; }
        if (currentCell) currentCell.classList.remove('stat-cell-active');

        const tip = document.createElement('div');
        tip.className = 'stat-tooltip';
        tip.innerHTML = `<div class="stat-tooltip-inner"><div class="stat-tooltip-loading">Loading…</div></div>`;
        tip.addEventListener('mouseenter', () => {
            tipHover = true;
            clearTimeout(hideTimer);
        });
        tip.addEventListener('mouseleave', () => {
            tipHover = false;
            scheduleHide();
        });
        // Delegated click on a row → open that session in Session Manager via Blazor.
        tip.addEventListener('click', (e) => {
            const row = e.target.closest && e.target.closest('tr.stat-tt-clickable');
            if (!row) return;
            const sid = parseInt(row.getAttribute('data-session-id') || '0', 10);
            if (!sid) return;
            if (dotNetRef) {
                try { dotNetRef.invokeMethodAsync('OpenSession', sid); } catch (_) { /* circuit gone */ }
            }
            hideCurrent();
        });
        document.body.appendChild(tip);
        currentTip = tip;
        currentCell = cell;
        cell.classList.add('stat-cell-active');

        // Initial positioning before fade-in so animation originates near the cell.
        positionTip(tip, cell);
        requestAnimationFrame(() => tip.classList.add('stat-tooltip-show'));

        fetchDetail(metric, staffId, start, end).then(data => {
            if (currentTip !== tip) return; // superseded by another cell
            tip.innerHTML = buildTipHtml(metric, staffName, buildRangeLabel(start, end), data || { items: [], totalSec: 0 });
            // Re-measure + re-position now that the real content is in.
            positionTip(tip, cell);
        }).catch(() => {
            if (currentTip !== tip) return;
            tip.innerHTML = `<div class="stat-tooltip-inner"><div class="stat-tooltip-error">Failed to load detail.</div></div>`;
            positionTip(tip, cell);
        });
    }

    function scheduleHide() {
        clearTimeout(hideTimer);
        hideTimer = setTimeout(() => {
            if (tipHover) return;
            hideCurrent();
        }, 180);
    }

    // Delegate with capturing + manual target resolution (mouseenter doesn't bubble).
    document.addEventListener('mouseover', (e) => {
        const cell = e.target.closest && e.target.closest('[data-stat-metric]');
        if (!cell) return;
        if (cell === currentCell) { clearTimeout(hideTimer); return; }
        clearTimeout(showTimer);
        clearTimeout(hideTimer);
        showTimer = setTimeout(() => showFor(cell), 140);
    });

    document.addEventListener('mouseout', (e) => {
        const cell = e.target.closest && e.target.closest('[data-stat-metric]');
        if (!cell) return;
        // If moving to the tooltip itself, don't hide.
        const rel = e.relatedTarget;
        if (rel && (rel.closest && rel.closest('.stat-tooltip'))) return;
        clearTimeout(showTimer);
        scheduleHide();
    });

    // Touch support: tap to open, tap outside to close.
    document.addEventListener('click', (e) => {
        const cell = e.target.closest && e.target.closest('[data-stat-metric]');
        if (cell) {
            if (cell === currentCell) { hideCurrent(); return; }
            clearTimeout(showTimer);
            clearTimeout(hideTimer);
            showFor(cell);
            return;
        }
        if (currentTip && !(e.target.closest && e.target.closest('.stat-tooltip'))) {
            hideCurrent();
        }
    });

    // Hide on page scroll / resize to avoid floating in wrong spot.
    // Use capture so we see scrolls from any element, but ignore scrolls that
    // originate inside the tooltip itself — otherwise scrolling the tooltip's
    // own list closes it before the user can reach the bottom.
    window.addEventListener('scroll', (e) => {
        const t = e.target;
        if (t instanceof Element && t.closest && t.closest('.stat-tooltip')) return;
        hideCurrent();
    }, true);
    window.addEventListener('resize', hideCurrent);
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') hideCurrent(); });
})();
