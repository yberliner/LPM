(function () {
    if (window.__spkHoverCardInit) return;
    window.__spkHoverCardInit = true;

    const HIDE_DELAY_MS = 160; // enough time to cross the gap from trigger to card
    const pendingHides = new WeakMap();

    function cancelHide(trigger) {
        const h = pendingHides.get(trigger);
        if (h) { clearTimeout(h); pendingHides.delete(trigger); }
    }

    function getCard(trigger) {
        // Card is a direct child of .spk-hc
        for (const child of trigger.children) {
            if (child.classList && child.classList.contains('spk-hc-card')) return child;
        }
        return null;
    }

    function positionCard(trigger, card) {
        const rect = trigger.getBoundingClientRect();
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        const margin = 8;

        // visibility:hidden preserves layout, so offsetWidth/Height are already measurable
        const cardW = card.offsetWidth || 300;
        const cardH = card.offsetHeight || 200;

        let placement = 'top';
        let top = rect.top - 10 - cardH;
        if (top < margin) {
            placement = 'bottom';
            top = rect.bottom + 10;
        }

        let left = rect.left + rect.width / 2 - cardW / 2;
        if (left < margin) left = margin;
        if (left + cardW > vw - margin) left = vw - margin - cardW;

        if (top + cardH > vh - margin) top = Math.max(margin, vh - margin - cardH);

        card.style.setProperty('--spk-hc-top', top + 'px');
        card.style.setProperty('--spk-hc-left', left + 'px');
        card.dataset.placement = placement;
    }

    function show(trigger) {
        if (!trigger.classList.contains('spk-hc-active')) return;
        cancelHide(trigger);
        const card = getCard(trigger);
        if (!card) return;
        // Only (re)position when going from hidden → visible. Prevents flicker from
        // repeated mouseover events while the pointer moves inside the trigger.
        if (!card.classList.contains('spk-hc-visible')) {
            positionCard(trigger, card);
            card.classList.add('spk-hc-visible');
        }
    }

    function scheduleHide(trigger) {
        cancelHide(trigger);
        const h = setTimeout(() => {
            pendingHides.delete(trigger);
            const card = getCard(trigger);
            if (!card) return;
            // Keep visible if either the trigger or the card is still pointed/focused
            if (trigger.matches(':hover') || trigger.matches(':focus-within')) return;
            if (card.matches(':hover')) return;
            card.classList.remove('spk-hc-visible');
        }, HIDE_DELAY_MS);
        pendingHides.set(trigger, h);
    }

    function findTrigger(el) {
        if (!el || !el.closest) return null;
        return el.closest('.spk-hc.spk-hc-active');
    }

    // Delegated listeners on document — robust across Blazor re-renders
    document.addEventListener('mouseover', (e) => {
        const t = findTrigger(e.target);
        if (t) show(t);
    });
    document.addEventListener('mouseout', (e) => {
        const t = findTrigger(e.target);
        if (!t) return;
        // mouseout fires when crossing into a child; only hide on leaving the trigger/card entirely
        const related = e.relatedTarget;
        if (related && (t.contains(related) || (getCard(t)?.contains(related)))) return;
        scheduleHide(t);
    });

    // Keyboard / touch: focus-based show-hide
    document.addEventListener('focusin', (e) => {
        const t = findTrigger(e.target);
        if (t) show(t);
    });
    document.addEventListener('focusout', (e) => {
        const t = findTrigger(e.target);
        if (t) scheduleHide(t);
    });

    // Hide-on-scroll / resize — positions would otherwise drift
    const hideAll = () => {
        document.querySelectorAll('.spk-hc-card.spk-hc-visible').forEach(c => {
            c.classList.remove('spk-hc-visible');
        });
    };
    window.addEventListener('scroll', hideAll, true);
    window.addEventListener('resize', hideAll);
})();
