﻿using MediaBrowser.Common.Events;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Extensions;

namespace Emby.Server.Implementations.Collections
{
    public class CollectionManager : ICollectionManager
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILibraryMonitor _iLibraryMonitor;
        private readonly ILogger _logger;
        private readonly IProviderManager _providerManager;

        public event EventHandler<CollectionCreatedEventArgs> CollectionCreated;
        public event EventHandler<CollectionModifiedEventArgs> ItemsAddedToCollection;
        public event EventHandler<CollectionModifiedEventArgs> ItemsRemovedFromCollection;

        public CollectionManager(ILibraryManager libraryManager, IFileSystem fileSystem, ILibraryMonitor iLibraryMonitor, ILogger logger, IProviderManager providerManager)
        {
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _iLibraryMonitor = iLibraryMonitor;
            _logger = logger;
            _providerManager = providerManager;
        }

        public Folder GetCollectionsFolder(string userId)
        {
            return _libraryManager.RootFolder.Children.OfType<ManualCollectionsFolder>()
                .FirstOrDefault() ?? _libraryManager.GetUserRootFolder().Children.OfType<ManualCollectionsFolder>()
                .FirstOrDefault();
        }

        public IEnumerable<BoxSet> GetCollections(User user)
        {
            var folder = GetCollectionsFolder(user.Id.ToString("N"));
            return folder == null ?
                new List<BoxSet>() :
                folder.GetChildren(user, true).OfType<BoxSet>();
        }

        public BoxSet CreateCollection(CollectionCreationOptions options)
        {
            var name = options.Name;

            // Need to use the [boxset] suffix
            // If internet metadata is not found, or if xml saving is off there will be no collection.xml
            // This could cause it to get re-resolved as a plain folder
            var folderName = _fileSystem.GetValidFilename(name) + " [boxset]";

            var parentFolder = GetParentFolder(options.ParentId);

            if (parentFolder == null)
            {
                throw new ArgumentException();
            }

            var path = Path.Combine(parentFolder.Path, folderName);

            _iLibraryMonitor.ReportFileSystemChangeBeginning(path);

            try
            {
                _fileSystem.CreateDirectory(path);

                var collection = new BoxSet
                {
                    Name = name,
                    Path = path,
                    IsLocked = options.IsLocked,
                    ProviderIds = options.ProviderIds,
                    Shares = options.UserIds.Select(i => new Share
                    {
                        UserId = i,
                        CanEdit = true

                    }).ToList()
                };

                parentFolder.AddChild(collection, CancellationToken.None);

                if (options.ItemIdList.Length > 0)
                {
                    AddToCollection(collection.Id, options.ItemIdList, false, new MetadataRefreshOptions(_fileSystem)
                    {
                        // The initial adding of items is going to create a local metadata file
                        // This will cause internet metadata to be skipped as a result
                        MetadataRefreshMode = MetadataRefreshMode.FullRefresh
                    });
                }
                else
                {
                    _providerManager.QueueRefresh(collection.Id, new MetadataRefreshOptions(_fileSystem), RefreshPriority.High);
                }

                EventHelper.FireEventIfNotNull(CollectionCreated, this, new CollectionCreatedEventArgs
                {
                    Collection = collection,
                    Options = options

                }, _logger);

                return collection;
            }
            finally
            {
                // Refresh handled internally
                _iLibraryMonitor.ReportFileSystemChangeComplete(path, false);
            }
        }

        private Folder GetParentFolder(Guid? parentId)
        {
            if (parentId.HasValue)
            {
                if (parentId.Value == Guid.Empty)
                {
                    throw new ArgumentNullException("parentId");
                }

                var folder = _libraryManager.GetItemById(parentId.Value) as Folder;

                // Find an actual physical folder
                if (folder is CollectionFolder)
                {
                    var child = _libraryManager.RootFolder.Children.OfType<Folder>()
                        .FirstOrDefault(i => folder.PhysicalLocations.Contains(i.Path, StringComparer.OrdinalIgnoreCase));

                    if (child != null)
                    {
                        return child;
                    }
                }
            }

            return GetCollectionsFolder(string.Empty);
        }

        public void AddToCollection(Guid collectionId, IEnumerable<string> ids)
        {
            AddToCollection(collectionId, ids, true, new MetadataRefreshOptions(_fileSystem));
        }

        public void AddToCollection(Guid collectionId, IEnumerable<Guid> ids)
        {
            AddToCollection(collectionId, ids.Select(i => i.ToString("N")), true, new MetadataRefreshOptions(_fileSystem));
        }

        private void AddToCollection(Guid collectionId, IEnumerable<string> ids, bool fireEvent, MetadataRefreshOptions refreshOptions)
        {
            var collection = _libraryManager.GetItemById(collectionId) as BoxSet;

            if (collection == null)
            {
                throw new ArgumentException("No collection exists with the supplied Id");
            }

            var list = new List<LinkedChild>();
            var itemList = new List<BaseItem>();

            var linkedChildrenList = collection.GetLinkedChildren();
            var currentLinkedChildrenIds = linkedChildrenList.Select(i => i.Id).ToList();

            foreach (var id in ids)
            {
                var guidId = new Guid(id);
                var item = _libraryManager.GetItemById(guidId);

                if (item == null)
                {
                    throw new ArgumentException("No item exists with the supplied Id");
                }

                itemList.Add(item);

                if (!currentLinkedChildrenIds.Contains(guidId))
                {
                    list.Add(LinkedChild.Create(item));
                    linkedChildrenList.Add(item);
                }
            }

            if (list.Count > 0)
            {
                var newList = collection.LinkedChildren.ToList();
                newList.AddRange(list);
                collection.LinkedChildren = newList.ToArray(newList.Count);

                collection.UpdateRatingToItems(linkedChildrenList);

                collection.UpdateToRepository(ItemUpdateType.MetadataEdit, CancellationToken.None);

                refreshOptions.ForceSave = true;
                _providerManager.QueueRefresh(collection.Id, refreshOptions, RefreshPriority.High);

                if (fireEvent)
                {
                    EventHelper.FireEventIfNotNull(ItemsAddedToCollection, this, new CollectionModifiedEventArgs
                    {
                        Collection = collection,
                        ItemsChanged = itemList

                    }, _logger);
                }
            }
        }

        public void RemoveFromCollection(Guid collectionId, IEnumerable<string> itemIds)
        {
            RemoveFromCollection(collectionId, itemIds.Select(i => new Guid(i)));
        }

        public void RemoveFromCollection(Guid collectionId, IEnumerable<Guid> itemIds)
        {
            var collection = _libraryManager.GetItemById(collectionId) as BoxSet;

            if (collection == null)
            {
                throw new ArgumentException("No collection exists with the supplied Id");
            }

            var list = new List<LinkedChild>();
            var itemList = new List<BaseItem>();

            foreach (var guidId in itemIds)
            {
                var childItem = _libraryManager.GetItemById(guidId);

                var child = collection.LinkedChildren.FirstOrDefault(i => (i.ItemId.HasValue && i.ItemId.Value == guidId) || (childItem != null && string.Equals(childItem.Path, i.Path, StringComparison.OrdinalIgnoreCase)));

                if (child == null)
                {
                    throw new ArgumentException("No collection title exists with the supplied Id");
                }

                list.Add(child);

                if (childItem != null)
                {
                    itemList.Add(childItem);
                }
            }

            if (list.Count > 0)
            {
                collection.LinkedChildren = collection.LinkedChildren.Except(list).ToArray();
            }

            collection.UpdateToRepository(ItemUpdateType.MetadataEdit, CancellationToken.None);
            _providerManager.QueueRefresh(collection.Id, new MetadataRefreshOptions(_fileSystem)
            {
                ForceSave = true
            }, RefreshPriority.High);

            EventHelper.FireEventIfNotNull(ItemsRemovedFromCollection, this, new CollectionModifiedEventArgs
            {
                Collection = collection,
                ItemsChanged = itemList

            }, _logger);
        }

        public IEnumerable<BaseItem> CollapseItemsWithinBoxSets(IEnumerable<BaseItem> items, User user)
        {
            var results = new Dictionary<Guid, BaseItem>();

            var allBoxsets = GetCollections(user).ToList();

            foreach (var item in items)
            {
                var grouping = item as ISupportsBoxSetGrouping;

                if (grouping == null)
                {
                    results[item.Id] = item;
                }
                else
                {
                    var itemId = item.Id;

                    var currentBoxSets = allBoxsets
                        .Where(i => i.GetLinkedChildren().Any(j => j.Id == itemId))
                        .ToList();

                    if (currentBoxSets.Count > 0)
                    {
                        foreach (var boxset in currentBoxSets)
                        {
                            results[boxset.Id] = boxset;
                        }
                    }
                    else
                    {
                        results[item.Id] = item;
                    }
                }
            }

            return results.Values;
        }
    }
}
