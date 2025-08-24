(function ($) {
    if (!window.jQuery || !$.validator) return;

    function getRegex() {
        try {
            // Prefer full Unicode letters + marks, spaces, hyphen, apostrophe
            return new RegExp("^[\\p{L}\\p{M}\\s\\-']+$", "u");
        } catch (e) {
            // Fallback for older browsers (Latin-1 supplement)
            return /^[A-Za-zÀ-ÖØ-öø-ÿ' -]+$/;
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