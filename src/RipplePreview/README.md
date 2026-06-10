# Ripple Preview

Pixel-perfect, script-enabled live previews for Umbraco 17 block editors (Block Grid + Block List).

Ripple Preview renders your **real frontend Razor views** on the server and shows them in the
backoffice inside a same-origin iframe locked to a configurable **design width**, scaled to fit
the block entry. Because the preview is a real document at a real viewport width:

- `vw`/`vh` units, media queries and container math resolve exactly like the frontend — even for
  blocks nested many levels deep (Ripple computes each block's true width fraction through the
  whole layout tree).
- `@font-face` fonts load, inline `<style>` applies, and `<script>`s execute (carousels, Lottie,
  jQuery widgets all run).
- Nested **areas stay natively editable**: blocks with areas show their own chrome in the preview
  and the standard Umbraco area editor beneath — drag/drop, add, sort and inline edit keep working,
  and child blocks get their own previews recursively. **No changes to your Razor views required.**

## UX

- Click a preview to open the block's editor (native action bar stays too).
- Hover toolbar: **zoom** (large lightbox preview), **refresh**, **interact** (temporarily lets you
  click inside the preview — try a carousel).
- Re-renders are double-buffered: no white flash while typing.

## Getting started

```
dotnet add package RipplePreview
```

Enable per editor in `appsettings.json` (full IntelliSense ships with the package):

```json
"RipplePreview": {
  "DesignWidth": 1440,
  "BlockGrid": {
    "Enabled": true,
    "Stylesheets": [ "/css/site.css" ],
    "Scripts": [ "/js/site.js" ]
  },
  "BlockList": { "Enabled": true, "Stylesheets": [ "/css/site.css" ] }
}
```

That's the whole setup: enable the editors, list the stylesheets/scripts your blocks need.
Restart, and every block grid / block list entry in the backoffice renders live.

**Tips**

- Using Umbraco's default Block Grid rendering? Add the generated layout stylesheet so nested
  areas lay out correctly inside previews: `"Stylesheets": [ "/css/umbraco-blockgridlayout.css", ... ]`.
- Blocks implemented as **ViewComponents** (named after the element type alias) are supported and
  take precedence over partials — no configuration needed.
- Editors can switch all blocks between live previews and Umbraco's native cards at any time:
  via the eye icon in the header bar, the "Toggle live previews" entry in any block property's
  "..." menu, or Ctrl+Shift+P. A per-block toolbar offers zoom, refresh, interact and a global
  structure-outline mode.

## Supported editors

| Editor | Status |
| --- | --- |
| Block Grid (incl. nested areas, any depth) | ✅ |
| Block List (incl. inside block workspaces) | ✅ |
| Rich Text Editor blocks | ✅ (`"RichText": { "Enabled": true }`) |
| Single Block | ✅ (`"SingleBlock": { "Enabled": true }`) |

Requires Umbraco 17.3+.

## Migrating from Umbraco.Community.BlockPreview

The config shape is intentionally familiar (`Enabled`, `ContentTypes`, `IgnoredContentTypes`,
`ViewLocations`, `Stylesheets` per editor) — copy your section over and rename the root to
`RipplePreview`. Views written against BlockPreview keep working: `ViewData["blockPreview"]` is
set for compatibility, and you can remove any `GetPreviewBlockGridItemAreasHtmlAsync` calls —
Ripple needs no view modifications for areas.

### Options (per editor)

| Option | Description |
| --- | --- |
| `Enabled` | Turn previews on for this editor type (default `false`). |
| `ContentTypes` | Allowlist of element type aliases (empty = all). |
| `IgnoredContentTypes` | Denylist, applied when `ContentTypes` is empty. |
| `ViewLocations` | Extra view location templates searched before the defaults (`{0}` = alias). |
| `Stylesheets` / `Scripts` | Assets injected into every preview document. |
| `WrapperView` | Optional partial rendered around the block markup to replicate your grid chrome. Receives the block item as model, the inner markup via `ViewData["rippleInnerHtml"]`, and the block's width fraction via `ViewData["rippleWidthFraction"]`. |
| `FullAreaPreviewContentTypes` | (Grid) element types that render the full preview **without** the child editing strip. |
| `StackedAreaPreviewContentTypes` | (Grid) element types whose chrome previews on its own, with children rendered as individual previews below. |

### How blocks with areas render

| Mode | When | Behaviour |
| --- | --- | --- |
| **Full** (default) | Most parents | One document renders the parent **and all its children** through the real frontend pipeline — backgrounds, text colors, nesting and alignment are exact. Below the preview, a compact editing strip lists each child (click to edit; native add/drag/copy/delete all work). |
| **Stacked** | When children should preview individually | The parent's own chrome renders as its own preview, and children render as separate previews below it. Opt in per type via `StackedAreaPreviewContentTypes`. |
| **Solo** | Read-mostly sections | Full preview without the editing strip; children are edited through their own workspaces. Opt in via `FullAreaPreviewContentTypes`. |

Default view conventions: `/Views/Partials/blockgrid/Components/{alias}.cshtml` and
`/Views/Partials/blocklist/Components/{alias}.cshtml`.

In views, `ViewData["ripplePreview"]` is `true` (plus `blockGridPreview`/`blockListPreview`), and
`Context.IsRipplePreview()` is available via `RipplePreview.Extensions`.

ModelsBuilder is recommended but **not required** — without generated models, blocks render as
`BlockGridItem<IPublishedElement>`.

## Troubleshooting

- **"No view or ViewComponent found"** shown in a block: the message lists every searched
  location — add yours via `ViewLocations`.
- **Preview looks unstyled**: the preview document only loads what you list in `Stylesheets`
  (plus anything your views render inline). Check the browser network tab inside the preview.
- **A block renders blank**: blocks driven by page-level scripts can't run those in isolation;
  Ripple shows a striped placeholder with the block's name and size instead. Add a small
  preview-only script via `Scripts` if you want the real visual.
- **Wrong width for a nested block**: Ripple computes width from column spans through the whole
  layout tree against `DesignWidth`; if your frontend uses custom breakpoints/containers, use a
  `WrapperView` to replicate that chrome.
- **Sibling blocks don't stretch to equal height**: row-level CSS grid effects (equal-height
  columns) only apply between blocks rendered in the same document. Children inside one parent
  section stretch correctly; separate top-level blocks each render independently, so cross-block
  stretching cannot apply there.

## License

MIT. Portions of the server-side conversion pipeline adapted from
[Umbraco.Community.BlockPreview](https://github.com/rickbutterfield/Umbraco.Community.BlockPreview)
(MIT) — see NOTICE.md.
