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

    // See ADR-0034 ("data-theme doesn't survive Blazor's enhanced navigation on its own") for why this
    // exists and is re-run on every enhanced navigation, not just the first hard load.
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

    // See ADR-0034 ("the hook for 'every such navigation' is Blazor.addEventListener('enhancedload', ...)")
    // for why this is the real API and why registration retries instead of assuming it's already bound.
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
