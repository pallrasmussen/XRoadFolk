(function ($) {
    if (!$.validator) return;

    if ($.validator.methods.lettersonly) delete $.validator.methods.lettersonly;

    $.validator.addMethod("lettersonly", function (value, element) {
        if (this.optional(element)) return true;
        return /^[A-Za-zÀ-ÖØ-öø-ÿ' -]+$/.test(value || "");
    });

    if ($.validator.unobtrusive && !$.validator.unobtrusive.adapters.get("lettersonly")) {
        $.validator.unobtrusive.adapters.addBool("lettersonly");
    }
})(jQuery);