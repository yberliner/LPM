// Register custom sizes with Quill (must be done before any instance is created).
// Guard against missing Quill: if the library failed to load, skip registration so the
// rest of this file (the window.quillInterop wrapper) still defines, and Blazor's
// JS.InvokeVoidAsync calls hit a clear console error instead of "could not find" runtime crash.
if (typeof Quill !== 'undefined') {
    var SizeStyle = Quill.import('attributors/style/size');
    SizeStyle.whitelist = ['10px', '14px', '18px', '24px', '32px'];
    Quill.register(SizeStyle, true);
} else {
    console.error('[quillInterop] Quill library not loaded — rich editor will not work.');
}

window.step3Quill = (function () {
    function attachListeners(dotnetRef) {
        function checkEmpty() {
            var top = window['_quill_step3-top-editor'];
            var bot = window['_quill_step3-bottom-editor'];
            var topText = top ? top.getText().trim() : '';
            var botText = bot ? bot.getText().trim() : '';
            dotnetRef.invokeMethodAsync('UpdateNextCsSkipBadge', topText === '' && botText === '');
        }
        var t = window['_quill_step3-top-editor'];
        var b = window['_quill_step3-bottom-editor'];
        if (t) t.on('text-change', checkEmpty);
        if (b) b.on('text-change', checkEmpty);
    }

    return { attachListeners: attachListeners };
})();

window.quillInterop = (function () {
    const instances = new Map();

    function init(editorEl, initialHtml, dotNetRef) {
        if (typeof Quill === 'undefined') {
            console.error('[quillInterop] init called but Quill is not loaded — check that /assets/libs/quill/quill.min.js is reachable.');
            return;
        }
        if (instances.has(editorEl)) return;

        const quill = new Quill(editorEl, {
            theme: 'snow',
            placeholder: 'Write notes...',
            modules: {
                toolbar: [
                    [{ 'size': ['10px', '14px', '18px', '24px', '32px'] }],
                    ['bold', 'italic', 'underline', 'strike'],
                    [{ 'color': [] }, { 'background': [] }],
                    [{ 'list': 'ordered' }, { 'list': 'bullet' }],
                    [{ 'direction': 'rtl' }, { 'align': [] }],
                    ['clean']
                ]
            }
        });

        if (initialHtml && initialHtml.trim()) {
            // Use Quill's parser so the internal Delta stays in sync with the DOM.
            // Direct quill.root.innerHTML = html leaves the Delta out of sync, which is
            // what made Enter insert at index 0 and threw the 'composing' MutationObserver
            // error on Ctrl+A → Delete → type.
            //
            // dangerouslyPasteHTML internally calls setSelection(end, SILENT) which
            // updates the DOM selection. The browser then auto-scrolls the contenteditable
            // into view — pulling the page down to the editor whenever it pre-populates
            // with content below the fold. We snapshot scrollX/Y around the paste, blur
            // the editor to drop the selection, then restore the page position both
            // synchronously and on the next animation frame to defeat any queued scroll.
            // Bootstrap's reboot sets `:root { scroll-behavior: smooth }` so we
            // temporarily override to 'auto' — otherwise the restore animates and the
            // user sees a visible bounce.
            const savedX = window.scrollX, savedY = window.scrollY;
            const docEl = document.documentElement;
            const prevScrollBehavior = docEl.style.scrollBehavior;
            docEl.style.scrollBehavior = 'auto';
            quill.setContents([], 'silent');
            quill.clipboard.dangerouslyPasteHTML(0, initialHtml, 'silent');
            quill.blur();
            window.scrollTo(savedX, savedY);
            requestAnimationFrame(function () {
                window.scrollTo(savedX, savedY);
                docEl.style.scrollBehavior = prevScrollBehavior;
            });
        }

        quill.on('text-change', function () {
            const html = quill.root.innerHTML;
            const isEmpty = html === '<p><br></p>' || html.trim() === '';
            dotNetRef.invokeMethodAsync('OnContentChanged', isEmpty ? '' : html);
        });

        // Add tooltips to toolbar buttons
        const toolbar = quill.container.previousSibling;
        const tips = {
            'ql-bold':                        'Bold',
            'ql-italic':                      'Italic',
            'ql-underline':                   'Underline',
            'ql-strike':                      'Strikethrough',
            'ql-clean':                       'Remove formatting',
        };
        Object.entries(tips).forEach(([cls, label]) => {
            const el = toolbar.querySelector('.' + cls);
            if (el) el.title = label;
        });
        const orderedBtn = toolbar.querySelector('.ql-list[value="ordered"]');
        if (orderedBtn) orderedBtn.title = 'Numbered list';
        const bulletBtn = toolbar.querySelector('.ql-list[value="bullet"]');
        if (bulletBtn) bulletBtn.title = 'Bullet list';
        const colorPicker = toolbar.querySelector('.ql-color .ql-picker-label');
        if (colorPicker) colorPicker.title = 'Text color';
        const bgPicker = toolbar.querySelector('.ql-background .ql-picker-label');
        if (bgPicker) bgPicker.title = 'Highlight color';
        const sizePicker = toolbar.querySelector('.ql-size .ql-picker-label');
        if (sizePicker) sizePicker.title = 'Text size';
        const rtlBtn = toolbar.querySelector('.ql-direction');
        if (rtlBtn) rtlBtn.title = 'Right to left';
        const alignPicker = toolbar.querySelector('.ql-align .ql-picker-label');
        if (alignPicker) alignPicker.title = 'Text alignment';

        instances.set(editorEl, quill);
    }

    function destroy(editorEl) {
        const quill = instances.get(editorEl);
        if (quill) {
            // Disconnect the MutationObserver first so it can't fire on subsequent DOM
            // mutations (which would throw "Cannot read properties of undefined (reading
            // 'composing')" and — worse — make a ghost Quill process keys against a stale
            // delta in any new Quill instance that gets created on the same element).
            try { if (quill.scroll && quill.scroll.observer) quill.scroll.observer.disconnect(); } catch(e) {}
            try { quill.container.previousSibling.remove(); } catch(e) {}
            try { quill.container.remove(); } catch(e) {}
        }
        instances.delete(editorEl);
    }

    return { init, destroy };
})();
