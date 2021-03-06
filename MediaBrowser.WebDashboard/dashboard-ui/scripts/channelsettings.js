﻿(function ($, document, window) {

    function loadPage(page, config) {

        $('#selectChannelResolution', page).val(config.PreferredStreamingWidth || '').selectmenu("refresh");

        Dashboard.hideLoadingMsg();
    }

    function onSubmit() {

        Dashboard.showLoadingMsg();

        var form = this;

        ApiClient.getNamedConfiguration("channels").done(function (config) {

            // This should be null if empty
            config.PreferredStreamingWidth = $('#selectChannelResolution', form).val() || null;

            ApiClient.updateNamedConfiguration("channels", config).done(Dashboard.processServerConfigurationUpdateResult);
        });

        // Disable default form submission
        return false;
    }

    $(document).on('pageinit', "#channelSettingsPage", function () {

        var page = this;

        $('.channelSettingsForm', page).off('submit', onSubmit).on('submit', onSubmit);

    }).on('pageshowready', "#channelSettingsPage", function () {

        Dashboard.showLoadingMsg();

        var page = this;

        ApiClient.getNamedConfiguration("channels").done(function (config) {

            loadPage(page, config);

        });

    });

})(jQuery, document, window);
