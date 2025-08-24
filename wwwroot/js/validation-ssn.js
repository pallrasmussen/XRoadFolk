// Ensure SSN client rule allows empty and only checks when non-empty.
(function ($) {
    if (!$.validator) return;

    // Remove previous registration in hot-reload scenarios
    if ($.validator.methods.ssn) {
        delete $.validator.methods.ssn;
    }

    $.validator.addMethod("ssn", function (value, element) {
        // Optional: pass if empty. Cross-field rule handles requiredness.
        if (this.optional(element)) return true;

        // Accept exactly 9 digits (ignore non-digits like spaces or dashes)
        var digits = (value || "").replace(/\D/g, "");
        return digits.length === 9;
    });

    // If no explicit adapter exists, wire a bool adapter for data-val-ssn
    if ($.validator.unobtrusive && !$.validator.unobtrusive.adapters.get("ssn")) {
        $.validator.unobtrusive.adapters.addBool("ssn");
    }
})(jQuery);