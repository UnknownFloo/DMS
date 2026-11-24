//: ============================================================
//: Offline app-shell caching and request fallback.
//: ============================================================
const CACHE_NAME = 'paperless-v10-inline-pdf-editor';

//* Precache the app shell and activate the new worker immediately.
self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME).then(cache => cache.addAll([
      '/',
      '/index.html',
      '/manifest.json',
      '/icon.svg',
      '/css/common.css'
    ]))
  );
  self.skipWaiting();
});

//* Remove prior cache versions before taking control of open pages.
self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

//* Prefer the network; serve the cached shell if an offline request fails.
self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET' || event.request.url.includes('/api/')) return;
  if (new URL(event.request.url).origin !== self.location.origin) return;

  event.respondWith(
    fetch(event.request).catch(async () => {
      const cached = await caches.match(event.request);
      return cached || caches.match('/index.html');
    })
  );
});
