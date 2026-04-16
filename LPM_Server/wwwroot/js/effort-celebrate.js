(function () {
    if (window.lpmCelebrate) return;

    const palette = ['#ef4444','#f97316','#f59e0b','#eab308','#84cc16','#22c55e',
                     '#10b981','#14b8a6','#06b6d4','#0ea5e9','#3b82f6','#6366f1',
                     '#8b5cf6','#a855f7','#d946ef','#ec4899','#f43f5e'];

    function ensureStyles() {
        if (document.getElementById('lpm-celebrate-styles')) return;
        const css = `
            .lpm-celebrate-root {
                position:fixed; inset:0; pointer-events:none; z-index:20000; overflow:hidden;
            }
            .lpm-balloon {
                position:absolute; bottom:-80px; width:38px; height:48px;
                border-radius:50% 50% 46% 46% / 55% 55% 45% 45%;
                box-shadow: inset -4px -6px 0 rgba(0,0,0,.15);
                animation: lpm-balloon-float linear forwards;
            }
            .lpm-balloon::after {
                content:''; position:absolute; left:50%; top:100%;
                width:1px; height:70px; background:rgba(255,255,255,.6);
                transform:translateX(-50%);
            }
            @keyframes lpm-balloon-float {
                0%   { transform: translate3d(0, 0, 0) rotate(0deg); opacity:0; }
                8%   { opacity:1; }
                100% { transform: translate3d(var(--dx), calc(-100vh - 180px), 0) rotate(var(--rot)); opacity:1; }
            }
            .lpm-check-wrap {
                position:absolute; left:50%; top:50%;
                transform: translate(-50%, -50%);
                display:flex; align-items:center; justify-content:center;
                width:120px; height:120px; border-radius:50%;
                background: radial-gradient(circle at 30% 30%, #34d399, #059669);
                box-shadow: 0 14px 40px rgba(5,150,105,.5);
                animation: lpm-check-pop .7s cubic-bezier(.2,1.6,.35,1) both;
            }
            .lpm-check-wrap svg { width:68px; height:68px; stroke:#fff; stroke-width:6;
                fill:none; stroke-linecap:round; stroke-linejoin:round; }
            .lpm-check-path {
                stroke-dasharray: 60; stroke-dashoffset:60;
                animation: lpm-check-draw .5s .25s ease forwards;
            }
            @keyframes lpm-check-pop {
                0%   { transform: translate(-50%, -50%) scale(.3); opacity:0; }
                60%  { transform: translate(-50%, -50%) scale(1.1); opacity:1; }
                100% { transform: translate(-50%, -50%) scale(1); opacity:1; }
            }
            @keyframes lpm-check-draw { to { stroke-dashoffset:0; } }
            .lpm-check-wrap.fadeOut { animation: lpm-check-fade .6s ease forwards; }
            @keyframes lpm-check-fade { to { opacity:0; transform: translate(-50%, -50%) scale(.8); } }

            .lpm-toast-container {
                position:fixed; top:20px; right:20px; z-index:20001;
                display:flex; flex-direction:column; gap:10px; max-width:420px;
            }
            .lpm-toast {
                display:flex; align-items:center; gap:10px;
                padding:12px 16px; border-radius:10px;
                font-size:.9rem; color:#fff; font-weight:500;
                box-shadow: 0 10px 30px rgba(0,0,0,.25);
                animation: lpm-toast-in .25s ease-out, lpm-toast-out .3s 3.2s ease-in forwards;
                min-height:44px;
            }
            .lpm-toast.success { background: linear-gradient(135deg,#10b981,#059669); }
            .lpm-toast.error   { background: linear-gradient(135deg,#ef4444,#b91c1c); }
            .lpm-toast.info    { background: linear-gradient(135deg,#06b6d4,#0891b2); }
            @keyframes lpm-toast-in  { from { transform:translateX(120%); opacity:0; } to { transform:none; opacity:1; } }
            @keyframes lpm-toast-out { to   { transform:translateX(120%); opacity:0; } }
            @media (max-width: 480px) {
                .lpm-toast-container { left:10px; right:10px; top:10px; max-width:none; }
                .lpm-balloon { width:30px; height:38px; }
            }
        `;
        const s = document.createElement('style');
        s.id = 'lpm-celebrate-styles';
        s.textContent = css;
        document.head.appendChild(s);
    }

    function spawnBalloon(root, i) {
        const b = document.createElement('div');
        b.className = 'lpm-balloon';
        const x   = Math.random() * 100;                         // vw
        const dx  = (Math.random() - 0.5) * 160;                 // horizontal drift px
        const rot = (Math.random() - 0.5) * 40;                  // final rotation deg
        const dur = 3.6 + Math.random() * 2.4;                   // seconds
        const hue = palette[(Math.random() * palette.length) | 0];
        b.style.left = x + 'vw';
        b.style.background = `radial-gradient(circle at 30% 30%, #fff6, ${hue} 55%, ${hue})`;
        b.style.setProperty('--dx',  dx + 'px');
        b.style.setProperty('--rot', rot + 'deg');
        b.style.animationDuration = dur + 's';
        b.style.animationDelay    = (i * 0.03) + 's';
        root.appendChild(b);
    }

    window.lpmCelebrate = function () {
        try {
            ensureStyles();
            const root = document.createElement('div');
            root.className = 'lpm-celebrate-root';
            document.body.appendChild(root);

            // Balloons
            for (let i = 0; i < 40; i++) spawnBalloon(root, i);

            // Center checkmark
            const check = document.createElement('div');
            check.className = 'lpm-check-wrap';
            check.innerHTML = `<svg viewBox="0 0 64 64"><path class="lpm-check-path" d="M16 34 L28 46 L50 20"/></svg>`;
            root.appendChild(check);

            setTimeout(() => check.classList.add('fadeOut'), 1200);
            setTimeout(() => { try { root.remove(); } catch (_) {} }, 6200);
        } catch (e) { console.error('lpmCelebrate failed', e); }
    };

    function getToastContainer() {
        let c = document.querySelector('.lpm-toast-container');
        if (!c) {
            c = document.createElement('div');
            c.className = 'lpm-toast-container';
            document.body.appendChild(c);
        }
        return c;
    }

    // ── Wallet-create celebration: flying currency symbols + wallet pop ──
    window.lpmCelebrateWallet = function (currency) {
        try {
            ensureStyles();
            ensureWalletStyles();
            const symbol = currency === 'EUR' ? '€' : currency === 'USD' ? '$' : '₪';
            const hue    = currency === 'EUR' ? '#10b981' : currency === 'USD' ? '#0ea5e9' : '#3b82f6';

            const root = document.createElement('div');
            root.className = 'lpm-celebrate-root';
            document.body.appendChild(root);

            // Flying currency symbols (like confetti)
            for (let i = 0; i < 36; i++) {
                const s = document.createElement('div');
                s.className = 'lpm-currency-glyph';
                s.textContent = symbol;
                const x   = Math.random() * 100;
                const dx  = (Math.random() - 0.5) * 220;
                const rot = (Math.random() - 0.5) * 540;
                const dur = 2.8 + Math.random() * 1.6;
                s.style.left = x + 'vw';
                s.style.color = hue;
                s.style.setProperty('--dx',  dx + 'px');
                s.style.setProperty('--rot', rot + 'deg');
                s.style.animationDuration = dur + 's';
                s.style.animationDelay    = (i * 0.025) + 's';
                s.style.fontSize = (24 + Math.random() * 28) + 'px';
                root.appendChild(s);
            }

            // Center wallet card pop
            const card = document.createElement('div');
            card.className = 'lpm-wallet-pop';
            card.innerHTML = `
                <div class="lpm-wallet-card" style="background: linear-gradient(135deg, ${hue}, ${shade(hue, -0.25)});">
                    <div class="lpm-wallet-chip"></div>
                    <div class="lpm-wallet-currency">${symbol}</div>
                    <div class="lpm-wallet-label">New wallet</div>
                </div>
            `;
            root.appendChild(card);

            setTimeout(() => card.classList.add('fadeOut'), 1600);
            setTimeout(() => { try { root.remove(); } catch (_) {} }, 5200);
        } catch (e) { console.error('lpmCelebrateWallet failed', e); }
    };

    function shade(hex, pct) {
        const n = parseInt(hex.slice(1), 16);
        let r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
        const f = 1 + pct;
        r = Math.max(0, Math.min(255, Math.round(r * f)));
        g = Math.max(0, Math.min(255, Math.round(g * f)));
        b = Math.max(0, Math.min(255, Math.round(b * f)));
        return '#' + ((1 << 24) | (r << 16) | (g << 8) | b).toString(16).slice(1);
    }

    function ensureWalletStyles() {
        if (document.getElementById('lpm-wallet-styles')) return;
        const css = `
            .lpm-currency-glyph {
                position:absolute; bottom:-80px; font-weight:900;
                text-shadow: 0 3px 10px rgba(0,0,0,.25);
                animation: lpm-currency-float linear forwards;
            }
            @keyframes lpm-currency-float {
                0%   { transform: translate3d(0,0,0) rotate(0deg); opacity:0; }
                10%  { opacity:1; }
                100% { transform: translate3d(var(--dx), calc(-100vh - 160px), 0) rotate(var(--rot)); opacity:1; }
            }
            .lpm-wallet-pop {
                position:absolute; left:50%; top:50%;
                transform: translate(-50%, -50%);
                animation: lpm-wallet-pop .8s cubic-bezier(.2,1.7,.3,1) both;
            }
            .lpm-wallet-card {
                width:220px; height:140px; border-radius:16px;
                box-shadow: 0 24px 60px rgba(15,23,42,.45);
                padding:18px; color:#fff; position:relative; overflow:hidden;
                display:flex; flex-direction:column; justify-content:space-between;
            }
            .lpm-wallet-card::after {
                content:''; position:absolute; inset:0;
                background: radial-gradient(circle at 80% 10%, rgba(255,255,255,.35), transparent 55%);
                pointer-events:none;
            }
            .lpm-wallet-chip {
                width:38px; height:28px; border-radius:6px;
                background: linear-gradient(135deg, #fde68a, #f59e0b);
                box-shadow: inset 0 -3px 0 rgba(0,0,0,.15);
            }
            .lpm-wallet-currency {
                font-size:64px; font-weight:900; line-height:1;
                text-shadow: 0 4px 16px rgba(0,0,0,.3);
                position:absolute; right:18px; top:16px;
                letter-spacing:-.02em;
            }
            .lpm-wallet-label {
                font-weight:700; letter-spacing:.03em; font-size:.82rem;
                text-transform:uppercase; opacity:.9;
            }
            @keyframes lpm-wallet-pop {
                0%   { transform: translate(-50%, -50%) scale(.25) rotate(-12deg); opacity:0; }
                55%  { transform: translate(-50%, -50%) scale(1.15) rotate(3deg);  opacity:1; }
                100% { transform: translate(-50%, -50%) scale(1) rotate(0);        opacity:1; }
            }
            .lpm-wallet-pop.fadeOut { animation: lpm-wallet-fade .6s ease forwards; }
            @keyframes lpm-wallet-fade { to { opacity:0; transform: translate(-50%, -50%) scale(.85); } }
        `;
        const s = document.createElement('style');
        s.id = 'lpm-wallet-styles';
        s.textContent = css;
        document.head.appendChild(s);
    }

    window.lpmToast = function (message, kind) {
        try {
            ensureStyles();
            const cls = (kind === 'error' || kind === 'info') ? kind : 'success';
            const icon = cls === 'success' ? '✓' : cls === 'error' ? '✕' : 'ℹ';
            const c = getToastContainer();
            const t = document.createElement('div');
            t.className = 'lpm-toast ' + cls;
            t.innerHTML = `<span style="font-size:1.1rem;">${icon}</span><span>${String(message).replace(/</g,'&lt;')}</span>`;
            c.appendChild(t);
            setTimeout(() => { try { t.remove(); } catch (_) {} }, 3800);
        } catch (e) { console.error('lpmToast failed', e); }
    };
})();
