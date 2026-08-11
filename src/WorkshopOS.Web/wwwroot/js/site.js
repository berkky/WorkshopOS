(function () {
    'use strict';

    var reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

    function initSubmitStates() {
        document.querySelectorAll('form[method="post"]').forEach(function (form) {
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
        });
    }

    function initMobileNavigation() {
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
    }

    function initAccountMenus() {
        document.querySelectorAll('[data-wos-account-menu]').forEach(function (menu) {
            if (menu.dataset.wosMotionBound === 'true') {
                return;
            }

            var trigger = menu.querySelector('[data-wos-account-trigger]');
            var panel = menu.querySelector('[data-wos-account-panel]');
            if (!trigger || !panel) {
                return;
            }

            menu.dataset.wosMotionBound = 'true';

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
            document.addEventListener('keydown', function (event) {
                if (event.key === 'Escape' && menu.classList.contains('is-open')) {
                    closeMenu();
                    trigger.focus();
                }
            });
        });
    }

    function revealImmediately(elements) {
        elements.forEach(function (element) {
            element.classList.add('is-revealed');
        });
    }

    function initScrollReveal() {
        var revealElements = Array.prototype.slice.call(document.querySelectorAll('.wos-reveal'));
        if (revealElements.length === 0) {
            return;
        }

        document.querySelectorAll('[data-wos-reveal-stagger]').forEach(function (element) {
            Array.prototype.slice.call(element.children).forEach(function (child, index) {
                child.style.setProperty('--wos-stagger-index', String(Math.min(index, 7)));
            });
        });

        if (reducedMotion.matches || !('IntersectionObserver' in window)) {
            revealImmediately(revealElements);
            return;
        }

        document.documentElement.classList.add('wos-motion-enabled');
        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (!entry.isIntersecting) {
                    return;
                }

                entry.target.classList.add('is-revealed');
                observer.unobserve(entry.target);
            });
        }, {
            threshold: 0.12,
            rootMargin: '0px 0px -7% 0px'
        });

        revealElements.forEach(function (element) {
            observer.observe(element);
        });
    }

    function initPageEntry() {
        var pageEntries = Array.prototype.slice.call(document.querySelectorAll('[data-wos-page-entry]'));
        if (pageEntries.length === 0) {
            return;
        }

        if (reducedMotion.matches) {
            pageEntries.forEach(function (entry) {
                entry.classList.add('is-entered');
            });
            return;
        }

        document.documentElement.classList.add('wos-motion-enabled');
        window.requestAnimationFrame(function () {
            pageEntries.forEach(function (entry) {
                entry.classList.add('is-entered');
            });
        });
    }

    initSubmitStates();
    initMobileNavigation();
    initAccountMenus();
    initScrollReveal();
    initPageEntry();
})();
