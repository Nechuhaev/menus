const SENCE_WIDE_APATIVE = `
    <ins 
        class="adsbygoogle google-sence__google-sence-template-wide"
        style="display:block"
        data-ad-client="CLIENT_ID"
        data-ad-slot="SLOT_ID"
        data-adtest="TEST_MODE"
    >
    </ins>
`;

const SENCE_WIDE_HIGH_APATIVE = `
    <ins 
        class="adsbygoogle"
        style="display:block"
        data-ad-client="CLIENT_ID"
        data-ad-slot="SLOT_ID"
        data-adtest="TEST_MODE"
        data-ad-format="auto"
        data-full-width-responsive="true"
    >
    </ins>
`;

const SENCE_SQUARE_APATIVE = `
    <ins 
        class="adsbygoogle google-sence__google-sence-template-square"
        style="display:block"
        data-ad-client="CLIENT_ID"
        data-ad-slot="SLOT_ID"
        data-adtest="TEST_MODE"
    >
    </ins>
`;

const SENCE_VERTICAL_ADAPTIVE = `
    <ins 
        class="adsbygoogle"
        style="display:block"
        data-ad-client="CLIENT_ID"
        data-ad-slot="SLOT_ID"
        data-adtest="TEST_MODE"
        data-ad-format="auto"
        data-full-width-responsive="true"
    >
    </ins>
`;

const SENCE_WIDE_IDS = [
    {
        ClientId: 'ca-pub-1409649052535600',
        SlotId: '5961767298'
    },
    {
        ClientId: 'ca-pub-1409649052535600',
        SlotId: '3467202564'
    }
];

const SENCE_WIDE_HIGH_IDS = [
    {
        ClientId: 'ca-pub-1409649052535600',
        SlotId: '5961767298'
    },
    {
        ClientId: 'ca-pub-1409649052535600',
        SlotId: '3467202564'
    }
];

const SENCE_SQUARE_IDS = [
    {
        ClientId: 'ca-pub-1409649052535600',
        SlotId: '6057136061'
    },
    {
        ClientId: 'ca-pub-1409649052535600',
        SlotId: '7910261480'
    }
];

const SENCE_VERTICAL_IDS = [
    {
        ClientId: 'ca-pub-1409649052535600',
        SlotId: '7657992340'
    },
    {
        ClientId: 'ca-pub-1409649052535600',
        SlotId: '2405665663'
    }
];

function initSence(testMode) {
    initSenceContainer(testMode, '.google-sence__google-sence-wide-high', SENCE_WIDE_HIGH_APATIVE, SENCE_WIDE_HIGH_IDS);
    initSenceContainer(testMode, '.google-sence__google-sence-wide', SENCE_WIDE_APATIVE, SENCE_WIDE_IDS);
    initSenceContainer(testMode, '.google-sence__google-sence-square', SENCE_SQUARE_APATIVE, SENCE_SQUARE_IDS);
    initSenceContainer(testMode, '.google-sence__google-sence-vertical', SENCE_VERTICAL_ADAPTIVE, SENCE_VERTICAL_IDS);
}

function initSenceContainer(testMode, containerSelector, senceTemplate, senceDataIDs) {
    const $containers = $(containerSelector);
    const containerCounter = $containers.length;

    for (let senceCounter = 0; senceCounter < senceDataIDs.length; senceCounter++) {
        const hasContainerAndData = containerCounter > senceCounter && containerCounter !== 0;
        if (hasContainerAndData) {
            const senceData = senceDataIDs[senceCounter];
            const $container = $($containers[senceCounter]);
            let containerData = senceTemplate;
            containerData = containerData.replace('CLIENT_ID', senceData.ClientId);
            containerData = containerData.replace('SLOT_ID', senceData.SlotId);
            containerData = containerData.replace('TEST_MODE', testMode);
            $container.append(containerData);
            (adsbygoogle = window.adsbygoogle || []).push({});
        }
    }
}

function initSenceSticky() {
    const stickySelector = '.google-sence-sticky';
    const stickySelectorFixedClass = 'google-sence-sticky--fixed';
    const numberOfStickyElements = $(stickySelector).length;
    let top = 0;

    const adjustContainer = function () {
        // Get the position of the vertical scroll.
        const y = $(this).scrollTop();

        if (y >= top) {
            $(stickySelector).toggleClass(stickySelectorFixedClass, true);
        } else {
            $(stickySelector).removeClass(stickySelectorFixedClass, false);
        }
    };

    if (numberOfStickyElements > 0) {
        top = $(stickySelector).offset().top - 1;
        adjustContainer();

        $(window).scroll(function (e) {
            adjustContainer();
        });
    }
}
