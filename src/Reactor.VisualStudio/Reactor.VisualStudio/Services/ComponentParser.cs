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
        /// Scans C# source code text and returns component info including required constructor parameters.
        /// </summary>
        /// <param name="sourceCode">The raw C# source code string.</param>
        /// <returns>A list of <see cref="ComponentInfo"/> objects with name and parameter data.</returns>
        public static List<ComponentInfo> ParseComponentsWithParams(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                return new List<ComponentInfo>();
            }

            var tree = CSharpSyntaxTree.ParseText(sourceCode);

            var root = tree.GetRoot();

            var results = root.DescendantNodes()
                              .OfType<ClassDeclarationSyntax>()
                              .Where(c => IsReactorComponent(c))
                              .Select(c => BuildComponentInfo(c))
                              .ToList();

            return results;
        }

        /// <summary>
        /// Builds a <see cref="ComponentInfo"/> from a class declaration, extracting required constructor parameters.
        /// </summary>
        /// <param name="classDecl">The class declaration syntax node.</param>
        /// <returns>A <see cref="ComponentInfo"/> with name and parameters.</returns>
        private static ComponentInfo BuildComponentInfo(ClassDeclarationSyntax classDecl)
        {
            var name = classDecl.Identifier.Text;
            var parameters = new List<ComponentParameterInfo>();

            // Look for the primary constructor or the first explicit constructor
            var constructor = classDecl.DescendantNodes()
                                       .OfType<ConstructorDeclarationSyntax>()
                                       .FirstOrDefault();

            if (constructor?.ParameterList != null)
            {
                foreach (var param in constructor.ParameterList.Parameters)
                {
                    // Only include required parameters (those without a default value)
                    if (param.Default == null)
                    {
                        var paramName = param.Identifier.Text;
                        var paramType = param.Type?.ToString() ?? "object";

                        parameters.Add(new ComponentParameterInfo(paramName, paramType));
                    }
                }
            }

            return new ComponentInfo(name, parameters);
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

    /// <summary>
    /// Holds parsed information about a Reactor component class.
    /// </summary>
    public class ComponentInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentInfo"/> class.
        /// </summary>
        /// <param name="name">The component class name.</param>
        /// <param name="parameters">The list of required constructor parameters.</param>
        public ComponentInfo(string name, List<ComponentParameterInfo> parameters)
        {
            Name       = name;
            Parameters = parameters;
        }

        /// <summary>
        /// Gets the component class name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the list of required constructor parameters.
        /// </summary>
        public List<ComponentParameterInfo> Parameters { get; }
    }

    /// <summary>
    /// Describes a single required constructor parameter of a Reactor component.
    /// </summary>
    public class ComponentParameterInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentParameterInfo"/> class.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="typeName">The parameter type name.</param>
        public ComponentParameterInfo(string name, string typeName)
        {
            Name     = name;
            TypeName = typeName;
        }

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the parameter type name.
        /// </summary>
        public string TypeName { get; }
    }
}
