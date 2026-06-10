using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RipplePreview.Configuration;

namespace RipplePreview.Services;

/// <summary>
/// Wraps rendered block markup into a complete HTML document for the preview iframe.
/// The iframe is sized to the configured design width so viewport-relative units (vw/vh),
/// media queries, fonts and scripts behave exactly as on the frontend; the block itself
/// is laid out at its true fractional width of that viewport.
/// </summary>
public interface IRippleDocumentBuilder
{
    string BuildDocument(string markup, double widthFraction, RippleEditorOptions editorOptions, string? culture, bool backdrop = false, IReadOnlyList<RippleChildRef>? childRefs = null);
}

public class RippleDocumentBuilder : IRippleDocumentBuilder
{
    public string BuildDocument(string markup, double widthFraction, RippleEditorOptions editorOptions, string? culture, bool backdrop = false, IReadOnlyList<RippleChildRef>? childRefs = null)
    {
        var sb = new StringBuilder(markup.Length + 2048);

        sb.Append("<!DOCTYPE html>\n<html");
        if (!string.IsNullOrWhiteSpace(culture))
        {
            sb.Append(" lang=\"").Append(HtmlEncoder.Default.Encode(culture!)).Append('"');
        }
        sb.Append(" class=\"ripple-preview\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<base href=\"/\">\n");
        // Transparent body: the host decides the surface (white for root entries, the parent's
        // backdrop for nested ones).
        sb.Append("<style>html,body{margin:0;padding:0;background:transparent;}html{scrollbar-width:none;}body::-webkit-scrollbar{display:none;}</style>\n");
        if (backdrop)
        {
            // Stretch the block's chrome to fill the whole document so backgrounds/borders
            // cover the full area behind the natively rendered children.
            sb.Append("<style>html,body{height:100%;}.ripple-root{height:100%;}.ripple-root>*:not(style):not(script){min-height:100%;box-sizing:border-box;}</style>\n");
        }

        foreach (string stylesheet in editorOptions.Stylesheets.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            sb.Append("<link rel=\"stylesheet\" href=\"").Append(HtmlEncoder.Default.Encode(stylesheet)).Append("\">\n");
        }

        sb.Append("</head>\n<body class=\"ripple-preview-body\">\n");

        // When a wrapper view is configured it owns the block's footprint (it receives
        // ViewData["rippleWidthFraction"]); otherwise size the root to the block's true
        // fraction of the design-width viewport.
        bool wrapperHandlesLayout = !string.IsNullOrWhiteSpace(editorOptions.WrapperView);
        string fractionCss = wrapperHandlesLayout || widthFraction >= 0.999d
            ? "100vw"
            : FormattableString.Invariant($"calc(100vw * {widthFraction:0.####})");
        sb.Append("<div class=\"ripple-root\" style=\"width:").Append(fractionCss).Append(";overflow-x:hidden;\">\n");
        sb.Append(markup);
        sb.Append("\n</div>\n");

        foreach (string script in editorOptions.Scripts.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            sb.Append("<script src=\"").Append(HtmlEncoder.Default.Encode(script)).Append("\"></script>\n");
        }

        if (childRefs is { Count: > 0 })
        {
            // Child keys/aliases in document order, used by the bridge to map cursor
            // positions back to editable blocks.
            var map = childRefs.Select(c => new { k = c.Key, a = c.Alias });
            sb.Append("<script type=\"application/json\" id=\"ripple-child-map\">")
              .Append(JsonSerializer.Serialize(map))
              .Append("</script>\n");
        }

        sb.Append(BridgeScript);
        sb.Append("\n</body>\n</html>");
        return sb.ToString();
    }

    /// <summary>
    /// Reports the document height to the host custom view (same-origin srcdoc iframe) and
    /// neutralizes navigation/submission so the preview cannot escape its frame.
    /// </summary>
    private const string BridgeScript = """
        <script>
        (function () {
            function height() {
                // Content-bottom measurement. scrollHeight is unusable here: it never reports
                // less than the frame's viewport, which both inflates short blocks and ratchets
                // re-renders (heights could grow but never shrink).
                var body = document.body;
                if (!body) return 0;
                var max = 0;
                for (var i = 0; i < body.children.length; i++) {
                    var rect = body.children[i].getBoundingClientRect();
                    if (rect.bottom > max) max = rect.bottom;
                }
                return Math.ceil(max + (window.scrollY || 0));
            }
            var last = -1;
            function send() {
                var h = height();
                if (h === last) return;
                last = h;
                try { parent.postMessage({ source: 'ripple-preview', type: 'size', height: h }, '*'); } catch (e) { }
            }
            function sendForce() { last = -1; send(); }
            if (document.readyState === 'complete') { send(); }
            window.addEventListener('load', sendForce);
            document.addEventListener('DOMContentLoaded', send);
            if (typeof ResizeObserver !== 'undefined') {
                var ro = new ResizeObserver(function () { send(); });
                ro.observe(document.documentElement);
                if (document.body) ro.observe(document.body);
            }
            setTimeout(send, 250); setTimeout(send, 1000); setTimeout(sendForce, 3000);
            document.addEventListener('click', function (e) {
                var t = e.target;
                var a = t && t.closest ? t.closest('a') : null;
                if (a) { e.preventDefault(); }
            }, true);
            document.addEventListener('submit', function (e) { e.preventDefault(); }, true);

            // Child hit-testing: pair child keys (document order, from the embedded map) with
            // marker elements, then answer hover/click probes from the host with the block key
            // under the cursor. Disabled gracefully when markers can't be paired.
            var pairs = [];
            (function () {
                var mapEl = document.getElementById('ripple-child-map');
                if (!mapEl) return;
                var refs;
                try { refs = JSON.parse(mapEl.textContent || '[]'); } catch (e) { return; }
                if (!refs.length) return;
                var selector = '[data-element-type],[data-content-element-type-alias],.umb-block-grid__layout-item';
                var markers = Array.prototype.slice.call(document.querySelectorAll(selector));
                var aliasOf = function (el) {
                    return el.getAttribute('data-element-type')
                        || el.getAttribute('data-content-element-type-alias')
                        || '';
                };
                var i = 0;
                for (var m = 0; m < markers.length && i < refs.length; m++) {
                    var el = markers[m];
                    var alias = aliasOf(el);
                    // Skip duplicate markers nested inside the element already paired for this block.
                    var last = pairs.length ? pairs[pairs.length - 1] : null;
                    if (last && last.el.contains(el) && alias === last.a) continue;
                    if (alias && alias === refs[i].a) {
                        pairs.push({ el: el, k: refs[i].k, a: alias });
                        i++;
                    }
                }
                if (i !== refs.length) { pairs = []; }
            })();

            var highlighted = null;
            function highlight(el) {
                if (highlighted === el) return;
                if (highlighted) {
                    highlighted.style.outline = '';
                    highlighted.style.outlineOffset = '';
                }
                highlighted = el;
                if (el) {
                    el.style.outline = '2px solid rgba(53, 68, 177, 0.85)';
                    el.style.outlineOffset = '-2px';
                }
            }

            function childAt(x, y) {
                if (!pairs.length) return null;
                var el = document.elementFromPoint(x, y);
                while (el && el !== document.body) {
                    for (var p = pairs.length - 1; p >= 0; p--) {
                        if (pairs[p].el === el) return pairs[p];
                    }
                    el = el.parentElement;
                }
                return null;
            }

            window.addEventListener('message', function (e) {
                var d = e.data;
                if (!d || d.source !== 'ripple-preview-host') return;
                if (d.type === 'hit-test') {
                    var hit = childAt(d.x, d.y);
                    if (d.hover) highlight(hit ? hit.el : null);
                    try {
                        parent.postMessage({
                            source: 'ripple-preview',
                            type: 'child-hit',
                            hover: !!d.hover,
                            probeId: d.probeId,
                            key: hit ? hit.k : null,
                        }, '*');
                    } catch (err) { }
                } else if (d.type === 'hover-end') {
                    highlight(null);
                }
            });
        })();
        </script>
        """;
}
