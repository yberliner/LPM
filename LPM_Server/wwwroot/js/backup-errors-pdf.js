// Download Backup Errors PDF — mirrors window.PrintPurchase.printReceipt pattern.
window.lpmBackupErrorsPdf = {
    download: async function (payload) {
        try {
            const res = await fetch('/api/backup-errors/pdf', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            if (!res.ok) {
                alert('Failed to generate backup errors PDF (HTTP ' + res.status + ').');
                return;
            }
            const blob = await res.blob();
            const url = URL.createObjectURL(blob);
            const stamp = new Date();
            const pad = n => String(n).padStart(2, '0');
            const fname = 'LPM-BackupErrors-' + stamp.getFullYear()
                + pad(stamp.getMonth() + 1) + pad(stamp.getDate())
                + '-' + pad(stamp.getHours()) + pad(stamp.getMinutes()) + '.pdf';
            const a = document.createElement('a');
            a.href = url;
            a.download = fname;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
        } catch (err) {
            console.error('lpmBackupErrorsPdf.download failed:', err);
            alert('Failed to download backup errors PDF: ' + (err && err.message ? err.message : err));
        }
    }
};

// Copy arbitrary text to the clipboard, with legacy execCommand fallback
// for insecure contexts (HTTP). Returns true if the copy appeared to succeed.
window.lpmCopyText = async function (text) {
    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch (e) { /* fall through to legacy path */ }
    try {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.setAttribute('readonly', '');
        ta.style.position = 'fixed';
        ta.style.top = '0';
        ta.style.left = '0';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.focus();
        ta.select();
        const ok = document.execCommand('copy');
        document.body.removeChild(ta);
        return !!ok;
    } catch (e) {
        console.error('lpmCopyText failed:', e);
        return false;
    }
};
