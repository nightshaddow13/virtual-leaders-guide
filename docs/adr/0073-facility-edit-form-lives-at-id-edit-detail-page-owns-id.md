---
status: records where P8-4 (#168) puts the Facility edit form, against ADR-precedent and #168's own Dev note
---

# The Facility edit form lives at `/{id}/edit`; `/{id}` is reserved for the detail page

P8-4 (#168) turns `FacilityEditor` into a create-and-edit component. `EventEditor` (`/dashboard/events/{Id}`)
and `InfoPageEditor` (`.../info-pages/{InfoPageId}`) both put edit at the record's own URL, and #168's Dev
note points the same way: "this is also the page that will host the Program Area (P9), Building/Room (P10),
and Campsite (P11) builder-plus-live-tree sections."

The approved wireframe disagrees. Turn `6a` of `Facility Wireframes.dc.html` (badged `APPROVED MODEL`, drawn
under P8-1, #165, after #168 was written) is a Facility **detail** page: a tree pane on the left, a context
editor on the right, and an outlined "Edit name & type" button that leads to the separate Name/Type form in
turn `5f`. The builders live on the detail page; Name and Type live in a form one click away.

This ADR sides with the wireframe. The edit form claims `/dashboard/facilities/{id}/edit`, and
`/dashboard/facilities/{id}` stays unclaimed for the P9 detail page. #168's Dev note is superseded.

An Event's edit page can double as its hub only because an Event has no tree pane competing for the screen.
A Facility will.

## Considered options

- **`/{id}`, per `EventEditor`/`InfoPageEditor`** - rejected: it matches both precedents and #168's note, but
  collides with wireframe `6a`. P9 would have to move the route (breaking bookmarks) or abandon the approved
  layout.
- **Build `6a`'s detail-page shell in P8-4** - rejected: honors the wireframe fully, but roughly doubles this
  story's UI scope for a page whose only content would be an empty tree pane until P9.

## Consequences

- P9 owns `/dashboard/facilities/{id}`, the "Edit name & type" button that links to `/{id}/edit`, and the
  link-colored Name on the Facility list. P8-4 deliberately leaves the list's Name as plain text so P9 adds
  the link rather than retargeting one.
- Until P9 ships, the list's icon-only edit button (ADR-0037) is the only way into a Facility.
- A Facility that no longer exists renders a `Missing` panel on `/{id}/edit`, not the `Denied` panel: a global
  resource has no Event-scoping that could make "missing" and "not yours" look the same.
