// Client adapter for SsnAttribute (format checks; server is authoritative)
(function ($) {
    if (!$.validator) return;

    function stripNonDigits(v) { return (v || '').replace(/\D/g, ''); }
    function allSame(v) { return /^([0-9])\1{8}$/.test(v); }

    function isValidSsn(digits) {
        if (digits.length !== 9) return false;
        if (allSame(digits)) return false;

        var area = parseInt(digits.slice(0, 3), 10);
        var group = parseInt(digits.slice(3, 5), 10);
        var serial = parseInt(digits.slice(5), 10);

        if (area === 0 || area === 666 || area >= 900) return false; // 000, 666, 900–999
        if (group === 0) return false;       // 00
        if (serial === 0) return false;      // 0000
        return true;
    }

    $.validator.addMethod('ssn', function (value, element) {
        var v = (value || '').trim();
        if (!v) return true; // let [Required] (if present) decide empties
        var digits = stripNonDigits(v);
        return isValidSsn(digits);
    });

    // Message comes from data-val-ssn rendered by the server attribute
    $.validator.unobtrusive.adapters.addBool('ssn');
})(jQuery);