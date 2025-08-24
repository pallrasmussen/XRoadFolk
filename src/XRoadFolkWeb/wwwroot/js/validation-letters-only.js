(function ($) {
    if (!window.jQuery || !$.validator) return;

    function getRegex() {
        try {
            // Unicode property escapes: letters + marks + whitespace + apostrophes (straight/curly) + hyphen
            return new RegExp("^[\\p{L}\\p{M}\\s\\-\\'’]+$", "u");
        } catch (e) {
            // Fallback for older browsers (Latin scripts incl. Extended blocks)
            // Hyphen is escaped to avoid unintended ranges; includes straight ' (U+0027) and curly ’ (U+2019)
            return new RegExp("^[A-Za-z\\u00C0-\\u02AF\\u1E00-\\u1EFF\\s\\u0027\\u2019\\-]+$");
        }
    }

    $.validator.addMethod("lettersonly", function (value, element) {
        if (this.optional(element)) return true;
        var re = getRegex();
        return re.test(value);
    });

    if ($.validator.unobtrusive) {
        // Maps data-val-lettersonly="..." to the "lettersonly" rule
        $.validator.unobtrusive.adapters.addBool("lettersonly");
    }
})(jQuery);