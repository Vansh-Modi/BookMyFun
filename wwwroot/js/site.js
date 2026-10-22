// BookMyFun — site.js
// Dark / Light mode theme management (Multi-device synchronized)
// ─────────────────────────────────────────────────────────────────────────────

(function () {
    'use strict';

    var STORAGE_KEY = 'bmf-theme';
    var DARK = 'dark';
    var LIGHT = 'light';

    /** Icons for each state (Bootstrap Icons class names) */
    var ICONS = {
        dark: 'bi-sun-fill',         // currently dark  → show sun to switch to light
        light: 'bi-moon-stars-fill'  // currently light → show moon to switch to dark
    };

    /** Title tooltip for each state */
    var TITLES = {
        dark: 'Switch to Light Mode',
        light: 'Switch to Dark Mode'
    };

    /** Text labels for drawer/menu toggles */
    var LABELS = {
        dark: 'Light Mode',
        light: 'Dark Mode'
    };

    /**
     * Apply a theme to the document and update all toggle buttons and icons.
     * @param {string} theme  'dark' | 'light'
     * @param {boolean} save  whether to persist to localStorage
     */
    function applyTheme(theme, save) {
        document.documentElement.setAttribute('data-theme', theme);

        var icons = document.querySelectorAll('.theme-icon, #themeIcon');
        icons.forEach(function (icon) {
            icon.className = 'bi ' + ICONS[theme] + (icon.classList.contains('theme-icon') ? ' theme-icon' : '');
        });

        var labels = document.querySelectorAll('.theme-mode-label');
        labels.forEach(function (label) {
            label.textContent = LABELS[theme];
        });

        var btns = document.querySelectorAll('.theme-switch-btn, #themeSwitchBtn');
        btns.forEach(function (btn) {
            btn.title = TITLES[theme];
            btn.setAttribute('aria-label', TITLES[theme]);
        });

        var meta = document.getElementById('metaThemeColor');
        if (meta) {
            meta.setAttribute('content', theme === DARK ? '#0b1120' : '#0f172a');
        }

        if (save) {
            try { localStorage.setItem(STORAGE_KEY, theme); } catch (e) { /* quota exceeded / private mode */ }
        }
    }

    /** Toggle between dark and light. */
    function toggleTheme() {
        var current = document.documentElement.getAttribute('data-theme') || LIGHT;
        applyTheme(current === DARK ? LIGHT : DARK, true);
    }

    /** Initialise on DOM ready. */
    function init() {
        // Read persisted preference; fall back to OS preference, then light.
        var saved = null;
        try { saved = localStorage.getItem(STORAGE_KEY); } catch (e) { }

        var prefersDark = false;
        try {
            prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
        } catch (_) { }

        var theme = saved || (prefersDark ? DARK : LIGHT);
        applyTheme(theme, false);

        // Keep all tabs in sync when user switches theme in another tab
        window.addEventListener('storage', function (e) {
            if (e.key === STORAGE_KEY && (e.newValue === DARK || e.newValue === LIGHT)) {
                applyTheme(e.newValue, false);
            }
        });

        // Keep in sync if the user changes OS preference while the tab is open (no saved override).
        if (window.matchMedia) {
            try {
                window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function (e) {
                    try { if (localStorage.getItem(STORAGE_KEY)) return; } catch (_) { }
                    applyTheme(e.matches ? DARK : LIGHT, false);
                });
            } catch (_) { }
        }
    }

    // Delegated click listener for all theme switch buttons (works for static & dynamic elements)
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.theme-switch-btn, #themeSwitchBtn');
        if (btn) {
            e.preventDefault();
            toggleTheme();
        }
    });

    // ─────────────────────────────────────────────────────────────────────────────
    // Clickable Cards Universal Handler
    // ─────────────────────────────────────────────────────────────────────────────
    document.addEventListener('click', function (e) {
        // If clicked on or inside an explicit link/button/input, allow default behavior
        if (e.target.closest('a, button, input, select, textarea, label, [data-prevent-card-click]')) {
            return;
        }

        var card = e.target.closest('.property-card, .clickable-card, [data-card-url]');
        if (card) {
            var url = card.getAttribute('data-card-url');
            if (!url) {
                var link = card.querySelector('a.stretched-link, h3.property-title a, a.primary-card-link');
                if (link && link.href) {
                    url = link.href;
                }
            }
            if (url) {
                if (e.ctrlKey || e.metaKey) {
                    window.open(url, '_blank');
                } else {
                    window.location.href = url;
                }
            }
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
