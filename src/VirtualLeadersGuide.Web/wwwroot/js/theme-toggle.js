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
    // (crossing into e.g. the static-SSR Account/Manage page, or a POST-redirect like Account/Logout) has
    // been observed to reset <html>'s attributes to whatever the fresh server response contains, i.e. no
    // data-theme at all, silently falling back to the OS's prefers-color-scheme. Same logic as the
    // FOUC-prevention script in App.razor's <head> (which only covers the very first hard load), called
    // again on every enhanced navigation below so an explicit choice actually sticks everywhere.
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

    // Re-apply on every enhanced navigation, not just first load - restoreStoredTheme() undoes the reset
    // described above, syncLabel() re-derives the label for whatever button the navigation just rendered
    // (Blazor's enhanced nav puts back the server-rendered neutral label along with everything else).
    document.addEventListener('blazor:enhancedload', function () {
        restoreStoredTheme();
        syncLabel();
    });

    restoreStoredTheme();
    syncLabel();
})();
