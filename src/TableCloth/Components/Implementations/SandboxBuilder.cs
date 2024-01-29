﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using TableCloth.Models.Catalog;
using TableCloth.Models.Configuration;
using TableCloth.Models.WindowsSandbox;
using TableCloth.Resources;

namespace TableCloth.Components;

public sealed class SandboxBuilder(
    ISharedLocations sharedLocations) : ISandboxBuilder
{
    private readonly string _wdagUtilityAccountPath = @"C:\Users\WDAGUtilityAccount";

    private string GetAssetsPathForSandbox()
        => Path.Combine(_wdagUtilityAccountPath, "Desktop", "Assets");

    private string GetNPKIPathForSandbox(X509CertPair certPair)
    {
        var candidatePath = Path.Join("AppData", "LocalLow", "NPKI", certPair.Organization);

        if (certPair.IsPersonalCert)
            candidatePath = Path.Join(candidatePath, "USER", certPair.SubjectNameForNpkiApp);

        return Path.Join(_wdagUtilityAccountPath, candidatePath);
    }

    public async Task<string> GenerateSandboxConfigurationAsync(
        string outputDirectory,
        TableClothConfiguration tableClothConfiguration,
        IList<SandboxMappedFolder> excludedDirectories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tableClothConfiguration);

        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
        var hostessZipFilePath = Path.Combine(sharedLocations.ExecutableDirectoryPath, "Hostess.zip");
        await ExpandAssetZipAsync(hostessZipFilePath, outputDirectory, cancellationToken).ConfigureAwait(false);

        var assetsDirectory = Path.Combine(outputDirectory, "assets");
        if (!Directory.Exists(assetsDirectory))
            Directory.CreateDirectory(assetsDirectory);

        var batchFileContent = GenerateSandboxStartupScript(tableClothConfiguration);
        var batchFilePath = Path.Combine(assetsDirectory, "StartupScript.cmd");
        await File.WriteAllTextAsync(batchFilePath, batchFileContent, Encoding.Default, cancellationToken).ConfigureAwait(false);

        tableClothConfiguration.AssetsDirectoryPath = assetsDirectory;

        var wsbFilePath = Path.Combine(outputDirectory, "InternetBankingSandbox.wsb");
        var serializedXml = SerializeSandboxSpec(
            await BootstrapSandboxConfigurationAsync(tableClothConfiguration, cancellationToken).ConfigureAwait(false),
            excludedDirectories);
        await File.WriteAllTextAsync(wsbFilePath, serializedXml, cancellationToken).ConfigureAwait(false);

        return wsbFilePath;
    }

    private async Task<SandboxConfiguration> BootstrapSandboxConfigurationAsync(
        TableClothConfiguration tableClothConfig,
        CancellationToken cancellationToken = default)
    {
        const string Enable = "Enable";
        const string Disable = "Disable";

        var sandboxConfig = new SandboxConfiguration
        {
            AudioInput = tableClothConfig.EnableMicrophone ? Enable : Disable,
            VideoInput = tableClothConfig.EnableWebCam ? Enable : Disable,
            PrinterRedirection = tableClothConfig.EnablePrinters ? Enable : Disable,
        };

        if (!Directory.Exists(tableClothConfig.AssetsDirectoryPath))
            return sandboxConfig;

        sandboxConfig.MappedFolders.Clear();

        sandboxConfig.MappedFolders.Add(new SandboxMappedFolder
        {
            HostFolder = tableClothConfig.AssetsDirectoryPath,
            ReadOnly = bool.FalseString,
        });

        if (tableClothConfig.CertPair != null)
        {
            var certAssetsDirectoryPath = Path.Combine(tableClothConfig.AssetsDirectoryPath, "certs");
            if (!Directory.Exists(certAssetsDirectoryPath))
                Directory.CreateDirectory(certAssetsDirectoryPath);

            var destDerFilePath = Path.Combine(certAssetsDirectoryPath, "signCert.der");
            var destKeyFileName = Path.Combine(certAssetsDirectoryPath, "signPri.key");

            await File.WriteAllBytesAsync(destDerFilePath, tableClothConfig.CertPair.PublicKey, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(destKeyFileName, tableClothConfig.CertPair.PrivateKey, cancellationToken).ConfigureAwait(false);

            sandboxConfig.MappedFolders.Add(new SandboxMappedFolder
            {
                HostFolder = certAssetsDirectoryPath,
                ReadOnly = bool.TrueString,
            });
        }

        sandboxConfig.LogonCommand.Add(Path.Combine(GetAssetsPathForSandbox(), "StartupScript.cmd"));
        return sandboxConfig;
    }

    private string GenerateSandboxStartupScript(TableClothConfiguration tableClothConfiguration)
    {
        ArgumentNullException.ThrowIfNull(tableClothConfiguration);

        var certFileCopyScript = string.Empty;

        if (tableClothConfiguration.CertPair != null)
        {
            var npkiDirectoryPathInSandbox = GetNPKIPathForSandbox(tableClothConfiguration.CertPair);
            var desktopDirectoryPathInSandbox = "%userprofile%\\Desktop\\Certificates";
            var providedCertFilePath = Path.Combine(GetAssetsPathForSandbox(), "certs", "*.*");
            certFileCopyScript = $@"
if not exist ""{npkiDirectoryPathInSandbox}"" mkdir ""{npkiDirectoryPathInSandbox}""
copy /y ""{providedCertFilePath}"" ""{npkiDirectoryPathInSandbox}""
if not exist ""{desktopDirectoryPathInSandbox}"" mkdir ""{desktopDirectoryPathInSandbox}""
copy /y ""{providedCertFilePath}"" ""{desktopDirectoryPathInSandbox}""
del /f /q ""{providedCertFilePath}""
";
        }

        var switches = new List<string>();

        if (tableClothConfiguration.InstallEveryonesPrinter)
            switches.Add(ConstantStrings.TableCloth_Switch_InstallEveryonesPrinter);

        if (tableClothConfiguration.InstallAdobeReader)
            switches.Add(ConstantStrings.TableCloth_Switch_InstallAdobeReader);

        if (tableClothConfiguration.InstallHancomOfficeViewer)
            switches.Add(ConstantStrings.TableCloth_Switch_InstallHancomOfficeViewer);

        if (tableClothConfiguration.InstallRaiDrive)
            switches.Add(ConstantStrings.TableCloth_Switch_InstallRaiDrive);

        if (tableClothConfiguration.EnableInternetExplorerMode)
            switches.Add(ConstantStrings.TableCloth_Switch_EnableIEMode);

        var serviceIdList = (tableClothConfiguration.Services ?? Enumerable.Empty<CatalogInternetService>())
            .Select(x => x.Id).Distinct();
        var hostessFilePath = Path.Combine(GetAssetsPathForSandbox(), "Hostess.exe");
        var idList = string.Join(" ", serviceIdList);

        return $@"@echo off
pushd ""%~dp0""
{certFileCopyScript}
""{hostessFilePath}"" {idList} {string.Join(" ", switches)}
:exit
popd
@echo on
";
    }

    private static async Task ExpandAssetZipAsync(string zipFilePath, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var assetsDirectory = Path.Combine(outputDirectory, "assets");
        if (!Directory.Exists(assetsDirectory))
            Directory.CreateDirectory(assetsDirectory);

        // https://stackoverflow.com/a/17755048
        await Task.Run(() => ZipFile.ExtractToDirectory(zipFilePath, assetsDirectory, true), cancellationToken);
    }

    private static string SerializeSandboxSpec(SandboxConfiguration configuration, IList<SandboxMappedFolder> excludedFolders)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var unavailableDirectories = configuration.MappedFolders
            .Where(x => !Directory.Exists(x.HostFolder));

        configuration.MappedFolders.RemoveAll(x => unavailableDirectories.Contains(x));

        if (excludedFolders != null)
            foreach (var eachDirectory in unavailableDirectories)
                excludedFolders.Add(eachDirectory);

        var serializer = new XmlSerializer(typeof(SandboxConfiguration));
        var @namespace = new XmlSerializerNamespaces([new XmlQualifiedName(string.Empty)]);
        var targetEncoding = new UTF8Encoding(false);

        using var memStream = new MemoryStream();
        var contentStream = new StreamWriter(memStream);
        serializer.Serialize(contentStream, configuration, @namespace);
        return targetEncoding.GetString(memStream.ToArray());
    }
}
