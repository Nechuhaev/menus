const SEARCH_RESTARAUNT_VALUE = 'restaurant';
const SEARCH_DISH_VALUE = 'dish';
const EVALUATION_PERIOD = 1000 * 60 * 10;
const AB_TEST_EVALUATION_PERIOD = 1000 * 60 * 60 * 24 * 7;

const SORT_ORDER_DISTANCE = 'distance';
const SORT_ORDER_RATING = 'rating';

function setCookie(name, value, days) {
    var expires = "";
    if (days) {
        var date = new Date();
        date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
        expires = "; expires=" + date.toUTCString();
    }
    document.cookie = name + "=" + (value || "") + expires + "; path=/";
}

function initClosePopup() {
    if (isMobile) {
        $(document).on('click', (e) => {
            console.log('click ');

            if (typeof (closeAllOpenedElements) !== 'undefined') {
                closeAllOpenedElements(e);
            }
        });

        $('header').click((e) => {
            console.log('click header');

            if (typeof (closeAllOpenedElements) !== 'undefined') {
                closeAllOpenedElements(e);
            }
        });

        $('[itemscope]').click((e) => {
            console.log('click itemscope');

            if (typeof (closeAllOpenedElements) !== 'undefined') {
                closeAllOpenedElements(e);
            }
        });

        $('.popUpBlock').click((e) => {
            console.log('click popUpBlock');

            if (typeof (closeAllOpenedElements) !== 'undefined') {
                closeAllOpenedElements(e);
            }
        });
    } else {
        $(document).on('click', (e) => {
            if (typeof (closeAllOpenedElements) !== 'undefined') {
                closeAllOpenedElements(e);
            }
        });
    }

    $('.popUpCloser, .popUp_close').on('click',
        function (e) {
            e.preventDefault();
            $('.popUpBlock').removeClass('visible visible_gallery visible_map');
            $('.popUpBlock').removeClass('visible');
            $('.popUpWindow').fadeOut();
        }
    );

}

function closeAllOpenedElements(e) {
    let _isMobile = (typeof (isMobile) !== 'undefined') ? isMobile : false;
    let $leftBar = $('.logged_in, .user.header-user-controls-box');
    let $mainPageCityMiddle = $('.locationBlock');
    let $otherPagesCityMiddle = $('.site-header--right .search_restaurant.locationBlock');
    let $restaraintSchedule = $('.topSection .work-hours');
    let $popUps = $('.popUpBlock .popUpWindow');
    let $mobilePopUps = $('.search-form .popUpBlock_inner');
    let $servicePopUps = $('.popUpBlockServiceButtons .popUpBlock_inner');
    let $searchResults = $('.search-form .autocomplete-items, .desktop_search_restaurant .autocomplete-items');
    let $searchResultsButtons = $('.search-form button, .search-form input, .search_restaurant button, .search_restaurant input');

    let $popUpBackgroud = $('.popUpBlock');
    let $popUpCloser = $('.popUpCloser');

    let $languageDropDown = $('.dropdown_languages');

    if (_isMobile) {
        if ($leftBar.is(e.target)) {
            $('.logged_in .js-mobile-profile').toggleClass('active', false);
        }

        if (!$leftBar.is(e.target) && $leftBar.has(e.target).length === 0) {
            $('.logged_in .js-mobile-profile').toggleClass('active', false);
        }

        if ($mobilePopUps.is(e.target)) {
            $('.popUpBlock').toggleClass('visible', false);
            $('.popUpBlock .popUpWindow').hide();

        } else if ($servicePopUps.is(e.target)) {
            $('.popUpBlock').toggleClass('visible', false);
            $('.popUpBlock .popUpWindow').hide();
        } else {
            //console.log(e.target);
            //console.log($mobilePopUps);
        }

    } else {
        if (!$leftBar.is(e.target) && $leftBar.has(e.target).length === 0) {
            $('.logged_in .js-desctop-profile').toggleClass('active', false);
        }

        if (!$mainPageCityMiddle.is(e.target) && $mainPageCityMiddle.has(e.target).length === 0) {
            $('.locationBlock #choose-city-container').toggleClass('hidden', true);
        }

        if (!$otherPagesCityMiddle.is(e.target) && $otherPagesCityMiddle.has(e.target).length === 0) {
            $('.site-header--right .locationBlock #choose-city-container').toggleClass('hidden', true);
        }

        if (!$restaraintSchedule.is(e.target) && $restaraintSchedule.has(e.target).length === 0) {
            $('.topSection .work-hours .work-hours-dropdown').hide();
        }

        if (!$languageDropDown.is(e.target) && $languageDropDown.has(e.target).length === 0) {
            $('.dropdown_languages ul').hide();
        }

        if (($popUpCloser.is(e.target) || $popUpBackgroud.is(e.target)) && ($popUpBackgroud.css("z-index") > 0 || $popUpBackgroud.is(":visible"))) {
            $('.popUpBlock').toggleClass('visible', false);
            $('.popUpBlock .popUpWindow').hide();
        }

        if (!$searchResults.is(e.target) && $languageDropDown.has(e.target).length === 0 && !$searchResultsButtons.is(e.target)) {
            $searchResults.toggleClass('hidden', true);
        } else {
            $searchResults.toggleClass('hidden', false);
        }
    }
}

function initServiceButtons() {
    const popupSelector = '.popUpBlockServiceButtons';
    const popupContainerSelector = '.popUpServiceButtons';
    const serviceButtonSelector = '.menu-service-buttons__button';
    //Show popup and reset process
    $(serviceButtonSelector).click(function (event) {
        event.preventDefault();
        $(popupSelector).toggleClass('visible', true);
        $(popupContainerSelector).show();
    }); 
}

function initSearchRadioButton(findRestarauntsPath, findDishPath) {
    let lastSelectedValue = getSearchRadioButton();

    if (lastSelectedValue) {
        if (isMobile) {
            $("[data-select-search]").removeClass("current");
            $("[data-select-search=" + lastSelectedValue + "]").addClass("current");

            $(".header-bottom [data-search]").addClass("hidden");
            $("[data-search=" + lastSelectedValue + "]").removeClass("hidden").find("input[type=text]").focus();

            $(".mobile_search_block .inner").addClass("hidden");
            $(".mobile_search_block .inner_" + lastSelectedValue).removeClass("hidden")
                .find("input[type=text]").focus();

        } else {
            $("[data-select-search]").removeClass("current");
            var id = $(this).attr("data-select-search");
            $("[data-select-search=" + lastSelectedValue + "]").addClass("current");

            $("[data-search]").addClass("hidden");
            $("[data-search=" + lastSelectedValue + "]").removeClass("hidden").find("input[type=text]").focus();

            $(".desktop_search_fields").addClass("hidden");
            $(".desktop_search_" + lastSelectedValue).removeClass("hidden")
                .find("input[type=text]").focus();
        }

        if (lastSelectedValue === SEARCH_RESTARAUNT_VALUE) {
            $("form.search_restaurant").attr("action", findRestarauntsPath);
        }

        if (lastSelectedValue === SEARCH_DISH_VALUE) {
            $("form.search_restaurant").attr("action", findDishPath);
            initDefaultSortOrder();
        }
    }

}

function initDefaultSortOrder(){
    const savedSortOrder = getLastLocationAccess();
    if (savedSortOrder && savedSortOrder.lat) {
        setDishSearchOrder(SORT_ORDER_DISTANCE);
    } else {
        setDishSearchOrder(SORT_ORDER_RATING);
    }
}

function initSearchEvents(params) {
    if (isMobile) {
        initMobileSearchEvents(params);
    } else {
        initDesctopSearchEvents(params);
    }
}

function initMobileSearchEvents(params) {
    initSeachRadioButtonsEvents(params);
    initMobileSubmitEvents(params);
}

function initSeachRadioButtonsEvents(params) {
    const {
        searchSelectRadioButtonDataAttribute,
        searchSelectContainerAttribute,
        findRestaurantsPath,
        findDishesPath,
        formSelector
    } = params;

    const searchSelectRadioButtonSelector = `[${searchSelectRadioButtonDataAttribute}]`;

    $(searchSelectRadioButtonSelector).click(function (e) {
        e.preventDefault();

        const selectedSearch = $(this).attr(searchSelectRadioButtonDataAttribute);

        //remove current class from previosly selected radio button
        const existingSelector = `[${searchSelectRadioButtonDataAttribute}].current`;
        $(existingSelector).removeClass("current");

        //add class to current select radio button
        const currentSelector = `[${searchSelectRadioButtonDataAttribute}='${selectedSearch}']`;
        $(currentSelector).addClass('current');

        //add hidden class from previosly selected search container
        const existingContainerSelector = `[${searchSelectContainerAttribute}]:not('.hidden')`;
        $(existingContainerSelector).addClass("hidden");

        //remove hidden class from current selecteed search container
        const currentContainerSelector = `[${searchSelectContainerAttribute}='${selectedSearch}']`;
        $(currentContainerSelector).removeClass("hidden").find("input[type=text]").focus();

        if (selectedSearch === SEARCH_RESTARAUNT_VALUE) {
            $(formSelector).attr("action", findRestaurantsPath);
            setSearchRadioButton(SEARCH_RESTARAUNT_VALUE);
        } else {
            $(formSelector).attr("action", findDishesPath);
            setSearchRadioButton(SEARCH_DISH_VALUE);
            processDishSortOrder();
        }
    });
}

function processDishSortOrder() {
    const lastLocationAccess = getLastLocationAccess();
    if (!lastLocationAccess) {
        processGeolocation();
    } else {
        setDishSearchOrder(SORT_ORDER_RATING);
    }
}

function processGeolocation() {
    if (navigator.geolocation) {
        showGeolocationRequestPopup();
        navigator.geolocation.getCurrentPosition(function (position) {
            setCoordinates(position);
            setDishSearchOrder(SORT_ORDER_DISTANCE);
            hideGeolocationRequestPopup();
        }, function () {
            setLastLocationAccess(true)
            setDishSearchOrder(SORT_ORDER_RATING);
            showGeolocationErrorPopup();
        });
    } else {
        setLastLocationAccess(true)
        setDishSearchOrder(SORT_ORDER_RATING);
        hideGeolocationRequestPopup();        
    }
}

function setCoordinates(position) {
    const { latitude: lat } = position.coords;
    const { longitude: lon } = position.coords;

    setLastLocationAccess({ lat, lon })
    setCookie(".Menus.UserLatitude", lat, 99999);
    setCookie(".Menus.UserLongitude", lon, 99999);
}

function setDishSearchOrder(orderBy) {
    window.dishSearchOrder = orderBy;
}

function showGeolocationErrorPopup() {
    const popupSelectorRequestDiv = '.popUpBlockRequestLocation [data-request-geolocation-helper-result]';
    const popupSelectorErrorDiv = '.popUpBlockRequestLocation [data-request-geolocation-helper-error]';

    //Show active message
    $(popupSelectorRequestDiv).toggleClass('hidden', true);
    $(popupSelectorErrorDiv).toggleClass('hidden', false);
}

function showGeolocationRequestPopup() {
    const popupSelector = '.popUpBlockRequestLocation';
    const popupContainerSelector = '.popUpRequestGeolocation';
    
    const popupSelectorInternavlDivs = '.popUpBlockRequestLocation .popUpRequestGeolocation > div';
    const popupSelectorRequestDiv = '.popUpBlockRequestLocation [data-request-geolocation-helper-result]';

    //Show popup and reset process
    $(popupSelector).toggleClass('visible', true);
    $(popupContainerSelector).show();
    $(popupSelectorInternavlDivs).toggleClass('hidden', true);
    //Show active message
    $(popupSelectorRequestDiv).toggleClass('hidden', false);
}

function hideGeolocationRequestPopup() {
    const popupSelector = '.popUpBlockRequestLocation';
    const popupSelectorInternavlDivs = '.popUpBlockRequestLocation .popUpRequestGeolocation > div';
    const popupSelectorRequestDiv = '.popUpBlockRequestLocation [data-request-geolocation-helper-result]';

    //Show popup and reset process
    $(popupSelector).toggleClass('visible', false);
    $(popupSelectorInternavlDivs).toggleClass('hidden', true);
}

function initMobileSubmitEvents(params) {
    const {
        formSelector,
        viewAllDishesSelector,
        dishQueryDataAttribute,
        findDishesPath,
        formContentSelector
    } = params;

    $(formSelector).find("input[type=text]").on('keydown', function (e) {
        const selectedSearch = getSearchRadioButton() || SEARCH_RESTARAUNT_VALUE;
        if (e.keyCode === 13 && selectedSearch === SEARCH_RESTARAUNT_VALUE) {
            $(formSelector).submit();
        } else if (e.keyCode === 13 && selectedSearch === SEARCH_DISH_VALUE) {
            e.preventDefault();
            let query = $(`[name=${dishQueryDataAttribute}]`).val();
            query = encodeURIComponent(query);
            location.href = `${findDishesPath}?${dishQueryDataAttribute}=${query}&sort=${window.dishSearchOrder}`;
        }
    });

    $(viewAllDishesSelector).click(function (e) {
        e.preventDefault();
        let query = $(`[name=${dishQueryDataAttribute}]`).val();
        query = encodeURIComponent(query);
        location.href = `${findDishesPath}?${dishQueryDataAttribute}=${query}&sort=${window.dishSearchOrder}`;    
    });
}

function initDesctopSearchEvents(params) {
    initSeachRadioButtonsEvents(params);
    initDesctopSubmitEvents(params);
}

function initDesctopSubmitEvents(params) {
    const {
        formSelector,
        formContentSelector,
        findDishesPath,
        dishQueryDataAttribute,
        buttonSeachDishSelector
    } = params;

    $(formContentSelector).find("input[type=text]").on('keydown', function (e) {
        const selectedSearch = getSearchRadioButton() || SEARCH_RESTARAUNT_VALUE;
        if (e.keyCode === 13 && selectedSearch === SEARCH_RESTARAUNT_VALUE) {
            $(formSelector).submit();
        } else if (e.keyCode === 13 && selectedSearch === SEARCH_DISH_VALUE) {
            e.preventDefault();
            let query = $(`[name=${dishQueryDataAttribute}]`).val();
            query = encodeURIComponent(query);
            location.href = `${findDishesPath}?${dishQueryDataAttribute}=${query}&sort=${window.dishSearchOrder}`;
        }
    });


    $(buttonSeachDishSelector).click(function (e) {
        e.preventDefault();
        let query = $(`[name=${dishQueryDataAttribute}]`).val();
        query = encodeURIComponent(query);
        location.href = `${findDishesPath}?${dishQueryDataAttribute}=${query}&sort=${window.dishSearchOrder}`;
    });
}

function initLocalSearch(params) {

    initMobileLocalSearchEventsCommon(params);

    if (isMobile) {
        initMobileLocalSearchEvents(params);
    } else {
        initDesctopLocalSearchEvents(params);
    }
}

function initMobileLocalSearchEventsCommon(params) {
    const {
        localSearchButtonSelector,
        localSearchContainerSelector,
        categoriesContainerSelector,
        localSearchCloseButtonSelector,
        categoriesSelector,
        categoryItemsRowSelector,
        categoryItemsSelector,
        localSearchContainerInputSelector,
        categoriesSwiperSelector,
        topSectionSelector,
        noItemsText,
        topDishes
    } = params;

    const $noNotesNode = $(`<div class='local-search-no-items'>${noItemsText}</div>`);


    $(localSearchButtonSelector).click((e) => {
        const $categoriesSwiper = $(categoriesSwiperSelector);
        const $topSection = $(topSectionSelector);
        const $topDishes = $(topDishes);
        const $localSearchContainer = $(localSearchContainerSelector);

        if ($localSearchContainer.hasClass('hidden')) {
            $localSearchContainer.removeClass('hidden');
            $categoriesSwiper.hide();
            $topSection.hide();
            $topDishes.hide();
        }

        setTimeout(() => {
            $localSearchContainer[0].scrollIntoView(true);
        }, 50);
    });

    $(localSearchCloseButtonSelector).click((e) => {
        const $categoriesSwiper = $(categoriesSwiperSelector);
        const $topSection = $(topSectionSelector);
        const $topDishes = $(topDishes);
        const $localSearchContainer = $(localSearchContainerSelector);

        $localSearchContainer.addClass('hidden');
        $categoriesSwiper.show();
        $topSection.show();
        $topDishes.show();
        $(localSearchContainerInputSelector).val('');
        $noNotesNode;

        let categories = $(categoriesSelector);
        categories.each((index, category) => {
            category = $(category);
            if (index === 0) {
                category.show()
                $(category).toggleClass('open', true);
                $(category.find('.accordion_content')[0]).show();
            } else {
                category.show()
                $(category).toggleClass('open', false);
                $(category.find('.accordion_content')[0]).hide();
            }

            let categoryItems = category.find(categoryItemsRowSelector)
            categoryItems.each((index, item) => {
                item = $(item);
                item.show();
            });
        });
    });
}

function initMobileLocalSearchEvents(params) {
    const {
        localSearchButtonSelector,
        localSearchContainerSelector,
        categoriesContainerSelector,
        localSearchCloseButtonSelector,
        categoriesSelector,
        categoryItemsRowSelector,
        categoryItemsSelector,
        localSearchContainerInputSelector,
        categoriesSwiperSelector,
        topSectionSelector,
        noItemsText
    } = params;

    const $noNotesNode = $(`<div class='local-search-no-items'>${noItemsText}</div>`)

    $(localSearchContainerSelector).on('keyup', (e) => {
        e.preventDefault();
        let currentValue = $(e.target).val() || '';
        let categories = $(categoriesSelector);
        let anyCategoryVisible = false;
        let $categoriesContainer = $(categoriesContainerSelector);
        let alreadyHasNotItems = $categoriesContainer.find('.no-items').length > 0;

        categories.each((index, category) => {
            category = $(category);
            let categoryItems = category.find(categoryItemsSelector);
            let categoryItemsRow = category.find(categoryItemsRowSelector);

            let categoryHead = category.find('.accordion_title');
            let categoryText = $(categoryHead).text() || '';
            let categoryVisible = categoryText.toLowerCase().indexOf(currentValue.toLowerCase()) > -1;
            let categoryMatch = categoryText.toLowerCase().indexOf(currentValue.toLowerCase()) > -1;

            categoryItems.each((index, item) => {
                item = $(item);
                let categoryItem = $(categoryItemsRow[index]);
                const itemText = item.text() ? item.text().trim().toLowerCase() : '';
                const itemVisible = itemText.indexOf(currentValue.toLowerCase()) > -1;
                if (itemVisible && !categoryVisible) {
                    categoryVisible = true;
                    categoryItem.show();
                } else if (itemVisible) {
                    categoryItem.show();
                } else if (categoryMatch) {
                    categoryItem.show();
                } else {
                    categoryItem.hide();
                }
            });

            if (categoryVisible && !anyCategoryVisible) {
                anyCategoryVisible = true;
            }

            if (categoryVisible) {
                category.show();
                $(category).toggleClass('open', true);
                $(category.find('.accordion_content')[0]).show();
            } else {
                category.hide();
                $(category).toggleClass('open', false);
                $(category.find('.accordion_content')[0]).hide();
            }
        });

        if (anyCategoryVisible) {
            $noNotesNode.remove();
        } else if (!alreadyHasNotItems) {
            $categoriesContainer.append($noNotesNode);
        }
    });
}

function initDesctopLocalSearchEvents(params) {
    const {
        localSearchButtonSelector,
        localSearchContainerSelector,
        categoriesContainerSelector,
        localSearchCloseButtonSelector,
        categoriesSelector,
        categoryItemsRowSelector,
        categoryItemsSelector,
        localSearchContainerInputSelector,
        categoriesSwiperSelector,
        topSectionSelector,
        noItemsText
    } = params;

    const $noNotesNode = $(`<div class='local-search-no-items'>${noItemsText}</div>`);

    $(localSearchContainerSelector).on('keyup', (e) => {
        e.preventDefault();
        let currentValue = $(e.target).val() || '';
        let categories = $(categoriesSelector);
        let anyCategoryVisible = false;
        let $categoriesContainer = $(categoriesContainerSelector);
        let alreadyHasNotItems = $categoriesContainer.find('.no-items').length > 0;

        categories.each((index, category) => {
            category = $(category);
            let categoryItems = category.find(categoryItemsSelector);
            let categoryItemsRow = category.find(categoryItemsRowSelector);

            let categoryHead = category.find('.table_food thead');
            let categoryText = $(categoryHead).text() || '';
            let categoryVisible = categoryText.toLowerCase().indexOf(currentValue.toLowerCase()) > -1;
            let categoryMatch = categoryText.toLowerCase().indexOf(currentValue.toLowerCase()) > -1;

            categoryItems.each((index, item) => {
                item = $(item);
                let categoryItem = $(categoryItemsRow[index]);
                const itemText = item.text() || '';
                const itemVisible = itemText.toLowerCase().indexOf(currentValue.toLowerCase()) > -1;
                if (itemVisible && !categoryVisible) {
                    categoryVisible = true;
                    categoryItem.show();
                } else if (itemVisible) {
                    categoryItem.show();
                } else if (categoryMatch) {
                    categoryItem.show();
                } else {
                    categoryItem.hide();
                }
            });

            if (categoryVisible && !anyCategoryVisible) {
                anyCategoryVisible = true;
            }

            if (categoryVisible) {
                category.show();
                $(category.find('tbody')[0]).attr('style', 'display: table-row-group;');
            } else {
                category.hide();
                $(category.find('tbody')[0]).attr('style', 'display: none;');
            }
        });

        if (anyCategoryVisible) {
            $noNotesNode.remove()
        } else if (!alreadyHasNotItems) {
            $categoriesContainer.append($noNotesNode);
        }
    });    
}

//help functions

function setLastAbTestAccess(name) {
    let result = {
        name,
        createDate: Date.now()
    };

    result = JSON.stringify(result);
    localStorage.setItem(name, result);
}

function getLastAbTestAccess(name) {
    let result = localStorage.getItem(name);
    result = validateValueWithDate(result, AB_TEST_EVALUATION_PERIOD);

    if (result) {
        return result;
    }
}

function setLastLocationAccess(value) {
    let result = {
        value,
        createDate: Date.now()
    };

    result = JSON.stringify(result);
    localStorage.setItem("userLastLocationAccess", result);
}

function getLastLocationAccess() {
    let result = localStorage.getItem("userLastLocationAccess");
    result = validateValueWithDate(result);

    if (result) {
        const { value } = result;
        return value;
    }
}

function setSearchRadioButton(value) {
    let result = {
        value,
        createDate: Date.now()
    };

    result = JSON.stringify(result);
    localStorage.setItem("userSearchBarLastSelectedValue", result);
}

function getSearchRadioButton() {
    let result = localStorage.getItem("userSearchBarLastSelectedValue");
    result = validateValueWithDate(result);

    if (result) {
        const { value } = result;
        return value;
    }
}

function validateValueWithDate(value, evaluation) {
    if (value) {
        try {
            const result = JSON.parse(value);

            const { createDate } = result;
            const date = new Date(createDate);
            const currentDate = new Date();

            const noExpired = currentDate.getTime() - date.getTime() < (evaluation || EVALUATION_PERIOD);

            if (noExpired) {
                return result;
            }
        } catch (e) {
            return null;
        }
    }
}