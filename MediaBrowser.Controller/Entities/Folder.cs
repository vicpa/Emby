﻿using MediaBrowser.Common.Progress;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Controller.Entities
{
    /// <summary>
    /// Class Folder
    /// </summary>
    public class Folder : BaseItem
    {
        public static IUserManager UserManager { get; set; }
        public static IUserViewManager UserViewManager { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is root.
        /// </summary>
        /// <value><c>true</c> if this instance is root; otherwise, <c>false</c>.</value>
        public bool IsRoot { get; set; }

        public LinkedChild[] LinkedChildren { get; set; }

        [IgnoreDataMember]
        public DateTime? DateLastMediaAdded { get; set; }

        public Folder()
        {
            LinkedChildren = EmptyLinkedChildArray;
        }

        [IgnoreDataMember]
        public override bool SupportsThemeMedia
        {
            get { return true; }
        }

        [IgnoreDataMember]
        public virtual bool IsPreSorted
        {
            get { return false; }
        }

        [IgnoreDataMember]
        public virtual bool IsPhysicalRoot
        {
            get { return false; }
        }

        [IgnoreDataMember]
        public override bool SupportsInheritedParentImages
        {
            get
            {
                return true;
            }
        }

        [IgnoreDataMember]
        public override bool SupportsPlayedStatus
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is folder.
        /// </summary>
        /// <value><c>true</c> if this instance is folder; otherwise, <c>false</c>.</value>
        [IgnoreDataMember]
        public override bool IsFolder
        {
            get
            {
                return true;
            }
        }

        [IgnoreDataMember]
        public override bool IsDisplayedAsFolder
        {
            get
            {
                return true;
            }
        }

        [IgnoreDataMember]
        public virtual bool SupportsCumulativeRunTimeTicks
        {
            get
            {
                return false;
            }
        }

        [IgnoreDataMember]
        public virtual bool SupportsDateLastMediaAdded
        {
            get
            {
                return false;
            }
        }

        public override bool CanDelete()
        {
            if (IsRoot)
            {
                return false;
            }

            return base.CanDelete();
        }

        public override bool RequiresRefresh()
        {
            var baseResult = base.RequiresRefresh();

            if (SupportsCumulativeRunTimeTicks && !RunTimeTicks.HasValue)
            {
                baseResult = true;
            }

            return baseResult;
        }

        [IgnoreDataMember]
        public override string FileNameWithoutExtension
        {
            get
            {
                if (LocationType == LocationType.FileSystem)
                {
                    return System.IO.Path.GetFileName(Path);
                }

                return null;
            }
        }

        protected override bool IsAllowTagFilterEnforced()
        {
            if (this is ICollectionFolder)
            {
                return false;
            }
            if (this is UserView)
            {
                return false;
            }
            return true;
        }

        [IgnoreDataMember]
        protected virtual bool SupportsShortcutChildren
        {
            get { return false; }
        }

        /// <summary>
        /// Adds the child.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.InvalidOperationException">Unable to add  + item.Name</exception>
        public void AddChild(BaseItem item, CancellationToken cancellationToken)
        {
            item.SetParent(this);

            if (item.Id == Guid.Empty)
            {
                item.Id = LibraryManager.GetNewItemId(item.Path, item.GetType());
            }

            if (Children.Any(i => i.Id == item.Id))
            {
                throw new ArgumentException(string.Format("A child with the Id {0} already exists.", item.Id));
            }

            if (item.DateCreated == DateTime.MinValue)
            {
                item.DateCreated = DateTime.UtcNow;
            }
            if (item.DateModified == DateTime.MinValue)
            {
                item.DateModified = DateTime.UtcNow;
            }

            LibraryManager.CreateItem(item, cancellationToken);
        }

        /// <summary>
        /// Gets the actual children.
        /// </summary>
        /// <value>The actual children.</value>
        [IgnoreDataMember]
        public virtual IEnumerable<BaseItem> Children
        {
            get
            {
                return LoadChildren();
            }
        }

        /// <summary>
        /// thread-safe access to all recursive children of this folder - without regard to user
        /// </summary>
        /// <value>The recursive children.</value>
        [IgnoreDataMember]
        public IEnumerable<BaseItem> RecursiveChildren
        {
            get { return GetRecursiveChildren(); }
        }

        public override bool IsVisible(User user)
        {
            if (this is ICollectionFolder && !(this is BasePluginFolder))
            {
                if (user.Policy.BlockedMediaFolders != null)
                {
                    if (user.Policy.BlockedMediaFolders.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase) ||

                        // Backwards compatibility
                        user.Policy.BlockedMediaFolders.Contains(Name, StringComparer.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!user.Policy.EnableAllFolders && !user.Policy.EnabledFolders.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return base.IsVisible(user);
        }

        /// <summary>
        /// Loads our children.  Validation will occur externally.
        /// We want this sychronous.
        /// </summary>
        protected virtual List<BaseItem> LoadChildren()
        {
            //Logger.Debug("Loading children from {0} {1} {2}", GetType().Name, Id, Path);
            //just load our children from the repo - the library will be validated and maintained in other processes
            return GetCachedChildren();
        }

        public override double? GetRefreshProgress()
        {
            return ProviderManager.GetRefreshProgress(Id);
        }

        public Task ValidateChildren(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return ValidateChildren(progress, cancellationToken, new MetadataRefreshOptions(new DirectoryService(Logger, FileSystem)));
        }

        /// <summary>
        /// Validates that the children of the folder still exist
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="metadataRefreshOptions">The metadata refresh options.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <returns>Task.</returns>
        public Task ValidateChildren(IProgress<double> progress, CancellationToken cancellationToken, MetadataRefreshOptions metadataRefreshOptions, bool recursive = true)
        {
            return ValidateChildrenInternal(progress, cancellationToken, recursive, true, metadataRefreshOptions, metadataRefreshOptions.DirectoryService);
        }

        private Dictionary<Guid, BaseItem> GetActualChildrenDictionary()
        {
            var dictionary = new Dictionary<Guid, BaseItem>();

            var childrenList = Children.ToList();

            foreach (var child in childrenList)
            {
                var id = child.Id;
                if (dictionary.ContainsKey(id))
                {
                    Logger.Error("Found folder containing items with duplicate id. Path: {0}, Child Name: {1}",
                        Path ?? Name,
                        child.Path ?? child.Name);
                }
                else
                {
                    dictionary[id] = child;
                }
            }

            return dictionary;
        }

        protected override void TriggerOnRefreshStart()
        {
        }

        protected override void TriggerOnRefreshComplete()
        {
        }

        /// <summary>
        /// Validates the children internal.
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <param name="refreshChildMetadata">if set to <c>true</c> [refresh child metadata].</param>
        /// <param name="refreshOptions">The refresh options.</param>
        /// <param name="directoryService">The directory service.</param>
        /// <returns>Task.</returns>
        protected virtual async Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
        {
            if (recursive)
            {
                ProviderManager.OnRefreshStart(this);
            }

            try
            {
                await ValidateChildrenInternal2(progress, cancellationToken, recursive, refreshChildMetadata, refreshOptions, directoryService).ConfigureAwait(false);
            }
            finally
            {
                if (recursive)
                {
                    ProviderManager.OnRefreshComplete(this);
                }
            }
        }

        private async Task ValidateChildrenInternal2(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
        {
            var locationType = LocationType;

            cancellationToken.ThrowIfCancellationRequested();

            var validChildren = new List<BaseItem>();
            var validChildrenNeedGeneration = false;

            var allLibraryPaths = LibraryManager
              .GetVirtualFolders()
              .SelectMany(i => i.Locations)
              .ToList();

            if (locationType != LocationType.Remote && locationType != LocationType.Virtual)
            {
                IEnumerable<BaseItem> nonCachedChildren;

                try
                {
                    nonCachedChildren = GetNonCachedChildren(directoryService);
                }
                catch (IOException ex)
                {
                    nonCachedChildren = new BaseItem[] { };

                    Logger.ErrorException("Error getting file system entries for {0}", ex, Path);
                }

                if (nonCachedChildren == null) return; //nothing to validate

                progress.Report(5);

                if (recursive)
                {
                    ProviderManager.OnRefreshProgress(this, 5);
                }

                //build a dictionary of the current children we have now by Id so we can compare quickly and easily
                var currentChildren = GetActualChildrenDictionary();

                //create a list for our validated children
                var newItems = new List<BaseItem>();

                cancellationToken.ThrowIfCancellationRequested();

                foreach (var child in nonCachedChildren)
                {
                    BaseItem currentChild;

                    if (currentChildren.TryGetValue(child.Id, out currentChild))
                    {
                        validChildren.Add(currentChild);

                        if (currentChild.UpdateFromResolvedItem(child) > ItemUpdateType.None)
                        {
                            currentChild.UpdateToRepository(ItemUpdateType.MetadataImport, cancellationToken);
                        }

                        continue;
                    }

                    // Brand new item - needs to be added
                    child.SetParent(this);
                    newItems.Add(child);
                    validChildren.Add(child);
                }

                // If any items were added or removed....
                if (newItems.Count > 0 || currentChildren.Count != validChildren.Count)
                {
                    // That's all the new and changed ones - now see if there are any that are missing
                    var itemsRemoved = currentChildren.Values.Except(validChildren).ToList();

                    foreach (var item in itemsRemoved)
                    {
                        var itemLocationType = item.LocationType;
                        if (itemLocationType == LocationType.Virtual ||
                            itemLocationType == LocationType.Remote)
                        {
                        }

                        else if (!string.IsNullOrEmpty(item.Path) && IsPathOffline(item.Path, allLibraryPaths))
                        {
                        }
                        else
                        {
                            Logger.Debug("Removed item: " + item.Path);

                            item.SetParent(null);
                            LibraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = false }, this, false);
                        }
                    }

                    LibraryManager.CreateItems(newItems, this, cancellationToken);
                }
            }
            else
            {
                validChildrenNeedGeneration = true;
            }

            progress.Report(10);

            if (recursive)
            {
                ProviderManager.OnRefreshProgress(this, 10);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (recursive)
            {
                using (var innerProgress = new ActionableProgress<double>())
                {
                    var folder = this;
                    innerProgress.RegisterAction(p =>
                    {
                        double newPct = .80 * p + 10;
                        progress.Report(newPct);
                        ProviderManager.OnRefreshProgress(folder, newPct);
                    });

                    if (validChildrenNeedGeneration)
                    {
                        validChildren = Children.ToList();
                        validChildrenNeedGeneration = false;
                    }

                    await ValidateSubFolders(validChildren.OfType<Folder>().ToList(), directoryService, innerProgress, cancellationToken).ConfigureAwait(false);
                }
            }

            if (refreshChildMetadata)
            {
                progress.Report(90);

                if (recursive)
                {
                    ProviderManager.OnRefreshProgress(this, 90);
                }

                var container = this as IMetadataContainer;

                using (var innerProgress = new ActionableProgress<double>())
                {
                    var folder = this;
                    innerProgress.RegisterAction(p =>
                    {
                        double newPct = .10 * p + 90;
                        progress.Report(newPct);
                        if (recursive)
                        {
                            ProviderManager.OnRefreshProgress(folder, newPct);
                        }
                    });

                    if (container != null)
                    {
                        await RefreshAllMetadataForContainer(container, refreshOptions, innerProgress, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        if (validChildrenNeedGeneration)
                        {
                            validChildren = Children.ToList();
                        }

                        await RefreshMetadataRecursive(validChildren, refreshOptions, recursive, innerProgress, cancellationToken);
                    }
                }
            }
        }

        private async Task RefreshMetadataRecursive(List<BaseItem> children, MetadataRefreshOptions refreshOptions, bool recursive, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var numComplete = 0;
            var count = children.Count;
            double currentPercent = 0;

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var innerProgress = new ActionableProgress<double>())
                {
                    // Avoid implicitly captured closure
                    var currentInnerPercent = currentPercent;

                    innerProgress.RegisterAction(p =>
                    {
                        double innerPercent = currentInnerPercent;
                        innerPercent += p / (count);
                        progress.Report(innerPercent);
                    });

                    await RefreshChildMetadata(child, refreshOptions, recursive && child.IsFolder, innerProgress, cancellationToken)
                        .ConfigureAwait(false);
                }

                numComplete++;
                double percent = numComplete;
                percent /= count;
                percent *= 100;
                currentPercent = percent;

                progress.Report(percent);
            }
        }

        private async Task RefreshAllMetadataForContainer(IMetadataContainer container, MetadataRefreshOptions refreshOptions, IProgress<double> progress, CancellationToken cancellationToken)
        {
            // TODO: Move this into Series.RefreshAllMetadata
            var series = container as Series;
            if (series != null)
            {
                await series.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);

            }
            await container.RefreshAllMetadata(refreshOptions, progress, cancellationToken).ConfigureAwait(false);
        }

        private async Task RefreshChildMetadata(BaseItem child, MetadataRefreshOptions refreshOptions, bool recursive, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var container = child as IMetadataContainer;

            if (container != null)
            {
                await RefreshAllMetadataForContainer(container, refreshOptions, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (refreshOptions.RefreshItem(child))
                {
                    await child.RefreshMetadata(refreshOptions, cancellationToken).ConfigureAwait(false);
                }

                if (recursive)
                {
                    var folder = child as Folder;

                    if (folder != null)
                    {
                        await folder.RefreshMetadataRecursive(folder.Children.ToList(), refreshOptions, true, progress, cancellationToken);
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the children.
        /// </summary>
        /// <param name="children">The children.</param>
        /// <param name="directoryService">The directory service.</param>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private async Task ValidateSubFolders(IList<Folder> children, IDirectoryService directoryService, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var numComplete = 0;
            var count = children.Count;
            double currentPercent = 0;

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var innerProgress = new ActionableProgress<double>())
                {
                    // Avoid implicitly captured closure
                    var currentInnerPercent = currentPercent;

                    innerProgress.RegisterAction(p =>
                    {
                        double innerPercent = currentInnerPercent;
                        innerPercent += p / (count);
                        progress.Report(innerPercent);
                    });

                    await child.ValidateChildrenInternal(innerProgress, cancellationToken, true, false, null, directoryService)
                            .ConfigureAwait(false);
                }

                numComplete++;
                double percent = numComplete;
                percent /= count;
                percent *= 100;
                currentPercent = percent;

                progress.Report(percent);
            }
        }

        public bool IsPathOffline(string path, List<string> allLibraryPaths)
        {
            //if (FileSystem.FileExists(path))
            //{
            //    return false;
            //}

            var originalPath = path;

            // Depending on whether the path is local or unc, it may return either null or '\' at the top
            while (!string.IsNullOrWhiteSpace(path) && path.Length > 1)
            {
                if (FileSystem.DirectoryExists(path))
                {
                    return false;
                }

                if (allLibraryPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }

                path = FileSystem.GetDirectoryName(path);
            }

            return allLibraryPaths.Any(i => ContainsPath(i, originalPath));
        }

        private bool ContainsPath(string parent, string path)
        {
            return FileSystem.AreEqual(parent, path) || FileSystem.ContainsSubPath(parent, path);
        }

        /// <summary>
        /// Get the children of this folder from the actual file system
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        protected virtual IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
        {
            var collectionType = LibraryManager.GetContentType(this);
            var libraryOptions = LibraryManager.GetLibraryOptions(this);

            return LibraryManager.ResolvePaths(GetFileSystemChildren(directoryService), directoryService, this, libraryOptions, collectionType);
        }

        /// <summary>
        /// Get our children from the repo - stubbed for now
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        protected List<BaseItem> GetCachedChildren()
        {
            return ItemRepository.GetItemList(new InternalItemsQuery
            {
                Parent = this,
                GroupByPresentationUniqueKey = false,
                DtoOptions = new DtoOptions(true)
            });
        }

        public virtual int GetChildCount(User user)
        {
            if (LinkedChildren.Length > 0)
            {
                if (!(this is ICollectionFolder))
                {
                    return GetChildren(user, true).Count;
                }
            }

            var result = GetItems(new InternalItemsQuery(user)
            {
                Recursive = false,
                Limit = 0,
                Parent = this,
                DtoOptions = new DtoOptions(false)
                {
                    EnableImages = false
                }

            });

            return result.TotalRecordCount;
        }

        public virtual int GetRecursiveChildCount(User user)
        {
            return GetItems(new InternalItemsQuery(user)
            {
                Recursive = true,
                IsFolder = false,
                IsVirtualItem = false,
                EnableTotalRecordCount = true,
                Limit = 0,
                DtoOptions = new DtoOptions(false)
                {
                    EnableImages = false
                }

            }).TotalRecordCount;
        }

        public QueryResult<BaseItem> QueryRecursive(InternalItemsQuery query)
        {
            var user = query.User;

            if (!query.ForceDirect && RequiresPostFiltering(query))
            {
                IEnumerable<BaseItem> items;
                Func<BaseItem, bool> filter = i => UserViewBuilder.Filter(i, user, query, UserDataManager, LibraryManager);

                if (query.User == null)
                {
                    items = GetRecursiveChildren(filter);
                }
                else
                {
                    items = GetRecursiveChildren(user, query);
                }

                return PostFilterAndSort(items, query, true, true);
            }

            if (!(this is UserRootFolder) && !(this is AggregateFolder))
            {
                if (!query.ParentId.HasValue)
                {
                    query.Parent = this;
                }
            }

            if (RequiresPostFiltering2(query))
            {
                return QueryWithPostFiltering2(query);
            }

            return LibraryManager.GetItemsResult(query);
        }

        private QueryResult<BaseItem> QueryWithPostFiltering2(InternalItemsQuery query)
        {
            var startIndex = query.StartIndex;
            var limit = query.Limit;

            query.StartIndex = null;
            query.Limit = null;

            var itemsList = LibraryManager.GetItemList(query);
            var user = query.User;

            if (user != null)
            {
                // needed for boxsets
                itemsList = itemsList.Where(i => i.IsVisibleStandalone(query.User)).ToList();
            }

            BaseItem[] returnItems;
            int totalCount = 0;

            if (query.EnableTotalRecordCount)
            {
                var itemsArray = itemsList.ToArray();
                totalCount = itemsArray.Length;
                returnItems = itemsArray;
            }
            else
            {
                returnItems = itemsList.ToArray();
            }

            if (limit.HasValue)
            {
                returnItems = returnItems.Skip(startIndex ?? 0).Take(limit.Value).ToArray();
            }
            else if (startIndex.HasValue)
            {
                returnItems = returnItems.Skip(startIndex.Value).ToArray();
            }

            return new QueryResult<BaseItem>
            {
                TotalRecordCount = totalCount,
                Items = returnItems.ToArray()
            };
        }

        private bool RequiresPostFiltering2(InternalItemsQuery query)
        {
            if (query.IncludeItemTypes.Length == 1 && string.Equals(query.IncludeItemTypes[0], typeof(BoxSet).Name, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Debug("Query requires post-filtering due to BoxSet query");
                return true;
            }

            return false;
        }

        private bool RequiresPostFiltering(InternalItemsQuery query)
        {
            if (LinkedChildren.Length > 0)
            {
                if (!(this is ICollectionFolder))
                {
                    Logger.Debug("Query requires post-filtering due to LinkedChildren. Type: " + GetType().Name);
                    return true;
                }
            }

            if (query.IsInBoxSet.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsInBoxSet");
                return true;
            }

            // Filter by Video3DFormat
            if (query.Is3D.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to Is3D");
                return true;
            }

            if (query.HasOfficialRating.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasOfficialRating");
                return true;
            }

            if (query.IsPlaceHolder.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to IsPlaceHolder");
                return true;
            }

            if (query.HasSpecialFeature.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasSpecialFeature");
                return true;
            }

            if (query.HasSubtitles.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasSubtitles");
                return true;
            }

            if (query.HasTrailer.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to HasTrailer");
                return true;
            }

            // Filter by VideoType
            if (query.VideoTypes.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to VideoTypes");
                return true;
            }

            // Apply person filter
            if (query.ItemIdsFromPersonFilters != null)
            {
                Logger.Debug("Query requires post-filtering due to ItemIdsFromPersonFilters");
                return true;
            }

            if (UserViewBuilder.CollapseBoxSetItems(query, this, query.User, ConfigurationManager))
            {
                Logger.Debug("Query requires post-filtering due to CollapseBoxSetItems");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(query.AdjacentTo))
            {
                Logger.Debug("Query requires post-filtering due to AdjacentTo");
                return true;
            }

            if (query.SeriesStatuses.Length > 0)
            {
                Logger.Debug("Query requires post-filtering due to SeriesStatuses");
                return true;
            }

            if (query.AiredDuringSeason.HasValue)
            {
                Logger.Debug("Query requires post-filtering due to AiredDuringSeason");
                return true;
            }

            if (query.IsPlayed.HasValue)
            {
                if (query.IncludeItemTypes.Length == 1 && query.IncludeItemTypes.Contains(typeof(Series).Name))
                {
                    Logger.Debug("Query requires post-filtering due to IsPlayed");
                    return true;
                }
            }

            return false;
        }

        public QueryResult<BaseItem> GetItems(InternalItemsQuery query)
        {
            if (query.ItemIds.Length > 0)
            {
                var result = LibraryManager.GetItemsResult(query);

                if (query.OrderBy.Length == 0)
                {
                    var ids = query.ItemIds.ToList();

                    // Try to preserve order
                    result.Items = result.Items.OrderBy(i => ids.IndexOf(i.Id.ToString("N"))).ToArray();
                }
                return result;
            }

            return GetItemsInternal(query);
        }

        public BaseItem[] GetItemList(InternalItemsQuery query)
        {
            query.EnableTotalRecordCount = false;

            if (query.ItemIds.Length > 0)
            {
                var result = LibraryManager.GetItemList(query);

                if (query.OrderBy.Length == 0)
                {
                    var ids = query.ItemIds.ToList();

                    // Try to preserve order
                    return result.OrderBy(i => ids.IndexOf(i.Id.ToString("N"))).ToArray();
                }
                return result.ToArray(result.Count);
            }

            return GetItemsInternal(query).Items;
        }

        protected virtual QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
        {
            if (SourceType == SourceType.Channel)
            {
                try
                {
                    // Don't blow up here because it could cause parent screens with other content to fail
                    return ChannelManager.GetChannelItemsInternal(new ChannelItemQuery
                    {
                        ChannelId = ChannelId,
                        FolderId = Id.ToString("N"),
                        Limit = query.Limit,
                        StartIndex = query.StartIndex,
                        UserId = query.User.Id.ToString("N"),
                        OrderBy = query.OrderBy

                    }, new SimpleProgress<double>(), CancellationToken.None).Result;
                }
                catch
                {
                    // Already logged at lower levels
                    return new QueryResult<BaseItem>();
                }
            }

            if (query.Recursive)
            {
                return QueryRecursive(query);
            }

            var user = query.User;

            Func<BaseItem, bool> filter = i => UserViewBuilder.Filter(i, user, query, UserDataManager, LibraryManager);

            IEnumerable<BaseItem> items;

            if (query.User == null)
            {
                items = query.Recursive
                   ? GetRecursiveChildren(filter)
                   : Children.Where(filter);
            }
            else
            {
                items = query.Recursive
                   ? GetRecursiveChildren(user, query)
                   : GetChildren(user, true).Where(filter);
            }

            return PostFilterAndSort(items, query, true, true);
        }

        protected QueryResult<BaseItem> PostFilterAndSort(IEnumerable<BaseItem> items, InternalItemsQuery query, bool collapseBoxSetItems, bool enableSorting)
        {
            return UserViewBuilder.PostFilterAndSort(items, this, null, query, LibraryManager, ConfigurationManager, collapseBoxSetItems, enableSorting);
        }

        public virtual List<BaseItem> GetChildren(User user, bool includeLinkedChildren)
        {
            if (user == null)
            {
                throw new ArgumentNullException();
            }

            //the true root should return our users root folder children
            if (IsPhysicalRoot) return user.RootFolder.GetChildren(user, includeLinkedChildren);

            var result = new Dictionary<Guid, BaseItem>();

            AddChildren(user, includeLinkedChildren, result, false, null);

            return result.Values.ToList();
        }

        protected virtual IEnumerable<BaseItem> GetEligibleChildrenForRecursiveChildren(User user)
        {
            return Children;
        }

        /// <summary>
        /// Adds the children to list.
        /// </summary>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        private void AddChildren(User user, bool includeLinkedChildren, Dictionary<Guid, BaseItem> result, bool recursive, InternalItemsQuery query)
        {
            foreach (var child in GetEligibleChildrenForRecursiveChildren(user))
            {
                if (child.IsVisible(user))
                {
                    if (query == null || UserViewBuilder.FilterItem(child, query))
                    {
                        result[child.Id] = child;
                    }

                    if (recursive && child.IsFolder)
                    {
                        var folder = (Folder)child;

                        folder.AddChildren(user, includeLinkedChildren, result, true, query);
                    }
                }
            }

            if (includeLinkedChildren)
            {
                foreach (var child in GetLinkedChildren(user))
                {
                    if (child.IsVisible(user))
                    {
                        if (query == null || UserViewBuilder.FilterItem(child, query))
                        {
                            result[child.Id] = child;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets allowed recursive children of an item
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="includeLinkedChildren">if set to <c>true</c> [include linked children].</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public IEnumerable<BaseItem> GetRecursiveChildren(User user, bool includeLinkedChildren = true)
        {
            return GetRecursiveChildren(user, null);
        }

        public virtual IEnumerable<BaseItem> GetRecursiveChildren(User user, InternalItemsQuery query)
        {
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            var result = new Dictionary<Guid, BaseItem>();

            AddChildren(user, true, result, true, query);

            return result.Values;
        }

        /// <summary>
        /// Gets the recursive children.
        /// </summary>
        /// <returns>IList{BaseItem}.</returns>
        public IList<BaseItem> GetRecursiveChildren()
        {
            return GetRecursiveChildren(true);
        }

        public IList<BaseItem> GetRecursiveChildren(bool includeLinkedChildren)
        {
            return GetRecursiveChildren(i => true, includeLinkedChildren);
        }

        public IList<BaseItem> GetRecursiveChildren(Func<BaseItem, bool> filter)
        {
            return GetRecursiveChildren(filter, true);
        }

        public IList<BaseItem> GetRecursiveChildren(Func<BaseItem, bool> filter, bool includeLinkedChildren)
        {
            var result = new Dictionary<Guid, BaseItem>();

            AddChildrenToList(result, includeLinkedChildren, true, filter);

            return result.Values.ToList();
        }

        /// <summary>
        /// Adds the children to list.
        /// </summary>
        private void AddChildrenToList(Dictionary<Guid, BaseItem> result, bool includeLinkedChildren, bool recursive, Func<BaseItem, bool> filter)
        {
            foreach (var child in Children)
            {
                if (filter == null || filter(child))
                {
                    result[child.Id] = child;
                }

                if (recursive && child.IsFolder)
                {
                    var folder = (Folder)child;

                    // We can only support includeLinkedChildren for the first folder, or we might end up stuck in a loop of linked items
                    folder.AddChildrenToList(result, false, true, filter);
                }
            }

            if (includeLinkedChildren)
            {
                foreach (var child in GetLinkedChildren())
                {
                    if (filter == null || filter(child))
                    {
                        result[child.Id] = child;
                    }
                }
            }
        }


        /// <summary>
        /// Gets the linked children.
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        public List<BaseItem> GetLinkedChildren()
        {
            var linkedChildren = LinkedChildren;
            var list = new List<BaseItem>(linkedChildren.Length);

            foreach (var i in linkedChildren)
            {
                var child = GetLinkedChild(i);

                if (child != null)
                {
                    list.Add(child);
                }
            }
            return list;
        }

        protected virtual bool FilterLinkedChildrenPerUser
        {
            get
            {
                return false;
            }
        }

        public List<BaseItem> GetLinkedChildren(User user)
        {
            if (!FilterLinkedChildrenPerUser || user == null)
            {
                return GetLinkedChildren();
            }

            var linkedChildren = LinkedChildren;
            var list = new List<BaseItem>(linkedChildren.Length);

            if (linkedChildren.Length == 0)
            {
                return list;
            }

            var allUserRootChildren = user.RootFolder.Children.OfType<Folder>().ToList();

            var collectionFolderIds = allUserRootChildren
                .OfType<CollectionFolder>()
                .Where(i => i.IsVisible(user))
                .Select(i => i.Id)
                .ToList();

            foreach (var i in linkedChildren)
            {
                var child = GetLinkedChild(i);

                if (child == null)
                {
                    continue;
                }

                var childOwner = child.IsOwnedItem ? (child.GetOwner() ?? child) : child;

                if (childOwner != null && !(child is IItemByName))
                {
                    var childLocationType = childOwner.LocationType;
                    if (childLocationType == LocationType.Remote || childLocationType == LocationType.Virtual)
                    {
                        if (!childOwner.IsVisibleStandalone(user))
                        {
                            continue;
                        }
                    }
                    else if (childLocationType == LocationType.FileSystem)
                    {
                        var itemCollectionFolderIds =
                            LibraryManager.GetCollectionFolders(childOwner, allUserRootChildren).Select(f => f.Id);

                        if (!itemCollectionFolderIds.Any(collectionFolderIds.Contains))
                        {
                            continue;
                        }
                    }
                }

                list.Add(child);
            }

            return list;
        }

        /// <summary>
        /// Gets the linked children.
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        public IEnumerable<Tuple<LinkedChild, BaseItem>> GetLinkedChildrenInfos()
        {
            return LinkedChildren
                .Select(i => new Tuple<LinkedChild, BaseItem>(i, GetLinkedChild(i)))
                .Where(i => i.Item2 != null);
        }

        [IgnoreDataMember]
        protected override bool SupportsOwnedItems
        {
            get
            {
                return base.SupportsOwnedItems || SupportsShortcutChildren;
            }
        }

        protected override async Task<bool> RefreshedOwnedItems(MetadataRefreshOptions options, List<FileSystemMetadata> fileSystemChildren, CancellationToken cancellationToken)
        {
            var changesFound = false;

            if (LocationType == LocationType.FileSystem)
            {
                if (RefreshLinkedChildren(fileSystemChildren))
                {
                    changesFound = true;
                }
            }

            var baseHasChanges = await base.RefreshedOwnedItems(options, fileSystemChildren, cancellationToken).ConfigureAwait(false);

            return baseHasChanges || changesFound;
        }

        /// <summary>
        /// Refreshes the linked children.
        /// </summary>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        protected virtual bool RefreshLinkedChildren(IEnumerable<FileSystemMetadata> fileSystemChildren)
        {
            if (SupportsShortcutChildren)
            {
                var newShortcutLinks = fileSystemChildren
                    .Where(i => !i.IsDirectory && FileSystem.IsShortcut(i.FullName))
                    .Select(i =>
                    {
                        try
                        {
                            Logger.Debug("Found shortcut at {0}", i.FullName);

                            var resolvedPath = FileSystem.ResolveShortcut(i.FullName);

                            if (!string.IsNullOrEmpty(resolvedPath))
                            {
                                return new LinkedChild
                                {
                                    Path = resolvedPath,
                                    Type = LinkedChildType.Shortcut
                                };
                            }

                            Logger.Error("Error resolving shortcut {0}", i.FullName);

                            return null;
                        }
                        catch (IOException ex)
                        {
                            Logger.ErrorException("Error resolving shortcut {0}", ex, i.FullName);
                            return null;
                        }
                    })
                    .Where(i => i != null)
                    .ToList();

                var currentShortcutLinks = LinkedChildren.Where(i => i.Type == LinkedChildType.Shortcut).ToList();

                if (!newShortcutLinks.SequenceEqual(currentShortcutLinks, new LinkedChildComparer(FileSystem)))
                {
                    Logger.Info("Shortcut links have changed for {0}", Path);

                    newShortcutLinks.AddRange(LinkedChildren.Where(i => i.Type == LinkedChildType.Manual));
                    LinkedChildren = newShortcutLinks.ToArray(newShortcutLinks.Count);
                    return true;
                }
            }

            foreach (var child in LinkedChildren)
            {
                // Reset the cached value
                child.ItemId = null;
            }

            return false;
        }

        /// <summary>
        /// Marks the played.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="datePlayed">The date played.</param>
        /// <param name="resetPosition">if set to <c>true</c> [reset position].</param>
        /// <returns>Task.</returns>
        public override void MarkPlayed(User user,
            DateTime? datePlayed,
            bool resetPosition)
        {
            var query = new InternalItemsQuery
            {
                User = user,
                Recursive = true,
                IsFolder = false,
                EnableTotalRecordCount = false
            };

            if (!user.Configuration.DisplayMissingEpisodes)
            {
                query.IsVirtualItem = false;
            }

            var itemsResult = GetItemList(query);

            // Sweep through recursively and update status
            foreach (var item in itemsResult)
            {
                if (item.IsVirtualItem)
                {
                    // The querying doesn't support virtual unaired
                    var episode = item as Episode;
                    if (episode != null && episode.IsUnaired)
                    {
                        continue;
                    }
                }

                item.MarkPlayed(user, datePlayed, resetPosition);
            }
        }

        /// <summary>
        /// Marks the unplayed.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>Task.</returns>
        public override void MarkUnplayed(User user)
        {
            var itemsResult = GetItemList(new InternalItemsQuery
            {
                User = user,
                Recursive = true,
                IsFolder = false,
                EnableTotalRecordCount = false

            });

            // Sweep through recursively and update status
            foreach (var item in itemsResult)
            {
                item.MarkUnplayed(user);
            }
        }

        public override bool IsPlayed(User user)
        {
            var itemsResult = GetItemList(new InternalItemsQuery(user)
            {
                Recursive = true,
                IsFolder = false,
                IsVirtualItem = false,
                EnableTotalRecordCount = false

            });

            return itemsResult
                .All(i => i.IsPlayed(user));
        }

        public override bool IsUnplayed(User user)
        {
            return !IsPlayed(user);
        }

        [IgnoreDataMember]
        public virtual bool SupportsUserDataFromChildren
        {
            get
            {
                // These are just far too slow. 
                if (this is ICollectionFolder)
                {
                    return false;
                }
                if (this is UserView)
                {
                    return false;
                }
                if (this is UserRootFolder)
                {
                    return false;
                }
                if (this is Channel)
                {
                    return false;
                }
                if (SourceType != SourceType.Library)
                {
                    return false;
                }
                var iItemByName = this as IItemByName;
                if (iItemByName != null)
                {
                    var hasDualAccess = this as IHasDualAccess;
                    if (hasDualAccess == null || hasDualAccess.IsAccessedByName)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public override void FillUserDataDtoValues(UserItemDataDto dto, UserItemData userData, BaseItemDto itemDto, User user, ItemFields[] fields)
        {
            if (!SupportsUserDataFromChildren)
            {
                return;
            }

            if (itemDto != null)
            {
                if (fields.Contains(ItemFields.RecursiveItemCount))
                {
                    itemDto.RecursiveItemCount = GetRecursiveChildCount(user);
                }
            }

            if (SupportsPlayedStatus)
            {
                var unplayedQueryResult = GetItems(new InternalItemsQuery(user)
                {
                    Recursive = true,
                    IsFolder = false,
                    IsVirtualItem = false,
                    EnableTotalRecordCount = true,
                    Limit = 0,
                    IsPlayed = false,
                    DtoOptions = new DtoOptions(false)
                    {
                        EnableImages = false
                    }

                });

                double unplayedCount = unplayedQueryResult.TotalRecordCount;

                dto.UnplayedItemCount = unplayedQueryResult.TotalRecordCount;

                if (itemDto != null && itemDto.RecursiveItemCount.HasValue)
                {
                    if (itemDto.RecursiveItemCount.Value > 0)
                    {
                        var unplayedPercentage = (unplayedCount / itemDto.RecursiveItemCount.Value) * 100;
                        dto.PlayedPercentage = 100 - unplayedPercentage;
                        dto.Played = dto.PlayedPercentage.Value >= 100;
                    }
                }
                else
                {
                    dto.Played = (dto.UnplayedItemCount ?? 0) == 0;
                }
            }
        }
    }
}