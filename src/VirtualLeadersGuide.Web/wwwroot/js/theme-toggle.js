// Plain vanilla JS, not Blazor - per ADR-0034, this app has no interactive circuit anywhere, and a theme
// flip is a pure client-side DOM/localStorage operation that doesn't need one. Listens on `document` (not
// the button itself) so it survives Blazor's enhanced-navigation swapping <body> content between pages -
// the app-level FOUC-prevention script in App.razor's <head> reads the same 'vlg-theme' key on load.
(function () {
    function currentTheme() {
        var explicit = document.documentElement.getAttribute('data-theme');
        if (explicit === 'light' || explicit === 'dark') {
            return explicit;
        }

        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    // The server can't render the correct aria-label - theme state lives only in this browser's
    // localStorage, never sent to it - so the markup ships a neutral label and this refines it once the
    // real state is known, both on load and after every toggle.
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

    // Blazor's enhanced navigation re-renders <body> (including this button, back to its server-rendered
    // neutral label) without a full page reload - re-sync after every such navigation, not just on first load.
    document.addEventListener('blazor:enhancedload', syncLabel);

    syncLabel();
})();
