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
    }
};
