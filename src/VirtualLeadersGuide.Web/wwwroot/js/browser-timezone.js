// Exposes the browser's IANA timezone id to Blazor Server (P2-15, #102) - .NET can't ask an
// InteractiveServer circuit's client for this on its own, since the circuit runs on the server.
// BrowserTimeZoneAccessor calls this once per circuit via IJSRuntime and caches the result.
window.vlgGetTimeZone = function () {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
};
