using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace ZedUnity.Editor
{
    /// <summary>
    /// Registers Zed Editor as an external code editor in Unity.
    /// Implements <see cref="IExternalCodeEditor"/> from com.unity.code-editor.
    /// </summary>
    [InitializeOnLoad]
    public sealed class ZedCodeEditor : IExternalCodeEditor
    {
        // -----------------------------------------------------------------------
        // Registration
        // -----------------------------------------------------------------------

        static ZedCodeEditor()
        {
            var editor = new ZedCodeEditor();
            CodeEditor.Register(editor);
        }

        // -----------------------------------------------------------------------
        // IExternalCodeEditor – Installations
        // -----------------------------------------------------------------------

        public CodeEditor.Installation[] Installations
        {
            get
            {
                var list = new List<CodeEditor.Installation>();
                foreach (var path in ZedDiscovery.GetInstallationPaths())
                {
                    list.Add(new CodeEditor.Installation
                    {
                        Name = ZedDiscovery.EditorName,
                        Path = path
                    });
                }
                return list.ToArray();
            }
        }

        /// <summary>
        /// Returns true and populates <paramref name="installation"/> when <paramref name="editorPath"/>
        /// points to a Zed executable.
        /// </summary>
        public bool TryGetInstallationForPath(string editorPath,
            out CodeEditor.Installation installation)
        {
            installation = default;
            if (!ZedDiscovery.IsZedPath(editorPath))
                return false;

            installation = new CodeEditor.Installation
            {
                Name = ZedDiscovery.EditorName,
                Path = editorPath
            };
            return true;
        }

        // -----------------------------------------------------------------------
        // IExternalCodeEditor – Lifecycle
        // -----------------------------------------------------------------------

        private const string k_NullablePrefKey = "ZedUnity.NullableEnabled";

        private string _editorPath;
        private ProjectGeneration.ProjectGeneration _projectGeneration;

        public void Initialize(string editorInstallationPath)
        {
            _editorPath = editorInstallationPath;
            _projectGeneration = new ProjectGeneration.ProjectGeneration(
                Directory.GetParent(Application.dataPath).FullName);
        }

        // -----------------------------------------------------------------------
        // IExternalCodeEditor – Open
        // -----------------------------------------------------------------------

        /// <summary>
        /// Opens the Unity project (or a specific file) in Zed.
        /// Returns false if Zed could not be launched.
        /// </summary>
        public bool OpenProject(string filePath = "", int line = -1, int column = -1)
        {
            var editorPath = ResolveEditorPath();
            if (string.IsNullOrEmpty(editorPath))
            {
                UnityEngine.Debug.LogError("[ZedUnity] Could not find Zed executable. " +
                    "Make sure Zed is installed and set as the external script editor in Preferences.");
                return false;
            }

            string args;
            if (string.IsNullOrEmpty(filePath))
            {
                // Open project root as a workspace
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                args = ZedDiscovery.BuildOpenProjectArgs(projectRoot);
            }
            else
            {
                args = ZedDiscovery.BuildOpenFileArgs(filePath, line, column);

                // Also pass the project root so Zed opens the file in the right workspace
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                args = $"{ZedDiscovery.BuildOpenProjectArgs(projectRoot)} {args}";
            }

            return Launch(editorPath, args);
        }

        // -----------------------------------------------------------------------
        // IExternalCodeEditor – Project sync
        // -----------------------------------------------------------------------

        public void SyncAll()
        {
            EnsureProjectGeneration();
            _projectGeneration.NullableEnabled = EditorPrefs.GetBool(k_NullablePrefKey, false);
            _projectGeneration.GenerateAll();
            AssetDatabase.Refresh();
        }

        public void SyncIfNeeded(
            string[] addedFiles,
            string[] deletedFiles,
            string[] movedFiles,
            string[] movedFromFiles,
            string[] importedFiles)
        {
            EnsureProjectGeneration();

            var affectedFiles = addedFiles
                .Concat(deletedFiles)
                .Concat(movedFiles)
                .Concat(movedFromFiles)
                .Concat(importedFiles);

            // Only regenerate when script or assembly-definition assets change
            bool needsSync = affectedFiles.Any(f =>
                f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase));

            if (needsSync)
            {
                _projectGeneration.NullableEnabled = EditorPrefs.GetBool(k_NullablePrefKey, false);
                _projectGeneration.GenerateAll();
            }
        }

        // -----------------------------------------------------------------------
        // IExternalCodeEditor – GUI (Preferences panel)
        // -----------------------------------------------------------------------

        public void OnGUI()
        {
            EditorGUILayout.LabelField("Zed Editor Settings", EditorStyles.boldLabel);

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Enable Nullable", GUILayout.Width(120));
                var current = EditorPrefs.GetBool(k_NullablePrefKey, false);
                var updated = EditorGUILayout.Toggle(current, GUILayout.Width(20));
                if (updated != current)
                    EditorPrefs.SetBool(k_NullablePrefKey, updated);
            }

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Project Files", GUILayout.Width(120));
                if (GUILayout.Button("Regenerate .csproj / .sln", GUILayout.Width(200)))
                    SyncAll();
            }


            EditorGUILayout.Space(4);

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            EditorGUILayout.LabelField("Project Root", projectRoot, EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                "OmniSharp language server configuration is written to .zed/settings.json " +
                "in the project root on first sync.\n\n" +
                "For debugging: install the 'unity-debug' DAP adapter and see docs/setup-guide.md.",
                MessageType.Info);
        }

        // -----------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------

        private string ResolveEditorPath()
        {
            // Prefer explicitly initialized path
            if (!string.IsNullOrEmpty(_editorPath))
                return _editorPath;

            // Fall back to Unity's current editor path setting
            var currentPath = CodeEditor.CurrentEditorPath;
            if (!string.IsNullOrEmpty(currentPath) && ZedDiscovery.IsZedPath(currentPath))
                return currentPath;

            // Try auto-discovery
            return ZedDiscovery.GetInstallationPaths().FirstOrDefault();
        }

        private void EnsureProjectGeneration()
        {
            if (_projectGeneration == null)
            {
                _projectGeneration = new ProjectGeneration.ProjectGeneration(
                    Directory.GetParent(Application.dataPath).FullName);
            }
        }

        private static bool Launch(string executable, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = args,
                    UseShellExecute = true,
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ZedUnity] Failed to launch Zed: {ex.Message}");
                return false;
            }
        }
    }
}
