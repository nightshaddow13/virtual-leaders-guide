---
status: collection/single asymmetry generalized by ADR-0033 (a caller whose visible set is always empty, not just sometimes-narrowed, gets 403 on every shape instead of a silently-filtered collection)
---

# Event authorization is enforced in a JsonApiDotNetCore resource definition, not a controller

P2-7 (#16) needed Admin/Director scoping on `/api/events`: an Admin gets full CRUD over every Event; a
Director gets read/update on only the Events they're assigned to (from the internal JWT's role claims,
ADR-0007/ADR-0017), and never create or delete. The ticket itself asked us to confirm this still fits
ADR-0004's "zero hand-written controllers" framing before building it.

We decided the scoping lives in `EventResourceDefinition`, a `JsonApiResourceDefinition<Event, Guid>` -
`OnApplyFilter` narrows what a Director's `GET` can see, and `OnWritingAsync` authorizes every write. This is
a JsonApiDotNetCore extension point that plugs into the same generated pipeline ADR-0004 chose, not a
hand-written controller or ASP.NET Core middleware sitting in front of it - ADR-0004's "zero hand-written
controllers" is about not writing per-entity controller classes, and a resource definition is exactly the
declarative, model-driven extension point that framing anticipates, the same way `[Attr]`/`[Resource]`
themselves are.

Two further choices fell out of building it:

- **A single-resource request outside the caller's access throws 403, not 404.** `GET /api/events/{id}` for
  an Event a Director isn't assigned to confirms the Event exists (someone gave them, or they guessed, a real
  id) even though they can't read it - this is what the ticket's acceptance criteria literally specify.
- **A collection request is filtered silently instead**, ANDing the caller's own filter with
  `Id IN (assigned event ids)` - a Director's `GET /api/events` (or a caller with zero role claims) just gets
  a 200 with whatever's visible, possibly empty. A 403 on a *list* endpoint would be strange (403 for what,
  the whole endpoint?), and JSON:API list filtering is the idiomatic way to scope a collection.

The two produce genuinely different information-leak postures for the same underlying "you can't see this"
fact, and that asymmetry is deliberate, not an inconsistency to fix later.

## Considered options

- An ASP.NET Core authorization policy/filter in front of the generated routes - rejected: it would need to
  hand-parse `/api/events/{id}` routing to know which Event a request targets, duplicating what
  JsonApiDotNetCore's own request pipeline already resolves before a resource definition hook ever runs.
- A hand-written controller for `events` instead of using `[Resource]`'s generated one - rejected outright by
  ADR-0004: the entire point was no hand-written controllers.
- Making the collection case 403-on-any-non-Admin-filter instead of silently narrowing it - rejected: JSON:API
  list endpoints are conventionally scoped by filtering, and a blanket 403 would break `GET /api/events` for
  every legitimate Director, not just deny an out-of-scope one.

## Consequences

Authorization logic for Events lives in `EventResourceDefinition`, discoverable wherever someone is already
looking at JsonApiDotNetCore's other resource-definition extension points, rather than in a separate
middleware/filter layer a reader would need to know to go looking for. The 403/404 asymmetry means a
determined caller can enumerate real Event ids by probing `/api/events/{guid}` even without read access to
any of them - accepted, since the ticket's acceptance criteria require the 403 response on that path
specifically, and Event ids are Guids, not sequential or guessable.
