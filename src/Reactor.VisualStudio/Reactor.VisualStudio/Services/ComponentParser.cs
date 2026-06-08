namespace Reactor.VisualStudio.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    /// <summary>
    /// Parses C# source code files to discover Reactor component classes using Roslyn.
    /// </summary>
    public static class ComponentParser
    {
        /// <summary>
        /// Scans C# source code text and returns all class names inheriting from a Reactor Component.
        /// </summary>
        /// <param name="sourceCode">The raw C# source code string.</param>
        /// <returns>A list of class names inheriting from Reactor Component.</returns>
        public static List<string> ParseComponents(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                return new List<string>();
            }

            var tree = CSharpSyntaxTree.ParseText(sourceCode);

            var root = tree.GetRoot();

            var classes = root.DescendantNodes()
                              .OfType<ClassDeclarationSyntax>()
                              .Where(c => IsReactorComponent(c))
                              .Select(c => c.Identifier.Text)
                              .ToList();

            return classes;
        }

        /// <summary>
        /// Determines if a given class declaration represents a Reactor component.
        /// </summary>
        /// <param name="classDecl">The class declaration syntax to check.</param>
        /// <returns>True if the class is a Reactor component; otherwise, false.</returns>
        private static bool IsReactorComponent(ClassDeclarationSyntax classDecl)
        {
            if (classDecl.BaseList == null)
            {
                return false;
            }

            var matches = classDecl.BaseList.Types
                .Select(t => t.Type.ToString())
                .Any(name => name == "Component" ||
                            name.StartsWith("Component<") ||
                            name.Contains("Microsoft.UI.Reactor.Component"));

            return matches;
        }
    }
}
