(function ($) {
    if (!window.jQuery || !$.validator) return;

    // Cross-field rule: SSN OR (FirstName + LastName + DateOfBirth)
    $.validator.addMethod("ssnornamedob", function (value, element) {
        var $form = $(element).closest("form");
        var ssn = $.trim(($form.find('[name="Ssn"]').val() || ""));
        var first = $.trim(($form.find('[name="FirstName"]').val() || ""));
        var last = $.trim(($form.find('[name="LastName"]').val() || ""));
        var dob = $.trim(($form.find('[name="DateOfBirth"]').val() || ""));

        if (ssn.length > 0) return true;
        if (first.length > 0 && last.length > 0 && dob.length > 0) return true;
        return false;
    }, function () {
        // Default message; can be localized by overriding window.ValidationMessages.ProvideSsnOrNameDob
        return (window.ValidationMessages && window.ValidationMessages.ProvideSsnOrNameDob) ||
               "Provide SSN or First/Last name with DOB.";
    });

    // Attach the rule to all involved fields if present on the page
    $(function () {
        $('form').each(function () {
            var $f = $(this);
            // Only if jQuery Validate is attached
            if (!$f.data('validator')) return;

            var $targets = $f.find('[name="Ssn"],[name="FirstName"],[name="LastName"],[name="DateOfBirth"]');
            if ($targets.length !== 4) return;

            $targets.each(function () {
                $(this).rules('add', { ssnornamedob: true });
            });
        });
    });
})(jQuery);