﻿using Serilog;
using Serilog.Formatting.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using TableCloth.Contracts;
namespace TableCloth.Implementations.WPF
{
    public sealed class WPFUserInterface : IAppUserInterface
    {
        public WPFUserInterface(
            IAppStartup appStartup,
            ICatalogDeserializer catalogDeserializer,
            IX509CertPairScanner certPairScanner,
            ISandboxBuilder sandboxBuilder,
            IAppMessageBox appMessageBox,
            ISandboxLauncher sandboxLauncher)
        {
            _appStartup = appStartup;
            _catalogDeserializer = catalogDeserializer;
            _certPairScanner = certPairScanner;
            _sandboxBuilder = sandboxBuilder;
            _appMessageBox = appMessageBox;
            _sandboxLauncher = sandboxLauncher;
        }

        private readonly IAppStartup _appStartup;
        private readonly ICatalogDeserializer _catalogDeserializer;
        private readonly IX509CertPairScanner _certPairScanner;
        private readonly ISandboxBuilder _sandboxBuilder;
        private readonly IAppMessageBox _appMessageBox;
        private readonly ISandboxLauncher _sandboxLauncher;

        private TableClothApp _appInstance;

        public object MainWindowHandle
            => _appInstance.MainWindow;

        public void StartApplication(IEnumerable<string> args)
        {
            var appThread = new Thread(new ParameterizedThreadStart(_ =>
            {
                Log.Logger = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .WriteTo.File(new JsonFormatter(), Path.Combine(_appStartup.AppDataDirectoryPath, "ApplicationLog.jsonl"))
                    .CreateLogger();

                _appInstance = new TableClothApp();
                _appInstance.Run();
            }));

            appThread.SetApartmentState(ApartmentState.STA);
            appThread.Start(args);
            appThread.Join();
        }
    }
}
