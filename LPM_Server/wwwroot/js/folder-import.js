window.folderImport = {
    openPicker: function (inputId) {
        var el = document.getElementById(inputId);
        if (el) {
            el.value = '';
            el.click();
        }
    },

    readManifest: function (inputId) {
        var el = document.getElementById(inputId);
        if (!el || !el.files) return [];
        var manifest = [];
        for (var i = 0; i < el.files.length; i++) {
            var f = el.files[i];
            manifest.push({
                index: i,
                relativePath: f.webkitRelativePath || f.name,
                size: f.size,
                lastModified: new Date(f.lastModified).toISOString()
            });
        }
        return manifest;
    },

    getTotalSize: function (inputId) {
        var el = document.getElementById(inputId);
        if (!el || !el.files) return 0;
        var total = 0;
        for (var i = 0; i < el.files.length; i++) total += el.files[i].size;
        return total;
    },

    readFileAsBase64: async function (inputId, fileIndex) {
        var el = document.getElementById(inputId);
        if (!el || !el.files || fileIndex >= el.files.length) return null;
        var file = el.files[fileIndex];
        return new Promise(function (resolve) {
            var reader = new FileReader();
            reader.onload = function () {
                var base64 = reader.result.split(',')[1] || '';
                resolve(base64);
            };
            reader.onerror = function () { resolve(null); };
            reader.readAsDataURL(file);
        });
    }
};
