(function () {
    'use strict';

    var hero = document.querySelector('.wos-hero');
    if (!hero) {
        return;
    }

    var prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var finePointer = window.matchMedia('(pointer: fine)').matches;
    var scene = hero.querySelector('.wos-hero-scene');
    var stage = hero.querySelector('.wos-hero-visual-stage');
    var layers = hero.querySelectorAll('[data-depth]');
    var header = document.querySelector('.wos-public-header');

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function setLayerTransform(layer, px, py, scrollOffset) {
        var depth = parseFloat(layer.getAttribute('data-depth')) || 1;
        var baseZ = parseFloat(layer.getAttribute('data-z')) || 0;
        var tx = px * depth;
        var ty = py * depth + scrollOffset * depth * 0.3;
        var rotY = px * 0.08 * depth;
        var rotX = -py * 0.06 * depth;
        layer.style.transform =
            'translate3d(' + tx.toFixed(2) + 'px,' + ty.toFixed(2) + 'px,' + baseZ + 'px) ' +
            'rotateX(' + rotX.toFixed(3) + 'deg) rotateY(' + rotY.toFixed(3) + 'deg)';
    }

    function resetLayers() {
        layers.forEach(function (layer) {
            layer.style.transform = '';
        });
        if (stage) {
            stage.style.transform = '';
        }
    }

    /* Apply static depth on load for non-parallax devices */
    if (!finePointer && !prefersReducedMotion) {
        layers.forEach(function (layer) {
            var baseZ = parseFloat(layer.getAttribute('data-z')) || 0;
            layer.style.transform = 'translate3d(0, 0, ' + baseZ + 'px)';
        });
    }

    if (!prefersReducedMotion) {
        hero.classList.add('wos-hero--animate-in');

        if (finePointer && scene) {
            var bounds = { width: 0, height: 0 };
            var targetX = 0;
            var targetY = 0;
            var currentX = 0;
            var currentY = 0;
            var rafId = 0;

            function updateBounds() {
                var rect = scene.getBoundingClientRect();
                bounds.width = rect.width;
                bounds.height = rect.height;
            }

            function onPointerMove(event) {
                var rect = scene.getBoundingClientRect();
                var nx = (event.clientX - rect.left) / rect.width - 0.5;
                var ny = (event.clientY - rect.top) / rect.height - 0.5;
                targetX = clamp(nx * 16, -8, 8);
                targetY = clamp(ny * 12, -6, 6);
            }

            function onPointerLeave() {
                targetX = 0;
                targetY = 0;
            }

            function tick() {
                currentX += (targetX - currentX) * 0.08;
                currentY += (targetY - currentY) * 0.08;

                var scrollOffset = clamp(window.scrollY * 0.04, 0, 24);

                if (stage) {
                    stage.style.transform =
                        'translate3d(0,' + (-scrollOffset).toFixed(2) + 'px,0) ' +
                        'rotateX(' + (-currentY * 0.15).toFixed(3) + 'deg) ' +
                        'rotateY(' + (currentX * 0.2).toFixed(3) + 'deg)';
                }

                layers.forEach(function (layer) {
                    setLayerTransform(layer, currentX, currentY, scrollOffset);
                });

                var glow = hero.querySelector('.wos-hero-layer--glow');
                if (glow) {
                    var opacity = clamp(1 - window.scrollY / 600, 0.4, 1);
                    glow.style.opacity = String(opacity);
                }

                rafId = window.requestAnimationFrame(tick);
            }

            updateBounds();
            window.addEventListener('resize', updateBounds);
            scene.addEventListener('pointermove', onPointerMove);
            scene.addEventListener('pointerleave', onPointerLeave);
            rafId = window.requestAnimationFrame(tick);

            window.addEventListener('pagehide', function () {
                window.cancelAnimationFrame(rafId);
            });
        } else {
            window.addEventListener('scroll', function () {
                var scrollOffset = clamp(window.scrollY * 0.04, 0, 24);
                if (stage) {
                    stage.style.transform = 'translate3d(0,' + (-scrollOffset).toFixed(2) + 'px,0)';
                }
                var glow = hero.querySelector('.wos-hero-layer--glow');
                if (glow) {
                    glow.style.opacity = String(clamp(1 - window.scrollY / 600, 0.4, 1));
                }
            }, { passive: true });
        }
    } else {
        hero.classList.add('wos-hero--reduced-motion');
        layers.forEach(function (layer) {
            var baseZ = parseFloat(layer.getAttribute('data-z')) || 0;
            layer.style.transform = 'translate3d(0, 0, ' + baseZ + 'px)';
        });
    }

    if (header) {
        function onScrollHeader() {
            if (window.scrollY > 16) {
                header.classList.add('is-scrolled');
            } else {
                header.classList.remove('is-scrolled');
            }
        }
        onScrollHeader();
        window.addEventListener('scroll', onScrollHeader, { passive: true });
    }
})();
