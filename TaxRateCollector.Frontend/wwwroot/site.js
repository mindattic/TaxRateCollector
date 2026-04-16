// ── Theme ─────────────────────────────────────────────────────────────────────
window.applyTheme = function (theme) {
    document.documentElement.setAttribute("data-theme", theme);
    localStorage.setItem("trc-theme", theme);
};

window.initTheme = function (serverTheme) {
    const stored = localStorage.getItem("trc-theme") || serverTheme || "light";
    document.documentElement.setAttribute("data-theme", stored);
    return stored;
};

window.getTheme = function () {
    return localStorage.getItem("trc-theme") || "light";
};

// ── Font ──────────────────────────────────────────────────────────────────────
const FONT_MAP = {
    "outfit":  "'Outfit', system-ui, sans-serif",
    "roboto":  "'Roboto', system-ui, sans-serif",
    "lato":    "'Lato', system-ui, sans-serif",
    "calibri": "'Calibri', Candara, system-ui, sans-serif",
    "arial":   "Arial, Helvetica, sans-serif",
    "times":   "'Times New Roman', Times, serif",
};

window.applyFont = function (fontKey, fontSize) {
    const family = FONT_MAP[fontKey] || FONT_MAP["outfit"];
    document.documentElement.style.setProperty("--app-font", family);
    document.documentElement.style.setProperty("--app-font-size", fontSize + "px");
    localStorage.setItem("trc-font", fontKey);
    localStorage.setItem("trc-font-size", fontSize);
};

window.initFont = function (serverFont, serverSize) {
    const fontKey  = localStorage.getItem("trc-font")      || serverFont || "outfit";
    const fontSize = localStorage.getItem("trc-font-size") || serverSize || 14;
    window.applyFont(fontKey, parseInt(fontSize, 10));
};

// ── File download ─────────────────────────────────────────────────────────────
window.downloadBase64File = function (fileName, mimeType, base64) {
    const bytes = atob(base64);
    const buf = new Uint8Array(bytes.length);
    for (let i = 0; i < bytes.length; i++) buf[i] = bytes.charCodeAt(i);
    const blob = new Blob([buf], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};
