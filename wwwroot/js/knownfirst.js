window.knownFirst = {
    setDocumentLanguage: languageCode => {
        document.documentElement.lang = languageCode;
    },
    setDocumentTheme: themeName => {
        document.documentElement.dataset.theme = themeName;
    },
    revealElement: element => {
        if (!element) {
            return;
        }

        const bounds = element.getBoundingClientRect();
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
        const isFullyVisible = bounds.top >= 0 && bounds.bottom <= viewportHeight;

        if (!isFullyVisible) {
            element.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
    },
    focusElement: element => {
        if (element) {
            element.focus({ preventScroll: true });
        }
    },
    shortcuts: {
        register: (pageId, dotNetHelper) => {
            if (!window.knownFirst.shortcuts._handlers) {
                window.knownFirst.shortcuts._handlers = new Map();
                window.knownFirst.shortcuts._globalListener = (e) => {
                    const active = document.activeElement;
                    if (active) {
                        const tag = active.tagName.toUpperCase();
                        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || tag === 'BUTTON' || tag === 'A' || active.isContentEditable) {
                            return;
                        }
                    }
                    if (e.isComposing || e.keyCode === 229) {
                        return;
                    }
                    window.knownFirst.shortcuts._handlers.forEach(helper => {
                        helper.invokeMethodAsync('OnKeyDown', e.key, e.code, e.repeat);
                    });
                };
                window.addEventListener('keydown', window.knownFirst.shortcuts._globalListener);
            }
            window.knownFirst.shortcuts._handlers.set(pageId, dotNetHelper);
        },
        unregister: (pageId) => {
            if (window.knownFirst.shortcuts._handlers) {
                window.knownFirst.shortcuts._handlers.delete(pageId);
                if (window.knownFirst.shortcuts._handlers.size === 0) {
                    window.removeEventListener('keydown', window.knownFirst.shortcuts._globalListener);
                    window.knownFirst.shortcuts._handlers = null;
                    window.knownFirst.shortcuts._globalListener = null;
                }
            }
        }
    }
};

document.addEventListener("keydown", event => {
    if (event.key === "Enter"
        && event.target instanceof Element
        && event.target.closest("[data-destructive-confirm]")) {
        event.preventDefault();
    }
});
