(function () {
    function bindSubmitState(form) {
        if (form.dataset.wosSubmitBound === 'true') {
            return;
        }

        form.dataset.wosSubmitBound = 'true';
        form.addEventListener('submit', function () {
            if (!form.checkValidity || form.checkValidity()) {
                var submitter = form.querySelector('[type="submit"]');
                if (submitter && !submitter.disabled) {
                    submitter.disabled = true;
                    if (!submitter.dataset.wosOriginalText) {
                        submitter.dataset.wosOriginalText = submitter.textContent || '';
                    }
                    submitter.textContent = 'Processing...';
                }
            }
        });
    }

    document.querySelectorAll('form[method="post"]').forEach(bindSubmitState);

    document.querySelectorAll('.wos-offcanvas-nav .wos-nav-link').forEach(function (link) {
        link.addEventListener('click', function () {
            var offcanvasEl = document.getElementById('wosMobileNav');
            if (offcanvasEl && window.bootstrap && bootstrap.Offcanvas) {
                var instance = bootstrap.Offcanvas.getInstance(offcanvasEl);
                if (instance) {
                    instance.hide();
                }
            }
        });
    });
})();
