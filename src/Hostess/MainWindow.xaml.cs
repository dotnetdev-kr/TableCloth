﻿using Hostess.SiteList;
using Hostess.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Xml;
using System.Xml.Serialization;
using TableCloth.Resources;

namespace Hostess
{
    public partial class MainWindow : Window
    {
        public MainWindow()
            => InitializeComponent();

        private readonly string[] validAccountNames = new string[]
        {
            "ContainerAdministrator",
            "ContainerUser",
            "WDAGUtilityAccount",
        };

        private void SetDesktopWallpaper()
        {
            var picturesDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            if (!Directory.Exists(picturesDirectoryPath))
                Directory.CreateDirectory(picturesDirectoryPath);

            var wallpaperPath = Path.Combine(picturesDirectoryPath, "Signature.jpg");
            Properties.Resources.Signature.Save(wallpaperPath, ImageFormat.Jpeg);

            _ = NativeMethods.SystemParametersInfoW(
                NativeMethods.SetDesktopWallpaper, 0, wallpaperPath,
                NativeMethods.UpdateIniFile | NativeMethods.SendWinIniChange);

            NativeMethods.UpdatePerUserSystemParameters(
                IntPtr.Zero, IntPtr.Zero, "1, True", 0);
        }

        private void CheckWindowsContainerEnvironment()
        {
            if (validAccountNames.Contains(Environment.UserName, StringComparer.Ordinal))
                return;

            var questionMessage = (string)Application.Current.Resources["QuestionForNonSandboxEnvironment"];
            var questionTitle = (string)Application.Current.Resources["QuestionDialogTitle"];
            var result = MessageBox.Show(this, questionMessage, questionTitle, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                Close();
                return;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Width = MinWidth;
            Height = SystemParameters.PrimaryScreenHeight * 0.5;
            Top = (SystemParameters.PrimaryScreenHeight / 2) - (Height / 2);
            Left = SystemParameters.PrimaryScreenWidth - Width;

            CheckWindowsContainerEnvironment();
            SetDesktopWallpaper();

            var catalog = Application.Current.GetCatalogDocument();
            var targets = Application.Current.GetInstallSites();
            var packages = new List<InstallItemViewModel>();

            foreach (var eachTargetName in targets)
            {
                var targetService = catalog.Services.FirstOrDefault(x => string.Equals(eachTargetName, x.Id, StringComparison.Ordinal));

                if (targetService == null)
                {
                    continue;
                }

                packages.AddRange(targetService.Packages.Select(eachPackage => new InstallItemViewModel()
                {
                    TargetSiteName = targetService.DisplayName,
                    TargetSiteUrl = targetService.Url,
                    PackageName = eachPackage.Name,
                    PackageUrl = eachPackage.Url,
                    Arguments = eachPackage.Arguments,
                    SkipIEMode = eachPackage.SkipIEMode,
                    Installed = null,
                }));
            }

            InstallList.ItemsSource = new ObservableCollection<InstallItemViewModel>(packages);

            if (catalog.Services.Where(x => targets.Contains(x.Id)).Any(x => !string.IsNullOrWhiteSpace(x.CompatibilityNotes?.Trim())))
            {
                var window = new PrecautionsWindow();
                window.ShowDialog();
            }
            else
            {
                var peer = new ButtonAutomationPeer(PerformInstallButton);
                var invokeProv = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                invokeProv.Invoke();
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show(this,
                StringResources.AboutDialog_BodyText, StringResources.AppName,
                MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

        private async void PerformInstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ieModeRequiredDomainList = new List<string>();

                PerformInstallButton.IsEnabled = false;
                var hasAnyFailure = false;

                var downloadFolderPath = NativeMethods.GetKnownFolderPath(NativeMethods.DownloadFolderGuid);

                if (!Directory.Exists(downloadFolderPath))
                    Directory.CreateDirectory(downloadFolderPath);

                var catalog = Application.Current.GetCatalogDocument();

                foreach (var eachUrl in catalog.Services.Select(x => x.Url))
                {
                    if (!Uri.TryCreate(eachUrl, UriKind.Absolute, out Uri parsedUrl))
                        continue;

                    var rootDomain = RootDomainParser.InferenceRootDomain(parsedUrl);

                    if (string.IsNullOrWhiteSpace(rootDomain))
                        continue;

                    if (!ieModeRequiredDomainList.Contains(rootDomain, StringComparer.Ordinal))
                        ieModeRequiredDomainList.Add(rootDomain);
                }

                foreach (InstallItemViewModel eachItem in InstallList.ItemsSource)
                {
                    try
                    {
                        eachItem.Installed = null;
                        eachItem.StatusMessage = StringResources.Hostess_Download_InProgress;

                        if (eachItem.SkipIEMode &&
                            Uri.TryCreate(eachItem.TargetSiteUrl, UriKind.Absolute, out Uri parsedUrl))
                        {
                            var rootDomainName = RootDomainParser.InferenceRootDomain(parsedUrl);
                            ieModeRequiredDomainList.Remove(rootDomainName);
                        }

                        var tempFileName = $"installer_{Guid.NewGuid():n}.exe";
                        var tempFilePath = System.IO.Path.Combine(downloadFolderPath, tempFileName);
                        
                        if (File.Exists(tempFilePath))
                            File.Delete(tempFilePath);

                        using (var webClient = new WebClient())
                        {
                            webClient.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml");
                            webClient.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Trident/7.0; rv:11.0) like Gecko");
                            await webClient.DownloadFileTaskAsync(eachItem.PackageUrl, tempFilePath);

                            eachItem.StatusMessage = StringResources.Hostess_Install_InProgress;
                            var psi = new ProcessStartInfo(tempFilePath, eachItem.Arguments)
                            {
                                UseShellExecute = false,
                            };

                            var cpSource = new TaskCompletionSource<int>();
                            using (var process = new Process() { StartInfo = psi, })
                            {
                                process.EnableRaisingEvents = true;
                                process.Exited += (_sender, _e) =>
                                {
                                    var realSender = _sender as Process;
                                    cpSource.SetResult(realSender.ExitCode);
                                };

                                if (!process.Start())
                                    throw new ApplicationException(StringResources.HostessError_Package_CanNotStart);

                                await cpSource.Task;
                                eachItem.StatusMessage = StringResources.Hostess_Install_Succeed;
                                eachItem.Installed = true;
                                eachItem.ErrorMessage = null;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        hasAnyFailure = true;
                        eachItem.StatusMessage = StringResources.Hostess_Install_Failed;
                        eachItem.Installed = false;
                        eachItem.ErrorMessage = ex is AggregateException exception ? exception.InnerException.Message : ex.Message;
                        await Task.Delay(100);
                    }
                }

                var internetExplorerExists = File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Internet Explorer", "iexplore.exe"));

                if (internetExplorerExists)
                {
                    try
                    {
                        var ieSiteListPath = @"C:\ie_site_list.xml";

                        // HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge > InternetExplorerIntegrationLevel (REG_DWORD) with value 1
                        using (var ieModeKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge", true))
                        {
                            ieModeKey.SetValue("InternetExplorerIntegrationLevel", 1, RegistryValueKind.DWord);
                        }

                        // HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Internet Explorer\Main\EnterpriseMode > SiteList (REG_SZ) with path to your XML-sitelist
                        using (var ieModeKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Internet Explorer\Main\EnterpriseMode", true))
                        {
                            ieModeKey.SetValue("SiteList", ieSiteListPath);
                        }

                        // HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\MicrosoftEdge\Main\EnterpriseMode > SiteList (REG_SZ) with path to your XML-sitelist
                        using (var ieModeKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\MicrosoftEdge\Main\EnterpriseMode", true))
                        {
                            ieModeKey.SetValue("SiteList", ieSiteListPath);
                        }

                        // HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge > InternetExplorerIntegrationLevel (REG_DWORD) with value 1, InternetExplorerIntegrationSiteList (REG_SZ)
                        using (var ieModeKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge", true))
                        {
                            ieModeKey.SetValue("InternetExplorerIntegrationLevel", 1, RegistryValueKind.DWord);
                            ieModeKey.SetValue("InternetExplorerIntegrationSiteList", ieSiteListPath, RegistryValueKind.String);
                        }

                        var siteListDocument = new SiteListDocument();
                        siteListDocument.Sites.AddRange(ieModeRequiredDomainList.Select(x => new Site() { Url = x }));

                        var serializer = new XmlSerializer(typeof(SiteListDocument));
                        var @namespace = new XmlSerializerNamespaces(new[] { new XmlQualifiedName(string.Empty) });
                        var targetEncoding = new UTF8Encoding(false);

                        using (var fileStream = File.OpenWrite(ieSiteListPath))
                        {
                            var contentStream = new StreamWriter(fileStream);
                            serializer.Serialize(contentStream, siteListDocument, @namespace);
                        }

                        // IE 모드 목록이 바로 로딩되지 않아서 별도 사이트를 한 번 띄울 필요가 있음.
                        var process = Process.Start(new ProcessStartInfo("https://www.naver.com/")
                        {
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Maximized,
                        });
                        await Task.Delay(2000);
                        process.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.ToString());
                    }
                }

                if (!hasAnyFailure)
                {
                    var targets = Application.Current.GetInstallSites();

                    foreach (var eachUrl in catalog.Services.Where(x => targets.Contains(x.Id)).Select(x => x.Url))
                    {
                        Process.Start(new ProcessStartInfo(eachUrl)
                        {
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Maximized,
                        });
                    }

                    Close();
                    return;
                }
            }
            finally
            {
                PerformInstallButton.IsEnabled = true;
            }
        }
    }
}
