# Reactor.VisualStudio

Reactor.VisualStudio is a Visual Studio extension that enhances the developer experience for building WinUI 3 applications using the Microsoft.UI.Reactor framework. It provides tools for exploring, previewing, and quickly integrating Reactor components into your code.

## Features

### Components Tool Window

The Components Tool Window (`View > Other Windows > Reactor Components`) scans your open C# files for classes derived from `Component`. It lists these components, grouping them by their component type or application entry point.

**Key capabilities:**
- **Live Discovery:** Automatically updates as you edit your files to show the available components.
- **App Descriptions:** For `App` components, it scans for `ReactorApp.Run<T>(...)` calls and uses the provided description to make it easier to identify the application entry point.
- **Context Menu:** Right-click on a component to quickly navigate to its implementation (`Go to Implementation`) or insert it at the current cursor location (`Insert code`).
- **Tooltips:** Hover over a component in the list to see the name of the file where it is implemented.

### Drag-and-Drop Insertion

You can drag components directly from the Components Tool Window and drop them into your active code editor. 

The extension intelligently analyzes the drop location using Roslyn:
- If the drop location is within a valid element-producing context (e.g., inside a `Render` method or an expression returning an `Element` or `VisualNode`), the component code will be inserted.
- If you drop the component onto an existing `Element` invocation, the extension will attempt to wrap the existing element within the dropped component.
- **Validation:** If the drop location is not a valid context for a Reactor component, the insertion is blocked, and an error is logged to the "Reactor Preview" output window.

### Code Insertion at Cursor

Similar to drag-and-drop, you can insert a component at your current cursor position using the `Insert code` context menu option in the Components Tool Window. The same intelligent validation and wrapping logic applies.

### Live Preview

*(Currently in development/preview)*

The extension aims to provide a live HTML-based preview of your Reactor components. It uses AST parsing to understand the structure of your `Render` methods, resolving interpolated strings and parameter mappings where possible to provide a static approximation of your UI layout.

### Logging and Diagnostics

The extension provides a dedicated Output Window pane named **"Reactor Preview"**. 

- **Accessing the Output Window:** Go to `View > Output`, and select "Reactor Preview" from the "Show output from:" dropdown.
- **Diagnostics:** The extension logs initialization steps, component discovery, insertion validation errors, and other diagnostic information to this pane, which is helpful for troubleshooting.

## Usage Guide

1. **Open a Reactor Project:** Open a Visual Studio solution containing a Microsoft.UI.Reactor project.
2. **Open Components Window:** Navigate to `View > Other Windows > Reactor Components`.
3. **Explore Components:** The window will populate with components discovered in your open files.
4. **Insert a Component:** 
   - Position your text cursor inside a `Render` method in your code editor.
   - Drag a component from the Components Tool Window and drop it at the cursor position.
   - Alternatively, right-click the component and select `Insert code`.
5. **View Diagnostics:** If an insertion fails or you want to see what the extension is doing, check the "Reactor Preview" output window pane.

## Development and Architecture

This extension is built using the Visual Studio Extensibility framework (VSSDK).

- **`ReactorInProcPackage.cs`**: The main package entry point, responsible for command initialization and tool window registration.
- **`ComponentsToolWindow`**: The UI for listing available components.
- **`ReactorComponentDropHandler`**: Implements MEF drop handling to support drag-and-drop code insertion into the Visual Studio editor.
- **`AstParser`**: Uses Roslyn to parse C# syntax trees and understand component structures for preview and insertion context validation.
