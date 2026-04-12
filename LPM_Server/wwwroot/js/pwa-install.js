let deferredPrompt = null;

window.pwa = {
    init: (dotnetRef) => {
        window.addEventListener('beforeinstallprompt', (e) => {
            e.preventDefault();
            deferredPrompt = e;
            if (dotnetRef) dotnetRef.invokeMethodAsync('SetCanInstall', true);
        });

        // If already installed, hide button
        window.addEventListener('appinstalled', () => {
            deferredPrompt = null;
            if (dotnetRef) dotnetRef.invokeMethodAsync('SetCanInstall', false);
        });

        // Show iOS Safari banner if needed
        pwa._showIosBannerIfNeeded();
    },

    canInstall: () => deferredPrompt !== null,

    install: async () => {
        if (!deferredPrompt) return false;
        deferredPrompt.prompt();
        const res = await deferredPrompt.userChoice;
        deferredPrompt = null;
        return res && res.outcome === "accepted";
    },

    isIOS: () => {
        // Standard iOS detection
        if (/iphone|ipad|ipod/i.test(navigator.userAgent)) return true;
        // iPadOS 13+ in desktop mode reports as "Macintosh" — detect via touch
        if (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1) return true;
        return false;
    },

    _isIOSSafari: () => {
        if (!pwa.isIOS()) return false;
        var ua = navigator.userAgent;
        // On iOS, non-Safari browsers inject their own token
        if (/CriOS|FxiOS|EdgiOS|OPiOS/i.test(ua)) return false;
        return true;
    },

    _isStandalone: () => {
        // iOS sets navigator.standalone when launched from home screen
        if (window.navigator.standalone === true) return true;
        if (window.matchMedia && window.matchMedia('(display-mode: standalone)').matches) return true;
        return false;
    },

    _showIosBannerIfNeeded: () => {
        if (!pwa.isIOS()) return;
        if (pwa._isIOSSafari()) return;   // already in Safari — no need
        if (pwa._isStandalone()) return;   // already installed

        // Check dismissal
        try {
            if (localStorage.getItem('lpm-ios-safari-dismissed') === '1') return;
        } catch (e) { /* private browsing — show anyway */ }

        var banner = document.getElementById('ios-safari-banner');
        if (banner) banner.style.display = 'flex';
    },

    dismissIosBanner: () => {
        var banner = document.getElementById('ios-safari-banner');
        if (banner) banner.style.display = 'none';
        try { localStorage.setItem('lpm-ios-safari-dismissed', '1'); } catch (e) {}
    }
};

// Auto-run banner check on page load (doesn't depend on Blazor calling pwa.init)
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => pwa._showIosBannerIfNeeded());
} else {
    pwa._showIosBannerIfNeeded();
}
