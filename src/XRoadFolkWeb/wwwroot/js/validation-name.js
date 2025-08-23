(function ($) {
    if (!$.validator) return;

    // Must start with a letter; letters+marks/spaces/hyphen/apostrophe; 2-50 chars
    const rx = /^[\p{L}][\p{L}\p{M}\s\-']{1,49}$/u;

    $.validator.addMethod('name', function (value, element) {
        if (!value) return true; // not [Required]
        return rx.test(value.trim());
    });

    $.validator.unobtrusive.adapters.addBool('name');
})(jQuery);