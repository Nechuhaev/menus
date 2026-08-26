(function () {
    var userModes = {
        user: "user",
        restaurant: "restaurant"
    };
    var tabModes = {
        UserMode: 0,
        SignIn: 1,
        SignUp: 2,
    };
    var currentUserMode = userModes.user;
    function getParamFromUrl(name) {
        var link = location.href;
        var result;
        var urlRegex = new RegExp(name + '=([^&]+)', 'm');
        var match = urlRegex.exec(link);
        if (match !== null) {
            result = match[1];
        }
        else {
            result = '';
        }
        return result;
    }
    function setCookie(name, value, days) {
        var expires = "";
        if (days) {
            var date = new Date();
            date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
            expires = "; expires=" + date.toUTCString();
        }
        document.cookie = name + "=" + (value || "") + expires + "; path=/";
    }
    var signinModal = document.querySelector('.signin-modal-container');
    var signinBg = document.querySelector('.signin-modal-container .modal-background');
    var signinCloseModal = document.querySelector('.signin-modal-container .close-modal');
    var signintabButton = document.querySelector('.tab-choose-sign-in');
    var signuptabButton = document.querySelector('.tab-choose-sign-up');
    var signintab = document.querySelector('#signin-tab');
    var signuptab = document.querySelector('#signup-tab');
    var signinCheck = document.querySelector('#signin-tab .checkbox');
    var signupCheck = document.querySelector('#signup-tab .checkbox');
    var signinCheckInput = document.querySelector('#remember-sign-in');
    var signupCheckInput = document.querySelector('#remember-sign-up');
    var popupOpenButtons = document.querySelectorAll('[data-sign-in]');
    var popupSignUpOpenButtons = document.querySelectorAll('[data-sign-up]');
    var usermodetab = document.querySelector('#user-mode-tab');
    ;
    var popupUserModeButtons = document.querySelectorAll('[data-user-mode]');
    var usermodeInput = document.querySelector('[name="usermode"]');
    signintabButton.onclick = function (e) {
        e.preventDefault();
        signuptab.classList.add('hidden');
        signintab.classList.remove('hidden');
        signuptabButton.classList.remove('active');
        signintabButton.classList.add('active');
    };
    signuptabButton.onclick = function (e) {
        e.preventDefault();
        signintab.classList.add('hidden');
        signuptab.classList.remove('hidden');
        signintabButton.classList.remove('active');
        signuptabButton.classList.add('active');
    };
    signinCheck.onclick = function () {
        signinCheck.classList.toggle('checked');
        signinCheckInput.checked = !signinCheckInput.checked;
    };
    signupCheck.onclick = function () {
        signupCheck.classList.toggle('checked');
        signupCheckInput.checked = !signupCheckInput.checked;
    };
    document.addEventListener("click", function (e) {
        if (e.target.classList.contains("close-modal")) {
            e.preventDefault();
            signinModal.classList.add('hidden');
            var loginModal = document.querySelector(".modal.sign-in");
            var restoreModal = document.querySelector(".modal.restore-pwd");
            loginModal.classList.remove("hidden");
            restoreModal.classList.add("hidden");
        }
    });
    signinBg.onclick = function () {
        signinModal.classList.add('hidden');
    };
    if (location.href.indexOf("ReturnUrl") !== -1) {
        signinModal.classList.remove('hidden');
        document.querySelectorAll(".signin-modal-container .modal").forEach(function (elem) {
            elem.classList.add("hidden");
        });
        document.querySelector(".modal.sign-in").classList.remove("hidden");
        signuptab.classList.add('hidden');
        signintab.classList.remove('hidden');
        signuptabButton.classList.remove('active');
        signintabButton.classList.add('active');
        document.querySelector('#signin-tab .signin-form input[name=username]').focus();
    }
    for (var i = 0; i < popupUserModeButtons.length; i++) {
        var elem = popupUserModeButtons[i];
        elem.addEventListener('click', function (event) {
            event.preventDefault();
            showTab(tabModes.UserMode, event);
        });
    }
    for (var i = 0; i < popupOpenButtons.length; i++) {
        var elem = popupOpenButtons[i];
        elem.addEventListener('click', function (event) {
            event.preventDefault();
            showTab(tabModes.SignIn, event);
        });
    }
    for (var i = 0; i < popupSignUpOpenButtons.length; i++) {
        var elem = popupSignUpOpenButtons[i];
        elem.addEventListener('click', function (event) {
            event.preventDefault();
            showTab(tabModes.SignUp, event);
        });
    }
    var signinForm = document.querySelector('#signin-tab .signin-form');
    signinForm.onsubmit = function (e) {
        e.preventDefault();
        fetch('/Account/Login', {
            method: 'POST',
            credentials: "same-origin",
            body: new FormData(signinForm)
        })
            .then(function (e) {
            if (!e.ok) {
                document.querySelector(".disclaimer-error.wrong-pwd-error").classList.remove("hidden");
                return;
            }
            if (e.redirected) {
                location.href = decodeURIComponent('ru/restaurants/manage/');
            }
            else {
                var returnUrl = getParamFromUrl("ReturnUrl");
                if (returnUrl) {
                    location.href = decodeURIComponent(returnUrl);
                }
                else {
                    location.reload();
                }
            }
        })
            .catch(function () {
            console.error("Can't register/log in");
        });
    };
    var signupForm = document.querySelector('#signup-tab .signin-form');
    signupForm.onsubmit = function (e) {
        e.preventDefault();
        fetch('/Account/Register', {
            method: 'POST',
            credentials: "same-origin",
            body: new FormData(signupForm)
        })
            .then(function (e) {
            if (e.status === 400) {
                document.querySelector(".disclaimer-error.user-exist-error").classList.remove("hidden");
                return;
            }
            e.json().then(function (x) {
                var isEmail = x.isEmail;
                var returnUrl = getParamFromUrl("ReturnUrl");
                if (isEmail) {
                    document.querySelector(".modal.sign-in").classList.add("hidden");
                    document.querySelector(".modal.email-pwd").classList.remove("hidden");
                    if (x.link)
                        location.href = x.link;
                }
                else if (returnUrl)
                    location.href = decodeURIComponent(returnUrl);
                else
                    location.reload();
            });
        })
            .catch(function () {
            console.error("Can't register/log in");
        });
    };
    var fbSignin = document.querySelector('#signin-tab .sign-in-with-fb');
    var gSignin = document.querySelector('#signin-tab .sign-in-with-google');
    var fbSignup = document.querySelector('#signup-tab .sign-in-with-fb');
    var gSignup = document.querySelector('#signup-tab .sign-in-with-google');
    var externalLoginForm = document.querySelector('#external-login');
    var externalLoginProvider = document.querySelector('#external-login-provider');
    fbSignin.onclick = function (e) {
        if (fbSignin.attributes.getNamedItem("href").value !== "#") {
            e.preventDefault();
            document.querySelector(".modal.sign-in").classList.add("hidden");
            document.querySelector(".modal.webview-error").classList.remove("hidden");
            return;
        }
        e.preventDefault();
        setCookie(".Menus.RedirectUrl", btoa(encodeURIComponent(location.href)), 1);
        externalLoginProvider.value = "Facebook";
        externalLoginForm.submit();
    };
    fbSignup.onclick = function (e) {
        if (fbSignup.attributes.getNamedItem("href").value !== "#") {
            e.preventDefault();
            document.querySelector(".modal.sign-in").classList.add("hidden");
            document.querySelector(".modal.webview-error").classList.remove("hidden");
            return;
        }
        e.preventDefault();
        setCookie(".Menus.RedirectUrl", btoa(encodeURIComponent(location.href)), 1);
        externalLoginProvider.value = "Facebook";
        externalLoginForm.submit();
    };
    gSignin.onclick = function (e) {
        if (gSignin.attributes.getNamedItem("href").value !== "#") {
            e.preventDefault();
            document.querySelector(".modal.sign-in").classList.add("hidden");
            document.querySelector(".modal.webview-error").classList.remove("hidden");
            return;
        }
        e.preventDefault();
        setCookie(".Menus.RedirectUrl", btoa(encodeURIComponent(location.href)), 1);
        externalLoginProvider.value = "Google";
        externalLoginForm.submit();
    };
    gSignup.onclick = function (e) {
        if (gSignup.attributes.getNamedItem("href").value !== "#") {
            e.preventDefault();
            document.querySelector(".modal.sign-in").classList.add("hidden");
            document.querySelector(".modal.webview-error").classList.remove("hidden");
            return;
        }
        e.preventDefault();
        setCookie(".Menus.RedirectUrl", btoa(encodeURIComponent(location.href)), 1);
        externalLoginProvider.value = "Google";
        externalLoginForm.submit();
    };
    document.addEventListener("click", function (e) {
        if (e.target.classList.contains("forgot-password")) {
            e.preventDefault();
            var loginModal = document.querySelector(".modal.sign-in");
            var restoreModal = document.querySelector(".modal.restore-pwd");
            loginModal.classList.add("hidden");
            restoreModal.classList.remove("hidden");
        }
    });
    var restoreForm = document.querySelector('.restore-form');
    restoreForm.onsubmit = function (e) {
        e.preventDefault();
        fetch('/Account/RestorePassword', {
            method: 'POST',
            credentials: "same-origin",
            body: new FormData(restoreForm)
        })
            .then(function (e) {
            if (e.status === 400) {
                document.querySelector(".disclaimer-error.restore-pwd-error").classList.remove("hidden");
                return;
            }
            document.querySelector(".modal.restore-pwd").classList.add("hidden");
            document.querySelector(".modal.email-pwd").classList.remove("hidden");
            e.json().then(function (x) {
                if (x.link)
                    location.href = x.link;
            });
        })
            .catch(function () {
            console.error("Can't reset password");
        });
    };
    var showTab = function (tabMode, event) {
        var userModeNode = event.srcElement.attributes.getNamedItem('data-usermode');
        var userModeNodeValue = userModeNode && userModeNode.value;
        signinModal.classList.remove('hidden');
        document.querySelectorAll(".signin-modal-container .modal").forEach(function (elem) {
            elem.classList.toggle("hidden", true);
        });
        var currentUserModeNode = document.createAttribute("data-current-usermode");
        currentUserModeNode.value = userModeNodeValue;
        currentUserMode = userModeNodeValue;
        usermodeInput.value = currentUserMode;
        switch (tabMode) {
            case tabModes.UserMode: {
                document.querySelector(".modal.user-mode").classList.toggle("hidden", false);
                break;
            }
            case tabModes.SignIn: {
                var signinSignUpPopup = document.querySelector(".modal.sign-in");
                signinSignUpPopup.classList.toggle("hidden", false);
                signinSignUpPopup.attributes.setNamedItem(currentUserModeNode);
                signuptab.classList.toggle('hidden', true);
                signintab.classList.toggle('hidden', false);
                signuptabButton.classList.toggle('active', false);
                signintabButton.classList.toggle('active', true);
                document.querySelector('#signin-tab .signin-form input[name=username]').focus();
                break;
            }
            case tabModes.SignUp: {
                var signinSignUpPopup = document.querySelector(".modal.sign-in");
                signinSignUpPopup.classList.toggle("hidden", false);
                signinSignUpPopup.attributes.setNamedItem(currentUserModeNode);
                signintab.classList.toggle('hidden', true);
                signuptab.classList.toggle('hidden', false);
                signintabButton.classList.toggle('active', false);
                signuptabButton.classList.toggle('active', true);
                document.querySelector('#signup-tab .signin-form input[name=username]').focus();
                break;
            }
        }
    };
})();
//# sourceMappingURL=signin-popups.js.map