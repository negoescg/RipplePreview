# Ripple Preview

**See your actual pages while you edit them.**

Ripple Preview replaces the plain block entries in the Umbraco backoffice with the real thing:
every block is rendered through your site's own views, stylesheets and scripts, then shown
scaled to fit the editor. What editors see while writing is what visitors see after publishing.

## What you get

- **Real previews, not approximations.** Blocks render server-side through the same Razor views
  your frontend uses. Backgrounds, fonts, colors, columns and spacing all come out exactly as
  they do on the live page — whatever CSS approach your site uses, including viewport-based
  sizing (`vw`/`vh`) and media queries, which resolve against a configurable design width.
- **Sections render whole.** A block that contains other blocks renders as one piece, children
  included — so a dark section shows its white text on dark, side-by-side columns line up, and
  nested layouts at any depth look like the page.
- **Scripts run.** Carousels slide, animations play, embeds load.
- **Everything stays editable.** Click a block to open it. Hover over a section and the block
  under your cursor highlights — click to open exactly that block. A compact editing strip under
  each section handles adding, reordering, copying and deleting children, and the standard
  Umbraco action bar works as always.
- **Editors stay in control.** A toolbar on every block offers zoom (a large lightbox view),
  refresh, and an interact mode for trying carousels right in the preview. Structure mode
  outlines and names every block on the page when you want to see the layout skeleton. And one
  click (the eye icon in the top bar, or Ctrl+Shift+P) switches the whole backoffice back to
  the classic block cards at any time.

## Getting started

```
dotnet add package RipplePreview
```

Enable the editors you use in `appsettings.json` (the package ships IntelliSense for all options):

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

That's the whole setup: enable the editors, list the stylesheets and scripts your blocks need.
Restart, and every block renders live.

**Tips**

- Using Umbraco's default Block Grid rendering? Add the generated layout stylesheet so nested
  areas lay out correctly: `"Stylesheets": [ "/css/umbraco-blockgridlayout.css", ... ]`.
- Blocks implemented as **ViewComponents** (named after the element type alias) work without
  configuration and take precedence over partial views.

## Supported editors

| Editor | Status |
| --- | --- |
| Block Grid (nested areas at any depth) | ✅ |
| Block List (including inside block workspaces) | ✅ |
| Rich Text Editor blocks | ✅ (`"RichText": { "Enabled": true }`) |
| Single Block | ✅ (`"SingleBlock": { "Enabled": true }`) |

Requires Umbraco 17.3+.

## Options (per editor)

| Option | Description |
| --- | --- |
| `Enabled` | Turn previews on for this editor type (default `false`). |
| `ContentTypes` | Allowlist of element type aliases (empty = all). |
| `IgnoredContentTypes` | Denylist, applied when `ContentTypes` is empty. |
| `ViewLocations` | Extra view location templates searched before the defaults (`{0}` = alias). |
| `Stylesheets` / `Scripts` | Assets loaded into every preview. |
| `WrapperView` | Optional partial rendered around the block markup to replicate your grid chrome. Receives the block item as model, the inner markup via `ViewData["rippleInnerHtml"]`, the width fraction via `ViewData["rippleWidthFraction"]`, the owning page via `ViewData["rippleOwner"]` and ancestor blocks via `ViewData["rippleAncestors"]`. |
| `FullAreaPreviewContentTypes` | (Grid) element types that render the full preview **without** the child editing strip. |
| `StackedAreaPreviewContentTypes` | (Grid) element types whose own chrome previews separately, with children rendered as individual previews below. |

Default view conventions: `/Views/Partials/blockgrid/Components/{alias}.cshtml`,
`/Views/Partials/blocklist/Components/{alias}.cshtml` and
`/Views/Partials/richtext/Components/{alias}.cshtml`.

In views, `ViewData["ripplePreview"]` is `true` during preview rendering, and
`Context.IsRipplePreview()` is available via `RipplePreview.Extensions`.

ModelsBuilder is recommended but **not required** — without generated models, blocks render as
`BlockGridItem<IPublishedElement>`.

## Matching your site's markup

Most sites need nothing beyond `Enabled` and `Stylesheets`. Two situations need a little more:

### Your site renders grids with its own markup

If your templates lay out block grids through custom markup — your own container classes and
column CSS instead of Umbraco's defaults — previews need that chrome too, or blocks will render
unwrapped. Provide it once with a wrapper view:

```json
"BlockGrid": { "WrapperView": "/Views/Partials/ripplePreview/gridWrapper.cshtml" }
```

The wrapper receives the block as its model plus everything needed to reproduce your page chrome:

```cshtml
@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage<Umbraco.Cms.Core.Models.Blocks.BlockGridItem>
@using Umbraco.Cms.Core.Models.PublishedContent
@{
    var inner = ViewData["rippleInnerHtml"] as Microsoft.AspNetCore.Html.HtmlString;
    var fraction = ViewData["rippleWidthFraction"] is double f ? f : 1d;          // block's share of the page width
    var owner = ViewData["rippleOwner"] as IPublishedContent;                     // the page being edited
    var nested = (ViewData["rippleAncestors"] as List<IPublishedElement>)?.Count > 0;
    var width = fraction >= 0.999 ? "100vw"
        : FormattableString.Invariant($"calc(100vw * {fraction:0.####})");

    // Example: paint a per-page background, but only for top-level blocks —
    // nested blocks sit on their parent's background instead.
    var pageBackground = nested ? null : owner?.Value<string>("pageBackgroundColor");
}
@if (!string.IsNullOrEmpty(pageBackground))
{
    <style>html.ripple-preview body { background: @pageBackground; }</style>
}
<style>
    /* Repeat the grid classes your page template normally provides, so blocks —
       and their children, which render inside this document too — lay out identically. */
    .my-grid-container { display: grid; grid-template-columns: repeat(12, minmax(0, 1fr)); }
    .my-col-6 { grid-column: span 6; }
    /* ... the rest of your column/row classes ... */
</style>
<div class="my-grid-container" style="width:@width">
    @inner
</div>
```

Rule of thumb: anything your page template contributes to how blocks look — grid classes,
container widths, page backgrounds — belongs in the wrapper, because a block preview renders
without the page template around it.

### Blocks that depend on page-level scripts

If a block's visual is produced by a script living in your page template (for example, a script
that counts rows and generates numbered buttons), that script isn't part of the block's own
markup, so the preview can't run it. Recreate just the visual part in a small script that loads
**only inside previews**:

```json
"BlockGrid": { "Scripts": [ "/js/my-preview-helpers.js" ] }
```

```js
// my-preview-helpers.js — loaded only inside Ripple previews
(function () {
    function init() {
        // recreate the page-level behavior your block relies on, e.g. generate
        // the elements a template script would normally inject
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
```

Your live site is untouched — these scripts exist only in the preview documents.

### How blocks with children render

| Mode | When | Behaviour |
| --- | --- | --- |
| **Full** (default) | Most parents | The parent and all its children render as one piece, exactly like the frontend. A compact editing strip below the preview lists each child for editing and reordering. |
| **Stacked** | When children should preview individually | The parent's own markup previews on its own; children render as separate previews below. Opt in per type via `StackedAreaPreviewContentTypes`. |
| **Solo** | Read-mostly sections | Full preview without the editing strip; children are edited through their own workspaces. Opt in via `FullAreaPreviewContentTypes`. |

## Troubleshooting

- **"No view or ViewComponent found"** shown in a block: the message lists every searched
  location — add yours via `ViewLocations`.
- **Preview looks unstyled**: the preview only loads what you list in `Stylesheets` (plus
  anything your views render inline).
- **A block renders blank**: blocks driven by page-level scripts can't run those in isolation;
  Ripple shows a labeled placeholder with the block's name and size instead. Add a small
  preview-only script via `Scripts` if you want the real visual.
- **Sibling blocks don't stretch to equal height**: row-level grid effects only apply between
  blocks rendered together. Children inside one section stretch correctly; separate top-level
  blocks render independently.

## License and credits

MIT — see LICENSE.md.

Parts of the server-side machinery that convert backoffice editor values into renderable
models were adapted from [Umbraco.Community.BlockPreview](https://github.com/rickbutterfield/Umbraco.Community.BlockPreview)
(MIT) — attribution details in NOTICE.md.
