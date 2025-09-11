// Client adapter for SsnAttribute (format checks; server is authoritative)
(function ($) {
    'use strict';
    if (!$.validator) return;

    const stripNonDigits = (v) => (v || '').replace(/\D/g, '');
    const allSame = (v) => /^([0-9])\1{8}$/.test(v);

    const isValidSsn = (digits) => {
        if (digits.length !== 9) return false;
        if (allSame(digits)) return false;

        const area = parseInt(digits.slice(0, 3), 10);
        const group = parseInt(digits.slice(3, 5), 10);
        const serial = parseInt(digits.slice(5), 10);

        // 000, 666, 900–999 not allowed; group 00 and serial 0000 invalid
        if (area === 0 || area === 666 || area >= 900) return false;
        if (group === 0) return false;
        if (serial === 0) return false;
        return true;
    };

    $.validator.addMethod('ssn', function (value, element) {
        const v = (value || '').trim();
        if (!v) return true; // let [Required] (if present) decide empties
        const digits = stripNonDigits(v);
        return isValidSsn(digits);
    });

    // Message comes from data-val-ssn rendered by the server attribute
    $.validator.unobtrusive.adapters.addBool('ssn');
})(jQuery);