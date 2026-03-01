using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace ZedUnity.Editor.ProjectGeneration
{
    /// <summary>
    /// Generates MSBuild .csproj and .sln files for all Unity assemblies.
    /// These files are consumed by OmniSharp inside Zed for IntelliSense.
    /// </summary>
    internal sealed class ProjectGeneration
    {
        // -----------------------------------------------------------------------
        // Constants
        // -----------------------------------------------------------------------

        private const string k_ProjectFileExtension = ".csproj";
        private const string k_SolutionFileExtension = ".sln";
        private const string k_ZedSettingsDir = ".zed";
        private const string k_ZedSettingsFile = "settings.json";

        // Target framework for Unity 2020+
        // Unity 2021+ uses net48, earlier uses net471 – we default to net471 for compatibility
        private const string k_TargetFramework = "v4.7.1";
        private const string k_LangVersion = "9.0";

        // -----------------------------------------------------------------------
        // Fields
        // -----------------------------------------------------------------------

        private readonly string _projectDirectory;
        private readonly string _assetsDirectory;

        public bool NullableEnabled { get; set; } = false;

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------

        public ProjectGeneration(string projectDirectory)
        {
            _projectDirectory = projectDirectory;
            _assetsDirectory = Path.Combine(projectDirectory, "Assets");
        }

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Generates .csproj files for all Unity assemblies, a matching .sln,
        /// and writes OmniSharp / Zed workspace configuration.
        /// </summary>
        public void GenerateAll()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            var projectFiles = new List<(string name, string guid, string path)>();

            foreach (var assembly in assemblies)
            {
                var (csprojPath, guid) = WriteCsproj(assembly);
                projectFiles.Add((assembly.name, guid, csprojPath));
            }

            WriteSolution(projectFiles);
            WriteZedSettings();

            Debug.Log($"[ZedUnity] Generated {projectFiles.Count} .csproj file(s), .sln, and Zed settings.");
        }

        // -----------------------------------------------------------------------
        // .csproj generation
        // -----------------------------------------------------------------------

        private (string path, string guid) WriteCsproj(Assembly assembly)
        {
            var guid = DeterministicGuid(assembly.name);
            var path = Path.Combine(_projectDirectory, assembly.name + k_ProjectFileExtension);

            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
            sb.AppendLine(@"<Project ToolsVersion=""4.0"" DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">");

            // --- PropertyGroup ---
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine($"    <LangVersion>{k_LangVersion}</LangVersion>");
            sb.AppendLine("    <Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>");
            sb.AppendLine("    <Platform Condition=\" '$(Platform)' == '' \">AnyCPU</Platform>");
            sb.AppendLine("    <ProductVersion>10.0.20506</ProductVersion>");
            sb.AppendLine("    <SchemaVersion>2.0</SchemaVersion>");
            sb.AppendLine($"    <RootNamespace>{SecurityElement.Escape(assembly.name)}</RootNamespace>");
            sb.AppendLine($"    <ProjectGuid>{{{guid}}}</ProjectGuid>");
            sb.AppendLine("    <OutputType>Library</OutputType>");
            sb.AppendLine($"    <AssemblyName>{SecurityElement.Escape(assembly.name)}</AssemblyName>");
            sb.AppendLine($"    <TargetFrameworkVersion>{k_TargetFramework}</TargetFrameworkVersion>");
            sb.AppendLine("    <BaseIntermediateOutputPath>Temp\\obj\\</BaseIntermediateOutputPath>");
            sb.AppendLine("    <BaseOutputPath>Temp\\bin\\</BaseOutputPath>");
            sb.AppendLine("    <Deterministic>true</Deterministic>");
            if (NullableEnabled)
                sb.AppendLine("    <Nullable>enable</Nullable>");

            var defines = string.Join(";", assembly.defines.Select(d => SecurityElement.Escape(d)));
            sb.AppendLine($"    <DefineConstants>{defines}</DefineConstants>");
            sb.AppendLine("  </PropertyGroup>");

            // --- Source files ---
            sb.AppendLine("  <ItemGroup>");
            foreach (var src in assembly.sourceFiles)
            {
                var rel = MakeRelativePath(_projectDirectory, src);
                sb.AppendLine($"    <Compile Include=\"{SecurityElement.Escape(rel)}\" />");
            }
            sb.AppendLine("  </ItemGroup>");

            // --- References ---
            sb.AppendLine("  <ItemGroup>");

            // Assembly references (other Unity assemblies)
            foreach (var asmRef in assembly.assemblyReferences)
            {
                var refCsprojPath = asmRef.name + k_ProjectFileExtension;
                var refGuid = DeterministicGuid(asmRef.name);
                sb.AppendLine("    <ProjectReference Include=\"" + SecurityElement.Escape(refCsprojPath) + "\">");
                sb.AppendLine($"      <Project>{{{refGuid}}}</Project>");
                sb.AppendLine($"      <Name>{SecurityElement.Escape(asmRef.name)}</Name>");
                sb.AppendLine("    </ProjectReference>");
            }

            // DLL references (Unity engine assemblies etc.)
            foreach (var compRef in assembly.compiledAssemblyReferences)
            {
                // Skip managed assemblies that are already project references
                var refName = Path.GetFileNameWithoutExtension(compRef);
                sb.AppendLine($"    <Reference Include=\"{SecurityElement.Escape(refName)}\">");
                sb.AppendLine($"      <HintPath>{SecurityElement.Escape(compRef)}</HintPath>");
                sb.AppendLine("      <Private>False</Private>");
                sb.AppendLine("    </Reference>");
            }

            sb.AppendLine("  </ItemGroup>");
            // --- Unity Roslyn Analyzer ---
            var analyzerPath = GetAnalyzerPath();
            if (analyzerPath != null)
            {
                sb.AppendLine("  <ItemGroup>");
                sb.AppendLine($"    <Analyzer Include=\"{SecurityElement.Escape(analyzerPath)}\" />");
                sb.AppendLine("  </ItemGroup>");
            }

            sb.AppendLine("  <Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />");
            sb.AppendLine("</Project>");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return (path, guid);
        }

        // -----------------------------------------------------------------------
        // .sln generation
        // -----------------------------------------------------------------------

        private void WriteSolution(List<(string name, string guid, string path)> projects)
        {
            var slnPath = Path.Combine(_projectDirectory, Path.GetFileName(_projectDirectory) + k_SolutionFileExtension);
            var solutionGuid = DeterministicGuid("Solution");

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            sb.AppendLine("# Visual Studio Version 16");
            sb.AppendLine("VisualStudioVersion = 16.0.28729.10");
            sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

            foreach (var (name, guid, path) in projects)
            {
                var rel = MakeRelativePath(_projectDirectory, path);
                sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", \"{rel}\", \"{{{guid}}}\"");
                sb.AppendLine("EndProject");
            }

            sb.AppendLine("Global");
            sb.AppendLine("  GlobalSection(SolutionConfigurationPlatforms) = preSolution");
            sb.AppendLine("    Debug|Any CPU = Debug|Any CPU");
            sb.AppendLine("    Release|Any CPU = Release|Any CPU");
            sb.AppendLine("  EndGlobalSection");
            sb.AppendLine("  GlobalSection(ProjectConfigurationPlatforms) = postSolution");
            foreach (var (_, guid, _) in projects)
            {
                sb.AppendLine($"    {{{guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
                sb.AppendLine($"    {{{guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU");
                sb.AppendLine($"    {{{guid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU");
                sb.AppendLine($"    {{{guid}}}.Release|Any CPU.Build.0 = Release|Any CPU");
            }
            sb.AppendLine("  EndGlobalSection");
            sb.AppendLine("  GlobalSection(SolutionProperties) = preSolution");
            sb.AppendLine("    HideSolutionNode = FALSE");
            sb.AppendLine("  EndGlobalSection");
            sb.AppendLine("EndGlobal");

            File.WriteAllText(slnPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        // -----------------------------------------------------------------------
        // Zed configuration
        // -----------------------------------------------------------------------

        /// <summary>
        /// Writes .zed/settings.json configuring the C# language server for Unity.
        /// </summary>
        private void WriteZedSettings()
        {
            var zedDir = Path.Combine(_projectDirectory, k_ZedSettingsDir);
            Directory.CreateDirectory(zedDir);

            var settingsPath = Path.Combine(zedDir, k_ZedSettingsFile);

            // Preserve existing settings by merging (only create if absent)
            if (File.Exists(settingsPath))
                return;

            var json = @"{
  ""languages"": {
    ""CSharp"": {
      ""language_servers"": [""roslyn"", ""!omnisharp"", ""...""],
      ""format_on_save"": ""off""
    }
  }
}
";
            File.WriteAllText(settingsPath, json, Encoding.UTF8);
        }

        // -----------------------------------------------------------------------
        // Utilities
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the path to the bundled Microsoft.Unity.Analyzers.dll, or null if not found.
        /// </summary>
        private static string GetAnalyzerPath()
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ProjectGeneration).Assembly);
            if (pkg == null) return null;
            var path = Path.Combine(pkg.resolvedPath, "Analyzers~", "Microsoft.Unity.Analyzers.dll");
            return File.Exists(path) ? path : null;
        }

        /// <summary>Creates a deterministic GUID from a string name.</summary>
        private static string DeterministicGuid(string name)
        {
            // Use MD5 as a deterministic hash – not for security purposes
            using var md5 = System.Security.Cryptography.MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(name));
            return new Guid(bytes).ToString("D").ToUpperInvariant();
        }

        /// <summary>Returns a path relative to <paramref name="basePath"/>.</summary>
        private static string MakeRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            // Unity may return relative paths; resolve them against the project directory
            if (!Path.IsPathRooted(fullPath))
                fullPath = Path.GetFullPath(fullPath);

            var baseUri = new Uri(basePath);
            var fullUri = new Uri(fullPath);

            var relUri = baseUri.MakeRelativeUri(fullUri);
            return Uri.UnescapeDataString(relUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
