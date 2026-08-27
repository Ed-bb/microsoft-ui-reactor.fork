namespace Reactor.VisualStudio.Commands
{
    using System;
    using System.ComponentModel.Design;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.Shell;

    /// <summary>
    /// Command handler for showing the Reactor Components tool window.
    /// </summary>
    public sealed class ShowComponentsCommand
    {
        /// <summary>
        /// Command set GUID string.
        /// </summary>
        public static readonly Guid CommandSet = new("e87f1190-2c77-4c8d-8fb8-2231ab46618e");

        /// <summary>
        /// Command ID for Reactor Components.
        /// </summary>
        public const int CommandId = 0x0101;

        private readonly AsyncPackage _package;

        private ShowComponentsCommand(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static ShowComponentsCommand? Instance { get; private set; }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            ReactorInProcPackage.Log("ShowComponentsCommand: InitializeAsync started.");

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as IMenuCommandService;

            if (commandService != null)
            {
                var menuCommandId = new CommandID(CommandSet, CommandId);
                var menuItem = new OleMenuCommand(OnExecute, menuCommandId);

                commandService.AddCommand(menuItem);

                ReactorInProcPackage.Log("ShowComponentsCommand: Registered components menu command.");
            }
            else
            {
                ReactorInProcPackage.Log("ShowComponentsCommand: IMenuCommandService was null. Command was not registered.");
            }

            Instance = new ShowComponentsCommand(package);

            ReactorInProcPackage.Log("ShowComponentsCommand: InitializeAsync completed.");
        }

        private static void OnExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ReactorInProcPackage.Log("ShowComponentsCommand: Components menu command invoked.");

            _ = OpenComponentsWindowAsync();
        }

        private static async Task OpenComponentsWindowAsync()
        {
            try
            {
                ReactorInProcPackage.Log("ShowComponentsCommand: OpenComponentsWindowAsync started.");

                var package = Instance?._package as ReactorInProcPackage;

                if (package != null)
                {
                    ReactorInProcPackage.Log("ShowComponentsCommand: Found ReactorInProcPackage instance. Calling ShowComponentsWindowAsync.");

                    await package.ShowComponentsWindowAsync(default);

                    ReactorInProcPackage.Log("ShowComponentsCommand: ShowComponentsWindowAsync returned.");
                }
                else
                {
                    ReactorInProcPackage.Log("ShowComponentsCommand: ReactorInProcPackage instance is null. Cannot open components window.");
                }
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ShowComponentsCommand: Error opening components window: {ex.Message}");
            }
        }
    }
}
