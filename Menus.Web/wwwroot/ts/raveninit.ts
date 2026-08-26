interface Window {
    Sentry: any;
    Logger: ILogger;
}

document.addEventListener("DOMContentLoaded", event => {
    // Init sentry/raven
    if (typeof (window.Sentry) != "undefined") window.Sentry.init({ dsn: 'https://503b1f370f7e45d19a94df6d768a4e15@sentry.io/1236741' });

    window.Logger = new Logger();

    window.onerror = (message: string, url: string, line: number, row: number, error: Error) => {
        window.Logger.logError(error);
    };
});

interface ILogger {
    logError: Function,
    logTextError: Function,
    logDebug: Function,
    logInfo: Function;
}


class Logger implements ILogger {
    logError(error: Error) {
        if (typeof (window.Sentry) != "undefined" && window.Sentry.captureException) window.Sentry.captureException(error);
        //else console.error(error);
    }

    logTextError(text: string) {
        console.error(text);
        if (typeof (window.Sentry) != "undefined" && window.Sentry.captureMessage) window.Sentry.captureMessage(text, "error");
    }  

    logDebug(text: string) {
        console.debug(text);
    }

    logInfo(text: string) {
        console.info(text);
        if (typeof (window.Sentry) != "undefined" && window.Sentry.captureMessage) window.Sentry.captureMessage(text, "info");
    }   
}