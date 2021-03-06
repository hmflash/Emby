﻿(function () {

    function renderJob(page, job, dialogOptions) {

        require(['paperbuttonstyle']);

        var html = '';

        html += '<div>';
        html += Globalize.translate('ValueDateCreated', parseISO8601Date(job.DateCreated, { toLocal: true }).toLocaleString());
        html += '</div>';
        html += '<br/>';
        html += '<div class="formFields"></div>';

        html += '<br/>';
        html += '<br/>';
        html += '<button type="submit" data-role="none" class="clearButton">';
        html += '<paper-button raised class="submit block"><iron-icon icon="check"></iron-icon><span>' + Globalize.translate('ButtonSave') + '</span></paper-button>';
        html += '</button>';

        $('.syncJobForm', page).html(html).trigger('create');
        SyncManager.renderForm({
            elem: $('.formFields', page),
            dialogOptions: dialogOptions,
            dialogOptionsFn: getTargetDialogOptionsFn(dialogOptions),
            showName: true,
            readOnlySyncTarget: true
        });
        fillJobValues(page, job, dialogOptions);
    }

    function getTargetDialogOptionsFn(dialogOptions) {

        return function (targetId) {

            var deferred = $.Deferred();

            deferred.resolveWith(null, [dialogOptions]);
            return deferred.promise();
        };
    }

    function getJobItemHtml(jobItem, index) {

        var html = '';

        html += '<paper-icon-item data-itemid="' + jobItem.Id + '" data-status="' + jobItem.Status + '" data-remove="' + jobItem.IsMarkedForRemoval + '">';

        var hasActions = ['Queued', 'Cancelled', 'Failed', 'ReadyToTransfer', 'Transferring', 'Converting', 'Synced'].indexOf(jobItem.Status) != -1;

        var imgUrl;

        if (jobItem.PrimaryImageItemId) {

            imgUrl = ApiClient.getImageUrl(jobItem.PrimaryImageItemId, {
                type: "Primary",
                width: 80,
                tag: jobItem.PrimaryImageTag,
                minScale: 1.5
            });
        }

        if (imgUrl) {
            html += '<paper-fab class="listAvatar blue" style="background-image:url(\'' + imgUrl + '\');background-repeat:no-repeat;background-position:center center;background-size: cover;" item-icon></paper-fab>';
        }
        else {
            html += '<paper-fab class="listAvatar blue" icon="refresh" item-icon></paper-fab>';
        }

        html += '<paper-item-body three-line>';

        html += '<div>';
        html += jobItem.ItemName;
        html += '</div>';

        if (jobItem.Status == 'Failed') {
            html += '<div secondary style="color:red;">';
        } else {
            html += '<div secondary>';
        }
        html += Globalize.translate('SyncJobItemStatus' + jobItem.Status);
        if (jobItem.Status == 'Synced' && jobItem.IsMarkedForRemoval) {
            html += '<br/>';
            html += Globalize.translate('SyncJobItemStatusSyncedMarkForRemoval');
        }
        html += '</div>';

        html += '</paper-item-body>';

        if (hasActions) {

            html += '<paper-icon-button icon="' + AppInfo.moreIcon + '" class="btnJobItemMenu"></paper-icon-button>';
        } else {
            html += '<paper-icon-button icon="' + AppInfo.moreIcon + '" class="btnJobItemMenu" disabled></paper-icon-button>';
        }

        html += '</paper-icon-item>';
        return html;
    }

    function renderJobItems(page, items) {

        var html = '';

        html += '<h1>' + Globalize.translate('HeaderItems') + '</h1>';

        html += '<div class="paperList">';

        var index = 0;
        html += items.map(function (i) {

            return getJobItemHtml(i, index++);

        }).join('');

        html += '</div>';

        var elem = $('.jobItems', page).html(html).trigger('create').lazyChildren();

        $('.btnJobItemMenu', elem).on('click', function () {
            showJobItemMenu(this);
        });
    }

    function showJobItemMenu(elem) {

        var page = $(elem).parents('.page');
        var listItem = $(elem).parents('paper-icon-item');
        var jobItemId = listItem.attr('data-itemid');
        var status = listItem.attr('data-status');
        var remove = listItem.attr('data-remove').toLowerCase() == 'true';

        var menuItems = [];

        if (status == 'Failed') {
            menuItems.push({
                name: Globalize.translate('ButtonQueueForRetry'),
                id: 'retry',
                ironIcon: 'check'
            });
        }
        else if (status == 'Cancelled') {
            menuItems.push({
                name: Globalize.translate('ButtonReenable'),
                id: 'retry',
                ironIcon: 'check'
            });
        }
        else if (status == 'Queued' || status == 'Transferring' || status == 'Converting' || status == 'ReadyToTransfer') {
            menuItems.push({
                name: Globalize.translate('ButtonCancelItem'),
                id: 'cancel',
                ironIcon: 'delete'
            });
        }
        else if (status == 'Synced' && remove) {
            menuItems.push({
                name: Globalize.translate('ButtonUnmarkForRemoval'),
                id: 'unmarkforremoval',
                ironIcon: 'check'
            });
        }
        else if (status == 'Synced') {
            menuItems.push({
                name: Globalize.translate('ButtonMarkForRemoval'),
                id: 'markforremoval',
                ironIcon: 'delete'
            });
        }

        require(['actionsheet'], function () {

            ActionSheetElement.show({
                items: menuItems,
                positionTo: elem,
                callback: function (id) {

                    switch (id) {

                        case 'cancel':
                            cancelJobItem(page, jobItemId);
                            break;
                        case 'retry':
                            retryJobItem(page, jobItemId);
                            break;
                        case 'markforremoval':
                            markForRemoval(page, jobItemId);
                            break;
                        case 'unmarkforremoval':
                            unMarkForRemoval(page, jobItemId);
                            break;
                        default:
                            break;
                    }
                }
            });

        });
    }

    function cancelJobItem(page, jobItemId) {

        // Need a timeout because jquery mobile will not show a popup while another is in the act of closing

        Dashboard.showLoadingMsg();

        ApiClient.ajax({

            type: "DELETE",
            url: ApiClient.getUrl('Sync/JobItems/' + jobItemId)

        }).done(function () {

            loadJob(page);
        });

    }

    function markForRemoval(page, jobItemId) {

        ApiClient.ajax({

            type: "POST",
            url: ApiClient.getUrl('Sync/JobItems/' + jobItemId + '/MarkForRemoval')

        }).done(function () {

            loadJob(page);
        });
    }

    function unMarkForRemoval(page, jobItemId) {

        ApiClient.ajax({

            type: "POST",
            url: ApiClient.getUrl('Sync/JobItems/' + jobItemId + '/UnmarkForRemoval')

        }).done(function () {

            loadJob(page);
        });
    }

    function retryJobItem(page, jobItemId) {

        ApiClient.ajax({

            type: "POST",
            url: ApiClient.getUrl('Sync/JobItems/' + jobItemId + '/Enable')

        }).done(function () {

            loadJob(page);
        });
    }

    function fillJobValues(page, job, editOptions) {

        $('#txtSyncJobName', page).val(job.Name);
        $('#selectProfile', page).val(job.Profile || '').trigger('change').selectmenu('refresh');
        $('#selectQuality', page).val(job.Quality || '').trigger('change').selectmenu('refresh');
        $('#chkUnwatchedOnly', page).checked(job.UnwatchedOnly).checkboxradio('refresh');
        $('#chkSyncNewContent', page).checked(job.SyncNewContent).checkboxradio('refresh');
        $('#txtItemLimit', page).val(job.ItemLimit);

        if (job.Bitrate) {
            $('#txtBitrate', page).val(job.Bitrate / 1000000);
        } else {
            $('#txtBitrate', page).val('');
        }

        var target = editOptions.Targets.filter(function (t) {
            return t.Id == job.TargetId;
        })[0];
        var targetName = target ? target.Name : '';

        $('#selectSyncTarget', page).val(targetName);
    }

    var _jobOptions;
    function loadJob(page) {

        Dashboard.showLoadingMsg();
        var id = getParameterByName('id');

        ApiClient.getJSON(ApiClient.getUrl('Sync/Jobs/' + id)).done(function (job) {

            ApiClient.getJSON(ApiClient.getUrl('Sync/Options', {

                UserId: job.UserId,
                ItemIds: (job.RequestedItemIds && job.RequestedItemIds.length ? job.RequestedItemIds.join('') : null),

                ParentId: job.ParentId,
                Category: job.Category,
                TargetId: job.TargetId

            })).done(function (options) {

                _jobOptions = options;
                renderJob(page, job, options);
                Dashboard.hideLoadingMsg();
            });
        });

        ApiClient.getJSON(ApiClient.getUrl('Sync/JobItems', {

            JobId: id,
            AddMetadata: true

        })).done(function (result) {

            renderJobItems(page, result.Items);
            Dashboard.hideLoadingMsg();
        });
    }

    function loadJobInfo(page, job, jobItems) {

        renderJob(page, job, _jobOptions);
        renderJobItems(page, jobItems);
        Dashboard.hideLoadingMsg();
    }

    function saveJob(page) {

        Dashboard.showLoadingMsg();
        var id = getParameterByName('id');

        ApiClient.getJSON(ApiClient.getUrl('Sync/Jobs/' + id)).done(function (job) {

            SyncManager.setJobValues(job, page);

            ApiClient.ajax({

                url: ApiClient.getUrl('Sync/Jobs/' + id),
                type: 'POST',
                data: JSON.stringify(job),
                contentType: "application/json"

            }).done(function () {

                Dashboard.hideLoadingMsg();
                Dashboard.alert(Globalize.translate('SettingsSaved'));
            });
        });

    }

    function onWebSocketMessage(e, msg) {

        var page = $.mobile.activePage;

        if (msg.MessageType == "SyncJob") {
            loadJobInfo(page, msg.Data.Job, msg.Data.JobItems);
        }
    }

    function startListening(page) {

        var startParams = "0,1500";

        startParams += "," + getParameterByName('id');

        if (ApiClient.isWebSocketOpen()) {
            ApiClient.sendWebSocketMessage("SyncJobStart", startParams);
        }

    }

    function stopListening() {

        if (ApiClient.isWebSocketOpen()) {
            ApiClient.sendWebSocketMessage("SyncJobStop", "");
        }

    }

    function onSubmit() {
        var form = this;

        var page = $(form).parents('.page');

        saveJob(page);

        return false;
    }

    $(document).on('pageinit', ".syncJobPage", function () {

        $('.syncJobForm').off('submit', onSubmit).on('submit', onSubmit);

    }).on('pageshowready', ".syncJobPage", function () {

        var page = this;
        loadJob(page);

        startListening(page);
        $(ApiClient).on("websocketmessage", onWebSocketMessage);

    }).on('pagebeforehide', ".syncJobPage", function () {

        var page = this;

        stopListening();
        $(ApiClient).off("websocketmessage", onWebSocketMessage);
    });

})();