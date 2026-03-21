// LPM Voice Commands (PCFolder) — en-US for best accuracy
window.lpmVoice = (function () {

    var _dotNetRef  = null;
    var _paneId     = null;
    var _recog      = null;
    var _running    = false;
    var _lastCursor = { x: 0, y: 0 };
    var _uttFired   = false;
    var _watchdog   = null;

    function resetWatchdog() {
        if (_watchdog) clearTimeout(_watchdog);
        if (!_running) return;
        _watchdog = setTimeout(function () {
            if (!_running) return;
            console.log("[lpmVoice] watchdog restart");
            try { _recog.stop(); } catch (e) {}   // onend will restart it
        }, 4000);
    }

    var _commands = [
        { words: ["double","side"],              cmd: "dual",     direct: false },
        { words: ["split"],                      cmd: "split",    direct: false },
        { words: ["text"],                       cmd: "text",     direct: false },
        { words: ["next","right","forward"],      cmd: "next",     direct: true  },
        { words: ["back","previous","left","black","bag","pack","go back"], cmd: "prev", direct: true },
        { words: ["zoom in"],                    cmd: "zoomin",   direct: false },
        { words: ["zoom out"],                   cmd: "zoomout",  direct: false },
        { words: ["summary"],                    cmd: "summary",  direct: false },
        { words: ["undo"],                       cmd: "undo",     direct: false },
        { words: ["download"],                   cmd: "download", direct: false },
    ];

    var _badgeLabels = {
        dual: "Double", split: "Split", text: "Text",
        next: "Next", prev: "Back", zoomin: "Zoom In",
        zoomout: "Zoom Out", summary: "Summary",
        undo: "Undo", download: "Download"
    };

    function showBadge(cmd) {
        var label = (_badgeLabels[cmd] || cmd) + " \u2713";
        var el = document.createElement("div");
        el.style.cssText = "position:fixed;bottom:80px;left:50%;transform:translateX(-50%) translateY(20px);background:rgba(0,0,0,.8);color:#fff;padding:8px 20px;border-radius:20px;font-size:15px;font-weight:600;z-index:99999;opacity:0;transition:opacity .2s,transform .2s;pointer-events:none;";
        el.textContent = "\uD83C\uDFA4 " + label;
        document.body.appendChild(el);
        requestAnimationFrame(function () {
            el.style.opacity = "1";
            el.style.transform = "translateX(-50%) translateY(0)";
            setTimeout(function () {
                el.style.opacity = "0";
                el.style.transform = "translateX(-50%) translateY(-10px)";
                setTimeout(function () { el.remove(); }, 250);
            }, 1200);
        });
    }

    function trackCursor(e) {
        var el = document.elementFromPoint(e.clientX, e.clientY);
        if (el && (el.tagName === "CANVAS" || el.closest(".pcf-pane"))) {
            _lastCursor = { x: e.clientX, y: e.clientY };
        }
    }

    function triggerPointerAtCursor() {
        var el = document.elementFromPoint(_lastCursor.x, _lastCursor.y);
        if (el) el.dispatchEvent(new PointerEvent("pointerdown", {
            bubbles: true, cancelable: true,
            clientX: _lastCursor.x, clientY: _lastCursor.y,
            pointerId: 1, pointerType: "mouse", isPrimary: true
        }));
    }

    // Execute viewer actions directly in JS — no SignalR round-trip
    function execDirect(cmd) {
        var v = window.pcfViewer;
        if (!v) return;
        if (cmd === "next" || cmd === "prev") {
            // Simulate the arrow key — reuses the existing keyboard navigation handler
            document.dispatchEvent(new KeyboardEvent("keydown", {
                key: cmd === "next" ? "ArrowRight" : "ArrowLeft",
                bubbles: true, cancelable: true
            }));
        }
    }

    function match(transcript) {
        var t = transcript.toLowerCase().trim();
        for (var i = 0; i < _commands.length; i++) {
            var entry = _commands[i];
            for (var j = 0; j < entry.words.length; j++) {
                var w = entry.words[j];
                // Whole-word match: word boundary on each side
                var re = new RegExp("(^|\\s)" + w.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "(\\s|$)");
                if (re.test(t)) return entry;
            }
        }
        return null;
    }

    function buildRecog() {
        var SR = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SR) { console.warn("[lpmVoice] SpeechRecognition not supported"); return null; }
        var r = new SR();
        r.lang = "en-US";
        r.continuous = false;       // false = more reliable; onend fires after each utterance
        r.interimResults = true;
        r.maxAlternatives = 3;

        r.onresult = function (e) {
            resetWatchdog();
            for (var i = e.resultIndex; i < e.results.length; i++) {
                var isFinal     = e.results[i].isFinal;
                var alreadyFired = _uttFired;
                if (isFinal) _uttFired = false;
                if (alreadyFired) continue;

                // Check all alternatives for a match
                var entry = null;
                for (var a = 0; a < e.results[i].length; a++) {
                    entry = match(e.results[i][a].transcript);
                    if (entry) break;
                }

                if (entry) {
                    _uttFired = true;
                    console.log("[lpmVoice]", isFinal ? "final" : "interim", entry.cmd);
                    showBadge(entry.cmd);
                    if (entry.direct) {
                        execDirect(entry.cmd);
                    } else {
                        if (_dotNetRef) _dotNetRef.invokeMethodAsync("OnVoiceCommand", entry.cmd);
                    }
                }
            }
        };

        r.onend = function () {
            resetWatchdog();
            if (_running) { try { r.start(); } catch (ex) {} }
        };
        r.onerror = function (e) {
            if (e.error === "not-allowed" || e.error === "service-not-allowed") {
                _running = false;
                if (_watchdog) { clearTimeout(_watchdog); _watchdog = null; }
                if (_dotNetRef) _dotNetRef.invokeMethodAsync("OnVoiceMicBlocked");
            }
        };
        return r;
    }

    return {
        init: function (dotNetRef, paneId) {
            _dotNetRef = dotNetRef;
            _paneId    = paneId || null;
            document.addEventListener("mousemove", trackCursor, { passive: true });
        },
        setPane: function (paneId) { _paneId = paneId; },
        start: function () {
            if (_running) return;
            if (!_recog) _recog = buildRecog();
            if (!_recog) return;
            _running = true;
            try { _recog.start(); resetWatchdog(); } catch (ex) {}
        },
        stop: function () {
            _running = false;
            if (_watchdog) { clearTimeout(_watchdog); _watchdog = null; }
            if (_recog) { try { _recog.stop(); } catch (ex) {} }
        },
        triggerClickAtCursor: triggerPointerAtCursor,
        isSupported: function () { return !!(window.SpeechRecognition || window.webkitSpeechRecognition); }
    };
})();
