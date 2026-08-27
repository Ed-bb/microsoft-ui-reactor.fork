namespace Reactor.VisualStudio.ToolWindows
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.VisualStudio.Shell;

    /// <summary>
    /// The Visual Studio tool window hosting the Reactor Preview.
    /// </summary>
    [Guid("4a123f11-9a74-4bf8-b618-912b591b61ab")]
    public class PreviewToolWindow : ToolWindowPane
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PreviewToolWindow"/> class.
        /// </summary>
        public PreviewToolWindow()
            : base(null)
        {
            this.Caption = "Reactor Preview";

            this.Content = new PreviewToolWindowControl();
        }
    }
}
