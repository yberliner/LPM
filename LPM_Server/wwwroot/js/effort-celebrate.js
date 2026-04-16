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
