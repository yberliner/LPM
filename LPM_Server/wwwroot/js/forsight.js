window.scrollFileListTop = () => {
    const el = document.querySelector('.file-list');
    if (el) el.scrollTop = 0;
};

window.unfocusButtons = () => {
    // blur the currently focused element if it's a delete button
    const active = document.activeElement;
    if (active && active.tagName === 'BUTTON' && active.classList.contains('btn-danger')) {
        active.blur();
    }
};

/*
window.initBootstrapModal = (element) => {
    return new bootstrap.Modal(element);
};
*/

window.initBootstrapModal = (element) => {
    const modal = new bootstrap.Modal(element, { backdrop: "static", keyboard: false });

    function wait(eventName, action) {
        return new Promise(resolve => {
            element.addEventListener(eventName, function handler() {
                element.removeEventListener(eventName, handler);
                resolve();
            }, { once: true });
            action();                       // show()/hide()
        });
    }

    return {
        show: () => wait("shown.bs.modal", () => modal.show()),
        hide: () => wait("hidden.bs.modal", () => modal.hide()),
        instance: () => modal              // (optional) expose raw instance
    };
};

window.initNewTooltips = () => {
    // Every element that was *not* initialised yet will get a Tooltip instance
    document
        .querySelectorAll('[data-bs-toggle="tooltip"]:not([data-bs-initialised])')
        .forEach(el => {
            new bootstrap.Tooltip(el);
            el.setAttribute('data-bs-initialised', '');   // mark as done
        });
};

window.downloadTextFile = (fileName, mime, text) => {
    const blob = new Blob([text], { type: mime });
    const url = URL.createObjectURL(blob);

    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    a.style.display = "none";

    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);

    URL.revokeObjectURL(url);           // free memory
};
