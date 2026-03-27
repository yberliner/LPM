// Register custom sizes with Quill (must be done before any instance is created)
var SizeStyle = Quill.import('attributors/style/size');
SizeStyle.whitelist = ['10px', '14px', '18px', '24px', '32px'];
Quill.register(SizeStyle, true);

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
            quill.root.innerHTML = initialHtml;
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
        instances.delete(editorEl);
    }

    return { init, destroy };
})();
