var cityInputId = "search_cities_input";
var cityContainerId = "choose-city-container";
var cityButtonId = "chosen-city-button";
var dishesBuffer = "";
var restaurantsBuffer = "";

// A (possibly faster) way to get the current timestamp as an integer.
window.now = Date.now || function () {
    return new Date().getTime();
};

window.isTypeahedRequestRunning = false;

// Returns a function, that, when invoked, will only be triggered at most once
// during a given window of time. Normally, the throttled function will run
// as much as it can, without ever going more than once per `wait` duration;
// but if you'd like to disable the execution on the leading edge, pass
// `{leading: false}`. To disable execution on the trailing edge, ditto.
window.throttle = function (func, wait, options) {
    var timeout, context, args, result;
    var previous = 0;
    if (!options) options = { };

    var throttled = function () {
        var now = window.now();
        if (!previous && options.leading === false) previous = now;
        var remaining = window.isTypeahedRequestRunning ? wait - 1 : wait - (now - previous);
        context = this;
        args = arguments;
        if (remaining <= 0 || remaining > wait) {
            if (timeout) {
                clearTimeout(timeout);
                timeout = setTimeout(function() {
                    window.isTypeahedRequestRunning = false;
                }, 500);
            }
            previous = now;
            window.isTypeahedRequestRunning = true;
            //clearTimeout(secondTimeout);
            //secondTimeout = setTimeout(function () {}, );
            result = func.apply(context, args);
            if (!timeout) context = args = null;
        } else if (!timeout && options.trailing !== false) {
            timeout = setTimeout(throttled, remaining);
        }
        return result;
    };

    throttled.cancel = function () {
        clearTimeout(timeout);
        previous = 0;
        timeout = context = args = null;
    };

    return throttled;
};

function autocomplete(inp, arrInp, showAll) {
    var currentFocus;
    var arr = arrInp;

    var inputListener = window.throttle(function (e) {
        var a, b, i, val = this.value;
        closeAllLists();

        if (!inp.value) {
            window.isTypeahedRequestRunning = false;
            toggleLoadingIcon(false);
        }

        if (inp.id === "search_dishes_input" && dishesBuffer !== inp.value && inp.value.length > 0) {
            dishesBuffer = inp.value;
            toggleLoadingIcon(true);

            let body = `q=${dishesBuffer}&sortOrder=${window.dishSearchOrder}`

            if (!dishesBuffer) {
                window.isTypeahedRequestRunning = false;
                return false;
            }

            fetch('/Home/DishAutocomplete', {
                method: 'POST',
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded"
                },
                body
            })
                .then(function (e) {
                    e.json().then(function (data) {
                        arr = data;

                        var event = new Event('input', {
                            'bubbles': true,
                            'cancelable': true
                        });

                        window.isTypeahedRequestRunning = false;
                        inp.dispatchEvent(event);
                    });
                })
                .catch(function () {

                });

            return false;
        }

        if (inp.id === "search_restaurants_input" && restaurantsBuffer !== inp.value && inp.value.length > 0) {
            restaurantsBuffer = inp.value;

            fetch('/Home/RestaurantsAutocomplete', {
                method: 'POST',
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded"
                },
                body: "q=" + restaurantsBuffer
            })
                .then(function (e) {
                    e.json().then(function (data) {
                        arr = data;

                        var event = new Event('input', {
                            'bubbles': true,
                            'cancelable': true
                        });

                        window.isTypeahedRequestRunning = false;
                        inp.dispatchEvent(event);
                    });
                })
                .catch(function () {

                });

            return false;
        }

        if (!showAll) {
            if (!val) { return false; }
        }
        currentFocus = -1;
        a = document.createElement("DIV");
        a.setAttribute("id", this.id + "autocomplete-list");
        a.setAttribute("class", "autocomplete-items");
        this.parentNode.appendChild(a);
        for (i = 0; i < arr.length; i++) {
            var startPosition = arr[i].name.toUpperCase().indexOf(val.toUpperCase());
            var secondaryStartPosition = arr[i].terms.toUpperCase().indexOf(val.toUpperCase());
            if (startPosition !== -1 || secondaryStartPosition !== -1 || (showAll && !val)) {
                var itemContainer = document.createElement("DIV");
                b = document.createElement("DIV");
                if (secondaryStartPosition !== -1 && startPosition === -1) {
                    b.innerHTML = arr[i].name;
                    b.innerHTML += "<input type='hidden' data-id='" + arr[i].id + "' value='" + arr[i].name + "'>";
                }
                else if (startPosition === 0) {
                    b.innerHTML = "<strong>" + arr[i].name.substr(0, val.length) + "</strong>";
                    b.innerHTML += arr[i].name.substr(val.length);
                    b.innerHTML += "<input type='hidden' data-id='" + arr[i].id + "' value='" + arr[i].name + "'>";
                }
                else {
                    b.innerHTML = arr[i].name.substr(0, startPosition);
                    b.innerHTML += "<strong>" + arr[i].name.substring(startPosition, startPosition + val.length) + "</strong>";
                    if ((startPosition + val.length) !== (arr[i].name.length)) {
                        b.innerHTML += arr[i].name.substr(startPosition + val.length);
                    }
                    b.innerHTML += "<input type='hidden' data-id='" + arr[i].id + "' value='" + arr[i].name + "'>";
                }

                itemContainer.appendChild(b);

                var placeContainer = document.createElement("DIV");
                placeContainer.classList.add("search-place-container");
                placeContainer.innerHTML = "<i class=\"place-marker\"></i> " + arr[i].location;

                var ratingContainer = document.createElement("DIV");
                ratingContainer.classList.add("search-rating-container");
                const rating = parseInt(arr[i].rating)
                if (rating > 2) {
                    ratingContainer.innerHTML = "<span class=\"dish_rate_star rate_" + arr[i].ratingPercent + "\"></span>" + rating;
                }                
                if (arr[i].distance) ratingContainer.innerHTML += " <span class='distance'>" + arr[i].distance + "</span>";

                if (arr[i].location) itemContainer.appendChild(placeContainer);
                if (arr[i].rating) itemContainer.appendChild(ratingContainer);

                itemContainer.addEventListener("click", function (e) {
                    var secondInp = document.querySelector("[name=" + inp.name + "_id]");
                    inp.value = this.getElementsByTagName("input")[0].value;
                    secondInp.value = this.getElementsByTagName("input")[0].getAttribute("data-id");

                    if (secondInp.name == "q_id") {
                        if (!window.isMobile) document.querySelector(".search-form").submit();
                        else document.querySelector(".search_restaurant").submit();
                    }

                    if (secondInp.name == "dish_q_id") {
                        if (!window.isMobile) document.querySelector("[data-search-dish]").click();
                        else document.querySelector(".search_restaurant").submit();
                    }

                    if (secondInp.name == "search_cities_input_id") {
                        document.getElementById("chosen-city-button").innerText = inp.value;
                        var cityPlaceholder = document.querySelector(".selected-city em");
                        if (cityPlaceholder) cityPlaceholder.innerText = inp.value;
                        document.getElementById("choose-city-container").classList.add("hidden");
                        inp.value = "";


                        fetch('/Home/SelectCity', {
                            method: 'POST',
                            credentials: "same-origin",
                            headers: {
                                "Content-Type": "application/x-www-form-urlencoded"
                            },
                            body: "cityId=" + secondInp.value
                        })
                            .then(function (e) {
                                location.reload();
                            })
                            .catch(function () {

                            });

                    }

                    closeAllLists();
                });
                a.appendChild(itemContainer);
            }
        }
        toggleLoadingIcon(false)
        window.isTypeahedRequestRunning = false;
    }, 50);

    inp.addEventListener("input", inputListener);
    inp.addEventListener("keydown", function (e) {
        var x = document.getElementById(this.id + "autocomplete-list");
        if (x) x = x.getElementsByTagName("div");
        if (e.keyCode == 40) {
            currentFocus++;
            addActive(x);
        } else if (e.keyCode == 38) {
            currentFocus--;
            addActive(x);
        } else if (e.keyCode == 13) {
            e.preventDefault();
            if (currentFocus > -1) {
                if (x) x[currentFocus].click();
            }
        }
    });

    function toggleLoadingIcon(show) {
        if (show) {
            document.querySelector(".loading-icon").classList.remove("hidden");
            let viewAllBtn = document.querySelector(".view-all-icon")
            if (viewAllBtn) {
                document.querySelector(".view-all-icon").classList.add("hidden");
            }
            
        } else {
            document.querySelector(".loading-icon").classList.add("hidden");
            let viewAllBtn = document.querySelector(".view-all-icon")
            if (viewAllBtn) {
                document.querySelector(".view-all-icon").classList.remove("hidden");
            }            
        }        
    }

    function addActive(x) {
        if (!x) return false;
        removeActive(x);
        if (currentFocus >= x.length) currentFocus = 0;
        if (currentFocus < 0) currentFocus = (x.length - 1);
        x[currentFocus].classList.add("autocomplete-active");
    }
    function removeActive(x) {
        for (var i = 0; i < x.length; i++) {
            x[i].classList.remove("autocomplete-active");
        }
    }
    function closeAllLists(elmnt) {
        var x = document.getElementsByClassName("autocomplete-items");
        for (var i = 0; i < x.length; i++) {
            if (elmnt != x[i] && elmnt != inp) {
                x[i].parentNode.removeChild(x[i]);
            }
        }
    }
    document.addEventListener("click", function (e) {
        if (e.target.id !== cityButtonId && e.target.classList.contains("selected-city")) {
            closeAllLists(e.target);
            document.getElementById(cityContainerId).classList.add("hidden");
        }
    });

    return {
        changeArr: function (newArr) {
            arr = newArr;
        }
    };
}
//var restaurants = ["3B Cafe", "Puzata Hata", "Mafia", "Bar 45", "Hutorok", "Kabachok na bochok"];
//var cities = [{ name: "Киев", id: "1" }, { name: "Одесса", id: "1" }, { name: "Львов", id: "1" }, { name: "Харьков", id: "1"}];

var searchAC = autocomplete(document.getElementById("search_restaurants_input"), []);
var dishAC = autocomplete(document.getElementById("search_dishes_input"), []);
var cityAC = autocomplete(document.getElementById(cityInputId), cities, true);

var cities_button = document.getElementById(cityButtonId);
cities_button.addEventListener("click", function () {
    var input = document.getElementById(cityInputId);
    document.getElementById(cityContainerId).classList.toggle("hidden");
    input.focus();
    var event = new Event('input', {
        'bubbles': true,
        'cancelable': true
    });

    input.dispatchEvent(event);
});

if (document.querySelector(".selected-city") !== null) {
    document.querySelector(".selected-city").addEventListener("click", function () {
        cities_button.dispatchEvent(new Event('click', {
            'bubbles': true,
            'cancelable': true
        }));

        document.querySelector(".mobile_search_block").style.display = "none";
    });
}
