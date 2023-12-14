﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using TableCloth.Components;
using TableCloth.Contracts;
using TableCloth.Models;
using TableCloth.Models.Catalog;
using TableCloth.Models.Configuration;
using TableCloth.Models.WindowsSandbox;
using TableCloth.Resources;
using TableCloth.ViewModels;

namespace TableCloth.Pages
{
    /// <summary>
    /// Interaction logic for DetailPage.xaml
    /// </summary>
    public partial class DetailPage : Page, IPageArgument<DetailPageArgumentModel>
    {
        public DetailPage()
        {
            InitializeComponent();
        }

        public DetailPageViewModel ViewModel
            => (DetailPageViewModel)DataContext;

        public DetailPageArgumentModel Arguments { get; set; } = default;

        private string ComposeCommandLineArguments()
        {
            var options = new List<string>();

            if (ViewModel.EnableMicrophone)
                options.Add(StringResources.TableCloth_Switch_EnableMicrophone);
            if (ViewModel.EnableWebCam)
                options.Add(StringResources.TableCloth_Switch_EnableCamera);
            if (ViewModel.EnablePrinters)
                options.Add(StringResources.TableCloth_Switch_EnablePrinter);
            if (ViewModel.InstallEveryonesPrinter)
                options.Add(StringResources.TableCloth_Switch_InstallEveryonesPrinter);
            if (ViewModel.InstallAdobeReader)
                options.Add(StringResources.TableCloth_Switch_InstallAdobeReader);
            if (ViewModel.InstallHancomOfficeViewer)
                options.Add(StringResources.TableCloth_Switch_InstallHancomOfficeViewer);
            if (ViewModel.InstallRaiDrive)
                options.Add(StringResources.TableCloth_Switch_InstallRaiDrive);
            if (ViewModel.EnableInternetExplorerMode)
                options.Add(StringResources.TableCloth_Switch_EnableIEMode);
            if (ViewModel.MapNpkiCert)
                options.Add(StringResources.Tablecloth_Switch_EnableCert);

            var firstSite = ViewModel.SelectedService;

            if (firstSite != null)
                options.Add(firstSite.Id);

            return string.Join(' ', options.ToArray());
        }

        private void RunSandbox(TableClothConfiguration config)
        {
            if (config.CertPair != null)
            {
                var now = DateTime.Now;
                var expireWindow = StringResources.Cert_ExpireWindow;

                if (now < config.CertPair.NotBefore)
                    ViewModel.AppMessageBox.DisplayError(StringResources.Error_Cert_MayTooEarly(now, config.CertPair.NotBefore), false);

                if (now > config.CertPair.NotAfter)
                    ViewModel.AppMessageBox.DisplayError(StringResources.Error_Cert_Expired, false);
                else if (now > config.CertPair.NotAfter.Add(expireWindow))
                    ViewModel.AppMessageBox.DisplayInfo(StringResources.Error_Cert_ExpireSoon(now, config.CertPair.NotAfter, expireWindow));
            }

            var tempPath = ViewModel.SharedLocations.GetTempPath();
            var excludedFolderList = new List<SandboxMappedFolder>();
            var wsbFilePath = ViewModel.SandboxBuilder.GenerateSandboxConfiguration(tempPath, config, excludedFolderList);

            if (excludedFolderList.Any())
                ViewModel.AppMessageBox.DisplayError(StringResources.Error_HostFolder_Unavailable(excludedFolderList.Select(x => x.HostFolder)), false);

            ViewModel.SandboxCleanupManager.SetWorkingDirectory(tempPath);
            ViewModel.SandboxLauncher.RunSandbox(wsbFilePath);
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedService = Arguments.SelectedService;

            var currentConfig = ViewModel.PreferencesManager.LoadPreferences();

            if (currentConfig == null)
                currentConfig = ViewModel.PreferencesManager.GetDefaultPreferences();

            ViewModel.EnableLogAutoCollecting = currentConfig.UseLogCollection;
            ViewModel.V2UIOptIn = currentConfig.V2UIOptIn;
            ViewModel.EnableMicrophone = currentConfig.UseAudioRedirection;
            ViewModel.EnableWebCam = currentConfig.UseVideoRedirection;
            ViewModel.EnablePrinters = currentConfig.UsePrinterRedirection;
            ViewModel.InstallEveryonesPrinter = currentConfig.InstallEveryonesPrinter;
            ViewModel.InstallAdobeReader = currentConfig.InstallAdobeReader;
            ViewModel.InstallHancomOfficeViewer = currentConfig.InstallHancomOfficeViewer;
            ViewModel.InstallRaiDrive = currentConfig.InstallRaiDrive;
            ViewModel.EnableInternetExplorerMode = currentConfig.EnableInternetExplorerMode;
            ViewModel.LastDisclaimerAgreedTime = currentConfig.LastDisclaimerAgreedTime;

            var foundCandidate = ViewModel.CertPairScanner.ScanX509Pairs(ViewModel.CertPairScanner.GetCandidateDirectories()).FirstOrDefault();

            if (foundCandidate != null)
            {
                ViewModel.SelectedCertFile = foundCandidate;
                ViewModel.MapNpkiCert = true;
            }

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            if (ViewModel.ShouldNotifyDisclaimer)
            {
                var disclaimerWindow = new DisclaimerWindow() { Owner = Window.GetWindow(this), };
                var result = disclaimerWindow.ShowDialog();

                if (result.HasValue && result.Value)
                    ViewModel.LastDisclaimerAgreedTime = DateTime.UtcNow;
            }

            if (Arguments.BuiltFromCommandLine)
                RunSandbox(Arguments.GetTableClothConfiguration());

            if (!string.IsNullOrEmpty(Arguments.CurrentSearchString))
            {
                SiteCatalogFilter.Text = Arguments.CurrentSearchString;
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var currentConfig = ViewModel.PreferencesManager.LoadPreferences();

            if (currentConfig == null)
                currentConfig = ViewModel.PreferencesManager.GetDefaultPreferences();

            var window = Window.GetWindow(this);

            switch (e.PropertyName)
            {
                case nameof(MainWindowViewModel.EnableLogAutoCollecting):
                    currentConfig.UseLogCollection = ViewModel.EnableLogAutoCollecting;
                    if (ViewModel.AppRestartManager.AskRestart())
                    {
                        ViewModel.AppRestartManager.ReserveRestart = true;
                        window?.Close();
                    }
                    break;

                case nameof(MainWindowViewModel.V2UIOptIn):
                    currentConfig.V2UIOptIn = ViewModel.V2UIOptIn;
                    if (ViewModel.AppRestartManager.AskRestart())
                    {
                        ViewModel.AppRestartManager.ReserveRestart = true;
                        window?.Close();
                    }
                    break;

                case nameof(MainWindowViewModel.EnableMicrophone):
                    currentConfig.UseAudioRedirection = ViewModel.EnableMicrophone;
                    break;

                case nameof(MainWindowViewModel.EnableWebCam):
                    currentConfig.UseVideoRedirection = ViewModel.EnableWebCam;
                    break;

                case nameof(MainWindowViewModel.EnablePrinters):
                    currentConfig.UsePrinterRedirection = ViewModel.EnablePrinters;
                    break;

                case nameof(MainWindowViewModel.InstallEveryonesPrinter):
                    currentConfig.InstallEveryonesPrinter = ViewModel.InstallEveryonesPrinter;
                    break;

                case nameof(MainWindowViewModel.InstallAdobeReader):
                    currentConfig.InstallAdobeReader = ViewModel.InstallAdobeReader;
                    break;

                case nameof(MainWindowViewModel.InstallHancomOfficeViewer):
                    currentConfig.InstallHancomOfficeViewer = ViewModel.InstallHancomOfficeViewer;
                    break;

                case nameof(MainWindowViewModel.InstallRaiDrive):
                    currentConfig.InstallRaiDrive = ViewModel.InstallRaiDrive;
                    break;

                case nameof(MainWindowViewModel.EnableInternetExplorerMode):
                    currentConfig.EnableInternetExplorerMode = ViewModel.EnableInternetExplorerMode;
                    break;

                case nameof(MainWindowViewModel.LastDisclaimerAgreedTime):
                    currentConfig.LastDisclaimerAgreedTime = ViewModel.LastDisclaimerAgreedTime;
                    break;

                default:
                    return;
            }

            ViewModel.PreferencesManager.SavePreferences(currentConfig);
        }

        private void GoBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SandboxLauncher.IsSandboxRunning())
            {
                ViewModel.AppMessageBox.DisplayError(StringResources.Error_Windows_Sandbox_Already_Running, false);
                return;
            }

            var selectedCert = ViewModel.SelectedCertFile;

            if (!ViewModel.MapNpkiCert)
                selectedCert = null;

            var config = new TableClothConfiguration()
            {
                CertPair = selectedCert,
                EnableMicrophone = ViewModel.EnableMicrophone,
                EnableWebCam = ViewModel.EnableWebCam,
                EnablePrinters = ViewModel.EnablePrinters,
                InstallEveryonesPrinter = ViewModel.InstallEveryonesPrinter,
                InstallAdobeReader = ViewModel.InstallAdobeReader,
                InstallHancomOfficeViewer = ViewModel.InstallHancomOfficeViewer,
                InstallRaiDrive = ViewModel.InstallRaiDrive,
                EnableInternetExplorerMode = ViewModel.EnableInternetExplorerMode,
                Companions = new CatalogCompanion[] { }, /*ViewModel.CatalogDocument.Companions*/
                Services = new[] { ViewModel.SelectedService, },
            };

            RunSandbox(config);
        }

        private void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
        {
            var targetPath = ViewModel.SharedLocations.ExecutableFilePath;
            var linkName = StringResources.AppName;

            var firstSite = ViewModel.SelectedService;
            var iconFilePath = default(string);

            if (firstSite != null)
            {
                linkName = firstSite.DisplayName;

                iconFilePath = Path.Combine(
                    ViewModel.SharedLocations.GetImageDirectoryPath(),
                    $"{firstSite.Id}.ico");

                if (!File.Exists(iconFilePath))
                    iconFilePath = null;
            }

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var fullPath = Path.Combine(desktopPath, linkName + ".lnk");

            for (int i = 1; File.Exists(fullPath); ++i)
                fullPath = Path.Combine(desktopPath, linkName + $" ({i}).lnk");

            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(fullPath);
                shortcut.TargetPath = targetPath;

                if (iconFilePath != null && File.Exists(iconFilePath))
                    shortcut.IconLocation = iconFilePath;

                shortcut.Arguments = ComposeCommandLineArguments();
                shortcut.Save();
            }
            catch
            {
                ViewModel.AppMessageBox.DisplayInfo(StringResources.Error_ShortcutFailed);
                return;
            }

            ViewModel.AppMessageBox.DisplayInfo(StringResources.Info_ShortcutSuccess);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var certSelectWindow = new CertSelectWindow() { Owner = Window.GetWindow(this) };
            var response = certSelectWindow.ShowDialog();

            if (!response.HasValue || !response.Value)
                return;

            if (certSelectWindow.ViewModel.SelectedCertPair != null)
                ViewModel.SelectedCertFile = certSelectWindow.ViewModel.SelectedCertPair;
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Hyperlink link)
                return;

            if (!Uri.TryCreate(link.Tag?.ToString(), UriKind.Absolute, out Uri uri) ||
                uri == null)
                return;

            Process.Start(new ProcessStartInfo(uri.ToString())
            {
                UseShellExecute = true,
            });
        }

        private void SiteCatalogFilter_LostFocus(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(
                new Uri("Pages/CatalogPage.xaml", UriKind.Relative),
                new CatalogPageArgumentModel(SiteCatalogFilter.Text));
        }

        // https://stackoverflow.com/questions/660554/how-to-automatically-select-all-text-on-focus-in-wpf-textbox
        private void SiteCatalogFilter_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Fixes issue when clicking cut/copy/paste in context menu
            if (SiteCatalogFilter.SelectionLength < 1)
                SiteCatalogFilter.SelectAll();
        }

        private void CopyCommandLineButton_Click(object sender, RoutedEventArgs e)
        {
            var targetFilePath = ViewModel.SharedLocations.ExecutableFilePath;
            var args = ComposeCommandLineArguments();
            var expression = $"\"{targetFilePath}\" {args}";

            Clipboard.SetText(expression);

            ViewModel.AppMessageBox.DisplayInfo(
                @$"자동화 작업 실행 시 사용할 수 있는 명령어를 클립보드에 복사했으며, 명령어는 다음과 같습니다: 

{expression}",
                MessageBoxButton.OK);
        }

        private void SiteCatalogFilter_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape || e.Key == Key.Tab)
            {
                NavigationService.Navigate(
                    new Uri("Pages/CatalogPage.xaml", UriKind.Relative),
                    new CatalogPageArgumentModel(SiteCatalogFilter.Text));
            }
        }
    }
}
