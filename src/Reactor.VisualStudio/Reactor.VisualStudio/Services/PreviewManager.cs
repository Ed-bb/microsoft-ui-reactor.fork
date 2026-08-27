namespace Reactor.VisualStudio.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Manages the state of the active Reactor preview session in Visual Studio.
    /// Handles parser invocation and file watcher triggers.
    /// </summary>
    public static class PreviewManager
    {
        private static string? _currentFilePath;
        private static string? _selectedComponent;
        private static List<string> _currentComponents = new();
        private static FileSystemWatcher? _watcher;

        /// <summary>
        /// Gets the current file path being previewed.
        /// </summary>
        public static string? CurrentFilePath => _currentFilePath;

        /// <summary>
        /// Gets the currently selected component name.
        /// </summary>
        public static string? SelectedComponent => _selectedComponent;

        /// <summary>
        /// Gets the list of components in the active file.
        /// </summary>
        public static List<string> CurrentComponents => _currentComponents;

        /// <summary>
        /// Starts the preview session for the specified document file.
        /// </summary>
        /// <param name="filePath">The file path to preview.</param>
        public static void StartPreview(string filePath)
        {
            _currentFilePath   = filePath;
            _selectedComponent = null;

            SetupWatcher();

            RefreshPreview();
        }

        /// <summary>
        /// Updates the selected component name and triggers preview refresh.
        /// </summary>
        /// <param name="componentName">The component to select.</param>
        public static void SelectComponent(string componentName)
        {
            _selectedComponent = componentName;

            RefreshPreview();
        }

        /// <summary>
        /// Stops the current preview session and cleans up resources.
        /// </summary>
        public static void StopPreview()
        {
            _currentFilePath   = null;
            _selectedComponent = null;

            _currentComponents.Clear();

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;

                _watcher.Dispose();

                _watcher = null;
            }

            if (ReactorInProcPackage.Instance != null)
            {
                _ = ReactorInProcPackage.Instance.StopPreviewAsync(default);
            }
        }

        private static void SetupWatcher()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;

                _watcher.Dispose();

                _watcher = null;
            }

            if (string.IsNullOrEmpty(_currentFilePath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(_currentFilePath);
            var fileName  = Path.GetFileName(_currentFilePath);

            if (!string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(fileName))
            {
                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnFileChanged;
            }
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce file system events
            Thread.Sleep(100);

            RefreshPreview();
        }

        /// <summary>
        /// Refreshes the preview window by running the AST parser and updating the WebView2 control.
        /// </summary>
        public static void RefreshPreview()
        {
            var filePath = _currentFilePath;

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return;
            }

            try
            {
                var content = File.ReadAllText(filePath);

                var parsed = ComponentParser.ParseComponents(content);

                _currentComponents = parsed;

                if (_currentComponents.Count == 0)
                {
                    return;
                }

                if (string.IsNullOrEmpty(_selectedComponent) || !_currentComponents.Contains(_selectedComponent!))
                {
                    _selectedComponent = _currentComponents[0];
                }

                var errors = new List<string>();
                var astTree = AstParser.ParseAst(content, _selectedComponent!);

                var html = HtmlRenderer.GeneratePreviewHtml(astTree, errors, _selectedComponent!);

                if (ReactorInProcPackage.Instance != null)
                {
                    _ = ReactorInProcPackage.Instance.UpdatePreviewHtmlAsync(
                        filePath!,
                        _selectedComponent!,
                        html,
                        _currentComponents,
                        default);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reactor] RefreshPreview failed: {ex.Message}");
            }
        }
    }
}
