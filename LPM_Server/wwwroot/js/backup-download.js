// File-by-file backup download using File System Access API
window.lpmFileBackup = (function () {
    let _cancel = false;
    let _running = false;
    let _dotNetRef = null;
    let _dirHandle = null;

    // Windows-invalid filename chars
    const INVALID_CHARS = /[<>:"|?*\x00-\x1f]/g;
    // Zero-width / BiDi format marks (U+200B..U+200F, U+202A..U+202E, U+2060,
    // U+2066..U+2069, U+FEFF). Windows Explorer + Hebrew clipboards sometimes
    // inject LRM (U+200E) or similar into filenames. The File System Access API
    // rejects these with "Name is not allowed", and String.trim() does NOT strip
    // them (they're formatting controls, not whitespace).
    const BIDI_FORMAT = /[​-‏‪-‮⁠⁦-⁩﻿]/g;
    function sanitize(name) {
        return name.replace(BIDI_FORMAT, '').replace(INVALID_CHARS, '_').replace(/\.+$/, '').trim() || '_';
    }

    async function getOrCreateDir(parentHandle, subPath) {
        const parts = subPath.split('/').filter(Boolean);
        let handle = parentHandle;
        for (const part of parts) {
            handle = await handle.getDirectoryHandle(sanitize(part), { create: true });
        }
        return handle;
    }

    async function readManifest(dirHandle) {
        try {
            const fileHandle = await dirHandle.getFileHandle('_manifest.json');
            const file = await fileHandle.getFile();
            const text = await file.text();
            return JSON.parse(text);
        } catch { return {}; }
    }

    async function writeManifest(dirHandle, manifest) {
        const fileHandle = await dirHandle.getFileHandle('_manifest.json', { create: true });
        const writable = await fileHandle.createWritable();
        await writable.write(JSON.stringify(manifest, null, 2));
        await writable.close();
    }

    async function downloadPhase(token, phase, phaseDir, dotNetRef) {
        // Fetch file list
        const listResp = await fetch(`/api/backup-file-list?phase=${phase}&token=${encodeURIComponent(token)}`);
        if (!listResp.ok) throw new Error(`File list failed: ${listResp.status}`);
        const files = await listResp.json();

        const manifest = await readManifest(phaseDir);
        let current = 0, skipped = 0, errors = 0;
        const total = files.length;
        let manifestDirty = 0;

        for (const entry of files) {
            if (_cancel) break;

            current++;
            const key = entry.path;
            const fullPath = entry.fullPath || '';
            const pcName = entry.pcName || null;

            // Incremental: skip if manifest has same modified date and not alwaysDownload
            if (!entry.alwaysDownload && manifest[key] === entry.modified) {
                skipped++;
                try { dotNetRef.invokeMethodAsync('OnFileProgress', current, total, key, skipped, errors, true); } catch {}
                continue;
            }

            // Download with retry
            let ok = false;
            let lastErrorDetail = '';
            for (let attempt = 0; attempt < 3 && !ok && !_cancel; attempt++) {
                try {
                    const resp = await fetch(`/api/backup-file?phase=${phase}&path=${encodeURIComponent(key)}&token=${encodeURIComponent(token)}`);
                    if (!resp.ok) {
                        if (resp.status === 404) {
                            console.warn(`[Backup] File not found (skipping): ${key}`);
                            errors++;
                            try { dotNetRef.invokeMethodAsync('OnBackupError', 'http_404', key, fullPath, pcName, new Date().toISOString(), ''); } catch {}
                            ok = true; // don't retry 404
                            break;
                        }
                        throw new Error(`HTTP ${resp.status}`);
                    }
                    const blob = await resp.blob();

                    // Create directory structure and write file
                    const lastSlash = key.lastIndexOf('/');
                    let dirHandle = phaseDir;
                    let fileName = key;
                    if (lastSlash >= 0) {
                        dirHandle = await getOrCreateDir(phaseDir, key.substring(0, lastSlash));
                        fileName = key.substring(lastSlash + 1);
                    }
                    fileName = sanitize(fileName);

                    const fileHandle = await dirHandle.getFileHandle(fileName, { create: true });
                    const writable = await fileHandle.createWritable();
                    await writable.write(blob);
                    await writable.close();

                    manifest[key] = entry.modified;
                    manifestDirty++;
                    ok = true;
                } catch (err) {
                    console.error(`[Backup] ${phase}/${key} attempt ${attempt + 1} failed:`, err);
                    lastErrorDetail = (err && (err.message || err.toString())) || 'Unknown error';
                    if (attempt === 2) {
                        errors++;
                        try { dotNetRef.invokeMethodAsync('OnBackupError', 'download_failed', key, fullPath, pcName, new Date().toISOString(), lastErrorDetail); } catch {}
                    }
                }
            }

            // Write manifest every 50 files
            if (manifestDirty >= 50) {
                await writeManifest(phaseDir, manifest);
                manifestDirty = 0;
            }

            try { dotNetRef.invokeMethodAsync('OnFileProgress', current, total, key, skipped, errors, false); } catch {}
        }

        // Final manifest write
        if (manifestDirty > 0) await writeManifest(phaseDir, manifest);

        return { total, skipped, errors };
    }

    return {
        isSupported: function () { return typeof window.showDirectoryPicker === 'function'; },

        isRunning: function () { return _running; },

        pickDirectory: async function () {
            _dirHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
            return _dirHandle != null;
        },

        start: async function (token, dotNetRef) {
            if (_running) return;
            if (!_dirHandle) return;
            _cancel = false;
            _running = true;
            _dotNetRef = dotNetRef;

            // Run async — Blazor doesn't await this (would timeout)
            (async function () {
                var userResult = { total: 0, skipped: 0, errors: 0 };
                var serverResult = { total: 0, skipped: 0, errors: 0 };
                var ok = false, error = null;
                try {
                    // Create User and Server subdirectories
                    var userDir = await _dirHandle.getDirectoryHandle('User', { create: true });
                    var serverDir = await _dirHandle.getDirectoryHandle('Server', { create: true });

                    // Phase 1: User
                    try { dotNetRef.invokeMethodAsync('OnPhaseChanged', 'user'); } catch {}
                    userResult = await downloadPhase(token, 'user', userDir, dotNetRef);

                    if (!_cancel) {
                        // Phase 2: Server
                        try { dotNetRef.invokeMethodAsync('OnPhaseChanged', 'server'); } catch {}
                        serverResult = await downloadPhase(token, 'server', serverDir, dotNetRef);
                    }

                    ok = !_cancel;
                    if (_cancel) error = 'Cancelled';
                } catch (err) {
                    error = err.message;
                } finally {
                    _running = false;
                }
                // Notify Blazor
                try {
                    dotNetRef.invokeMethodAsync('OnBackupComplete', ok, error,
                        userResult.total, userResult.skipped, userResult.errors,
                        serverResult.total, serverResult.skipped, serverResult.errors);
                } catch {}
            })();
        },

        cancel: function () { _cancel = true; },

        reconnectDotNetRef: function (newRef) { _dotNetRef = newRef; }
    };
})();
