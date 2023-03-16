using System.Runtime.InteropServices;
using Extensions;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.NuGet;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

partial class Build
{
    AbsolutePath NuGetArtifactsDirectory => NuGetArtifacts ?? (OutputDirectory / "nuget-artifacts");

    Target BuildNuGetPackages => _ => _
        .Description(
            "Builds the NuGet packages of the project assuming that any necessary build artifacts were already downloaded.")
        .DependsOn(BuildManagedSrcNuGetPackages)
        .DependsOn(CopyIntegrationsJsonForNuGetPackage)
        .DependsOn(SetupRuntimeNativeFolderForNuGetPackage)
        .DependsOn(BuildNuSpecNuGetPackages);

    Target TestNuGetPackages => _ => _
        .Description(
            "Test the NuGet packages of the project assuming that the packages are available at bin/nuget-artifacts.")
        .DependsOn(BuildNuGetPackagesTests)
        .DependsOn(BuildNuGetPackagesTestApplications)
        .DependsOn(RunNuGetPackagesTests);

    Target BuildManagedSrcNuGetPackages => _ => _
        .Description("Build the NuGet packages that are generated directly from src/**/*.csproj files")
        .Executes(() =>
        {
            foreach (var project in Solution.GetManagedSrcProjects().Where(p => !p.Name.EndsWith("AdditionalDeps")))
            {
                DotNetPack(x => x
                    .SetProject(project)
                    .SetConfiguration(BuildConfiguration)
                    .SetVersionSuffix(NuGetVersionSuffix)
                    .SetOutputDirectory(NuGetArtifactsDirectory));
            }
        });

    Target CopyIntegrationsJsonForNuGetPackage => _ => _
        .Unlisted()
        .Executes(() =>
        {
            var source = RootDirectory / "integrations.json";
            var dest = RootDirectory / "nuget" / "OpenTelemetry.AutoInstrumentation" /
                "contentFiles" / "any" / "any";
            CopyFileToDirectory(source, dest, FileExistsPolicy.Overwrite);
        });

    Target SetupRuntimeNativeFolderForNuGetPackage => _ => _
        .Unlisted()
        .Description("Setup the \"runtimes/{platform}-{architecture}/native\" folders under \"nuget/OpenTelemetry.AutoInstrumentation.Runtime.Native\".")
        .Executes(() =>
        {
            const string ciArtifactsDirectory = "bin/ci-artifacts";
            const string baseRuntimeNativePath = "./nuget/OpenTelemetry.AutoInstrumentation.Runtime.Native/";

            var requiredArtifacts = new string[]
            {
                "bin-alpine/linux-musl-x64",
                "bin-centos/linux-x64",
                "bin-macos-11/osx-x64",
                "bin-windows-2022/win-x64",
                "bin-windows-2022/win-x86"
            };

            foreach (var artifactFolder in requiredArtifacts)
            {
                var sourcePath = Path.Combine(ciArtifactsDirectory, artifactFolder);

                var platformAndArchitecture = Path.GetFileName(artifactFolder);
                var destinationPath =
                    Path.Combine(baseRuntimeNativePath, "runtimes", platformAndArchitecture, "native");
                DeleteDirectory(destinationPath);

                CopyDirectoryRecursively(sourcePath, destinationPath);
            }
        });

    Target BuildNuSpecNuGetPackages => _ => _
        .Description("Build the NuGet packages specified by nuget/**/*.nuspec projects.")
        .After(CopyIntegrationsJsonForNuGetPackage)
        .After(SetupRuntimeNativeFolderForNuGetPackage)
        .Executes(() =>
        {
            // .nuspec files don't support .props or another way to share properties.
            // To avoid repeating these values on all .nuspec files they are going to
            // be passed as properties.
            // Keeping common values here and using them as properties
            var nuspecCommonProperties = new Dictionary<string, object>
            {
                { "NoWarn", "NU5128" },
                { "NuGetLicense", "Apache-2.0" },
                { "NuGetPackageVersion", $"{NuGetBaseVersionNumber}{NuGetVersionSuffix}" },
                { "NuGetRequiredLicenseAcceptance", "true" },
                { "OpenTelemetryAuthors", "OpenTelemetry Authors" }
            };

            var nuspecSolutionFolder = Solution.GetSolutionFolder("nuget")
                ?? throw new InvalidOperationException("Couldn't find the expected \"nuget\" solution folder.");

            var nuspecProjects = nuspecSolutionFolder.Items.Keys.ToArray();
            foreach (var nuspecProject in nuspecProjects)
            {
                NuGetTasks.NuGetPack(s => s
                    .SetTargetPath(nuspecProject)
                    .SetConfiguration(BuildConfiguration)
                    .SetProperties(nuspecCommonProperties)
                    .SetOutputDirectory(NuGetArtifactsDirectory));
            }
        });

    Target BuildNuGetPackagesTests => _ => _
        .Description("Builds the NuGetPackagesTests project")
        .Executes(() =>
        {
            var nugetPackagesTestProject = Solution.GetProject("NuGetPackagesTests");
            DotNetBuild(s => s
                .SetProjectFile(nugetPackagesTestProject)
                .SetConfiguration(BuildConfiguration));
        });

    Target BuildNuGetPackagesTestApplications => _ => _
        .Description("Builds the TestApplications.* used by the NuGetPackagesTests")
        .Executes(() =>
        {
            foreach (var packagesTestApplicationProject in Solution.GetNuGetPackagesTestApplications())
            {
                // Unlike the integration apps these require a restore step.
                DotNetBuild(s => s
                    .SetProjectFile(packagesTestApplicationProject)
                    .SetProperty("NuGetPackageVersion", $"{NuGetBaseVersionNumber}{NuGetVersionSuffix}")
                    .SetRuntime(RuntimeInformation.RuntimeIdentifier)
                    .SetConfiguration(BuildConfiguration)
                    .SetPlatform(Platform));
            }
        });

    Target RunNuGetPackagesTests => _ => _
        .Description("Run the NuGetPackagesTests.")
        .After(BuildNuGetPackagesTests)
        .After(BuildNuGetPackagesTestApplications)
        .Executes(() =>
        {
            var nugetPackagesTestProject = Solution.GetProject("NuGetPackagesTests");

            for (var i = 0; i < TestCount; i++)
            {
                DotNetMSBuild(config => config
                    .SetConfiguration(BuildConfiguration)
                    .SetFilter(AndFilter(TestNameFilter(), ContainersFilter()))
                    .SetBlameHangTimeout("5m")
                    .EnableTrxLogOutput(GetResultsDirectory(nugetPackagesTestProject))
                    .SetTargetPath(nugetPackagesTestProject)
                    .DisableRestore()
                    .RunTests()
                );
            }
        });
}