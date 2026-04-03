// Minimal service worker for PWA share target support.
// Does NOT cache anything — just passes through all requests.
// Required: without a service worker, share_target in the manifest is ignored.

self.addEventListener('install', (e) => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()));

self.addEventListener('fetch', (e) => {
    // Never intercept Blazor SignalR or _blazor paths
    if (e.request.url.includes('_blazor')) return;
    // Pass through everything else
    e.respondWith(fetch(e.request));
});
