using System;
using Chomyfin.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace Chomyfin.Services
{
    /// <summary>
    /// Registro central de servicios de Chomyfin.
    ///
    /// Nota técnica sobre Emby 4.10 SDK:
    /// Se verificó que el SDK de Emby no expone IDependencyModule ni registro
    /// de servicios propios en el contenedor DI del servidor.
    /// Emby instancia IMediaSourceProvider e IScheduledTask directamente mediante
    /// su contenedor conociendo únicamente las interfaces de Emby. Por ello,
    /// los componentes del plugin resuelven y comparten sus servicios a través
    /// de este registro central inicializado en el arranque del Plugin.
    /// </summary>
    internal static class ChomyfinServices
    {
        public static ILogger Logger { get; private set; }
        public static Func<ChomyfinOptions> OptionsAccessor { get; private set; }
        public static IChomikujService ChomikujService { get; private set; }
        public static StrmCacheService StrmCacheService { get; private set; }
        public static SyncStatusStore SyncStatusStore { get; } = new SyncStatusStore();
        public static SiblingFinder SiblingFinder { get; internal set; }
        public static SiblingResolutionQueue SiblingResolutionQueue { get; private set; }
        public static string PluginDataPath { get; private set; }
        public static MediaBrowser.Model.Globalization.ILocalizationManager LocalizationManager { get; set; }

        public static void Initialize(
            ILogger logger,
            Func<ChomyfinOptions> optionsAccessor,
            IFileSystem fileSystem,
            MediaBrowser.Controller.Library.ILibraryManager libraryManager = null,
            string pluginDataPath = null,
            MediaBrowser.Model.Globalization.ILocalizationManager localizationManager = null)
        {
            Logger = logger;
            OptionsAccessor = optionsAccessor;
            LocalizationManager = localizationManager;
            ChomikujService = new ChomikujService(logger, optionsAccessor);
            if (fileSystem != null)
            {
                StrmCacheService = new StrmCacheService(logger, fileSystem, () => ChomikujService);
            }
            if (fileSystem != null)
            {
                SiblingFinder = new SiblingFinder(logger, fileSystem, libraryManager);
            }
            SiblingResolutionQueue = new SiblingResolutionQueue(logger, () => StrmCacheService, () => ChomikujService);
            PluginDataPath = pluginDataPath;
        }

        public static void EnsureServices(IFileSystem fileSystem, MediaBrowser.Controller.Library.ILibraryManager libraryManager = null)
        {
            if (Logger == null) return;

            if (StrmCacheService == null && fileSystem != null)
            {
                StrmCacheService = new StrmCacheService(Logger, fileSystem, () => ChomikujService);
            }

            if (SiblingFinder == null && fileSystem != null)
            {
                SiblingFinder = new SiblingFinder(Logger, fileSystem, libraryManager);
            }

            if (SiblingResolutionQueue == null)
            {
                SiblingResolutionQueue = new SiblingResolutionQueue(Logger, () => StrmCacheService, () => ChomikujService);
            }
        }

        public static void EnsureStrmCacheService(IFileSystem fileSystem)
        {
            EnsureServices(fileSystem, null);
        }

        internal static void ResetForTests()
        {
            Logger = null;
            OptionsAccessor = null;
            ChomikujService = null;
            StrmCacheService = null;
            SiblingFinder = null;
            SiblingResolutionQueue?.ResetInstanceForTests();
            SiblingResolutionQueue = null;
            SiblingResolutionQueue.ResetForTests();
            PluginDataPath = null;
            LocalizationManager = null;
            SyncStatusStore.ResetForTests();
        }
    }
}
