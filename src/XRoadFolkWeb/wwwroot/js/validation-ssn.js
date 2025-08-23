// Client adapter for SsnAttribute (simple 9-digit check; server is authoritative)
(function ($) {
    if (!$.validator) return;

    $.validator.addMethod('ssn', function (value, element) {
        if (!value) return true; // let [Required] handle empties
        const digits = (value || '').replace(/\D/g, '');
        return digits.length === 9;
    });

    // Message comes from data-val-ssn rendered by the server
    $.validator.unobtrusive.adapters.addBool('ssn');
})(jQuery);