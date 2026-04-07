// ── LPM Passkey (WebAuthn) Interop ──
window.lpmPasskey = {
    isSupported() {
        return !!window.PublicKeyCredential;
    },

    _b64ToArr(b64) {
        const s = b64.replace(/-/g, '+').replace(/_/g, '/');
        const pad = s.length % 4 === 0 ? '' : '='.repeat(4 - (s.length % 4));
        return Uint8Array.from(atob(s + pad), c => c.charCodeAt(0));
    },

    _arrToB64(buf) {
        const bytes = new Uint8Array(buf);
        let binary = '';
        for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
    }
};

// ── High-level flows called from Blazor ──
window.lpmPasskeyFlow = {
    async login(username) {
        try {
            // 1. Get options from server
            const optRes = await fetch('/api/passkey/login-options', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ username })
            });
            const optData = await optRes.json();
            if (optData.hasPasskeys === false) return 'no_passkeys';

            // 2. Decode challenge and credential IDs
            const opts = optData;
            opts.challenge = lpmPasskey._b64ToArr(opts.challenge);
            if (opts.allowCredentials) {
                opts.allowCredentials = opts.allowCredentials.map(c => ({
                    ...c, id: lpmPasskey._b64ToArr(c.id)
                }));
            }

            // 3. Browser prompts user for fingerprint/PIN
            const assertion = await navigator.credentials.get({ publicKey: opts });

            // 4. Send assertion to server
            const body = JSON.stringify({
                id: assertion.id,
                rawId: lpmPasskey._arrToB64(assertion.rawId),
                type: assertion.type,
                response: {
                    authenticatorData: lpmPasskey._arrToB64(assertion.response.authenticatorData),
                    clientDataJSON: lpmPasskey._arrToB64(assertion.response.clientDataJSON),
                    signature: lpmPasskey._arrToB64(assertion.response.signature),
                    userHandle: assertion.response.userHandle ? lpmPasskey._arrToB64(assertion.response.userHandle) : null
                }
            });

            const verifyRes = await fetch('/api/passkey/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body
            });

            if (verifyRes.ok) {
                const result = await verifyRes.json();
                return result.success ? 'success' : 'failed';
            }
            return 'failed';
        } catch (e) {
            console.error('[Passkey] Login error:', e);
            return e.name === 'NotAllowedError' ? 'cancelled' : 'failed';
        }
    },

    async register() {
        try {
            // 1. Get options from server
            const optRes = await fetch('/api/passkey/register-options', { method: 'POST' });
            if (!optRes.ok) return 'failed';
            const optData = await optRes.json();

            // 2. Decode challenge, user.id, excludeCredentials
            const opts = optData;
            opts.challenge = lpmPasskey._b64ToArr(opts.challenge);
            opts.user.id = lpmPasskey._b64ToArr(opts.user.id);
            if (opts.excludeCredentials) {
                opts.excludeCredentials = opts.excludeCredentials.map(c => ({
                    ...c, id: lpmPasskey._b64ToArr(c.id)
                }));
            }

            // 3. Browser prompts user to create credential
            const credential = await navigator.credentials.create({ publicKey: opts });

            // 4. Send attestation to server
            const body = JSON.stringify({
                id: credential.id,
                rawId: lpmPasskey._arrToB64(credential.rawId),
                type: credential.type,
                response: {
                    attestationObject: lpmPasskey._arrToB64(credential.response.attestationObject),
                    clientDataJSON: lpmPasskey._arrToB64(credential.response.clientDataJSON)
                }
            });

            const regRes = await fetch('/api/passkey/register', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body
            });

            if (regRes.ok) {
                const result = await regRes.json();
                return result.success ? 'success' : 'failed';
            }
            return 'failed';
        } catch (e) {
            console.error('[Passkey] Register error:', e);
            return e.name === 'NotAllowedError' ? 'cancelled' : 'failed';
        }
    }
};
