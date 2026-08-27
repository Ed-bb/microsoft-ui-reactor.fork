namespace Reactor.VisualStudio.ToolWindows
{
    using System;
    using System.ComponentModel.Composition;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Text.Editor;
    using Microsoft.VisualStudio.Text.Editor.DragDrop;
    using Microsoft.VisualStudio.Utilities;

    /// <summary>
    /// MEF provider to supply the drop handler for Reactor component drops.
    /// </summary>
    [Export(typeof(IDropHandlerProvider))]
    [DropFormat(ComponentsToolWindowControl.DragDropFormat)]
    [Name("ReactorComponentDropHandler")]
    [Order(Before = "DefaultFileDropHandler")]
    public class ReactorComponentDropHandlerProvider : IDropHandlerProvider
    {
        /// <summary>
        /// Creates and returns the drop handler instance for the specified text view.
        /// </summary>
        /// <param name="wpfTextView">The text view being dragged over.</param>
        /// <returns>The drop handler instance.</returns>
        public IDropHandler GetAssociatedDropHandler(IWpfTextView wpfTextView)
        {
            return new ReactorComponentDropHandler(wpfTextView);
        }
    }

    /// <summary>
    /// Handles drag-and-drop operations for Reactor components in the text editor.
    /// </summary>
    public class ReactorComponentDropHandler : IDropHandler
    {
        private readonly IWpfTextView _wpfTextView;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReactorComponentDropHandler"/> class.
        /// </summary>
        /// <param name="wpfTextView">The target text view.</param>
        public ReactorComponentDropHandler(IWpfTextView wpfTextView)
        {
            _wpfTextView = wpfTextView;
        }

        /// <inheritdoc />
        public DragDropPointerEffects HandleDragStarted(DragDropInfo dragDropInfo)
        {
            return DragDropPointerEffects.Copy;
        }

        /// <inheritdoc />
        public bool IsDropEnabled(DragDropInfo dragDropInfo)
        {
            return true;
        }

        /// <inheritdoc />
        public DragDropPointerEffects HandleDraggingOver(DragDropInfo dragDropInfo)
        {
            return DragDropPointerEffects.Copy;
        }

        /// <inheritdoc />
        public DragDropPointerEffects HandleDataDropped(DragDropInfo dragDropInfo)
        {
            try
            {
                if (dragDropInfo.Data.GetDataPresent(ComponentsToolWindowControl.DragDropFormat))
                {
                    var item = dragDropInfo.Data.GetData(ComponentsToolWindowControl.DragDropFormat) as ComponentItem;

                    if (item != null)
                    {
                        var position = dragDropInfo.VirtualBufferPosition.Position;

                        ComponentsToolWindowControl.InsertCodeAtOffset(_wpfTextView.TextBuffer, item, position.Position);

                        return DragDropPointerEffects.Copy;
                    }
                }
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ReactorComponentDropHandler: HandleDataDropped failed: {ex}");
            }

            return DragDropPointerEffects.None;
        }

        /// <inheritdoc />
        public void HandleDragCompleted(DragDropInfo dragDropInfo, DragDropPointerEffects finalEffect)
        {
        }

        /// <inheritdoc />
        public void HandleDragCanceled()
        {
        }
    }
}
