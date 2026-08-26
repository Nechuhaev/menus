(() => {
    const userModes = {
        user: "user",
        restaurant: "restaurant"
    };

    const tabModes = {
        UserMode: 0,
        SignIn: 1,
        SignUp: 2,
    };

    let currentUserMode = userModes.user;

    function getParamFromUrl(name: string) {
        let link = location.href;
        let result: any;
        let urlRegex = new RegExp(name + '=([^&]+)', 'm');
        let match = urlRegex.exec(link);
        if (match !== null) {
            result = match[1];
        } else {
            result = '';
        }

        return result;
    }

    function setCookie(name: string, value: string, days: number) {
        var expires = "";
        if (days) {
            var date = new Date();
            date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
            expires = "; expires=" + date.toUTCString();
        }
        document.cookie = name + "=" + (value || "") + expires + "; path=/";
    }

    let signinModal = document.querySelector('.signin-modal-container') as HTMLElement;
    let signinBg = document.querySelector('.signin-modal-container .modal-background') as HTMLElement;
    let signinCloseModal = document.querySelector('.signin-modal-container .close-modal') as HTMLElement;
    let signintabButton = document.querySelector('.tab-choose-sign-in') as HTMLElement;
    let signuptabButton = document.querySelector('.tab-choose-sign-up') as HTMLElement;
    let signintab = document.querySelector('#signin-tab') as HTMLElement;
    let signuptab = document.querySelector('#signup-tab') as HTMLElement;
    let signinCheck = document.querySelector('#signin-tab .checkbox') as HTMLElement;
    let signupCheck = document.querySelector('#signup-tab .checkbox') as HTMLElement;
    let signinCheckInput = document.querySelector('#remember-sign-in') as HTMLInputElement;
    let signupCheckInput = document.querySelector('#remember-sign-up') as HTMLInputElement;
    let popupOpenButtons = document.querySelectorAll('[data-sign-in]');
    let popupSignUpOpenButtons = document.querySelectorAll('[data-sign-up]');

    let usermodetab = document.querySelector('#user-mode-tab') as HTMLInputElement;;
    let popupUserModeButtons = document.querySelectorAll('[data-user-mode]');

    let usermodeInput = document.querySelector('[name="usermode"]') as HTMLInputElement;

    signintabButton.onclick = (e) => {
        e.preventDefault();
        signuptab.classList.add('hidden');
        signintab.classList.remove('hidden');
        signuptabButton.classList.remove('active');
        signintabButton.classList.add('active');
    };

    signuptabButton.onclick = (e) => {
        e.preventDefault();
        signintab.classList.add('hidden');
        signuptab.classList.remove('hidden');
        signintabButton.classList.remove('active');
        signuptabButton.classList.add('active');
    };

    signinCheck.onclick = () => {
        signinCheck.classList.toggle('checked');
        signinCheckInput.checked = !signinCheckInput.checked;
    };

    signupCheck.onclick = () => {
        signupCheck.classList.toggle('checked');
        signupCheckInput.checked = !signupCheckInput.checked;
    };

    document.addEventListener("click", (e) => {
        if ((e.target as HTMLElement).classList.contains("close-modal")) {
            e.preventDefault();

            signinModal.classList.add('hidden');
            let loginModal = document.querySelector(".modal.sign-in");
            let restoreModal = document.querySelector(".modal.restore-pwd");

            loginModal.classList.remove("hidden");
            restoreModal.classList.add("hidden");
        }
    });

    //signinCloseModal.onclick = () => {
    //    signinModal.classList.add('hidden');
    //    let loginModal = document.querySelector(".modal.sign-in");
    //    let restoreModal = document.querySelector(".modal.restore-pwd");

    //    loginModal.classList.remove("hidden");
    //    restoreModal.classList.add("hidden");
    //};

    signinBg.onclick = () => {
        signinModal.classList.add('hidden');
    };

    if (location.href.indexOf("ReturnUrl") !== -1) {
        signinModal.classList.remove('hidden');
        document.querySelectorAll(".signin-modal-container .modal").forEach((elem) => {
            elem.classList.add("hidden");
        });
        document.querySelector(".modal.sign-in").classList.remove("hidden");
        signuptab.classList.add('hidden');
        signintab.classList.remove('hidden');
        signuptabButton.classList.remove('active');
        signintabButton.classList.add('active');
        (document.querySelector('#signin-tab .signin-form input[name=username]') as HTMLInputElement).focus();
    }

    for (let i = 0; i < popupUserModeButtons.length; i++) {
        let elem = popupUserModeButtons[i] as HTMLElement;
        elem.addEventListener('click', (event) => {
            event.preventDefault();
            showTab(tabModes.UserMode, event);
        });
    }

    for (let i = 0; i < popupOpenButtons.length; i++) {
        let elem = popupOpenButtons[i] as HTMLElement;
        elem.addEventListener('click', (event) => {
            event.preventDefault();
            showTab(tabModes.SignIn, event);
        });
    }

    for (let i = 0; i < popupSignUpOpenButtons.length; i++) {
        let elem = popupSignUpOpenButtons[i] as HTMLElement;
        elem.addEventListener('click', (event) => {
            event.preventDefault();
            showTab(tabModes.SignUp, event);
            
        });
    }



    let signinForm = document.querySelector('#signin-tab .signin-form') as HTMLFormElement;
    signinForm.onsubmit = (e) => {
        e.preventDefault();

        fetch('/Account/Login', {
                method: 'POST',
                credentials: "same-origin",
                body: new FormData(signinForm)
            })
            .then((e) => {
                if (!e.ok) {
                    document.querySelector(".disclaimer-error.wrong-pwd-error").classList.remove("hidden");
                    return;
                }

                if (e.redirected) {
                    location.href = decodeURIComponent('ru/restaurants/manage/');
                } else {
                    let returnUrl = getParamFromUrl("ReturnUrl");
                    if (returnUrl) {
                        location.href = decodeURIComponent(returnUrl);
                    }
                    else {
                        location.reload();
                    }
                }
                
            })
            .catch(() => {
                console.error("Can't register/log in");
            });
    };

    let signupForm = document.querySelector('#signup-tab .signin-form') as HTMLFormElement;
    signupForm.onsubmit = (e) => {
        e.preventDefault();

        fetch('/Account/Register', {
                method: 'POST',
                credentials: "same-origin",
                body: new FormData(signupForm)
            })
            .then((e) => {
                if (e.status === 400) {
                    document.querySelector(".disclaimer-error.user-exist-error").classList.remove("hidden");
                    return;
                }

                e.json().then((x) => {
                    let isEmail = x.isEmail;

                    let returnUrl = getParamFromUrl("ReturnUrl");
                    if (isEmail) {
                        document.querySelector(".modal.sign-in").classList.add("hidden");
                        document.querySelector(".modal.email-pwd").classList.remove("hidden");
                        if (x.link) location.href = x.link;
                    }
                    else if (returnUrl) location.href = decodeURIComponent(returnUrl);
                    else location.reload();
                });
            })
            .catch(() => {
                console.error("Can't register/log in");
            });
    };



    let fbSignin = document.querySelector('#signin-tab .sign-in-with-fb') as HTMLInputElement;
    let gSignin = document.querySelector('#signin-tab .sign-in-with-google') as HTMLInputElement;
    let fbSignup = document.querySelector('#signup-tab .sign-in-with-fb') as HTMLInputElement;
    let gSignup = document.querySelector('#signup-tab .sign-in-with-google') as HTMLInputElement;

    let externalLoginForm = document.querySelector('#external-login') as HTMLFormElement;
    let externalLoginProvider = document.querySelector('#external-login-provider') as HTMLInputElement;

    fbSignin.onclick = (e) => {
        if (fbSignin.attributes.getNamedItem("href").value !== "#") {
            e.preventDefault();
            //(document.querySelector("#external-login-webview") as HTMLFormElement).submit();
            document.querySelector(".modal.sign-in").classList.add("hidden");
            document.querySelector(".modal.webview-error").classList.remove("hidden");
            return;
        }

        e.preventDefault();

        setCookie(".Menus.RedirectUrl", btoa(encodeURIComponent(location.href)), 1);
        externalLoginProvider.value = "Facebook";
        externalLoginForm.submit();
    };

    fbSignup.onclick = (e) => {
        if (fbSignup.attributes.getNamedItem("href").value !== "#") {
            e.preventDefault();
            //(document.querySelector("#external-login-webview") as HTMLFormElement).submit();
            document.querySelector(".modal.sign-in").classList.add("hidden");
            document.querySelector(".modal.webview-error").classList.remove("hidden");
            return;
        }

        e.preventDefault();

        setCookie(".Menus.RedirectUrl", btoa(encodeURIComponent(location.href)), 1);
        externalLoginProvider.value = "Facebook";
        externalLoginForm.submit();
    };

    gSignin.onclick = (e) => {
        if (gSignin.attributes.getNamedItem("href").value !== "#") {
            e.preventDefault();
            //(document.querySelector("#external-login-webview") as HTMLFormElement).submit();
            document.querySelector(".modal.sign-in").classList.add("hidden");
            document.querySelector(".modal.webview-error").classList.remove("hidden");
            return;
        }

        e.preventDefault();

        setCookie(".Menus.RedirectUrl", btoa(encodeURIComponent(location.href)), 1);
        externalLoginProvider.value = "Google";
        externalLoginForm.submit();
    };

    gSignup.onclick = (e) => {
        if (gSignup.attributes.getNamedItem("href").value !== "#") {
            e.preventDefault();
            //(document.querySelector("#external-login-webview") as HTMLFormElement).submit();
            document.querySelector(".modal.sign-in").classList.add("hidden");
            document.querySelector(".modal.webview-error").classList.remove("hidden");
            return;
        }

        e.preventDefault();

        setCookie(".Menus.RedirectUrl", btoa(encodeURIComponent(location.href)), 1);
        externalLoginProvider.value = "Google";
        externalLoginForm.submit();
    };

    document.addEventListener("click", (e) => {
        if ((e.target as HTMLElement).classList.contains("forgot-password")) {
            e.preventDefault();

            let loginModal = document.querySelector(".modal.sign-in");
            let restoreModal = document.querySelector(".modal.restore-pwd");

            loginModal.classList.add("hidden");
            restoreModal.classList.remove("hidden");
        }
    });


    let restoreForm = document.querySelector('.restore-form') as HTMLFormElement;
    restoreForm.onsubmit = (e) => {
        e.preventDefault();

        fetch('/Account/RestorePassword', {
                method: 'POST',
                credentials: "same-origin",
                body: new FormData(restoreForm)
            })
            .then((e) => {
                if (e.status === 400) {
                    document.querySelector(".disclaimer-error.restore-pwd-error").classList.remove("hidden");
                    return;
                }
                //document.querySelector(".restore-pwd-sent").classList.remove("hidden");
                document.querySelector(".modal.restore-pwd").classList.add("hidden");
                document.querySelector(".modal.email-pwd").classList.remove("hidden");
                e.json().then((x) => {
                    if (x.link) location.href = x.link;
                });
            })
            .catch(() => {
                //document.querySelector(".disclaimer-error.restore-pwd-error").classList.remove("hidden");
                console.error("Can't reset password");
            });
    };

    const showTab = (tabMode: number, event: MouseEvent) => {
        const userModeNode = event.srcElement.attributes.getNamedItem('data-usermode');
        const userModeNodeValue = userModeNode && userModeNode.value;

        signinModal.classList.remove('hidden');
        document.querySelectorAll(".signin-modal-container .modal").forEach((elem) => {
            elem.classList.toggle("hidden", true);
        });

        const currentUserModeNode = document.createAttribute("data-current-usermode");
        currentUserModeNode.value = userModeNodeValue;
        currentUserMode = userModeNodeValue;
        usermodeInput.value = currentUserMode;

        switch (tabMode) {
            case tabModes.UserMode: {
                document.querySelector(".modal.user-mode").classList.toggle("hidden", false);
                break;
            }
            case tabModes.SignIn: {
                const signinSignUpPopup = document.querySelector(".modal.sign-in") as HTMLElement;
                signinSignUpPopup.classList.toggle("hidden", false);
                signinSignUpPopup.attributes.setNamedItem(currentUserModeNode);

                signuptab.classList.toggle('hidden', true);
                signintab.classList.toggle('hidden', false);
                
                signuptabButton.classList.toggle('active', false);
                signintabButton.classList.toggle('active', true);
                (document.querySelector('#signin-tab .signin-form input[name=username]') as HTMLInputElement).focus();
                break;
            }
            case tabModes.SignUp: {
                const signinSignUpPopup = document.querySelector(".modal.sign-in") as HTMLElement;
                signinSignUpPopup.classList.toggle("hidden", false);
                signinSignUpPopup.attributes.setNamedItem(currentUserModeNode);

                signintab.classList.toggle('hidden', true);
                signuptab.classList.toggle('hidden', false);

                signintabButton.classList.toggle('active', false);
                signuptabButton.classList.toggle('active', true);
                (document.querySelector('#signup-tab .signin-form input[name=username]') as HTMLInputElement).focus();
                break;
            }
        } 
    };

})();