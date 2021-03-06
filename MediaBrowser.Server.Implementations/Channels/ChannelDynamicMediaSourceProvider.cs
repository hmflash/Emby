﻿using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Implementations.Channels
{
    public class ChannelDynamicMediaSourceProvider : IMediaSourceProvider
    {
        private readonly ChannelManager _channelManager;

        public ChannelDynamicMediaSourceProvider(IChannelManager channelManager)
        {
            _channelManager = (ChannelManager)channelManager;
        }

        public Task<IEnumerable<MediaSourceInfo>> GetMediaSources(IHasMediaSources item, CancellationToken cancellationToken)
        {
            var channelItem = item as IChannelMediaItem;

            if (channelItem != null)
            {
                return _channelManager.GetDynamicMediaSources(channelItem, cancellationToken);
            }

            return Task.FromResult<IEnumerable<MediaSourceInfo>>(new List<MediaSourceInfo>());
        }

        public Task<MediaSourceInfo> OpenMediaSource(string openToken, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task CloseMediaSource(string liveStreamId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
