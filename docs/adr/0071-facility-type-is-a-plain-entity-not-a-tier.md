---
status: records why CONTEXT.md's Facility Type entry no longer cites ADR-0046's Tier lifecycle
---

# Facility Type is a plain entity, not a Tier

CONTEXT.md's Facility Type entry (CONTEXT.md) used to read "Same lazy-create lifecycle as Tier", citing
ADR-0046's lazy-create/auto-delete/never-rename lifecycle for the Tab/SubTab/Section/SubSection entities
Activity/InfoPage Placement is built on. P8-2 (#166), the first story to actually build Facility Type,
found that ADR-0046's two reasons for that lifecycle don't transfer:

- ADR-0046 needed a **stable id** so Phase 6's Section photo feature has something to attach to. Nothing
  ever attaches to a Facility Type.
- ADR-0046 needed a home for the **"different parent, different Section" scoping rule** - the same name
  typed under two different parents produces two distinct rows. A Facility Type has no parent to scope
  under at all.

With neither reason present, and no Tier table shipped yet either (so there's no existing implementation to
stay consistent with - only CONTEXT.md's prose), Facility Type is instead a plain lookup `[Resource]`:
created once, offered by autofill afterward, and **never hard-deleted** once nothing references it. CONTEXT.md's
entry is amended accordingly.

`FacilityType` is exposed as its own JSON:API resource at `/api/facilityTypes` rather than a non-resource
lookup like `PageType` - the autofill needs to list existing values (GET, filterable) and create a new one
inline the moment an Admin types a name with no match (POST), both natural JsonApiDotNetCore operations once
it's a real `[Resource]`, avoiding a bespoke endpoint. Every verb on this resource, Read included, is
Admin-only (`FacilityAccessPolicy.IsAdmin`) - unlike `Facility` itself (ADR-0070), there's no broader
audience: the only consumer is the Admin-gated Facility create/edit form, and nothing later in Phase 8 needs
a Director to browse Facility Types the way they'll need to browse Facilities/Locations.

## Considered options

- **Real lazy-create/auto-delete entity, per ADR-0046's letter** - rejected: would require reaping logic
  (resolve-or-create on write, re-read-the-old-value-and-delete-if-now-unreferenced on update/delete) that
  every later Facility-editing story (P8-4, P8-5) would inherit for no benefit, since nothing needs a Facility
  Type to disappear automatically the way a Tier does.
- **Lookup table, no JSON:API resource, like `PageType`** - rejected: `PageType` is seeded and closed
  (`PageTypeIds.InfoPage`), never created at runtime. Facility Type needs runtime creation (the "use as new"
  autofill path) and a way to list existing values for that same autofill - both of which a JSON:API resource
  gives for free, where a non-resource lookup would need a bespoke endpoint for each.

## Consequences

- `FacilityType` never participates in any reaping/cascade-delete logic - P8-4 (Edit) and P8-5 (Delete) start
  from zero obligation here. Changing or clearing a Facility's Type on edit may leave an unreferenced
  `FacilityType` row behind; accepted, not a bug.
- `Facility`→`FacilityType`'s foreign key restricts deletion (`VirtualLeadersGuideDbContext.ConfigureFacilities`),
  mirroring `Page`→`PageType`'s own Restrict - a lookup row can't vanish out from under rows that reference
  it, even though no story in this phase actually exposes deleting one.
- `FacilityTypeResourceDefinition` pre-checks `Name` uniqueness (case-insensitively) before a create/update,
  returning 409 on collision - this is what keeps the Web client's resolve-or-create autofill flow from
  racing itself into two near-duplicate rows for the same typed name.
