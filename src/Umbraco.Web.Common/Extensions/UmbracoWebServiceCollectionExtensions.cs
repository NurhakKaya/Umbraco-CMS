﻿using System.Buffers;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Web.Caching;
using SixLabors.ImageSharp.Web.Commands;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Processors;
using SixLabors.ImageSharp.Web.Providers;
using Smidge;
using Smidge.Nuglify;
using Umbraco.Core;
using Umbraco.Core.Configuration;
using Umbraco.Web.Common.ApplicationModels;
using Umbraco.Web.Common.ModelBinding;

namespace Umbraco.Extensions
{
    public static class UmbracoWebServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the web components needed for Umbraco
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddUmbracoWebComponents(this IServiceCollection services)
        {
            services.TryAddSingleton<UmbracoJsonModelBinder>();
            services.TryAddSingleton<UmbracoJsonModelBinderProvider>();
            services.TryAddSingleton<UmbracoJsonModelBinderFactory>();
            //services.ConfigureOptions<UmbracoMvcConfigureOptions>();
            services.TryAddEnumerable(ServiceDescriptor.Transient<IApplicationModelProvider, UmbracoApiBehaviorApplicationModelProvider>());

            // TODO: We need to avoid this, surely there's a way? See ContainerTests.BuildServiceProvider_Before_Host_Is_Configured
            var serviceProvider = services.BuildServiceProvider();
            var configs = serviceProvider.GetService<Configs>();
            var imagingSettings = configs.Imaging();
            services.AddUmbracoImageSharp(imagingSettings);

            return services;
        }

        /// <summary>
        /// Adds Image Sharp with Umbraco settings
        /// </summary>
        /// <param name="services"></param>
        /// <param name="imagingSettings"></param>
        /// <returns></returns>
        public static IServiceCollection AddUmbracoImageSharp(this IServiceCollection services, IImagingSettings imagingSettings)
        {
            services.AddImageSharpCore(
                    options =>
                    {
                        options.Configuration = SixLabors.ImageSharp.Configuration.Default;
                        options.MaxBrowserCacheDays = imagingSettings.MaxBrowserCacheDays;
                        options.MaxCacheDays = imagingSettings.MaxCacheDays;
                        options.CachedNameLength = imagingSettings.CachedNameLength;
                        options.OnParseCommands = context =>
                        {
                            RemoveIntParamenterIfValueGreatherThen(context.Commands, ResizeWebProcessor.Width, imagingSettings.MaxResizeWidth);
                            RemoveIntParamenterIfValueGreatherThen(context.Commands, ResizeWebProcessor.Height, imagingSettings.MaxResizeHeight);
                        };
                        options.OnBeforeSave = _ => { };
                        options.OnProcessed = _ => { };
                        options.OnPrepareResponse = _ => { };
                    })
                .SetRequestParser<QueryCollectionRequestParser>()
                .SetMemoryAllocator(provider => ArrayPoolMemoryAllocator.CreateWithMinimalPooling())
                .Configure<PhysicalFileSystemCacheOptions>(options =>
                {
                    options.CacheFolder = imagingSettings.CacheFolder;
                })
                .SetCache<PhysicalFileSystemCache>()
                .SetCacheHash<CacheHash>()
                .AddProvider<PhysicalFileSystemProvider>()
                .AddProcessor<ResizeWebProcessor>()
                .AddProcessor<FormatWebProcessor>()
                .AddProcessor<BackgroundColorWebProcessor>();

            return services;
        }

        /// <summary>
        /// Adds the Umbraco runtime minifier
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static IServiceCollection AddUmbracoRuntimeMinifier(this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSmidge(configuration.GetSection(Core.Constants.Configuration.ConfigRuntimeMinification));
            services.AddSmidgeNuglify();

            return services;
        }

        private static void RemoveIntParamenterIfValueGreatherThen(IDictionary<string, string> commands, string parameter, int maxValue)
        {
            if (commands.TryGetValue(parameter, out var command))
            {
                if (int.TryParse(command, out var i))
                {
                    if (i > maxValue)
                    {
                        commands.Remove(parameter);
                    }
                }
            }
        }

        /// <summary>
        /// Options for configuring MVC 
        /// </summary>
        /// <remarks>
        /// We generally don't want to change the global MVC settings since we want to be unobtrusive as possible but some
        /// global mods are needed - so long as they don't interfere with normal user usages of MVC.
        /// </remarks>
        private class UmbracoMvcConfigureOptions : IConfigureOptions<MvcOptions>
        {
            private readonly IHttpRequestStreamReaderFactory _readerFactory;
            private readonly ILoggerFactory _logger;
            private readonly ArrayPool<char> _arrayPool;
            private readonly ObjectPoolProvider _objectPoolProvider;

            public UmbracoMvcConfigureOptions(IHttpRequestStreamReaderFactory readerFactory, ILoggerFactory logger, ArrayPool<char> arrayPool, ObjectPoolProvider objectPoolProvider)
            {
                _readerFactory = readerFactory;
                _logger = logger;
                _arrayPool = arrayPool;
                _objectPoolProvider = objectPoolProvider;
            }

            public void Configure(MvcOptions options)
            {                
                options.ModelBinderProviders.Insert(0, new UmbracoJsonModelBinderProvider(_readerFactory, _logger, _arrayPool, _objectPoolProvider));
            }
        }


    }

}
