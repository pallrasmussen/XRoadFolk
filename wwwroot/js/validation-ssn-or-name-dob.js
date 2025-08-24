// Registers a method "ssnornamedob" and an unobtrusive adapter with params: ssn, first, last, dob.
(function ($) {
    function trim(v) { return (v || "").toString().trim(); }

    $.validator.addMethod("ssnornamedob", function (value, element, params) {
        var $form = $(element.form);

        function byName(name) {
            // Works with Tag Helpers; uses the input name attribute
            return $form.find(":input[name='" + name + "']");
        }

        var ssn = trim(byName(params.ssn).val());
        if (ssn) return true; // Rule satisfied by SSN

        var first = trim(byName(params.first).val());
        var last = trim(byName(params.last).val());
        var dob = trim(byName(params.dob).val());

        // Rule satisfied by all three present
        var ok = first && last && dob;

        // Revalidate when dependencies change
        byName(params.first).add(byName(params.last)).add(byName(params.dob))
            .off(".ssnOrNameDob")
            .on("keyup.ssnOrNameDob change.ssnOrNameDob", function () {
                $(element).valid();
            });

        return ok;
    });

    $.validator.unobtrusive.adapters.add("ssnornamedob", ["ssn", "first", "last", "dob"], function (options) {
        options.rules["ssnornamedob"] = {
            ssn: options.params.ssn,
            first: options.params.first,
            last: options.params.last,
            dob: options.params.dob
        };
        options.messages["ssnornamedob"] = options.message;
    });
})(jQuery);