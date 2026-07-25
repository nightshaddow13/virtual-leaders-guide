# API is internal-only; Blazor Server is the sole public surface

Blazor Web App runs in Interactive Server mode, so the browser only ever talks to the Blazor Server circuit —
never directly to the API. We decided the `Api` project gets internal-only Container Apps ingress (unreachable
from the public internet), while `Web` gets external ingress and is the only public surface, calling `Api` over
the internal network. `Api` skips OIDC/JWT entirely and instead checks a static shared-secret header
(`X-Internal-Key`, known only to `Web`) as defense-in-depth against future ingress misconfiguration.

## Consequences

This avoids a whole tier of complexity — no CIAM tenant, no dual-auth API, no public API attack surface — versus
designing the API as a standalone public product. The trade-off is that `Api` cannot be reused directly by a
future separate public client without revisiting this boundary.
