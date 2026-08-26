# Prefer Radzen components over custom CSS for layout, typography, and status roles

P2-9 (#18) shipped `Dashboard.razor`/`EventEditor.razor` with roughly a dozen hand-rolled `.vlg-*` CSS
classes for things that aren't really Virtual Leaders Guide-specific design: a flex row with space-between
(`.vlg-page-header`), a flex row with a gap (`.vlg-button-row`), small muted caption text (`.vlg-subhead`,
`.vlg-hint`), a breadcrumb trail (`.vlg-breadcrumb`), a status pill (`.vlg-badge`), an informational aside
(`.vlg-note`), and a centered column (`.vlg-denied`). Raised in code review: this app already took on
Radzen.Blazor as its component library (ADR-0034) specifically to avoid hand-rolling exactly this kind of
thing, and Radzen.Blazor 11.2.6 (the version this app has installed) ships components for every one of
them - `RadzenStack`, `RadzenBreadCrumb`/`RadzenBreadCrumbItem`, `RadzenBadge`, `RadzenText`, `RadzenAlert`.

## The rule

A layout, typography, or status-display need is met with a Radzen component first. Custom CSS is for two
things only: this app's own design tokens (the `--vlg-*` block and its `--rz-*` aliasing, both already
established by ADR-0034) and a genuinely bespoke pattern Radzen has no component for.

| Need | Radzen component, not custom CSS |
|---|---|
| Flex row, space-between or with a gap | `RadzenStack` (`Orientation`, `JustifyContent`, `AlignItems`, `Gap`) |
| Small/muted/caption text | `RadzenText` (`TextStyle.Caption`/`Overline`/etc.) |
| A trail like "Events / Event name" | `RadzenBreadCrumb`/`RadzenBreadCrumbItem` - and its optional `Path` gets a real link for free, which the plain-text version P2-9 first shipped didn't have |
| A status pill ("VIEW ONLY") | `RadzenBadge` |
| An informational aside | `RadzenAlert` (`AlertStyle.Info`, distinct from the `AlertStyle.Danger` this app already uses for errors) |

This isn't unique to Dashboard/EventEditor - it's the standing rule for every future page. A `.vlg-*` class
for a layout/typography/status need introduced after this ADR is a deviation, not a new default, the same
way ADR-0037 treats a text row-action button.

## What's exempt

Two patterns in `EventEditor.razor` stayed custom CSS rather than becoming a Radzen component,
deliberately:

- **The Slug field's `/e/` affix** (`.vlg-slug-field`/`.vlg-slug-prefix`). `RadzenFormField`'s `Start`/`End`
  slots are the component-shaped answer, but every input in this app's `EditForm`s is a plain
  `InputText`/`ValidationMessage` pair (matching `Components/Account/Pages/Manage/Index.razor`'s
  established shape) - not a `RadzenTextBox`. Swapping the input component itself to fit inside
  `RadzenFormField` is a materially bigger, riskier change than a display affix (it touches
  `EditContext`/`ValidationMessageStore` wiring this story didn't need to touch) and is out of scope here.
  Revisit if/when this app adopts `RadzenTextBox` inside `EditForm` generally.
- **The read-only label/value pairing** (`.vlg-readonly-field`). No Radzen component models "a label
  above a value, divided from the next pair" - `RadzenText TextStyle="Overline"` covers the label's
  typography, but the pairing/divider layout itself has no component equivalent and stays a small custom
  class.

## Considered options

- **Leave the custom CSS as shipped** - rejected: it duplicates components this app already ships
  Radzen.Blazor for, with no compensating benefit (the custom classes don't do anything Radzen's own
  components don't already do, once themed through the `--vlg-*`/`--rz-*` alias layer ADR-0034 built for
  exactly this) - and it loses a genuine improvement for free: `RadzenBreadCrumbItem`'s `Path="dashboard"`
  is real, clickable navigation the plain-text version never had, rendered as a plain `<a href>` rather
  than a Blazor `NavigateTo` call, so it isn't exposed to the render-mode navigation hazard ADR-0036
  documents.
- **Introduce `RadzenTextBox`/`RadzenFormField` everywhere, including the Slug affix** - rejected for this
  story: changes this app's established `EditForm`/`InputText` pattern app-wide, a bigger decision than
  "reduce custom CSS" on its own and out of scope for a code-review fix-up.

## Consequences

- `app.css`'s P2-9 block shrank from ~100 lines to the two exempt patterns above (~25 lines).
- `Dashboard.razor`/`EventEditor.razor`'s markup is now composed largely of Radzen components rather than
  plain HTML elements carrying `.vlg-*` classes - consistent with every other interactive page this app
  will build.
- The Events/My events breadcrumb segment is now a real link back to `/dashboard`, which it wasn't before
  this ADR - a fix that fell out of using `RadzenBreadCrumbItem` rather than a separate ask.
