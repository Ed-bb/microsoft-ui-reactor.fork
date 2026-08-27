#pragma warning disable ISB001

namespace Reactor.VisualStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using Reactor.VisualStudio.Commands;
    using Reactor.VisualStudio.Services;
    using Reactor.VisualStudio.ToolWindows;
    using Reactor.VisualStudio.UiElements;

    /// <summary>
    /// Visual Studio package providing in-process preview capabilities.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(PreviewToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057", Transient = false)]
    [ProvideToolWindow(typeof(ComponentsToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057", Transient = false)]
    public sealed class ReactorInProcPackage : AsyncPackage
    {
        /// <summary>
        /// Unique package GUID string.
        /// </summary>
        public const string PackageGuidString = "e87f1190-2c77-4c8d-8fb8-2231ab46618e";

        private ReactorStatusBarControl? _statusBarControl;
        private string? _currentFilePath;
        private List<string> _currentComponents = new();
        private string? _selectedComponent;

        /// <summary>
        /// Gets the singleton instance of the package.
        /// </summary>
        public static ReactorInProcPackage? Instance { get; private set; }

        /// <summary>
        /// Gets the list of current components under preview.
        /// </summary>
        public List<string> CurrentComponents => _currentComponents;

        /// <summary>
        /// Gets the currently selected component name.
        /// </summary>
        public string? SelectedComponent => _selectedComponent;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReactorInProcPackage"/> class.
        /// </summary>
        public ReactorInProcPackage()
        {
            LogToFile("ReactorInProcPackage: Constructor invoked.");
        }

        /// <inheritdoc />
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            LogToFile("ReactorInProcPackage: InitializeAsync started.");

            try
            {
                Instance = this;

                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                Log("Initialize: In-process package initializing...");

                LogToFile("Initializing commands...");

                await ShowPreviewContextCommand.InitializeAsync(this);
                await ShowComponentsCommand.InitializeAsync(this);

                LogToFile("Commands initialized.");

                try
                {
                    LogToFile("Injecting status bar control...");

                    _statusBarControl = new ReactorStatusBarControl();

                    _ = StatusBarInjector.InjectControlAsync(_statusBarControl);

                    LogToFile("Status bar control injected.");
                }
                catch (Exception ex)
                {
                    Log($"[Init] Failed to initialize status bar control: {ex.Message}");

                    LogToFile($"StatusBar exception: {ex}");
                }

                Log("Initialize: In-process package initialization complete.");
                LogToFile("ReactorInProcPackage: InitializeAsync completed.");
            }
            catch (Exception ex)
            {
                LogToFile($"CRITICAL EXCEPTION in InitializeAsync: {ex}");

                throw;
            }
        }

        /// <summary>
        /// Starts the preview session for a file.
        /// </summary>
        /// <param name="filePath">The file path to preview.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task StartPreviewAsync(string filePath, CancellationToken cancellationToken)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Log($"StartPreview: Starting preview for file: {filePath}");

            _currentFilePath = filePath;

            _statusBarControl?.UpdateState();

            var window = await FindWindowPaneAsync<PreviewToolWindow>(true, cancellationToken);

            if (window?.Frame is IVsWindowFrame frame)
            {
                frame.Show();
            }

            PreviewManager.StartPreview(filePath);
        }

        /// <summary>
        /// Updates the WebView2 preview panel with fresh HTML.
        /// </summary>
        /// <param name="filePath">The active file path.</param>
        /// <param name="componentName">The name of the component.</param>
        /// <param name="html">The generated HTML markup.</param>
        /// <param name="components">The list of component names in the file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task UpdatePreviewHtmlAsync(string filePath, string componentName, string html, List<string> components, CancellationToken cancellationToken)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _currentFilePath   = filePath;
            _selectedComponent = componentName;
            _currentComponents = components;

            _statusBarControl?.UpdateState();

            var window = await FindWindowPaneAsync<PreviewToolWindow>(true, cancellationToken);

            if (window?.Frame is IVsWindowFrame frame)
            {
                frame.Show();

                var control = window.Content as PreviewToolWindowControl;

                if (control != null)
                {
                    await control.InitializeWebViewAsync(html, components, componentName);
                }
            }
        }

        /// <summary>
        /// Stops the active preview session.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task StopPreviewAsync(CancellationToken cancellationToken)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Log("StopPreview: Stopping preview session.");

            _currentFilePath   = null;
            _currentComponents.Clear();
            _selectedComponent = null;

            _statusBarControl?.UpdateState();

            var window = await FindWindowPaneAsync<PreviewToolWindow>(true, cancellationToken);

            if (window?.Frame is IVsWindowFrame frame)
            {
                ErrorHandler.ThrowOnFailure(frame.Hide());
            }
        }

        /// <summary>
        /// Selects a component name for preview and triggers refresh.
        /// </summary>
        /// <param name="componentName">The component to select.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SelectComponentAsync(string componentName)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            _selectedComponent = componentName;

            _statusBarControl?.UpdateState();

            PreviewManager.SelectComponent(componentName);
        }

        /// <summary>
        /// Shows the Components list tool window.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ShowComponentsWindowAsync(CancellationToken cancellationToken)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Log("[Components] ShowComponentsWindowAsync invoked.");

            ToolWindowPane? window;

            try
            {
                window = await this.ShowToolWindowAsync(typeof(ComponentsToolWindow), 0, true, cancellationToken);

                Log($"[Components] ShowToolWindowAsync returned. Window null: {window == null}.");
            }
            catch (Exception ex)
            {
                Log($"[Components] Failed to create/show components tool window: {ex.Message}");

                return;
            }

            if (window?.Frame is IVsWindowFrame frame)
            {
                Log("[Components] Valid IVsWindowFrame found. Calling frame.Show().");

                ErrorHandler.ThrowOnFailure(frame.Show());

                Log("[Components] frame.Show() completed.");

                var control = window.Content as ComponentsToolWindowControl;

                Log($"[Components] Window content type: {window.Content?.GetType().FullName ?? "<null>"}.");

                if (control != null)
                {
                    Log("[Components] ComponentsToolWindowControl found. Calling RefreshList().");

                    control.RefreshList();

                    Log("[Components] RefreshList() completed.");
                }
                else
                {
                    Log("[Components] ComponentsToolWindowControl was not found on window content.");
                }
            }
            else
            {
                Log("[Components] Components tool window was created without a valid frame.");
            }
        }

        /// <summary>
        /// Gets the current file path.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The current file path, or null.</returns>
        public Task<string?> GetCurrentFilePathAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_currentFilePath);
        }

        private async Task<TWindow?> FindWindowPaneAsync<TWindow>(bool create, CancellationToken cancellationToken)
            where TWindow : ToolWindowPane
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            return this.FindToolWindow(typeof(TWindow), 0, create) as TWindow;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_statusBarControl != null)
                {
                    JoinableTaskFactory.Run(async () =>
                    {
                        await JoinableTaskFactory.SwitchToMainThreadAsync();
                        _statusBarControl.Dispose();
                    });
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Logs a message to the Reactor Preview output window pane.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Log(string message)
        {
            LogToFile($"Log: {message}");

            if (Instance == null)
            {
                LogToFile($"Log skipped (Instance is null): {message}");

                return;
            }

            _ = Instance.JoinableTaskFactory.RunAsync(async () =>
            {
                await Instance.JoinableTaskFactory.SwitchToMainThreadAsync();

                var outWindow = await Instance.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;

                if (outWindow != null)
                {
                    Guid customPaneGuid = new Guid(PackageGuidString);

                    outWindow.GetPane(ref customPaneGuid, out IVsOutputWindowPane pane);

                    if (pane == null)
                    {
                        outWindow.CreatePane(ref customPaneGuid, "Reactor Preview", 1, 1);
                        outWindow.GetPane(ref customPaneGuid, out pane);
                    }

                    pane?.OutputString($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                }
            });
        }

        private static void LogToFile(string message)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "reactor_preview_extension_debug.log");

                System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [InProc] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
