// The top bar's dropdowns are native <details> (ADR-0034, ADR-0080), which stay open until their own summary
// is clicked again. Close them on a click elsewhere, on Escape, or when focus leaves. Listens on document so it survives enhanced
// navigation, like theme-toggle.js.
(function () {
    function closeMenus(except) {
        document.querySelectorAll('details.vlg-menu[open]').forEach(function (menu) {
            if (!except || !menu.contains(except)) {
                menu.removeAttribute('open');
            }
        });
    }

    // pointerdown, not click: iOS Safari doesn't deliver a tap on plain content to a document click listener.
    document.addEventListener('pointerdown', function (event) {
        closeMenus(event.target);
    });

    // Back can restore a page from the browser's cache exactly as it was left, menu open.
    window.addEventListener('pageshow', function () {
        closeMenus();
    });

    // Escape hands focus back to the menu's own button, rather than leaving it on a link that is now hidden.
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') {
            return;
        }

        var menu = document.activeElement && document.activeElement.closest('details.vlg-menu[open]');
        closeMenus();
        if (menu) {
            menu.querySelector('summary').focus();
        }
    });

    // Tabbing out of a menu closes it. A tap that focuses nothing has no relatedTarget and is handled above.
    document.addEventListener('focusout', function (event) {
        if (event.relatedTarget) {
            closeMenus(event.relatedTarget);
        }
    });
})();
 // The top bar's dropdowns are native <details> (ADR-0034, ADR-0080), which stay open until their own summary
// is clicked again. Close them on a click elsewhere, on Escape, or when focus leaves. Listens on document so it
// survives enhanced navigation, like theme-toggle.js.
(function () {
    function closeMenus(except) {
        document.querySelectorAll('details.vlg-menu[open]').forEach(function (menu) {
            if (!except || !menu.contains(except)) {
                menu.removeAttribute('open');
            }
        });
    }

    // pointerdown, not click: iOS Safari doesn't deliver a tap on plain content to a document click listener.
    document.addEventListener('pointerdown', function (event) {
        closeMenus(event.target);
    });

    // Back can restore a page from the browser's cache exactly as it was left, menu open.
    window.addEventListener('pageshow', function () {
        closeMenus();
    });

    // Escape hands focus back to the menu's own button, rather than leaving it on a link that is now hidden.
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') {
            return;
        }

        var menu = document.activeElement && document.activeElement.closest('details.vlg-menu[open]');
        closeMenus();
        if (menu) {
            menu.querySelector('summary').focus();
        }
    });

    // Tabbing out of a menu closes it. A click that focuses nothing has no relatedTarget and is handled above.
    document.addEventListener('focusout', function (event) {
        if (event.relatedTarget) {
            closeMenus(event.relatedTarget);
        }
    });
})();
