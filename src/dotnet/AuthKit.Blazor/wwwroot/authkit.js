// AuthKit browser storage.
//
// sessionStorage is used instead of localStorage on purpose: the session dies with the tab,
// which limits the window for token theft after an XSS. See docs/threat-model.md (T-05).

export function getItem(key) {
    return sessionStorage.getItem(key);
}

export function setItem(key, value) {
    sessionStorage.setItem(key, value);
}

export function removeItem(key) {
    sessionStorage.removeItem(key);
}
