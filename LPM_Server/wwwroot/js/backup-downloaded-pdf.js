// Download "Actual Downloaded Files" PDF — mirrors backup-errors-pdf.js.
window.lpmBackupDownloadedPdf = {
    download: async function (payload) {
        try {
            const res = await fetch('/api/backup-downloaded/pdf', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            if (!res.ok) {
                alert('Failed to generate downloaded-files PDF (HTTP ' + res.status + ').');
                return;
            }
            const blob = await res.blob();
            const url = URL.createObjectURL(blob);
            const stamp = new Date();
            const pad = n => String(n).padStart(2, '0');
            const fname = 'LPM-BackupDownloaded-' + stamp.getFullYear()
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
            console.error('lpmBackupDownloadedPdf.download failed:', err);
            alert('Failed to download backup-downloaded PDF: ' + (err && err.message ? err.message : err));
        }
    }
};
