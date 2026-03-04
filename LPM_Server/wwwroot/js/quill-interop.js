// Register custom sizes with Quill (must be done before any instance is created)
var SizeStyle = Quill.import('attributors/style/size');
SizeStyle.whitelist = ['10px', '14px', '18px', '24px', '32px'];
Quill.register(SizeStyle, true);

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

        instances.set(editorEl, quill);
    }

    function destroy(editorEl) {
        instances.delete(editorEl);
    }

    return { init, destroy };
})();
