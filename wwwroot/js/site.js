// BookMyFun — site.js
// Dark / Light mode theme management
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

    /**
     * Apply a theme to the document and update the toggle button.
     * @param {string} theme  'dark' | 'light'
     * @param {boolean} save  whether to persist to localStorage
     */
    function applyTheme(theme, save) {
        document.documentElement.setAttribute('data-theme', theme);

        var icon = document.getElementById('themeIcon');
        var btn = document.getElementById('themeSwitchBtn');

        if (icon) {
            icon.className = 'bi ' + ICONS[theme];
        }
        if (btn) {
            btn.title = TITLES[theme];
            btn.setAttribute('aria-label', TITLES[theme]);
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

        var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
        var theme = saved || (prefersDark ? DARK : LIGHT);

        applyTheme(theme, false); // html attr was already set by the anti-FOUC script; just sync the button

        var btn = document.getElementById('themeSwitchBtn');
        if (btn) {
            btn.addEventListener('click', toggleTheme);
        }

        // Keep in sync if the user changes OS preference while the tab is open (no saved override).
        if (window.matchMedia) {
            window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function (e) {
                try { if (localStorage.getItem(STORAGE_KEY)) return; } catch (_) { }
                applyTheme(e.matches ? DARK : LIGHT, false);
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
