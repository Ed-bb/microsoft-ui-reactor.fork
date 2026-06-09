namespace Reactor.VisualStudio.ToolWindows
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using EnvDTE;
    using Microsoft.VisualStudio.Shell;
    using Reactor.VisualStudio.Services;

    /// <summary>
    /// Interaction logic for ComponentsToolWindowControl.xaml.
    /// Lists core elements and user-defined components, supporting double-click navigation.
    /// </summary>
    public partial class ComponentsToolWindowControl : UserControl
    {
        private EnvDTE80.DTE2? _dte;
        private DocumentEvents? _documentEvents;
        private SolutionEvents? _solutionEvents;
        private CancellationTokenSource? _scanCancellationTokenSource;

        /// <summary>
        /// Gets the collection of components bound to the list view.
        /// </summary>
        public ObservableCollection<ComponentItem> Items { get; } = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentsToolWindowControl"/> class.
        /// </summary>
        public ComponentsToolWindowControl()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ReactorInProcPackage.Log("ComponentsToolWindowControl: Constructor started.");

            try
            {
                InitializeComponent();

                this.DataContext = this;

                this.Loaded   += OnLoaded;
                this.Unloaded += OnUnloaded;

                this.SetResourceReference(UserControl.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
                ComponentsListView.SetResourceReference(ListView.ForegroundProperty, VsBrushes.ToolWindowTextKey);

                ReactorInProcPackage.Log("ComponentsToolWindowControl: Constructor UI elements initialized.");
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: Critical exception in constructor: {ex}");

                throw;
            }

            ReactorInProcPackage.Log("ComponentsToolWindowControl: Constructor completed.");
        }

        /// <summary>
        /// Rebuilds the static core component list.
        /// </summary>
        public void RefreshList()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ReactorInProcPackage.Log("ComponentsToolWindowControl: RefreshList started.");

            try
            {
                Items.Clear();

                AddCoreElements();

                StartBackgroundScan();

                ReactorInProcPackage.Log($"ComponentsToolWindowControl: RefreshList completed successfully. Items count: {Items.Count}");
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: RefreshList failed: {ex}");
            }
        }

        private void AddCoreElements()
        {
            var coreNames = new[]
            {
                "TextBlock", "Button", "CheckBox", "TextBox", "Image",
                "Slider", "ComboBox", "VStack", "HStack", "Grid",
                "ListView", "ItemsRepeater", "WebView2", "NavigationView",
                "Expander", "ToggleSwitch", "Border", "Canvas", "RelativePanel"
            };

            foreach (var name in coreNames)
            {
                Items.Add(new ComponentItem
                {
                    Name       = name,
                    TypeGroup  = "Core elements",
                    SourceFile = ""
                });
            }
        }

        /// <summary>
        /// Handles the Loaded event of the control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Event data.</param>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                _dte = Package.GetGlobalService(typeof(DTE)) as EnvDTE80.DTE2;

                if (_dte != null)
                {
                    _documentEvents = _dte.Events.DocumentEvents;
                    _documentEvents.DocumentSaved += OnDocumentSaved;

                    _solutionEvents = _dte.Events.SolutionEvents;
                    _solutionEvents.Opened       += OnSolutionChanged;
                    _solutionEvents.AfterClosing += OnSolutionChanged;
                    _solutionEvents.ProjectAdded   += OnSolutionProjectChanged;
                    _solutionEvents.ProjectRemoved += OnSolutionProjectChanged;
                    _solutionEvents.ProjectRenamed += OnSolutionProjectRenamed;
                }

                ComponentsListView.MouseDoubleClick += OnListViewDoubleClick;

                StartBackgroundScan();
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: OnLoaded failed: {ex}");
            }
        }

        /// <summary>
        /// Handles the Unloaded event of the control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Event data.</param>
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (_documentEvents != null)
                {
                    _documentEvents.DocumentSaved -= OnDocumentSaved;
                    _documentEvents = null;
                }

                if (_solutionEvents != null)
                {
                    _solutionEvents.Opened       -= OnSolutionChanged;
                    _solutionEvents.AfterClosing -= OnSolutionChanged;
                    _solutionEvents.ProjectAdded   -= OnSolutionProjectChanged;
                    _solutionEvents.ProjectRemoved -= OnSolutionProjectChanged;
                    _solutionEvents.ProjectRenamed -= OnSolutionProjectRenamed;
                    _solutionEvents = null;
                }

                ComponentsListView.MouseDoubleClick -= OnListViewDoubleClick;

                if (_scanCancellationTokenSource != null)
                {
                    _scanCancellationTokenSource.Cancel();
                    _scanCancellationTokenSource.Dispose();
                    _scanCancellationTokenSource = null;
                }
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: OnUnloaded failed: {ex}");
            }
        }

        /// <summary>
        /// Handles the DocumentSaved event of EnvDTE.
        /// </summary>
        /// <param name="document">The saved document.</param>
        private void OnDocumentSaved(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (document.FullName != null && document.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                StartBackgroundScan();
            }
        }

        /// <summary>
        /// Handles solution change events.
        /// </summary>
        private void OnSolutionChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            StartBackgroundScan();
        }

        /// <summary>
        /// Handles project added or removed events.
        /// </summary>
        /// <param name="project">The modified project.</param>
        private void OnSolutionProjectChanged(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            StartBackgroundScan();
        }

        /// <summary>
        /// Handles project renamed events.
        /// </summary>
        /// <param name="project">The renamed project.</param>
        /// <param name="oldName">The old name of the project.</param>
        private void OnSolutionProjectRenamed(Project project, string oldName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            StartBackgroundScan();
        }

        /// <summary>
        /// Starts the background scan of the solution for Reactor components.
        /// </summary>
        private void StartBackgroundScan()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_scanCancellationTokenSource != null)
            {
                _scanCancellationTokenSource.Cancel();
                _scanCancellationTokenSource.Dispose();
                _scanCancellationTokenSource = null;
            }

            _scanCancellationTokenSource = new CancellationTokenSource();

            var token = _scanCancellationTokenSource.Token;
            var filePaths = GetSolutionCSharpFiles();

            _ = Task.Run(async () =>
            {
                try
                {
                    var userComponents = ParseFiles(filePaths, token);

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);

                    UpdateItemsList(userComponents);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation
                }
                catch (Exception ex)
                {
                    ReactorInProcPackage.Log($"ComponentsToolWindowControl: Background scan error: {ex}");
                }
            }, token);
        }

        /// <summary>
        /// Traverses the active solution to find all C# source files.
        /// </summary>
        /// <returns>A list of C# source file paths.</returns>
        private List<string> GetSolutionCSharpFiles()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var files = new List<string>();

            if (_dte?.Solution == null || !_dte.Solution.IsOpen)
            {
                return files;
            }

            try
            {
                foreach (Project project in _dte.Solution.Projects)
                {
                    if (project == null)
                    {
                        continue;
                    }

                    RecurseProjectItems(project.ProjectItems, files);
                }
            }
            catch
            {
                // Ignore solution projects access errors
            }

            return files;
        }

        /// <summary>
        /// Recursively traverses a collection of ProjectItems to find C# files.
        /// </summary>
        /// <param name="projectItems">The collection of project items to traverse.</param>
        /// <param name="files">The list to populate with file paths.</param>
        private void RecurseProjectItems(ProjectItems projectItems, List<string> files)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (projectItems == null)
            {
                return;
            }

            try
            {
                foreach (ProjectItem item in projectItems)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    ProcessProjectItem(item, files);
                }
            }
            catch
            {
                // Ignore collection iteration errors
            }
        }

        /// <summary>
        /// Processes a single ProjectItem, checking for subprojects and files.
        /// </summary>
        /// <param name="item">The project item to process.</param>
        /// <param name="files">The list to populate with file paths.</param>
        private void ProcessProjectItem(ProjectItem item, List<string> files)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (item.SubProject != null)
                {
                    RecurseProjectItems(item.SubProject.ProjectItems, files);
                }
                else if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                {
                    RecurseProjectItems(item.ProjectItems, files);
                }
            }
            catch
            {
                // Ignore project item traversal errors
            }

            try
            {
                if (item.Name != null && item.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    AddProjectItemFilePath(item, files);
                }
            }
            catch
            {
                // Ignore file name check errors
            }
        }

        /// <summary>
        /// Adds the file path of a C# ProjectItem to the list.
        /// </summary>
        /// <param name="item">The project item.</param>
        /// <param name="files">The list of files.</param>
        private void AddProjectItemFilePath(ProjectItem item, List<string> files)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (item.FileCount > 0)
                {
                    string filePath = item.FileNames[1];

                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        files.Add(filePath);
                    }
                }
            }
            catch
            {
                // Ignore file names retrieval errors
            }
        }

        /// <summary>
        /// Parses multiple C# source files in the background to find components.
        /// </summary>
        /// <param name="filePaths">The list of file paths to parse.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A list of discovered ComponentItem objects.</returns>
        private static List<ComponentItem> ParseFiles(List<string> filePaths, CancellationToken cancellationToken)
        {
            var results = new List<ComponentItem>();

            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(filePath))
                    {
                        string content = File.ReadAllText(filePath);

                        var componentNames = ComponentParser.ParseComponents(content);

                        foreach (var name in componentNames)
                        {
                            results.Add(new ComponentItem
                            {
                                Name       = name,
                                TypeGroup  = "User Components",
                                SourceFile = filePath
                            });
                        }
                    }
                }
                catch
                {
                    // Ignore individual file read/parse exceptions
                }
            }

            return results;
        }

        /// <summary>
        /// Updates the items collection with the newly found user components.
        /// </summary>
        /// <param name="userComponents">The list of user components.</param>
        private void UpdateItemsList(List<ComponentItem> userComponents)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                Items.Clear();

                AddCoreElements();

                foreach (var item in userComponents)
                {
                    Items.Add(item);
                }

                ReactorInProcPackage.Log($"ComponentsToolWindowControl: Updated items. Total items: {Items.Count}");
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: UpdateItemsList failed: {ex}");
            }
        }

        /// <summary>
        /// Handles double-click events on the component list view for navigation.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Event data.</param>
        private void OnListViewDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (ComponentsListView.SelectedItem is ComponentItem selectedItem)
                {
                    if (!string.IsNullOrEmpty(selectedItem.SourceFile) && File.Exists(selectedItem.SourceFile))
                    {
                        var dte = Package.GetGlobalService(typeof(DTE)) as EnvDTE80.DTE2;

                        if (dte != null)
                        {
                            var window = dte.ItemOperations.OpenFile(selectedItem.SourceFile);

                            if (window != null)
                            {
                                NavigateCursorToClass(dte, selectedItem.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: Double-click navigation failed: {ex}");
            }
        }

        /// <summary>
        /// Navigates the cursor to the class definition of the specified component.
        /// </summary>
        /// <param name="dte">The DTE2 instance.</param>
        /// <param name="componentName">The name of the component class.</param>
        private void NavigateCursorToClass(EnvDTE80.DTE2 dte, string componentName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var doc = dte.ActiveDocument;

                if (doc != null && doc.Object("TextDocument") is TextDocument textDoc)
                {
                    var editPoint = textDoc.StartPoint.CreateEditPoint();
                    var content = editPoint.GetText(textDoc.EndPoint);
                    var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(content);
                    var root = tree.GetRoot();
                    var classDecl = root.DescendantNodes()
                                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                                        .FirstOrDefault(c => c.Identifier.Text == componentName);

                    if (classDecl != null)
                    {
                        var lineSpan = classDecl.SyntaxTree.GetLineSpan(classDecl.Span);
                        var startLine = lineSpan.StartLinePosition.Line + 1;
                        var startColumn = lineSpan.StartLinePosition.Character + 1;

                        textDoc.Selection.MoveToLineAndOffset(startLine, startColumn, false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reactor] NavigateCursorToClass failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Represents a single component item in the list control.
    /// </summary>
    public class ComponentItem
    {
        /// <summary>
        /// Gets or sets the name of the component.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the type grouping ("Core elements" or "User Components").
        /// </summary>
        public string TypeGroup { get; set; } = "";

        /// <summary>
        /// Gets or sets the file path for custom components.
        /// </summary>
        public string SourceFile { get; set; } = "";
    }
}
