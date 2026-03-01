// Dark mode toggle - reads/writes a cookie, uses Bootstrap 5.3 data-bs-theme
(function () {
    function getThemeCookie() {
        var match = document.cookie.match(/(?:^|;\s*)theme=([^;]*)/);
        return match ? match[1] : null;
    }

    function setThemeCookie(theme) {
        var d = new Date();
        d.setFullYear(d.getFullYear() + 1);
        document.cookie = "theme=" + theme + ";path=/;expires=" + d.toUTCString() + ";SameSite=Lax";
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-bs-theme", theme);
        var btn = document.getElementById("theme-toggle");
        if (btn) {
            btn.textContent = theme === "dark" ? "\u2600\uFE0F" : "\uD83C\uDF19";
            btn.title = theme === "dark" ? "Switch to light mode" : "Switch to dark mode";
        }
    }

    var saved = getThemeCookie() || "light";

    document.addEventListener("DOMContentLoaded", function () {
        applyTheme(saved);
        var btn = document.getElementById("theme-toggle");
        if (btn) {
            btn.addEventListener("click", function () {
                var current = document.documentElement.getAttribute("data-bs-theme") || "light";
                var next = current === "dark" ? "light" : "dark";
                applyTheme(next);
                setThemeCookie(next);
            });
        }
    });
})();
