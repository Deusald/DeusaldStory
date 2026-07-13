using System;
using System.Net;
using System.Text.RegularExpressions;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Turns a preview string into HTML that renders only a small whitelist of basic
    /// formatting tags (<c>&lt;b&gt; &lt;i&gt; &lt;br&gt; &lt;ins&gt; &lt;del&gt;
    /// &lt;sup&gt; &lt;sub&gt; &lt;small&gt;</c>). Everything else — including tag
    /// attributes, unknown tags and raw <c>&lt; &gt; &amp;</c> — stays escaped, so the
    /// result is safe to feed into a Blazor <c>MarkupString</c>. Mirrors the localizer's
    /// own preview sanitizer so story text renders the same basic tags as its source keys.
    ///
    /// Two project-image tags are also understood when an image resolver is supplied:
    /// <c>&lt;icon=Name&gt;</c> renders the named image inline (sized to the text) and
    /// <c>&lt;sprite=Name&gt;</c> renders it as a standalone picture. Both resolve their
    /// name against the project's image library; a name that does not resolve is left
    /// escaped so the author sees the unresolved tag.
    /// </summary>
    public static class PreviewHtmlSanitizer
    {
        // Matches an encoded whitelisted tag with no attributes, e.g. "&lt;b&gt;",
        // "&lt;/small&gt;" or a self-closing "&lt;br /&gt;". Longer tag names come first
        // so alternation prefers "ins" over "i". Attributes are intentionally not allowed:
        // anything with an attribute never matches and remains harmlessly escaped.
        private static readonly Regex _AllowedTag = new Regex(
            @"&lt;(/?)(br|ins|del|sup|sub|small|b|i)\s*/?&gt;",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Matches an encoded image tag "&lt;icon=Name&gt;" or "&lt;sprite=Name&gt;". The
        // name runs (non-greedily) up to the closing "&gt;"; "[^&]" keeps it from spilling
        // past the encoded delimiter (and so excludes names carrying escaped punctuation).
        private static readonly Regex _ImageTag = new Regex(
            @"&lt;(icon|sprite)=([^&]+?)&gt;",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Matches an encoded storage-variable reference "&lt;var=Name&gt;" — replaced by the
        // variable's slot label (e.g. SA), styled by storage kind, when a resolver is supplied.
        private static readonly Regex _VarTag = new Regex(
            @"&lt;var=([^&]+?)&gt;",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Renders <paramref name="text"/> to safe HTML. Pass <paramref name="resolveImage"/> to enable the
        /// <c>&lt;icon=Name&gt;</c> / <c>&lt;sprite=Name&gt;</c> tags: it is called with the tag name
        /// (<c>"icon"</c> or <c>"sprite"</c>, lower-cased) and the referenced image name, and returns the matching
        /// image or <c>null</c> to leave the tag unresolved (escaped).
        /// </summary>
        public static string ToSafeHtml(
            string text, Func<string, string, StoryImage?>? resolveImage = null,
            Func<string, StoryRegisterVariableNode?>? resolveVariable = null)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            string encoded = WebUtility.HtmlEncode(text);
            string result  = _AllowedTag.Replace(encoded, Restore);

            if (resolveImage is not null)
                result = _ImageTag.Replace(result, match => RestoreImage(match, resolveImage));

            if (resolveVariable is not null)
                result = _VarTag.Replace(result, match => RestoreVariable(match, resolveVariable));

            return result;
        }

        private static string Restore(Match match)
        {
            string tag = match.Groups[2].Value.ToLowerInvariant();
            // <br> is void — always render it self-contained, ignoring any stray slash.
            if (tag == "br") return "<br>";
            return $"<{match.Groups[1].Value}{tag}>";
        }

        private static string RestoreImage(Match match, Func<string, string, StoryImage?> resolveImage)
        {
            string      tag  = match.Groups[1].Value.ToLowerInvariant();
            string      name = WebUtility.HtmlDecode(match.Groups[2].Value);
            StoryImage? image = resolveImage(tag, name);
            if (image is null) return match.Value; // unresolved — keep the escaped tag visible

            // "icon" renders inline with the text; "sprite" renders as a standalone picture. base64 PNG
            // characters are attribute-safe; the name is encoded for the alt text.
            string cls = tag == "icon" ? "lpv-inline-icon" : "lpv-sprite";
            string alt = WebUtility.HtmlEncode(name);
            return $"<img class=\"{cls}\" src=\"data:image/png;base64,{image.Data}\" alt=\"{alt}\">";
        }

        private static string RestoreVariable(Match match, Func<string, StoryRegisterVariableNode?> resolveVariable)
        {
            string                     name = WebUtility.HtmlDecode(match.Groups[1].Value);
            StoryRegisterVariableNode? reg  = resolveVariable(name);
            if (reg is null) return match.Value; // unresolved — keep the escaped tag visible

            string label = WebUtility.HtmlEncode(StorageSlots.Label(reg.Type, reg.SlotIndex));
            string kind  = reg.Type.ToString().ToLowerInvariant();
            return $"<span class=\"lpv-var lpv-var-{kind}\">{label}</span>";
        }
    }
}
