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
    },

    canInstall: () => deferredPrompt !== null,

    install: async () => {
        if (!deferredPrompt) return false;
        deferredPrompt.prompt();
        const res = await deferredPrompt.userChoice;
        deferredPrompt = null;
        return res && res.outcome === "accepted";
    },

    isIOS: () => /iphone|ipad|ipod/i.test(navigator.userAgent)
};
