﻿using MediaBrowser.Model.IO;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.IO;

namespace MediaBrowser.Providers.Omdb
{
    public class OmdbImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClient _httpClient;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _configurationManager;

        public OmdbImageProvider(IJsonSerializer jsonSerializer, IHttpClient httpClient, IFileSystem fileSystem, IServerConfigurationManager configurationManager)
        {
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            _fileSystem = fileSystem;
            _configurationManager = configurationManager;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new List<ImageType>
            {
                ImageType.Primary
            };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var imdbId = item.GetProviderId(MetadataProviders.Imdb);

            var list = new List<RemoteImageInfo>();

            var provider = new OmdbProvider(_jsonSerializer, _httpClient, _fileSystem, _configurationManager);

            if (!string.IsNullOrWhiteSpace(imdbId))
            {
                OmdbProvider.RootObject rootObject = await provider.GetRootObject(imdbId, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(rootObject.Poster))
                {
                    if (item is Episode)
                    {
                        // img.omdbapi.com returning 404's
                        list.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Url = rootObject.Poster
                        });
                    }
                    else
                    {
                        list.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Url = string.Format("https://img.omdbapi.com/?i={0}&apikey=fe53f97e", imdbId)
                        });
                    }
                }
            }

            return list;
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClient.GetResponse(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url
            });
        }

        public string Name
        {
            get { return "The Open Movie Database"; }
        }

        public bool Supports(BaseItem item)
        {
            return item is Movie || item is Trailer || item is Episode;
        }

        public int Order
        {
            get
            {
                // After other internet providers, because they're better
                // But before fallback providers like screengrab
                return 90;
            }
        }
    }
}
