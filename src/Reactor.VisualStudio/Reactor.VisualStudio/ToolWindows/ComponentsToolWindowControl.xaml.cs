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
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;

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
        private Point _dragStartPoint;

        /// <summary>
        /// Gets the collection of components bound to the list view.
        /// </summary>
        public ObservableCollection<ComponentItem> Items { get; } = new();

        /// <summary>
        /// The custom data format string used for drag-and-drop operations.
        /// </summary>
        public const string DragDropFormat = "ReactorComponentItem";

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

        /// <summary>
        /// Maps core element names to their required DSL factory parameters.
        /// </summary>
        private static readonly Dictionary<string, string[]> CoreElementParams = new()
        {
            ["TextBlock"]      = new[] { "content: \"\"" },
            ["Button"]         = new[] { "label: \"\"" },
            ["CheckBox"]       = Array.Empty<string>(),
            ["TextBox"]        = Array.Empty<string>(),
            ["Image"]          = Array.Empty<string>(),
            ["Slider"]         = Array.Empty<string>(),
            ["ComboBox"]       = new[] { "items: new[] { \"\" }" },
            ["VStack"]         = Array.Empty<string>(),
            ["HStack"]         = Array.Empty<string>(),
            ["Grid"]           = Array.Empty<string>(),
            ["ListView"]       = Array.Empty<string>(),
            ["ItemsRepeater"]  = Array.Empty<string>(),
            ["WebView2"]       = Array.Empty<string>(),
            ["NavigationView"] = Array.Empty<string>(),
            ["Expander"]       = new[] { "header: \"\"", "content: null" },
            ["ToggleSwitch"]   = Array.Empty<string>(),
            ["Border"]         = new[] { "child: null" },
            ["Canvas"]         = Array.Empty<string>(),
            ["RelativePanel"]  = Array.Empty<string>()
        };

        /// <summary>
        /// Maps core element names to the name of their Element-type child parameter, if any.
        /// </summary>
        private static readonly Dictionary<string, string> CoreElementChildParam = new()
        {
            ["Border"]   = "child",
            ["Expander"] = "content"
        };

        private void AddCoreElements()
        {
            foreach (var kvp in CoreElementParams)
            {
                CoreElementChildParam.TryGetValue(kvp.Key, out var elementParam);

                Items.Add(new ComponentItem
                {
                    Name             = kvp.Key,
                    TypeGroup        = "Core elements",
                    SourceFile       = "",
                    FactoryParams    = kvp.Value,
                    ElementParamName = elementParam ?? ""
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

                        var components = ComponentParser.ParseComponentsWithParams(content);

                        foreach (var comp in components)
                        {
                            var elementParam = comp.Parameters
                                .FirstOrDefault(p => IsElementType(p.TypeName));

                            var name        = comp.Name;
                            var description = "";

                            if (name == "App")
                            {
                                description = FindAppDescription(content) ?? "";
                            }

                            results.Add(new ComponentItem
                            {
                                Name             = name,
                                Description      = description,
                                TypeGroup        = "User Components",
                                SourceFile       = filePath,
                                FactoryParams    = comp.Parameters
                                    .Select(p => $"{p.Name}: {GetDefaultPlaceholder(p.TypeName)}")
                                    .ToArray(),
                                ElementParamName = elementParam?.Name ?? ""
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
        /// Scans a file's content for a "ReactorApp.Run<T>(...)" call and extracts its first argument as the description.
        /// </summary>
        /// <param name="content">The source file content.</param>
        /// <returns>The description string if found, otherwise null.</returns>
        public static string? FindAppDescription(string content)
        {
            try
            {
                var tree    = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(content);
                var root    = tree.GetRoot();
                var runCall = root.DescendantNodes()
                                  .OfType<InvocationExpressionSyntax>()
                                  .FirstOrDefault(i => i.Expression.ToString().StartsWith("ReactorApp.Run"));

                if (runCall?.ArgumentList != null && runCall.ArgumentList.Arguments.Count > 0)
                {
                    var argExpr = runCall.ArgumentList.Arguments[0].Expression;

                    if (argExpr is LiteralExpressionSyntax literal && literal.Token.IsKind(SyntaxKind.StringLiteralToken))
                    {
                        return literal.Token.ValueText;
                    }

                    if (argExpr is InterpolatedStringExpressionSyntax interpolated)
                    {
                        var builder = new System.Text.StringBuilder();

                        foreach (var part in interpolated.Contents)
                        {
                            if (part is InterpolatedStringTextSyntax text)
                            {
                                builder.Append(text.TextToken.ValueText);
                            }
                        }

                        return builder.ToString();
                    }

                    return argExpr.ToString().Trim('"', '$', '@');
                }
            }
            catch
            {
                // Ignore and fall back
            }

            return null;
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
        /// Handles the PreviewMouseLeftButtonDown event of the ComponentsListView.
        /// Captures the start point of a potential drag operation.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Event data.</param>
        private void OnListViewPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        /// <summary>
        /// Handles the MouseMove event of the ComponentsListView.
        /// Initiates the drag-and-drop operation if the mouse has moved past the drag threshold.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Event data.</param>
        private void OnListViewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var mousePos = e.GetPosition(null);
                var diff     = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (ComponentsListView.SelectedItem is ComponentItem selectedItem)
                    {
                        var data = new DataObject(DragDropFormat, selectedItem);

                        DragDrop.DoDragDrop(ComponentsListView, data, DragDropEffects.Copy);
                    }
                }
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

            if (ComponentsListView.SelectedItem is ComponentItem selectedItem)
            {
                GoToImplementation(selectedItem);
            }
        }

        /// <summary>
        /// Handles the "Go to Implementation" context menu click.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Event data.</param>
        private void OnGoToImplementationClick(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is MenuItem menuItem &&
                GetContextMenuDataItem(menuItem) is ComponentItem item)
            {
                GoToImplementation(item);
            }
        }

        /// <summary>
        /// Handles the "Insert code" context menu click.
        /// Inserts the component factory call at the cursor position in the active document.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Event data.</param>
        private void OnInsertCodeClick(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is MenuItem menuItem &&
                GetContextMenuDataItem(menuItem) is ComponentItem item)
            {
                InsertCodeAtCursor(item);
            }
        }

        /// <summary>
        /// Resolves the data item from a MenuItem's parent ContextMenu placement target.
        /// </summary>
        /// <param name="menuItem">The clicked MenuItem.</param>
        /// <returns>The data item, or null if not resolvable.</returns>
        private static object? GetContextMenuDataItem(MenuItem menuItem)
        {
            if (menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement target)
            {
                return target.DataContext;
            }

            return null;
        }

        /// <summary>
        /// Opens the source file and navigates to the class declaration of the specified component.
        /// </summary>
        /// <param name="item">The component item to navigate to.</param>
        private void GoToImplementation(ComponentItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (string.IsNullOrEmpty(item.SourceFile) || !File.Exists(item.SourceFile))
                {
                    return;
                }

                var dte = Package.GetGlobalService(typeof(DTE)) as EnvDTE80.DTE2;

                if (dte != null)
                {
                    var window = dte.ItemOperations.OpenFile(item.SourceFile);

                    if (window != null)
                    {
                        NavigateCursorToClass(dte, item.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: GoToImplementation failed: {ex}");
            }
        }

        /// <summary>
        /// Inserts a component factory call at the cursor position, with context-aware wrapping.
        /// If an Element expression is found at the cursor, wraps or inserts before it.
        /// </summary>
        /// <param name="item">The component item whose code to insert.</param>
        private void InsertCodeAtCursor(ComponentItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as EnvDTE80.DTE2;

                if (dte?.ActiveDocument == null)
                {
                    return;
                }

                if (!(dte.ActiveDocument.Object("TextDocument") is TextDocument textDoc))
                {
                    return;
                }

                var fullText     = GetFullDocumentText(textDoc);
                var cursorOffset = GetCursorOffset(textDoc, fullText);

                if (!IsValidInsertionLocation(fullText, cursorOffset))
                {
                    ReactorInProcPackage.Log($"Error: Cannot insert component '{item.Name}'. The cursor location is not inside an Element-producing member or variable.");

                    return;
                }

                var elementRange = FindElementExpressionAtCursor(fullText, cursorOffset);

                if (elementRange != null)
                {
                    InsertWithElementContext(textDoc, item, fullText, elementRange.Value);
                }
                else
                {
                    var editPoint = textDoc.Selection.ActivePoint.CreateEditPoint();
                    editPoint.Insert(BuildSimpleSnippet(item));
                }
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: InsertCodeAtCursor failed: {ex}");
            }
        }

        /// <summary>
        /// Inserts a component factory call at the specified character offset in a text buffer.
        /// Performs context-aware wrapping or sibling insertion.
        /// </summary>
        /// <param name="textBuffer">The text buffer to modify.</param>
        /// <param name="item">The component item whose code to insert.</param>
        /// <param name="offset">The character offset to insert at.</param>
        public static void InsertCodeAtOffset(Microsoft.VisualStudio.Text.ITextBuffer textBuffer, ComponentItem item, int offset)
        {
            var snapshot = textBuffer.CurrentSnapshot;
            var fullText = snapshot.GetText();

            if (offset < 0 || offset > fullText.Length)
            {
                return;
            }

            if (!IsValidInsertionLocation(fullText, offset))
            {
                ReactorInProcPackage.Log($"Error: Cannot drop component '{item.Name}'. The drop location is not inside an Element-producing member or variable.");

                return;
            }

            var elementRange = FindElementExpressionAtCursor(fullText, offset);

            if (elementRange != null)
            {
                var range       = elementRange.Value;
                var elementText = fullText.Substring(range.Start, range.Length);

                if (!string.IsNullOrEmpty(item.ElementParamName))
                {
                    var snippet = BuildWrappingSnippet(item, elementText);

                    textBuffer.Replace(new Microsoft.VisualStudio.Text.Span(range.Start, range.Length), snippet);
                }
                else
                {
                    var snippet = BuildSimpleSnippet(item);
                    var indent  = GetIndentation(fullText, range.Start);

                    textBuffer.Insert(range.Start, snippet + ",\r\n" + indent);
                }
            }
            else
            {
                var snippet = BuildSimpleSnippet(item);

                textBuffer.Insert(offset, snippet);
            }
        }

        /// <summary>
        /// Validates if the specified character offset is within a member or variable declaration
        /// that produces an Element or VisualNode.
        /// </summary>
        /// <param name="sourceText">The full document source text.</param>
        /// <param name="offset">The zero-based character offset.</param>
        /// <returns>True if the insertion location is valid; otherwise, false.</returns>
        public static bool IsValidInsertionLocation(string sourceText, int offset)
        {
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetRoot();

            if (offset < 0 || offset > sourceText.Length)
            {
                return false;
            }

            var token = root.FindToken(offset);
            var node  = token.Parent;

            while (node != null)
            {
                if (node is MethodDeclarationSyntax method)
                {
                    return IsElementOrVisualNode(method.ReturnType.ToString());
                }

                if (node is LocalFunctionStatementSyntax localFunc)
                {
                    return IsElementOrVisualNode(localFunc.ReturnType.ToString());
                }

                if (node is PropertyDeclarationSyntax property)
                {
                    return IsElementOrVisualNode(property.Type.ToString());
                }

                if (node is IndexerDeclarationSyntax indexer)
                {
                    return IsElementOrVisualNode(indexer.Type.ToString());
                }

                if (node is VariableDeclarationSyntax varDecl)
                {
                    return IsElementOrVisualNode(varDecl.Type.ToString());
                }

                if (node is FieldDeclarationSyntax field)
                {
                    return IsElementOrVisualNode(field.Declaration.Type.ToString());
                }

                if (node is TypeDeclarationSyntax || node is NamespaceDeclarationSyntax || node is CompilationUnitSyntax)
                {
                    return false;
                }

                node = node.Parent;
            }

            return false;
        }

        private static bool IsElementOrVisualNode(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            return typeName.Contains("Element") ||
                   typeName.Contains("VisualNode") ||
                   typeName.Contains("IElement");
        }

        /// <summary>
        /// Inserts the component relative to an existing Element expression: wraps or inserts before.
        /// </summary>
        /// <param name="textDoc">The active text document.</param>
        /// <param name="item">The component to insert.</param>
        /// <param name="fullText">The full document text.</param>
        /// <param name="range">The span of the found Element expression.</param>
        private void InsertWithElementContext(TextDocument textDoc, ComponentItem item, string fullText, (int Start, int Length) range)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var elementText = fullText.Substring(range.Start, range.Length);

            if (!string.IsNullOrEmpty(item.ElementParamName))
            {
                var snippet = BuildWrappingSnippet(item, elementText);
                ReplaceRange(textDoc, fullText, range.Start, range.Length, snippet);
            }
            else
            {
                var snippet = BuildSimpleSnippet(item);
                var indent  = GetIndentation(fullText, range.Start);

                InsertTextAtOffset(textDoc, fullText, range.Start, snippet + ",\r\n" + indent);
            }
        }

        /// <summary>
        /// Builds a simple factory call snippet without wrapping.
        /// </summary>
        /// <param name="item">The component item.</param>
        /// <returns>The factory call text.</returns>
        private static string BuildSimpleSnippet(ComponentItem item)
        {
            var paramList = item.FactoryParams.Length > 0
                ? string.Join(", ", item.FactoryParams)
                : "";

            return $"{item.Name}({paramList})";
        }

        /// <summary>
        /// Builds a factory call that wraps an existing expression as the Element parameter.
        /// </summary>
        /// <param name="item">The component item.</param>
        /// <param name="innerExpression">The existing Element expression text to wrap.</param>
        /// <returns>The wrapping factory call text.</returns>
        private static string BuildWrappingSnippet(ComponentItem item, string innerExpression)
        {
            var paramParts = new List<string>(item.FactoryParams);
            bool replaced  = false;

            for (int i = 0; i < paramParts.Count; i++)
            {
                if (paramParts[i].StartsWith(item.ElementParamName + ": "))
                {
                    paramParts[i] = $"{item.ElementParamName}: {innerExpression}";
                    replaced      = true;

                    break;
                }
            }

            if (!replaced)
            {
                paramParts.Add($"{item.ElementParamName}: {innerExpression}");
            }

            return $"{item.Name}({string.Join(", ", paramParts)})";
        }

        /// <summary>
        /// Gets the full text content of a TextDocument.
        /// </summary>
        /// <param name="textDoc">The text document.</param>
        /// <returns>The full document text.</returns>
        private static string GetFullDocumentText(TextDocument textDoc)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var startPoint = textDoc.StartPoint.CreateEditPoint();

            return startPoint.GetText(textDoc.EndPoint);
        }

        /// <summary>
        /// Converts DTE cursor position to a character offset in the document text.
        /// </summary>
        /// <param name="textDoc">The text document.</param>
        /// <param name="fullText">The full document text.</param>
        /// <returns>The zero-based character offset.</returns>
        private static int GetCursorOffset(TextDocument textDoc, string fullText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var targetLine = textDoc.Selection.ActivePoint.Line;
            var targetCol  = textDoc.Selection.ActivePoint.LineCharOffset;
            int currentLine = 1;

            for (int i = 0; i < fullText.Length; i++)
            {
                if (currentLine == targetLine)
                {
                    return i + targetCol - 1;
                }

                if (fullText[i] == '\n')
                {
                    currentLine++;
                }
            }

            return fullText.Length;
        }

        /// <summary>
        /// Finds the span of an Element-producing invocation expression at the cursor offset.
        /// Walks up to capture the full fluent modifier chain.
        /// </summary>
        /// <param name="sourceText">The full document source text.</param>
        /// <param name="cursorOffset">The zero-based cursor offset.</param>
        /// <returns>The start and length of the expression, or null if none found.</returns>
        private static (int Start, int Length)? FindElementExpressionAtCursor(string sourceText, int cursorOffset)
        {
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetRoot();

            if (cursorOffset >= sourceText.Length)
            {
                return null;
            }

            var token = root.FindToken(cursorOffset);

            var invocation = FindContainingInvocation(token);

            if (invocation == null)
            {
                return null;
            }

            var baseName = GetBaseInvocationName(invocation);

            if (string.IsNullOrEmpty(baseName) || !char.IsUpper(baseName[0]))
            {
                return null;
            }

            // Walk up to capture the full fluent modifier chain
            Microsoft.CodeAnalysis.SyntaxNode expression = invocation;

            while (expression.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax &&
                   expression.Parent.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax outerInv)
            {
                expression = outerInv;
            }

            return (expression.Span.Start, expression.Span.Length);
        }

        /// <summary>
        /// Walks up from a token to find the nearest containing InvocationExpressionSyntax.
        /// </summary>
        /// <param name="token">The syntax token at the cursor.</param>
        /// <returns>The invocation expression, or null.</returns>
        private static Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax? FindContainingInvocation(Microsoft.CodeAnalysis.SyntaxToken token)
        {
            var node = token.Parent;

            while (node != null)
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax inv)
                {
                    return inv;
                }

                node = node.Parent;
            }

            return null;
        }

        /// <summary>
        /// Extracts the base method name from an invocation, drilling through fluent chains.
        /// </summary>
        /// <param name="node">The syntax node to examine.</param>
        /// <returns>The base method identifier name.</returns>
        private static string GetBaseInvocationName(Microsoft.CodeAnalysis.SyntaxNode node)
        {
            while (true)
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax inv)
                {
                    node = inv.Expression;
                }
                else if (node is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax ma)
                {
                    node = ma.Expression;
                }
                else if (node is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax id)
                {
                    return id.Identifier.Text;
                }
                else
                {
                    return "";
                }
            }
        }

        /// <summary>
        /// Replaces a range of text in the document.
        /// </summary>
        /// <param name="textDoc">The text document.</param>
        /// <param name="fullText">The full document text for offset conversion.</param>
        /// <param name="start">The zero-based start offset.</param>
        /// <param name="length">The number of characters to replace.</param>
        /// <param name="replacement">The replacement text.</param>
        private static void ReplaceRange(TextDocument textDoc, string fullText, int start, int length, string replacement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var (startLine, startCol) = OffsetToLineColumn(fullText, start);
            var (endLine, endCol)     = OffsetToLineColumn(fullText, start + length);

            var startEp = textDoc.StartPoint.CreateEditPoint();
            startEp.MoveToLineAndOffset(startLine, startCol);

            var endEp = textDoc.StartPoint.CreateEditPoint();
            endEp.MoveToLineAndOffset(endLine, endCol);

            startEp.Delete(endEp);
            startEp.Insert(replacement);
        }

        /// <summary>
        /// Inserts text at a character offset in the document.
        /// </summary>
        /// <param name="textDoc">The text document.</param>
        /// <param name="fullText">The full document text for offset conversion.</param>
        /// <param name="offset">The zero-based offset to insert at.</param>
        /// <param name="text">The text to insert.</param>
        private static void InsertTextAtOffset(TextDocument textDoc, string fullText, int offset, string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var (line, col) = OffsetToLineColumn(fullText, offset);

            var ep = textDoc.StartPoint.CreateEditPoint();
            ep.MoveToLineAndOffset(line, col);
            ep.Insert(text);
        }

        /// <summary>
        /// Converts a zero-based character offset to 1-based DTE line and column.
        /// </summary>
        /// <param name="text">The full text.</param>
        /// <param name="offset">The zero-based offset.</param>
        /// <returns>A 1-based (Line, Column) tuple.</returns>
        private static (int Line, int Column) OffsetToLineColumn(string text, int offset)
        {
            int line = 1;
            int col  = 1;

            for (int i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    col = 1;
                }
                else if (text[i] != '\r')
                {
                    col++;
                }
            }

            return (line, col);
        }

        /// <summary>
        /// Extracts the leading whitespace indentation at the line containing the given offset.
        /// </summary>
        /// <param name="text">The full text.</param>
        /// <param name="offset">The offset to find indentation for.</param>
        /// <returns>The whitespace prefix of the line.</returns>
        private static string GetIndentation(string text, int offset)
        {
            int lineStart = offset;

            while (lineStart > 0 && text[lineStart - 1] != '\n')
            {
                lineStart--;
            }

            int end = lineStart;

            while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            {
                end++;
            }

            return text.Substring(lineStart, end - lineStart);
        }

        /// <summary>
        /// Determines whether a type name represents an Element type.
        /// </summary>
        /// <param name="typeName">The type name to check.</param>
        /// <returns>True if the type is Element or Element?.</returns>
        private static bool IsElementType(string typeName)
        {
            return typeName == "Element" || typeName == "Element?";
        }

        /// <summary>
        /// Returns a type-appropriate default placeholder string for a parameter type.
        /// </summary>
        /// <param name="typeName">The C# type name.</param>
        /// <returns>A placeholder value suitable for code insertion.</returns>
        private static string GetDefaultPlaceholder(string typeName)
        {
            if (typeName == "string" || typeName == "String")
            {
                return "\"\"";
            }

            if (typeName == "int" || typeName == "Int32" ||
                typeName == "long" || typeName == "Int64" ||
                typeName == "short" || typeName == "Int16" ||
                typeName == "byte" || typeName == "Byte")
            {
                return "0";
            }

            if (typeName == "double" || typeName == "Double" ||
                typeName == "float" || typeName == "Single" ||
                typeName == "decimal" || typeName == "Decimal")
            {
                return "0.0";
            }

            if (typeName == "bool" || typeName == "Boolean")
            {
                return "false";
            }

            return "null";
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
        /// Gets or sets the description of the component (e.g. from execution statement).
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Gets the display name of the component, appending description if available.
        /// </summary>
        public string DisplayName => Name == "App" && !string.IsNullOrEmpty(Description) ? $"{Name} ({Description})" : Name;

        /// <summary>
        /// Gets or sets the type grouping ("Core elements" or "User Components").
        /// </summary>
        public string TypeGroup { get; set; } = "";

        /// <summary>
        /// Gets or sets the file path for custom components.
        /// </summary>
        public string SourceFile { get; set; } = "";

        /// <summary>
        /// Gets or sets the DSL factory parameter placeholders for code insertion.
        /// </summary>
        public string[] FactoryParams { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the name of the Element-type parameter, used for wrapping insertions.
        /// </summary>
        public string ElementParamName { get; set; } = "";

        /// <summary>
        /// Gets a value indicating whether this item has a source file (user component, not core).
        /// </summary>
        public bool HasSourceFile => !string.IsNullOrEmpty(SourceFile);

        /// <summary>
        /// Gets the filename (without path) where the component is implemented.
        /// </summary>
        public string SourceFileName => HasSourceFile ? Path.GetFileName(SourceFile) : "Dsl.cs";
    }
}
