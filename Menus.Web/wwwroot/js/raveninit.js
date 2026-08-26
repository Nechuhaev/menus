document.addEventListener("DOMContentLoaded", function (event) {
    if (typeof (window.Sentry) != "undefined")
        window.Sentry.init({ dsn: 'https://503b1f370f7e45d19a94df6d768a4e15@sentry.io/1236741' });
    window.Logger = new Logger();
    window.onerror = function (message, url, line, row, error) {
        window.Logger.logError(error);
    };
});
var Logger = (function () {
    function Logger() {
    }
    Logger.prototype.logError = function (error) {
        if (typeof (window.Sentry) != "undefined" && window.Sentry.captureException)
            window.Sentry.captureException(error);
    };
    Logger.prototype.logTextError = function (text) {
        console.error(text);
        if (typeof (window.Sentry) != "undefined" && window.Sentry.captureMessage)
            window.Sentry.captureMessage(text, "error");
    };
    Logger.prototype.logDebug = function (text) {
        console.debug(text);
    };
    Logger.prototype.logInfo = function (text) {
        console.info(text);
        if (typeof (window.Sentry) != "undefined" && window.Sentry.captureMessage)
            window.Sentry.captureMessage(text, "info");
    };
    return Logger;
}());
//# sourceMappingURL=raveninit.js.map