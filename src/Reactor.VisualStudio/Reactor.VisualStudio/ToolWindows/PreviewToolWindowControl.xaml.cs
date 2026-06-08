namespace Reactor.VisualStudio.ToolWindows
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Windows.Controls;
    using Microsoft.Web.WebView2.Core;
    using Microsoft.VisualStudio.Shell;
    using Reactor.VisualStudio.Services;

    /// <summary>
    /// Interaction logic for PreviewToolWindowControl.xaml.
    /// Handles WebView2 initialization, HTML generation, and JSON message dispatching.
    /// </summary>
    public partial class PreviewToolWindowControl : UserControl
    {
        private bool _isInitialized;
        private List<string> _components = new();
        private string _selectedComponent = "";
        private string _pendingHtml = "";

        /// <summary>
        /// Initializes a new instance of the <see cref="PreviewToolWindowControl"/> class.
        /// </summary>
        public PreviewToolWindowControl()
        {
            InitializeComponent();

            this.IsVisibleChanged += OnIsVisibleChanged;
        }

        /// <summary>
        /// Initializes WebView2 with isolated user data and navigates to the preview dashboard.
        /// </summary>
        public async System.Threading.Tasks.Task InitializeWebViewAsync(
            string html,
            List<string> components,
            string selected)
        {
            _components        = components;
            _selectedComponent = selected;
            _pendingHtml       = html;

            if (_isInitialized)
            {
                if (WebView.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.NavigateToString(html);
                }

                return;
            }

            try
            {
                var localAppFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ReactorPreviewVS");

                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: localAppFolder);

                await WebView.EnsureCoreWebView2Async(env);

                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled            = true;

                WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                _isInitialized = true;

                WebView.CoreWebView2.NavigateToString(_pendingHtml);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reactor] WebView2 Init Failed: {ex.Message}");
            }
        }

#pragma warning disable VSTHRD100 // Avoid "async void" methods
        private async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
#pragma warning restore VSTHRD100
        {
            try
            {
                var json = e.TryGetWebMessageAsString();

                if (string.IsNullOrEmpty(json))
                {
                    return;
                }

                using var doc = JsonDocument.Parse(json);

                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "selectComponent")
                {
                    if (root.TryGetProperty("name", out var nameProp))
                    {
                        var compName = nameProp.GetString();

                        if (compName is not null)
                        {
                            if (ReactorInProcPackage.Instance != null)
                            {
                                await ReactorInProcPackage.Instance.SelectComponentAsync(compName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reactor] Error handling web message: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the tool window's visibility change event.
        /// Activates preview synchronization with the active C# document when shown.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">Dependency property changed event args.</param>
        private void OnIsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if ((bool)e.NewValue)
            {
                SynchronizeWithActiveDocument();
            }
        }

        /// <summary>
        /// Checks the current active document in Visual Studio for Reactor components
        /// and synchronizes the preview window with it.
        /// </summary>
        private void SynchronizeWithActiveDocument()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;

                if (dte?.ActiveDocument != null && !string.IsNullOrEmpty(dte.ActiveDocument.FullName))
                {
                    var path = dte.ActiveDocument.FullName;

                    if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(path))
                        {
                            var content = File.ReadAllText(path);
                            var comps   = ComponentParser.ParseComponents(content);

                            if (comps.Count > 0)
                            {
                                PreviewManager.StartPreview(path);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reactor] SynchronizeWithActiveDocument failed: {ex.Message}");
            }
        }
    }
}
