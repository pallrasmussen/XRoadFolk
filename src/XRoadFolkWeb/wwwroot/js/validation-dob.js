(function ($) {
    if (!$.validator) return;

    function parseIsoDate(value) {
        // returns Date in UTC or null
        var dt = new Date(value + 'T00:00:00Z');
        return isNaN(dt.getTime()) ? null : dt;
    }

    function tryParseDob(value) {
        if (!value) return null;
        var s = value.trim();

        // YYYY-MM-DD
        var m = /^\d{4}-\d{2}-\d{2}$/.exec(s);
        if (m) return parseIsoDate(s);

        // DD-MM-YYYY
        m = /^(\d{2})-(\d{2})-(\d{4})$/.exec(s);
        if (m) {
            var y = m[3], mo = m[2], d = m[1];
            return parseIsoDate(y + '-' + mo + '-' + d);
        }

        // YYYY/MM/DD
        m = /^(\d{4})\/(\d{2})\/(\d{2})$/.exec(s);
        if (m) {
            return parseIsoDate(m[1] + '-' + m[2] + '-' + m[3]);
        }

        // DD/MM/YYYY
        m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(s);
        if (m) {
            return parseIsoDate(m[3] + '-' + m[2] + '-' + m[1]);
        }

        // YYYYMMDD
        m = /^(\d{4})(\d{2})(\d{2})$/.exec(s);
        if (m) {
            return parseIsoDate(m[1] + '-' + m[2] + '-' + m[3]);
        }

        // DDMMYYYY
        m = /^(\d{2})(\d{2})(\d{4})$/.exec(s);
        if (m) {
            return parseIsoDate(m[3] + '-' + m[2] + '-' + m[1]);
        }

        return null;
    }

    $.validator.addMethod('dob', function (value, element) {
        if (!value) return true; // not [Required]
        var dt = tryParseDob(value);
        if (!dt) return false;
        // Range checks
        var min = new Date('1900-01-01T00:00:00Z');
        var today = new Date(); today.setUTCHours(0,0,0,0);
        return dt >= min && dt <= today;
    });

    $.validator.unobtrusive.adapters.addBool('dob');
})(jQuery);