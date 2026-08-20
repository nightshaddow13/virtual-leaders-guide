// Plain vanilla JS, not Blazor - per ADR-0034, this app has no interactive circuit anywhere, and a theme
// flip is a pure client-side DOM/localStorage operation that doesn't need one.
(function () {
    function currentTheme() {
        var explicit = document.documentElement.getAttribute('data-theme');
        if (explicit === 'light' || explicit === 'dark') {
            return explicit;
        }

        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    // The server never renders data-theme - it's client-only state - and Blazor's enhanced navigation
    // (any link/form navigation it intercepts, not just the static-SSR/POST-redirect cases) replaces
    // <html>'s attributes with whatever the fresh server response contains, i.e. no data-theme at all,
    // silently falling back to the OS's prefers-color-scheme. Same logic as the FOUC-prevention script in
    // App.razor's <head> (which only covers the very first hard load), re-run on every enhanced navigation
    // below so an explicit choice actually sticks everywhere, not just on pages reached by a full reload.
    function restoreStoredTheme() {
        var stored = localStorage.getItem('vlg-theme');
        if (stored === 'light' || stored === 'dark') {
            document.documentElement.setAttribute('data-theme', stored);
        }
    }

    // The server can't render the correct aria-label either, for the same reason - the markup ships a
    // neutral label and this refines it once the real state is known.
    function syncLabel() {
        var button = document.getElementById('vlg-theme-toggle');
        if (button) {
            button.setAttribute('aria-label', currentTheme() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme');
        }
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('vlg-theme', theme);
        syncLabel();
    }

    document.addEventListener('click', function (event) {
        var button = event.target.closest('#vlg-theme-toggle');
        if (!button) {
            return;
        }

        applyTheme(currentTheme() === 'dark' ? 'light' : 'dark');
    });

    // `Blazor.addEventListener('enhancedload', ...)` is the real API for this - not a 'blazor:enhancedload'
    // DOM CustomEvent on `document` (there is no such thing; Blazor dispatches its lifecycle events through
    // its own object, confirmed by reading the shipped blazor.web.js directly rather than assuming). `Blazor`
    // exists as soon as its own <script> tag runs, but `.addEventListener` is only bound onto it partway
    // through Blazor's own (async) start sequence - this script runs `defer`, which can execute before that
    // sequence completes, so retry briefly instead of assuming the method is already there.
    function registerEnhancedLoadHandler() {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', function () {
                restoreStoredTheme();
                syncLabel();
            });
        } else {
            setTimeout(registerEnhancedLoadHandler, 50);
        }
    }

    registerEnhancedLoadHandler();
    restoreStoredTheme();
    syncLabel();
})();
