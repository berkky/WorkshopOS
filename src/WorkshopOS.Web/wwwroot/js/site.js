(function () {
    function bindSubmitState(form) {
        if (form.dataset.wosSubmitBound === 'true') {
            return;
        }

        form.dataset.wosSubmitBound = 'true';
        form.addEventListener('submit', function (event) {
            if (typeof form.checkValidity === 'function' && !form.checkValidity()) {
                return;
            }

            var submitter = event.submitter || form.querySelector('[type="submit"]');
            if (submitter && !submitter.disabled) {
                submitter.disabled = true;
                if (!submitter.dataset.wosOriginalText) {
                    submitter.dataset.wosOriginalText = submitter.textContent || '';
                }
                submitter.textContent = document.body.dataset.wosProcessingText || 'Processing...';
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

    document.querySelectorAll('[data-wos-account-menu]').forEach(function (menu) {
        var trigger = menu.querySelector('[data-wos-account-trigger]');
        var panel = menu.querySelector('[data-wos-account-panel]');
        if (!trigger || !panel) {
            return;
        }

        function closeMenu() {
            menu.classList.remove('is-open');
            trigger.setAttribute('aria-expanded', 'false');
        }

        trigger.addEventListener('click', function (event) {
            event.stopPropagation();
            var isOpen = menu.classList.toggle('is-open');
            trigger.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
        });

        panel.addEventListener('click', function (event) {
            event.stopPropagation();
        });

        document.addEventListener('click', closeMenu);

        trigger.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') {
                closeMenu();
                trigger.blur();
            }
        });
    });
})();
