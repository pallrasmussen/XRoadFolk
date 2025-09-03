(function ($) {
    'use strict';
    if (!$.validator) return;

    const parseIsoDate = (value) => {
        // returns Date in UTC or null
        const dt = new Date(value + 'T00:00:00Z');
        return isNaN(dt.getTime()) ? null : dt;
    };

    const tryParseDob = (value) => {
        if (!value) return null;
        const s = value.trim();

        // YYYY-MM-DD
        let m = /^\d{4}-\d{2}-\d{2}$/.exec(s);
        if (m) return parseIsoDate(s);

        // DD-MM-YYYY
        m = /^(\d{2})-(\d{2})-(\d{4})$/.exec(s);
        if (m) {
            const y = m[3], mo = m[2], d = m[1];
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
    };

    $.validator.addMethod('dob', function (value, element) {
        if (!value) return true; // not [Required]
        const dt = tryParseDob(value);
        if (!dt) return false;
        // Range checks
        const min = new Date('1900-01-01T00:00:00Z');
        const today = new Date(); today.setUTCHours(0,0,0,0);
        return dt >= min && dt <= today;
    });

    $.validator.unobtrusive.adapters.addBool('dob');
})(jQuery);