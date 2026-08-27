namespace Reactor.VisualStudio.UiElements
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Input;
    using System.Windows.Media;
    using EnvDTE;
    using Microsoft.VisualStudio.Imaging;
    using Microsoft.VisualStudio.Imaging.Interop;
    using Microsoft.VisualStudio.Shell;
    using Reactor.VisualStudio.Services;

    /// <summary>
    /// Visual Studio status bar widget displaying active Reactor preview state and components.
    /// </summary>
    public sealed class ReactorStatusBarControl : Border, IDisposable
    {
        private readonly CrispImage _icon;
        private readonly TextBlock _statusText;
        private readonly EnvDTE80.DTE2 _dte;
        private readonly SelectionEvents _selectionEvents;
        private readonly DocumentEvents _documentEvents;
        private readonly WindowEvents _windowEvents;
        private readonly SolutionEvents _solutionEvents;
        private readonly HttpClient _httpClient = new();

        private Popup? _popup;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReactorStatusBarControl"/> class.
        /// </summary>
        public ReactorStatusBarControl()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            this.Margin              = new Thickness(6, 0, 6, 0);
            this.Padding             = new Thickness(6, 3, 6, 3);
            this.CornerRadius        = new CornerRadius(3);
            this.VerticalAlignment   = VerticalAlignment.Center;
            this.HorizontalAlignment = HorizontalAlignment.Right;
            this.Background          = Brushes.Transparent;
            this.Cursor              = Cursors.Hand;
            this.ToolTip             = "Reactor Preview Status (Click to see file components)";

            _dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as EnvDTE80.DTE2
                ?? throw new InvalidOperationException("Failed to acquire DTE2 service.");

            _selectionEvents = _dte.Events.SelectionEvents;
            _selectionEvents.OnChange += OnSelectionChanged;

            _documentEvents = _dte.Events.DocumentEvents;
            _documentEvents.DocumentSaved += OnDocumentSaved;

            _windowEvents = _dte.Events.WindowEvents;
            _windowEvents.WindowActivated += OnWindowActivated;

            _solutionEvents = _dte.Events.SolutionEvents;
            _solutionEvents.Opened += OnSolutionOpened;
            _solutionEvents.AfterClosing += OnSolutionClosed;

            var panel = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _icon = new CrispImage
            {
                Width             = 14,
                Height            = 14,
                Margin            = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Moniker           = KnownMonikers.StatusInformation
            };

            _statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 11.5
            };

            _statusText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            panel.Children.Add(_icon);
            panel.Children.Add(_statusText);

            this.Child = panel;

            this.MouseEnter         += OnMouseEnter;
            this.MouseLeave         += OnMouseLeave;
            this.MouseLeftButtonUp += OnMouseLeftButtonUp;

            UpdateState();
        }

        /// <summary>
        /// Scans the active document and updates the status bar label.
        /// </summary>
        public void UpdateState()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_isDisposed)
            {
                return;
            }

            try
            {
                var activeFile = GetActiveFilePath();

                if (activeFile is null || !activeFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    _statusText.Text = "Reactor Preview";
                    _icon.Moniker    = KnownMonikers.StatusInformation;
                    _icon.Opacity    = 0.5;

                    return;
                }

                _icon.Opacity = 1.0;
                _icon.Moniker = KnownMonikers.Play;

                var components = GetComponentsInActiveFile();

                if (components.Count > 0)
                {
                    _statusText.Text = $"Reactor: {components[0]}";
                }
                else
                {
                    _statusText.Text = "Reactor: No Component";
                }
            }
            catch (Exception ex)
            {
                _statusText.Text = "Reactor: Error";
                _icon.Moniker    = KnownMonikers.StatusError;
                System.Diagnostics.Debug.WriteLine($"[Reactor] Error updating status bar: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            _selectionEvents.OnChange -= OnSelectionChanged;
            _documentEvents.DocumentSaved -= OnDocumentSaved;
            _windowEvents.WindowActivated -= OnWindowActivated;

            if (_solutionEvents != null)
            {
                _solutionEvents.Opened -= OnSolutionOpened;
                _solutionEvents.AfterClosing -= OnSolutionClosed;
            }

            this.MouseEnter         -= OnMouseEnter;
            this.MouseLeave         -= OnMouseLeave;
            this.MouseLeftButtonUp -= OnMouseLeftButtonUp;
        }

        private string? GetActiveFilePath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (_dte.ActiveDocument != null)
                {
                    return _dte.ActiveDocument.FullName;
                }
            }
            catch
            {
                // Ignore if active document is not available
            }

            return null;
        }

        private List<string> GetComponentsInActiveFile()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var doc = _dte.ActiveDocument;

                if (doc != null && doc.Object("TextDocument") is TextDocument textDoc)
                {
                    var startPoint = textDoc.StartPoint.CreateEditPoint();
                    var content    = startPoint.GetText(textDoc.EndPoint);

                    return ComponentParser.ParseComponents(content);
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return new List<string>();
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            this.Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128));
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            this.Background = Brushes.Transparent;
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_popup == null)
            {
                _popup = new Popup
                {
                    Placement          = PlacementMode.Top,
                    PlacementTarget    = this,
                    StaysOpen          = false,
                    AllowsTransparency = true,
                    PopupAnimation     = PopupAnimation.Fade
                };
            }

            RebuildPopupContent();

            _popup.IsOpen = true;
        }

        private void RebuildPopupContent()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_popup == null)
            {
                return;
            }

            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(12)
            };

            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);

            var mainStack = new StackPanel { Orientation = Orientation.Vertical };

            var header = new TextBlock
            {
                Text       = "File Reactor Components",
                FontWeight = FontWeights.Bold,
                FontSize   = 12.5,
                Margin     = new Thickness(0, 0, 0, 6)
            };

            header.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            mainStack.Children.Add(header);

            var separator = new Border
            {
                Height              = 1,
                Margin              = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            separator.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBorderKey);
            mainStack.Children.Add(separator);

            var components = GetComponentsInActiveFile();

            if (components.Count == 0)
            {
                var noneText = new TextBlock
                {
                    Text      = "No Reactor Components in active file.",
                    FontStyle = FontStyles.Italic,
                    Margin    = new Thickness(0, 4, 0, 4)
                };

                noneText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                mainStack.Children.Add(noneText);
            }
            else
            {
                foreach (var comp in components)
                {
                    var btn = new Button
                    {
                        Content           = comp,
                        Margin            = new Thickness(0, 2, 0, 2),
                        Padding           = new Thickness(8, 4, 8, 4),
                        Cursor            = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left
                    };

                    btn.SetResourceReference(Button.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
                    btn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                    btn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);

                    var capturedCompName = comp;

                    btn.Click += (s, e) =>
                    {
                        _ = SwitchComponentAsync(capturedCompName);
                        _popup.IsOpen = false;
                    };

                    mainStack.Children.Add(btn);
                }
            }

            border.Child = mainStack;
            _popup.Child = border;
        }

        private async Task SwitchComponentAsync(string componentName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (ReactorInProcPackage.Instance != null)
            {
                await ReactorInProcPackage.Instance.SelectComponentAsync(componentName);

                _statusText.Text = $"Reactor: {componentName}";

                NavigateCursorToClass(componentName);
            }
        }

        /// <summary>
        /// Moves the active document text editor cursor to the start of the class declaration of the specified component.
        /// </summary>
        /// <param name="componentName">The component class name to locate.</param>
        private void NavigateCursorToClass(string componentName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var doc = _dte.ActiveDocument;

                if (doc != null && doc.Object("TextDocument") is TextDocument textDoc)
                {
                    var editPoint = textDoc.StartPoint.CreateEditPoint();
                    var content   = editPoint.GetText(textDoc.EndPoint);
                    var tree      = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(content);
                    var root      = tree.GetRoot();
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

        private void OnSelectionChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateState();
        }

        private void OnDocumentSaved(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateState();
        }

        private void OnWindowActivated(EnvDTE.Window gotFocus, EnvDTE.Window lostFocus)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateState();
        }

        private void OnSolutionOpened()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateState();
        }

        private void OnSolutionClosed()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateState();
        }
    }
}
