(function () {
    'use strict';

    var hero = document.querySelector('[data-wos-hero-motion]');
    if (!hero) {
        return;
    }

    var prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var finePointer = window.matchMedia('(pointer: fine)').matches;
    var scene = hero.querySelector('.wos-hero-scene');
    var stage = hero.querySelector('.wos-hero-visual-stage');
    var layers = hero.querySelectorAll('[data-depth]');
    var header = document.querySelector('.wos-public-header');
    var pointerX = 0;
    var pointerY = 0;
    var targetX = 0;
    var targetY = 0;
    var scrollOffset = 0;
    var animationFrame = 0;

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function setLayerTransform(layer) {
        var depth = parseFloat(layer.getAttribute('data-depth')) || 1;
        var baseZ = parseFloat(layer.getAttribute('data-z')) || 0;
        var tx = pointerX * depth;
        var ty = pointerY * depth + scrollOffset * depth * 0.22;
        var rotY = pointerX * 0.055 * depth;
        var rotX = -pointerY * 0.045 * depth;
        layer.style.transform =
            'translate3d(' + tx.toFixed(2) + 'px,' + ty.toFixed(2) + 'px,' + baseZ + 'px) ' +
            'rotateX(' + rotX.toFixed(3) + 'deg) rotateY(' + rotY.toFixed(3) + 'deg)';
    }

    function renderScene() {
        animationFrame = 0;
        pointerX += (targetX - pointerX) * 0.14;
        pointerY += (targetY - pointerY) * 0.14;

        if (stage) {
            stage.style.transform =
                'translate3d(0,' + (-scrollOffset).toFixed(2) + 'px,0) ' +
                'rotateX(' + (-pointerY * 0.1).toFixed(3) + 'deg) ' +
                'rotateY(' + (pointerX * 0.12).toFixed(3) + 'deg)';
        }

        layers.forEach(setLayerTransform);
        hero.style.setProperty('--wos-hero-light-x', (68 + pointerX * 0.7).toFixed(2) + '%');
        hero.style.setProperty('--wos-hero-light-y', (22 + pointerY * 0.7).toFixed(2) + '%');

        if (Math.abs(targetX - pointerX) > 0.02 || Math.abs(targetY - pointerY) > 0.02) {
            animationFrame = window.requestAnimationFrame(renderScene);
        }
    }

    function scheduleScene() {
        if (!animationFrame) {
            animationFrame = window.requestAnimationFrame(renderScene);
        }
    }

    function updateScrollState() {
        scrollOffset = clamp(window.scrollY * 0.035, 0, 22);
        hero.style.setProperty('--wos-hero-scroll-fade', String(clamp(1 - window.scrollY / 760, 0.48, 1)));
        if (header) {
            header.classList.toggle('is-scrolled', window.scrollY > 16);
        }
        scheduleScene();
    }

    if (prefersReducedMotion) {
        hero.classList.add('wos-hero--reduced-motion');
        layers.forEach(function (layer) {
            var baseZ = parseFloat(layer.getAttribute('data-z')) || 0;
            layer.style.transform = 'translate3d(0, 0, ' + baseZ + 'px)';
        });
        if (header) {
            header.classList.toggle('is-scrolled', window.scrollY > 16);
        }
        return;
    }

    window.requestAnimationFrame(function () {
        hero.classList.add('wos-hero--animate-in');
    });

    if (finePointer && scene) {
        var sceneBounds = scene.getBoundingClientRect();

        function updateBounds() {
            sceneBounds = scene.getBoundingClientRect();
        }

        scene.addEventListener('pointermove', function (event) {
            var normalizedX = (event.clientX - sceneBounds.left) / sceneBounds.width - 0.5;
            var normalizedY = (event.clientY - sceneBounds.top) / sceneBounds.height - 0.5;
            targetX = clamp(normalizedX * 12, -6, 6);
            targetY = clamp(normalizedY * 9, -4.5, 4.5);
            scheduleScene();
        }, { passive: true });

        scene.addEventListener('pointerleave', function () {
            targetX = 0;
            targetY = 0;
            scheduleScene();
        });

        window.addEventListener('resize', updateBounds, { passive: true });
    }

    updateScrollState();
    window.addEventListener('scroll', updateScrollState, { passive: true });
    window.addEventListener('pagehide', function () {
        if (animationFrame) {
            window.cancelAnimationFrame(animationFrame);
        }
    });
})();
