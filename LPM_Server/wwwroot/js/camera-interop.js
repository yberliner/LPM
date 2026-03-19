window.lpmCamera = (function () {
    var _stream = null;

    function video()  { return document.getElementById('cam-video');  }
    function canvas() { return document.getElementById('cam-canvas'); }

    return {
        start: async function () {
            try {
                _stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false });
                var v = video();
                if (!v) return false;
                v.srcObject = _stream;
                await v.play();
                return true;
            } catch (e) {
                console.warn('[lpmCamera] start failed:', e);
                return false;
            }
        },

        snap: function () {
            var v = video();
            var c = canvas();
            if (!v || !c) return;
            c.width  = v.videoWidth  || 640;
            c.height = v.videoHeight || 480;
            c.getContext('2d').drawImage(v, 0, 0, c.width, c.height);
        },

        retake: function () {
            var v = video();
            if (v && _stream) {
                v.srcObject = _stream;
                v.play();
            }
        },

        getDataUrl: function () {
            var c = canvas();
            return c ? c.toDataURL('image/jpeg', 0.92) : '';
        },

        stop: function () {
            if (_stream) {
                _stream.getTracks().forEach(function (t) { t.stop(); });
                _stream = null;
            }
            var v = video();
            if (v) { v.srcObject = null; }
        }
    };
})();
