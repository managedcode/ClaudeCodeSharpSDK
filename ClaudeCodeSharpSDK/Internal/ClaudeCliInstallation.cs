using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Internal;

internal static class ClaudeCliInstallation
{
    private const string PackageName = "@anthropic-ai/claude-code";
    private const string PackageScopeDirectoryName = "@anthropic-ai";
    private const string PackageDirectoryName = "claude-code";
    private const string BinDirectoryName = ".bin";
    private const string NodeModulesDirectoryName = "node_modules";
    private const string MarkerFileName = ".managedcode-cli-installation";
    private const string MarkerValue = "ManagedCode.ClaudeCodeSharpSDK";
    private const string ManagedCodeDirectoryName = "ManagedCode";
    private const string SdkDirectoryName = "ManagedCode.ClaudeCodeSharpSDK";
    private const string InstallDirectoryName = "cli";
    private const string NpmCommand = "install";
    private const string BunCommand = "install";
    private const string CwdOption = "--cwd";
    private const string PrefixOption = "--prefix";
    private const string NoSaveOption = "--no-save";
    private const string NoAuditOption = "--no-audit";
    private const string NoFundOption = "--no-fund";
    private const string NoPackageLockOption = "--package-lock=false";
    private const string VersionMismatchMessage = "The installed Claude Code CLI version does not match the SDK compatibility target.";
    private const string RootNotEmptyMessage = "The installation root is not an SDK-owned Claude Code CLI installation.";
    private const string MarkerMismatchMessage = "The installation root belongs to a different SDK-managed CLI installation.";
    private const string InvalidManagerMessage = "The package-manager executable and script configuration is invalid.";
    private const string LocalApplicationDataRootMessage = "The local application-data root must be an absolute path.";
    private const string LockTimeoutMessage = "The CLI installation root remained busy past its configured lock timeout.";
    private const string PackageSeparator = "@";
    private const string NodeExecutableName = "node";
    private const string NodeWindowsExecutableName = "node.exe";
    private const string BunExecutableName = "bun";
    private const string BunWindowsExecutableName = "bun.exe";
    private const string NpmCliEntryPoint = "npm-cli.js";
    private const string NpmBinDirectoryName = "bin";
    private const string NpmDirectoryName = "npm";
    private const string NpmManifestName = "package.json";
    private const string InstallLockFileName = ".install.lock";
    private const string NpmNameProperty = "name";
    private const string NpmPackageName = "npm";
    private const string VersionPropertyName = "version";
    private const int LockRetryMilliseconds = 25;
    internal static async IAsyncEnumerable<CliInstallationUpdate> InstallOrUpdateAsync(
        CliInstallationOptions options,
        string localApplicationDataRoot,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataRoot);
        if (!Path.IsPathFullyQualified(localApplicationDataRoot))
        {
            throw new ArgumentException(LocalApplicationDataRootMessage, nameof(localApplicationDataRoot));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var root = ValidateOptions(options, localApplicationDataRoot);
        ValidatePackageManager(options);
        CliInstallationEnvironment.Validate(options.EnvironmentVariables);
        await using var rootLock = await AcquireRootLockAsync(root, options.InstallationLockTimeout, cancellationToken)
            .ConfigureAwait(false);
        EnsureOwnedRoot(root);
        var processEnvironment = CliInstallationEnvironment.Create(options.EnvironmentVariables, root);
        var manager = ResolvePackageManager(options);
        var packageVersion = PackageName + PackageSeparator + ClaudeCliCompatibility.TargetVersion;
        var arguments = options.PackageManager switch
        {
            CliPackageManager.Npm => new List<string>
            {
                NpmCommand, PrefixOption, root, NoSaveOption, NoAuditOption, NoFundOption, NoPackageLockOption, packageVersion
            },
            CliPackageManager.Bun => new List<string>
            {
                BunCommand, CwdOption, root, NoSaveOption, packageVersion
            },
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.PackageManager, InvalidManagerMessage)
        };
        await foreach (var update in CliInstallationProcessRunner.RunAsync(manager, arguments, processEnvironment, root,
                           options.InstallTimeout, options.ProcessTerminationTimeout, options.MaximumOutputCharacters,
                           cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }

        yield return new CliInstallationUpdate(CliInstallationStage.Verifying, 0, 0);
        var cliShim = Path.Combine(root, NodeModulesDirectoryName, BinDirectoryName,
            OperatingSystem.IsWindows() ? ClaudeCliLocator.ClaudeWindowsCommandName : ClaudeCliLocator.ClaudeExecutableName);
        var launch = ClaudeCliLocator.FindClaudeCommand(cliShim, processEnvironment, options.MaximumMetadataFileCharacters);
        var manifestPath = Path.Combine(root, NodeModulesDirectoryName, PackageScopeDirectoryName,
            PackageDirectoryName, NpmManifestName);
        var installedVersion = ReadInstalledPackageVersion(manifestPath, options.MaximumMetadataFileCharacters);
        if (!string.Equals(installedVersion, ClaudeCliCompatibility.TargetVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(VersionMismatchMessage);
        }

        yield return new CliInstallationUpdate(CliInstallationStage.Installed, 0, 0,
            new CliInstallationResult(installedVersion, ClaudeCliCompatibility.TargetVersion, root, launch));
    }

    private static string ValidateOptions(CliInstallationOptions options, string localApplicationDataRoot)
    {
        if (!Enum.IsDefined(options.PackageManager) || !Path.IsPathFullyQualified(options.PackageManagerExecutablePath) ||
            !File.Exists(options.PackageManagerExecutablePath) ||
            (options.PackageManager == CliPackageManager.Npm &&
             (string.IsNullOrWhiteSpace(options.NpmCliScriptPath) || !Path.IsPathFullyQualified(options.NpmCliScriptPath) ||
              !File.Exists(options.NpmCliScriptPath))) ||
            (options.PackageManager == CliPackageManager.Bun && options.NpmCliScriptPath is not null))
        {
            throw new ArgumentException(InvalidManagerMessage, nameof(options));
        }

        ArgumentNullException.ThrowIfNull(options.EnvironmentVariables);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.InstallTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ProcessTerminationTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.InstallationLockTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumOutputCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumMetadataFileCharacters);
        return Path.GetFullPath(Path.Combine(localApplicationDataRoot, ManagedCodeDirectoryName,
            SdkDirectoryName, InstallDirectoryName));
    }

    private static void ValidatePackageManager(CliInstallationOptions options)
    {
        var expectedExecutable = options.PackageManager == CliPackageManager.Bun
            ? OperatingSystem.IsWindows() ? BunWindowsExecutableName : BunExecutableName
            : OperatingSystem.IsWindows() ? NodeWindowsExecutableName : NodeExecutableName;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetFileName(options.PackageManagerExecutablePath), expectedExecutable, comparison))
        {
            throw new ArgumentException(InvalidManagerMessage, nameof(options));
        }

        if (options.PackageManager != CliPackageManager.Npm)
        {
            return;
        }

        var scriptPath = Path.GetFullPath(options.NpmCliScriptPath!);
        var npmRoot = Directory.GetParent(Path.GetDirectoryName(scriptPath)!)?.FullName;
        if (!string.Equals(Path.GetFileName(scriptPath), NpmCliEntryPoint, comparison) ||
            !string.Equals(Path.GetFileName(Path.GetDirectoryName(scriptPath)), NpmBinDirectoryName, comparison) ||
            npmRoot is null || !string.Equals(Path.GetFileName(npmRoot), NpmDirectoryName, comparison))
        {
            throw new ArgumentException(InvalidManagerMessage, nameof(options));
        }

        var manifestPath = Path.Combine(npmRoot, NpmManifestName);
        using var manifest = JsonDocument.Parse(BoundedMetadataFileReader.ReadAllText(
            manifestPath, options.MaximumMetadataFileCharacters));
        var names = manifest.RootElement.ValueKind == JsonValueKind.Object
            ? manifest.RootElement.EnumerateObject().Where(property => property.NameEquals(NpmNameProperty)).ToArray()
            : [];
        if (names.Length != 1 || names[0].Value.ValueKind != JsonValueKind.String ||
            !string.Equals(names[0].Value.GetString(), NpmPackageName, StringComparison.Ordinal))
        {
            throw new ArgumentException(InvalidManagerMessage, nameof(options));
        }
    }

    private static CliLaunchCommand ResolvePackageManager(CliInstallationOptions options) =>
        options.PackageManager == CliPackageManager.Npm
            ? new CliLaunchCommand(options.PackageManagerExecutablePath, [options.NpmCliScriptPath!])
            : new CliLaunchCommand(options.PackageManagerExecutablePath, []);

    private static async Task<FileStream> AcquireRootLockAsync(
        string root,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(root, InstallLockFileName);
        Directory.CreateDirectory(root);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (timer.Elapsed < timeout)
            {
                await Task.Delay(LockRetryMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                throw new TimeoutException(LockTimeoutMessage);
            }
        }
    }

    private static string ReadInstalledPackageVersion(string manifestPath, int maximumCharacters)
    {
        using var manifest = JsonDocument.Parse(BoundedMetadataFileReader.ReadAllText(manifestPath, maximumCharacters));
        var root = manifest.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(VersionMismatchMessage);
        }

        var names = root.EnumerateObject().Where(property => property.NameEquals(NpmNameProperty)).ToArray();
        var versions = root.EnumerateObject().Where(property => property.NameEquals(VersionPropertyName)).ToArray();
        if (names.Length != 1 || versions.Length != 1 || names[0].Value.ValueKind != JsonValueKind.String ||
            !string.Equals(names[0].Value.GetString(), PackageName, StringComparison.Ordinal) ||
            versions[0].Value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(VersionMismatchMessage);
        }

        return versions[0].Value.GetString()!;
    }

    private static void EnsureOwnedRoot(string root)
    {
        var marker = Path.Combine(root, MarkerFileName);
        if (File.Exists(marker))
        {
            if (new FileInfo(marker).Length != MarkerValue.Length ||
                !string.Equals(File.ReadAllText(marker), MarkerValue, StringComparison.Ordinal))
            {
                throw new SecurityException(MarkerMismatchMessage);
            }

            return;
        }

        if (Directory.EnumerateFileSystemEntries(root).Any(path =>
                !string.Equals(path, Path.Combine(root, InstallLockFileName), StringComparison.Ordinal)))
        {
            throw new SecurityException(RootNotEmptyMessage);
        }

        File.WriteAllText(marker, MarkerValue);
    }
}

internal static class CliInstallationEnvironment
{
    private const string PathName = "PATH";
    private const string HomeName = "HOME";
    private const string UserProfileName = "USERPROFILE";
    private const string SystemRootName = "SYSTEMROOT";
    private const string TempName = "TEMP";
    private const string TmpName = "TMP";
    private const string AppDataName = "APPDATA";
    private const string LocalAppDataName = "LOCALAPPDATA";
    private const string XdgConfigHomeName = "XDG_CONFIG_HOME";
    private const string XdgCacheHomeName = "XDG_CACHE_HOME";
    private const string LanguageName = "LANG";
    private const string LocaleName = "LC_ALL";
    private const string CertificateFileName = "SSL_CERT_FILE";
    private const string CertificateDirectoryName = "SSL_CERT_DIR";
    private const string HomeDirectoryName = ".home";
    private const string TempDirectoryName = ".tmp";
    private const string ConfigDirectoryName = ".config";
    private const string CacheDirectoryName = ".cache";
    private const string EnvironmentKeyMessage = "The package-manager environment contains a key outside the supported safe allowlist.";
    private const string EnvironmentPathMessage = "An explicit PATH value is required for CLI installation.";
    private const string SystemRootMessage = "An explicit SystemRoot value is required for Windows CLI installation.";
    private static readonly string[] AllowedNames =
    [
        PathName, HomeName, UserProfileName, SystemRootName, TempName, TmpName, AppDataName, LocalAppDataName,
        XdgConfigHomeName, XdgCacheHomeName, LanguageName, LocaleName, CertificateFileName, CertificateDirectoryName
    ];

    internal static Dictionary<string, string> Create(IReadOnlyDictionary<string, string> configured, string root)
    {
        Validate(configured);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in configured)
        {
            var canonicalName = AllowedNames.FirstOrDefault(name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase));
            if (canonicalName is null || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(EnvironmentKeyMessage, nameof(configured));
            }

            environment[canonicalName] = value;
        }

        var home = Path.Combine(root, HomeDirectoryName);
        var temp = Path.Combine(root, TempDirectoryName);
        environment[HomeName] = home;
        environment[UserProfileName] = home;
        environment[TempName] = temp;
        environment[TmpName] = temp;
        environment[XdgConfigHomeName] = Path.Combine(root, ConfigDirectoryName);
        environment[XdgCacheHomeName] = Path.Combine(root, CacheDirectoryName);
        environment[AppDataName] = environment[XdgConfigHomeName];
        environment[LocalAppDataName] = environment[XdgCacheHomeName];
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(environment[XdgConfigHomeName]);
        Directory.CreateDirectory(environment[XdgCacheHomeName]);
        return environment;
    }

    internal static void Validate(IReadOnlyDictionary<string, string> configured)
    {
        var pathFound = false;
        var systemRootFound = false;
        foreach (var (key, value) in configured)
        {
            var canonical = AllowedNames.FirstOrDefault(name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase));
            if (canonical is null || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(EnvironmentKeyMessage, nameof(configured));
            }

            pathFound |= string.Equals(canonical, PathName, StringComparison.Ordinal);
            systemRootFound |= string.Equals(canonical, SystemRootName, StringComparison.Ordinal);
        }

        if (!pathFound)
        {
            throw new ArgumentException(EnvironmentPathMessage, nameof(configured));
        }

        if (OperatingSystem.IsWindows() && !systemRootFound)
        {
            throw new ArgumentException(SystemRootMessage, nameof(configured));
        }
    }
}
