// ── Editor graph helpers ─────────────────────────────────────────
// Element measuring (for centring new nodes / minimap navigation) and a global
// Space-key bridge (Space + left-drag pans the canvas). One EditorGraph is alive
// at a time, so a single module-level dotnet reference is enough.

let _dotnet = null;

function inEditable() {
    const t = document.activeElement;
    return !!t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
}

function onKeyDown(e) {
    if (e.code !== 'Space' || inEditable()) return;
    e.preventDefault(); // stop the page from scrolling while Space is held to pan
    _dotnet && _dotnet.invokeMethodAsync('SetSpaceHeld', true);
}

function onKeyUp(e) {
    if (e.code !== 'Space') return;
    _dotnet && _dotnet.invokeMethodAsync('SetSpaceHeld', false);
}

function onBlur() {
    // Never leave Space "stuck" if focus leaves the window mid-hold.
    _dotnet && _dotnet.invokeMethodAsync('SetSpaceHeld', false);
}

export function register(dotnet) {
    _dotnet = dotnet;
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('blur', onBlur);
}

export function unregister() {
    window.removeEventListener('keydown', onKeyDown);
    window.removeEventListener('keyup', onKeyUp);
    window.removeEventListener('blur', onBlur);
    _dotnet = null;
}

// [left, top, width, height] of an element in viewport pixels.
export function rect(el) {
    const r = el.getBoundingClientRect();
    return [r.left, r.top, r.width, r.height];
}
