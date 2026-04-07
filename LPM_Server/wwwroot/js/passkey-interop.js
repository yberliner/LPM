// ── LPM Passkey (WebAuthn) Interop ──
window.lpmPasskey = {
    isSupported() {
        return !!window.PublicKeyCredential;
    },

    // Convert base64url to Uint8Array
    _b64ToArr(b64) {
        const s = b64.replace(/-/g, '+').replace(/_/g, '/');
        const pad = s.length % 4 === 0 ? '' : '='.repeat(4 - (s.length % 4));
        return Uint8Array.from(atob(s + pad), c => c.charCodeAt(0));
    },

    // Convert ArrayBuffer to base64url
    _arrToB64(buf) {
        const bytes = new Uint8Array(buf);
        let binary = '';
        for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
    },

    async register(optionsJson) {
        const opts = JSON.parse(optionsJson);
        opts.challenge = this._b64ToArr(opts.challenge);
        opts.user.id = this._b64ToArr(opts.user.id);
        if (opts.excludeCredentials) {
            opts.excludeCredentials = opts.excludeCredentials.map(c => ({
                ...c, id: this._b64ToArr(c.id)
            }));
        }

        const credential = await navigator.credentials.create({ publicKey: opts });

        return JSON.stringify({
            id: credential.id,
            rawId: this._arrToB64(credential.rawId),
            type: credential.type,
            response: {
                attestationObject: this._arrToB64(credential.response.attestationObject),
                clientDataJSON: this._arrToB64(credential.response.clientDataJSON)
            }
        });
    },

    async login(optionsJson) {
        const opts = JSON.parse(optionsJson);
        opts.challenge = this._b64ToArr(opts.challenge);
        if (opts.allowCredentials) {
            opts.allowCredentials = opts.allowCredentials.map(c => ({
                ...c, id: this._b64ToArr(c.id)
            }));
        }

        const assertion = await navigator.credentials.get({ publicKey: opts });

        return JSON.stringify({
            id: assertion.id,
            rawId: this._arrToB64(assertion.rawId),
            type: assertion.type,
            response: {
                authenticatorData: this._arrToB64(assertion.response.authenticatorData),
                clientDataJSON: this._arrToB64(assertion.response.clientDataJSON),
                signature: this._arrToB64(assertion.response.signature),
                userHandle: assertion.response.userHandle ? this._arrToB64(assertion.response.userHandle) : null
            }
        });
    }
};
