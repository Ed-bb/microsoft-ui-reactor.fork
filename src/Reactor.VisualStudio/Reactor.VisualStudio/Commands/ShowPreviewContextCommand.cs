namespace Reactor.VisualStudio.Commands
{
    using System;
    using System.ComponentModel.Design;
    using System.IO;
    using System.Threading.Tasks;
    using EnvDTE;
    using Microsoft.VisualStudio.Shell;
    using Reactor.VisualStudio;
    using Reactor.VisualStudio.Services;

    /// <summary>
    /// Command handler for the Solution Explorer C# file context menu "Show Preview..." command.
    /// </summary>
    public sealed class ShowPreviewContextCommand
    {
        /// <summary>
        /// Command set GUID string.
        /// </summary>
        public static readonly Guid CommandSet = new("e87f1190-2c77-4c8d-8fb8-2231ab46618e");

        /// <summary>
        /// Command ID for Show Preview.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command ID for Reactor Components.
        /// </summary>
        public const int ComponentsCommandId = 0x0101;

        private readonly AsyncPackage _package;

        private ShowPreviewContextCommand(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static ShowPreviewContextCommand? Instance { get; private set; }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            ReactorInProcPackage.Log("ShowPreviewContextCommand: InitializeAsync started.");

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as IMenuCommandService;

            if (commandService != null)
            {
                var previewCommandId = new CommandID(CommandSet, CommandId);
                var previewMenuItem = new OleMenuCommand(OnExecute, previewCommandId);

                previewMenuItem.BeforeQueryStatus += OnBeforeQueryStatus;

                commandService.AddCommand(previewMenuItem);

                var componentsCommandId = new CommandID(CommandSet, ComponentsCommandId);
                var componentsMenuItem = new OleMenuCommand(OnExecuteComponents, componentsCommandId);

                commandService.AddCommand(componentsMenuItem);

                ReactorInProcPackage.Log("ShowPreviewContextCommand: Registered preview and components menu commands.");
            }
            else
            {
                ReactorInProcPackage.Log("ShowPreviewContextCommand: IMenuCommandService was null. Commands were not registered.");
            }

            Instance = new ShowPreviewContextCommand(package);

            ReactorInProcPackage.Log("ShowPreviewContextCommand: InitializeAsync completed.");
        }

        private static void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand menuItem)
            {
                var path = GetSelectedPath();

                if (path is null || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    menuItem.Visible = false;
                    menuItem.Enabled = false;

                    return;
                }

                menuItem.Visible = true;

                var hasComponents = false;

                try
                {
                    if (File.Exists(path))
                    {
                        var content = File.ReadAllText(path);
                        var comps   = ComponentParser.ParseComponents(content);

                        hasComponents = comps.Count > 0;
                    }
                }
                catch
                {
                    // Ignore read exceptions in query status
                }

                menuItem.Enabled = hasComponents;
            }
        }

        private static void OnExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var path = GetSelectedPath();

            if (path is null)
            {
                return;
            }

            if (Instance?._package is ReactorInProcPackage package)
            {
                _ = package.StartPreviewAsync(path, default);
            }
        }

        private static void OnExecuteComponents(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ReactorInProcPackage.Log("ShowPreviewContextCommand: Components menu command invoked.");

            _ = OpenComponentsWindowAsync();
        }

        private static async Task OpenComponentsWindowAsync()
        {
            try
            {
                ReactorInProcPackage.Log("ShowPreviewContextCommand: OpenComponentsWindowAsync started.");

                var package = Instance?._package as ReactorInProcPackage;

                if (package != null)
                {
                    ReactorInProcPackage.Log("ShowPreviewContextCommand: Found ReactorInProcPackage instance. Calling ShowComponentsWindowAsync.");

                    await package.ShowComponentsWindowAsync(default);

                    ReactorInProcPackage.Log("ShowPreviewContextCommand: ShowComponentsWindowAsync returned.");
                }
                else
                {
                    ReactorInProcPackage.Log("ShowPreviewContextCommand: ReactorInProcPackage instance is null. Cannot open components window.");
                }
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ShowPreviewContextCommand: Error opening components window: {ex.Message}");
            }
        }

        private static string? GetSelectedPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as EnvDTE80.DTE2;

            if (dte == null)
            {
                return null;
            }

            if (dte.SelectedItems != null && dte.SelectedItems.Count > 0)
            {
                var selectedItem = dte.SelectedItems.Item(1);

                if (selectedItem?.ProjectItem != null)
                {
                    try
                    {
                        var path = selectedItem.ProjectItem.FileNames[1];

                        if (!string.IsNullOrEmpty(path))
                        {
                            return path;
                        }
                    }
                    catch
                    {
                        // FileNames is 1-indexed, throws if empty
                    }
                }
            }

            if (dte.ActiveDocument != null && !string.IsNullOrEmpty(dte.ActiveDocument.FullName))
            {
                return dte.ActiveDocument.FullName;
            }

            return null;
        }
    }
}
