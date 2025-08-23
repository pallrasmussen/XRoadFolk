(function ($) {
    if (!$.validator) return;

    $.validator.addMethod('dob', function (value, element) {
        if (!value) return true; // not [Required]
        const m = /^\d{4}-\d{2}-\d{2}$/.exec(value.trim());
        if (!m) return false;
        const dt = new Date(value + 'T00:00:00Z');
        if (isNaN(dt.getTime())) return false;
        const min = new Date('1900-01-01T00:00:00Z');
        const today = new Date(); today.setUTCHours(0,0,0,0);
        return dt >= min && dt <= today;
    });

    $.validator.unobtrusive.adapters.addBool('dob');
})(jQuery);