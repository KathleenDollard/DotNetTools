// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Tools.Internal;

namespace Microsoft.Extensions.SecretManager.Tools.Internal
{
    public class ProjectIdResolver
    {
        private const string DefaultConfig = "Debug";
        private readonly IReporter _reporter;
        private readonly string _targetsFile;
        private readonly string _workingDirectory;

        public ProjectIdResolver(IReporter reporter, string workingDirectory)
        {
            _workingDirectory = workingDirectory;
            _reporter = reporter;
            _targetsFile = FindTargetsFile();
        }

        public string Resolve(string project, string configuration)
        {
            var finder = new MsBuildProjectFinder(_workingDirectory);
            var projectFile = finder.FindMsBuildProject(project);

            _reporter.Verbose(Resources.FormatMessage_Project_File_Path(projectFile));

            configuration = !string.IsNullOrEmpty(configuration)
                ? configuration
                : DefaultConfig;

            var outputFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var args = new[]
                {
                    "msbuild",
                    projectFile,
                    "/nologo",
                    "/t:_ExtractUserSecretsMetadata", // defined in SecretManager.targets
                    "/p:_UserSecretsMetadataFile=" + outputFile,
                    "/p:Configuration=" + configuration,
                    "/p:CustomAfterMicrosoftCommonTargets=" + _targetsFile,
                    "/p:CustomAfterMicrosoftCommonCrossTargetingTargets=" + _targetsFile,
                };
                var psi = new ProcessStartInfo
                {
                    FileName = DotNetMuxer.MuxerPathOrDefault(),
                    Arguments = ArgumentEscaper.EscapeAndConcatenate(args),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

#if DEBUG
                _reporter.Verbose($"Invoking '{psi.FileName} {psi.Arguments}'");
#endif

                var process = Process.Start(psi);
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    _reporter.Verbose(process.StandardOutput.ReadToEnd());
                    _reporter.Verbose(process.StandardError.ReadToEnd());
                    throw new InvalidOperationException(Resources.FormatError_ProjectFailedToLoad(projectFile));
                }

                var id = File.ReadAllText(outputFile)?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    throw new InvalidOperationException(Resources.FormatError_ProjectMissingId(projectFile));
                }
                return id;

            }
            finally
            {
                TryDelete(outputFile);
            }
        }

        private string FindTargetsFile()
        {
            var assemblyDir = Path.GetDirectoryName(typeof(ProjectIdResolver).Assembly.Location);
            var searchPaths = new[]
            {
                AppContext.BaseDirectory,
                assemblyDir,
                Path.Combine(assemblyDir, "../../toolassets"), // from nuget cache
                Path.Combine(assemblyDir, "toolassets"), // from local build
                Path.Combine(AppContext.BaseDirectory, "../../toolassets"), // relative to packaged deps.json
            };

            var targetPath = searchPaths.Select(p => Path.Combine(p, "SecretManager.targets")).FirstOrDefault(File.Exists);
            if (targetPath == null)
            {
                _reporter.Error("Fatal error: could not find SecretManager.targets");
                return null;
            }
            return targetPath;
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // whatever
            }
        }
    }
}
