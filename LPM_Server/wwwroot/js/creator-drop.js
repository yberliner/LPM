window.lpmCreatorDrop = {
    position: function () {
        var drop = document.getElementById('cb-floating-drop');
        if (!drop) return;

        // Find the visible button that triggered the dropdown (only one of the two SM-toolbar
        // buttons is rendered at a time; the other branch's element doesn't exist in the DOM).
        var btns = document.querySelectorAll('.cb-anchor > button');
        var btn = null;
        for (var i = 0; i < btns.length; i++) {
            if (btns[i].offsetParent !== null) { btn = btns[i]; break; }
        }
        if (!btn) return;

        var rect = btn.getBoundingClientRect();
        var menuWidth = drop.offsetWidth || 280;
        var top = rect.bottom + 6;
        // Right-align with the button (so dropdown opens leftward like the original anchored design).
        var left = rect.right - menuWidth;
        if (left < 8) left = 8;
        if (left + menuWidth > window.innerWidth - 8) left = window.innerWidth - menuWidth - 8;

        drop.style.top = top + 'px';
        drop.style.left = left + 'px';
        drop.classList.add('cb-drop-positioned');
    },
    reset: function () {
        var drop = document.getElementById('cb-floating-drop');
        if (drop) drop.classList.remove('cb-drop-positioned');
    }
};
