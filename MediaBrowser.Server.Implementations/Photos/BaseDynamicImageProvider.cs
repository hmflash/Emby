﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.IO;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Implementations.Photos
{
    public abstract class BaseDynamicImageProvider<T> : IHasChangeMonitor, IForcedProvider, ICustomMetadataProvider<T>, IHasOrder
        where T : IHasMetadata
    {
        protected IFileSystem FileSystem { get; private set; }
        protected IProviderManager ProviderManager { get; private set; }
        protected IApplicationPaths ApplicationPaths { get; private set; }
        protected IImageProcessor ImageProcessor { get; set; }

        protected BaseDynamicImageProvider(IFileSystem fileSystem, IProviderManager providerManager, IApplicationPaths applicationPaths, IImageProcessor imageProcessor)
        {
            ApplicationPaths = applicationPaths;
            ProviderManager = providerManager;
            FileSystem = fileSystem;
            ImageProcessor = imageProcessor;
        }

        public virtual bool Supports(IHasImages item)
        {
            return true;
        }

        public virtual IEnumerable<ImageType> GetSupportedImages(IHasImages item)
        {
            return new List<ImageType>
            {
                ImageType.Primary,
                ImageType.Thumb
            };
        }

        public async Task<ItemUpdateType> FetchAsync(T item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            if (!Supports(item))
            {
                return ItemUpdateType.None;
            }

            var updateType = ItemUpdateType.None;
            var supportedImages = GetSupportedImages(item).ToList();

            if (supportedImages.Contains(ImageType.Primary))
            {
                var primaryResult = await FetchAsync(item, ImageType.Primary, options, cancellationToken).ConfigureAwait(false);
                updateType = updateType | primaryResult;
            }

            if (supportedImages.Contains(ImageType.Thumb))
            {
                var thumbResult = await FetchAsync(item, ImageType.Thumb, options, cancellationToken).ConfigureAwait(false);
                updateType = updateType | thumbResult;
            }


            return updateType;
        }

        protected async Task<ItemUpdateType> FetchAsync(IHasImages item, ImageType imageType, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            var items = await GetItemsWithImages(item).ConfigureAwait(false);
            var cacheKey = GetConfigurationCacheKey(items, item.Name);

            if (!HasChanged(item, imageType, cacheKey))
            {
                return ItemUpdateType.None;
            }

            return await FetchToFileInternal(item, items, imageType, cacheKey, cancellationToken).ConfigureAwait(false);
        }

        protected async Task<ItemUpdateType> FetchToFileInternal(IHasImages item,
            List<BaseItem> itemsWithImages,
            ImageType imageType,
            string cacheKey,
            CancellationToken cancellationToken)
        {
            var outputPath = Path.Combine(ApplicationPaths.TempDirectory, Guid.NewGuid() + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var imageCreated = await CreateImage(item, itemsWithImages, outputPath, imageType, 0).ConfigureAwait(false);

            if (!imageCreated)
            {
                return ItemUpdateType.None;
            }

            await ProviderManager.SaveImage(item, outputPath, "image/png", imageType, null, cacheKey, cancellationToken).ConfigureAwait(false);

            return ItemUpdateType.ImageUpdate;
        }

        protected abstract Task<List<BaseItem>> GetItemsWithImages(IHasImages item);

        private const string Version = "32";
        protected string GetConfigurationCacheKey(List<BaseItem> items, string itemName)
        {
            var parts = Version + "_" + (itemName ?? string.Empty) + "_" +
                        string.Join(",", items.Select(i => i.Id.ToString("N")).ToArray());

            return parts.GetMD5().ToString("N");
        }

        protected Task<bool> CreateThumbCollage(IHasImages primaryItem, List<BaseItem> items, string outputPath)
        {
            return CreateCollage(primaryItem, items, outputPath, 640, 360);
        }

        protected virtual IEnumerable<string> GetStripCollageImagePaths(IHasImages primaryItem, IEnumerable<BaseItem> items)
        {
            return items
                .Select(i => i.GetImagePath(ImageType.Primary) ?? i.GetImagePath(ImageType.Thumb))
                .Where(i => !string.IsNullOrWhiteSpace(i));
        }

        protected Task<bool> CreatePosterCollage(IHasImages primaryItem, List<BaseItem> items, string outputPath)
        {
            return CreateCollage(primaryItem, items, outputPath, 400, 600);
        }

        protected Task<bool> CreateSquareCollage(IHasImages primaryItem, List<BaseItem> items, string outputPath)
        {
            return CreateCollage(primaryItem, items, outputPath, 600, 600);
        }

        protected Task<bool> CreateThumbCollage(IHasImages primaryItem, List<BaseItem> items, string outputPath, int width, int height)
        {
            return CreateCollage(primaryItem, items, outputPath, width, height);
        }

        private Task<bool> CreateCollage(IHasImages primaryItem, List<BaseItem> items, string outputPath, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            var options = new ImageCollageOptions
            {
                Height = height,
                Width = width,
                OutputPath = outputPath,
                InputPaths = GetStripCollageImagePaths(primaryItem, items).ToArray()
            };

            if (options.InputPaths.Length == 0)
            {
                return Task.FromResult(false);
            }

            ImageProcessor.CreateImageCollage(options);
            return Task.FromResult(true);
        }

        public string Name
        {
            get { return "Dynamic Image Provider"; }
        }

        protected virtual async Task<bool> CreateImage(IHasImages item,
            List<BaseItem> itemsWithImages,
            string outputPath,
            ImageType imageType,
            int imageIndex)
        {
            if (itemsWithImages.Count == 0)
            {
                return false;
            }

            if (imageType == ImageType.Thumb)
            {
                return await CreateThumbCollage(item, itemsWithImages, outputPath).ConfigureAwait(false);
            }

            if (imageType == ImageType.Primary)
            {
                if (item is UserView)
                {
                    return await CreateSquareCollage(item, itemsWithImages, outputPath).ConfigureAwait(false);
                }
                if (item is PhotoAlbum || item is Playlist)
                {
                    return await CreateSquareCollage(item, itemsWithImages, outputPath).ConfigureAwait(false);
                }
                return await CreatePosterCollage(item, itemsWithImages, outputPath).ConfigureAwait(false);
            }

            throw new ArgumentException("Unexpected image type");
        }

        private const int MaxImageAgeDays = 7;

        public bool HasChanged(IHasMetadata item, IDirectoryService directoryService, DateTime date)
        {
            if (!Supports(item))
            {
                return false;
            }

            var supportedImages = GetSupportedImages(item).ToList();

            if (item is UserView || item is ICollectionFolder)
            {
                if (supportedImages.Contains(ImageType.Primary) && HasChanged(item, ImageType.Primary))
                {
                    return true;
                }
                if (supportedImages.Contains(ImageType.Thumb) && HasChanged(item, ImageType.Thumb))
                {
                    return true;
                }

                return false;
            }

            var items = GetItemsWithImages(item).Result;
            var cacheKey = GetConfigurationCacheKey(items, item.Name);

            if (supportedImages.Contains(ImageType.Primary) && HasChanged(item, ImageType.Primary, cacheKey))
            {
                return true;
            }
            if (supportedImages.Contains(ImageType.Thumb) && HasChanged(item, ImageType.Thumb, cacheKey))
            {
                return true;
            }

            return false;
        }

        protected bool HasChanged(IHasImages item, ImageType type, string cacheKey)
        {
            var image = item.GetImageInfo(type, 0);

            if (image != null)
            {
                if (!FileSystem.ContainsSubPath(item.GetInternalMetadataPath(), image.Path))
                {
                    return false;
                }

                var currentPathCacheKey = (Path.GetFileNameWithoutExtension(image.Path) ?? string.Empty).Split('_').LastOrDefault();
                if (string.Equals(cacheKey, currentPathCacheKey, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        protected bool HasChanged(IHasImages item, ImageType type)
        {
            var image = item.GetImageInfo(type, 0);

            if (image != null)
            {
                if (!FileSystem.ContainsSubPath(item.GetInternalMetadataPath(), image.Path))
                {
                    return false;
                }

                var age = DateTime.UtcNow - image.DateModified;
                if (age.TotalDays <= MaxImageAgeDays)
                {
                    return false;
                }
            }

            return true;
        }

        protected List<BaseItem> GetFinalItems(List<BaseItem> items)
        {
            return GetFinalItems(items, 4);
        }

        protected virtual List<BaseItem> GetFinalItems(List<BaseItem> items, int limit)
        {
            // Rotate the images once every x days
            var random = DateTime.Now.DayOfYear % MaxImageAgeDays;

            return items
                .OrderBy(i => (random + "" + items.IndexOf(i)).GetMD5())
                .Take(limit)
                .OrderBy(i => i.Name)
                .ToList();
        }

        public int Order
        {
            get
            {
                // Run before the default image provider which will download placeholders
                return 0;
            }
        }
    }
}
