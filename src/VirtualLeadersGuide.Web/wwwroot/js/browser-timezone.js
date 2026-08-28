// Called by VirtualLeadersGuide.Web.Time.BrowserTimeZoneAccessor - see its remarks for why this exists.
window.vlgGetTimeZone = function () {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
};
