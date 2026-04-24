using Ganss.Xss;

namespace LPM.Services;

/// <summary>
/// Whitelist-based HTML sanitization for the Quill rich-text editor output.
/// Session summaries, weekly remarks, CS review notes, and folder summaries flow
/// through this before being stored and before being rendered via MarkupString.
///
/// Registered as a singleton — HtmlSanitizer is thread-safe after construction.
/// </summary>
public class HtmlSanitizerService
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizerService()
    {
        _sanitizer = new HtmlSanitizer();

        // Tags Quill emits (plus a couple of common synonyms). Anything not here is stripped.
        _sanitizer.AllowedTags.Clear();
        foreach (var t in new[] {
            "p", "br", "hr", "div", "span",
            "strong", "b", "em", "i", "u", "s", "strike", "sub", "sup",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "ol", "ul", "li",
            "blockquote", "pre", "code",
            "a", "img",
            "table", "thead", "tbody", "tr", "th", "td",
        }) _sanitizer.AllowedTags.Add(t);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var a in new[] {
            "href", "target", "rel", "title",
            "src", "alt", "width", "height",
            "class", "style", "dir", "colspan", "rowspan",
        }) _sanitizer.AllowedAttributes.Add(a);

        // Quill uses inline style for color/bg/alignment/size — keep a safe subset.
        _sanitizer.AllowedCssProperties.Clear();
        foreach (var p in new[] {
            "color", "background-color", "text-align",
            "font-size", "font-weight", "font-style", "text-decoration",
            "margin", "margin-left", "margin-right", "margin-top", "margin-bottom",
            "padding", "padding-left", "padding-right", "padding-top", "padding-bottom",
            "list-style-type",
        }) _sanitizer.AllowedCssProperties.Add(p);

        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
        _sanitizer.AllowedSchemes.Add("data"); // for inline images pasted into Quill

        // Keep textual content even when a disallowed tag is removed — so stripping
        // `<script>foo</script>` leaves "foo" behind instead of deleting user words.
        _sanitizer.KeepChildNodes = true;
    }

    /// <summary>
    /// Sanitize untrusted HTML. Returns empty string for null/blank input.
    /// Safe to call on output already sanitized (idempotent).
    /// </summary>
    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        return _sanitizer.Sanitize(html);
    }
}
