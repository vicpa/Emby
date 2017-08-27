﻿using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities
{
    /// <summary>
    /// Specialized folder that can have items added to it's children by external entities.
    /// Used for our RootFolder so plug-ins can add items.
    /// </summary>
    public class AggregateFolder : Folder
    {
        public AggregateFolder()
        {
            PhysicalLocationsList = EmptyStringArray;
        }

        [IgnoreDataMember]
        public override bool IsPhysicalRoot
        {
            get { return true; }
        }

        public override bool CanDelete()
        {
            return false;
        }

        [IgnoreDataMember]
        public override bool SupportsPlayedStatus
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// The _virtual children
        /// </summary>
        private readonly ConcurrentBag<BaseItem> _virtualChildren = new ConcurrentBag<BaseItem>();

        /// <summary>
        /// Gets the virtual children.
        /// </summary>
        /// <value>The virtual children.</value>
        public ConcurrentBag<BaseItem> VirtualChildren
        {
            get { return _virtualChildren; }
        }

        [IgnoreDataMember]
        public override string[] PhysicalLocations
        {
            get
            {
                return PhysicalLocationsList;
            }
        }

        public string[] PhysicalLocationsList { get; set; }

        protected override FileSystemMetadata[] GetFileSystemChildren(IDirectoryService directoryService)
        {
            return CreateResolveArgs(directoryService, true).FileSystemChildren;
        }

        private Guid[] _childrenIds = null;
        private readonly object _childIdsLock = new object();
        protected override List<BaseItem> LoadChildren()
        {
            lock (_childIdsLock)
            {
                if (_childrenIds == null || _childrenIds.Length == 0)
                {
                    var list = base.LoadChildren();
                    _childrenIds = list.Select(i => i.Id).ToArray();
                    return list;
                }

                return _childrenIds.Select(LibraryManager.GetItemById).Where(i => i != null).ToList();
            }
        }

        private void ClearCache()
        {
            lock (_childIdsLock)
            {
                _childrenIds = null;
            }
        }

        private bool _requiresRefresh;
        public override bool RequiresRefresh()
        {
            var changed = base.RequiresRefresh() || _requiresRefresh;

            if (!changed)
            {
                var locations = PhysicalLocations;

                var newLocations = CreateResolveArgs(new DirectoryService(Logger, FileSystem), false).PhysicalLocations;

                if (!locations.SequenceEqual(newLocations))
                {
                    changed = true;
                }
            }

            return changed;
        }

        public override bool BeforeMetadataRefresh()
        {
            ClearCache();

            var changed = base.BeforeMetadataRefresh() || _requiresRefresh;
            _requiresRefresh = false;
            return changed;
        }

        private ItemResolveArgs CreateResolveArgs(IDirectoryService directoryService, bool setPhysicalLocations)
        {
            ClearCache();

            var path = ContainingFolderPath;

            var args = new ItemResolveArgs(ConfigurationManager.ApplicationPaths, directoryService)
            {
                FileInfo = FileSystem.GetDirectoryInfo(path),
                Path = path,
                Parent = Parent
            };

            // Gather child folder and files
            if (args.IsDirectory)
            {
                var isPhysicalRoot = args.IsPhysicalRoot;

                // When resolving the root, we need it's grandchildren (children of user views)
                var flattenFolderDepth = isPhysicalRoot ? 2 : 0;

                var files = FileData.GetFilteredFileSystemEntries(directoryService, args.Path, FileSystem, Logger, args, flattenFolderDepth: flattenFolderDepth, resolveShortcuts: isPhysicalRoot || args.IsVf);

                // Need to remove subpaths that may have been resolved from shortcuts
                // Example: if \\server\movies exists, then strip out \\server\movies\action
                if (isPhysicalRoot)
                {
                    files = LibraryManager.NormalizeRootPathList(files).ToArray();
                }

                args.FileSystemChildren = files;
            }

            _requiresRefresh = _requiresRefresh || !args.PhysicalLocations.SequenceEqual(PhysicalLocations);
            if (setPhysicalLocations)
            {
                PhysicalLocationsList = args.PhysicalLocations;
            }

            return args;
        }

        protected override IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
        {
            return base.GetNonCachedChildren(directoryService).Concat(_virtualChildren);
        }

        protected override async Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
        {
            ClearCache();

            await base.ValidateChildrenInternal(progress, cancellationToken, recursive, refreshChildMetadata, refreshOptions, directoryService)
                .ConfigureAwait(false);

            ClearCache();
        }

        /// <summary>
        /// Adds the virtual child.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <exception cref="System.ArgumentNullException"></exception>
        public void AddVirtualChild(BaseItem child)
        {
            if (child == null)
            {
                throw new ArgumentNullException();
            }

            _virtualChildren.Add(child);
        }

        /// <summary>
        /// Finds the virtual child.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>BaseItem.</returns>
        /// <exception cref="System.ArgumentNullException">id</exception>
        public BaseItem FindVirtualChild(Guid id)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentNullException("id");
            }

            foreach (var child in _virtualChildren)
            {
                if (child.Id == id)
                {
                    return child;
                }
            }
            return null;
        }
    }
}
