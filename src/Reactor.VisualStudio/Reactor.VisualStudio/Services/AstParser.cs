namespace Reactor.VisualStudio.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    /// <summary>
    /// Represents a lightweight AST-parsed layout element for preview.
    /// </summary>
    public class AstElement
    {
        /// <summary>
        /// Gets or sets the element name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the text content of the element.
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// Gets or sets the children elements.
        /// </summary>
        public List<AstElement> Children { get; set; } = new();

        /// <summary>
        /// Gets or sets properties of the element (e.g. Margin, FontWeight).
        /// </summary>
        public Dictionary<string, string> Properties { get; set; } = new();
    }

    /// <summary>
    /// Helper class to parse component class definitions directly into syntax trees.
    /// </summary>
    public static class AstParser
    {
        /// <summary>
        /// Parses the Render method of a component class from the syntax tree directly.
        /// </summary>
        /// <param name="sourceCode">The raw C# source code.</param>
        /// <param name="componentName">The name of the component class.</param>
        /// <returns>The root AstElement of the component layout, or null.</returns>
        public static AstElement? ParseAst(string sourceCode, string componentName)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(sourceCode);

                var root = tree.GetRoot();

                var classDecl = root.DescendantNodes()
                                    .OfType<ClassDeclarationSyntax>()
                                    .FirstOrDefault(c => c.Identifier.Text == componentName);

                if (classDecl == null)
                {
                    return null;
                }

                var renderMethod = classDecl.Members
                                            .OfType<MethodDeclarationSyntax>()
                                            .FirstOrDefault(m => m.Identifier.Text == "Render");

                if (renderMethod == null)
                {
                    return null;
                }

                ExpressionSyntax? bodyExpression = null;

                if (renderMethod.ExpressionBody != null)
                {
                    bodyExpression = renderMethod.ExpressionBody.Expression;
                }
                else if (renderMethod.Body != null)
                {
                    var returnStatement = renderMethod.Body.Statements
                                                            .OfType<ReturnStatementSyntax>()
                                                            .FirstOrDefault();

                    if (returnStatement != null)
                    {
                        bodyExpression = returnStatement.Expression;
                    }
                }

                if (bodyExpression != null)
                {
                    return ParseExpression(bodyExpression, classDecl);
                }
            }
            catch
            {
                // Fall back gracefully.
            }

            return null;
        }

        /// <summary>
        /// Retrieves the mapping for a control's primary text parameter, including its index and potential names.
        /// </summary>
        /// <param name="controlName">The name of the control.</param>
        /// <returns>A tuple containing the parameter position and a list of valid parameter names.</returns>
        private static (int position, string[] names) GetTextParameterMapping(string controlName)
        {
            var lower = controlName.ToLowerInvariant();

            return lower switch
            {
                "textblock" or "heading" or "subheading" or "caption" => (0, new[] { "content" }),
                "richtextblock" or "richtext" => (0, new[] { "text" }),
                "button" or "repeatbutton" or "togglebutton" or "threestatetogglebutton" or "dropdownbutton" or "splitbutton" or "togglesplitbutton" or "radiobutton" => (0, new[] { "label" }),
                "hyperlinkbutton" => (0, new[] { "content" }),
                "textbox" => (0, new[] { "value" }),
                "passwordbox" => (0, new[] { "password" }),
                "checkbox" or "threestatecheckbox" => (2, new[] { "label" }),
                "sectionheader" => (0, new[] { "title" }),
                "infobar" => (0, new[] { "title" }),
                "backdrop" => (0, new[] { "kind" }),
                _ => (-1, Array.Empty<string>())
            };
        }

        /// <summary>
        /// Determines if an argument should be treated as the main text/content of a control based on its name, index, or literal type.
        /// </summary>
        /// <param name="controlName">The name of the control.</param>
        /// <param name="argName">The optional name of the argument.</param>
        /// <param name="argIndex">The index of the argument in the argument list.</param>
        /// <param name="argExpr">The expression of the argument.</param>
        /// <returns>True if the argument is a text argument; otherwise, false.</returns>
        private static bool IsTextArgument(string controlName, string? argName, int argIndex, ExpressionSyntax argExpr)
        {
            var mapping = GetTextParameterMapping(controlName);

            if (argName != null)
            {
                if (mapping.names.Length > 0)
                {
                    return mapping.names.Contains(argName.ToLowerInvariant());
                }

                var commonNames = new[] { "content", "label", "text", "value", "password", "title" };

                return commonNames.Contains(argName.ToLowerInvariant());
            }

            if (mapping.position >= 0)
            {
                return argIndex == mapping.position;
            }

            if (argExpr is LiteralExpressionSyntax literal && literal.Token.IsKind(SyntaxKind.StringLiteralToken))
            {
                return argIndex == 0;
            }

            return false;
        }

        /// <summary>
        /// Recursively parses a C# expression into an <see cref="AstElement"/> layout.
        /// </summary>
        /// <param name="expr">The expression syntax to parse.</param>
        /// <param name="classDecl">The class declaration context.</param>
        /// <param name="parameterReplacements">Mapping of parameter names to their caller expressions.</param>
        /// <param name="expandingMethods">Set of methods currently being expanded to prevent recursion.</param>
        /// <returns>A parsed <see cref="AstElement"/> or null if the expression does not represent a renderable element.</returns>
        private static AstElement? ParseExpression(
            ExpressionSyntax expr,
            ClassDeclarationSyntax? classDecl = null,
            Dictionary<string, ExpressionSyntax>? parameterReplacements = null,
            HashSet<string>? expandingMethods = null)
        {
            if (expr == null)
            {
                return null;
            }

            if (expr is IdentifierNameSyntax exprId && parameterReplacements != null && parameterReplacements.TryGetValue(exprId.Identifier.Text, out var replacedExpr))
            {
                return ParseExpression(replacedExpr, classDecl, parameterReplacements, expandingMethods);
            }

            if (expr is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is SimpleNameSyntax simpleName)
                {
                    var resolvedMethodElement = ParseClassMethodInvocation(invocation, simpleName, classDecl, parameterReplacements, expandingMethods);

                    if (resolvedMethodElement != null)
                    {
                        return resolvedMethodElement;
                    }

                    var elem = new AstElement { Name = simpleName.Identifier.Text };

                    if (simpleName is GenericNameSyntax genericName)
                    {
                        var typeArgs = genericName.TypeArgumentList.Arguments
                                                   .Select(a => a.ToString())
                                                   .ToList();

                        if (typeArgs.Count > 0)
                        {
                            elem.Properties["GenericType"] = string.Join(", ", typeArgs);
                        }
                    }

                    ParseArguments(elem, invocation.ArgumentList.Arguments, classDecl, parameterReplacements, expandingMethods);

                    return elem;
                }

                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var baseElem = ParseExpression(memberAccess.Expression, classDecl, parameterReplacements, expandingMethods);

                    if (baseElem != null)
                    {
                        var methodName = memberAccess.Name.Identifier.Text;

                        var argTexts = new List<string>();

                        foreach (var arg in invocation.ArgumentList.Arguments)
                        {
                            var argExpr = arg.Expression;

                            if (argExpr is IdentifierNameSyntax argId && parameterReplacements != null && parameterReplacements.TryGetValue(argId.Identifier.Text, out var replaced))
                            {
                                argExpr = replaced;
                            }

                            argTexts.Add(argExpr.ToString());
                        }

                        baseElem.Properties[methodName] = string.Join(", ", argTexts);

                        return baseElem;
                    }
                }
            }

            if (expr is ParenthesizedExpressionSyntax parenthesized)
            {
                return ParseExpression(parenthesized.Expression, classDecl, parameterReplacements, expandingMethods);
            }

            if (expr is WithExpressionSyntax withExpr)
            {
                return ParseWithExpression(withExpr, classDecl, parameterReplacements, expandingMethods);
            }

            return null;
        }

        /// <summary>
        /// Parses an invocation of a helper method defined in the active component class.
        /// </summary>
        /// <param name="invocation">The invocation expression syntax node.</param>
        /// <param name="simpleName">The name syntax of the called method.</param>
        /// <param name="classDecl">The active class declaration syntax node.</param>
        /// <param name="parameterReplacements">Dictionary of caller argument replacements for parameter identifiers.</param>
        /// <param name="expandingMethods">Set of method names currently being expanded to prevent infinite recursion.</param>
        /// <returns>The parsed AstElement containing the expanded method content, or null.</returns>
        private static AstElement? ParseClassMethodInvocation(
            InvocationExpressionSyntax invocation,
            SimpleNameSyntax simpleName,
            ClassDeclarationSyntax? classDecl,
            Dictionary<string, ExpressionSyntax>? parameterReplacements,
            HashSet<string>? expandingMethods)
        {
            var methodName = simpleName.Identifier.Text;

            var methodDecl = classDecl?.Members
                                        .OfType<MethodDeclarationSyntax>()
                                        .FirstOrDefault(m => m.Identifier.Text == methodName);

            if (methodDecl == null || (expandingMethods != null && expandingMethods.Contains(methodName)))
            {
                return null;
            }

            var returnTypeName = methodDecl.ReturnType.ToString();

            bool returnsElement = returnTypeName.Contains("Element") || returnTypeName.Contains("VisualNode");

            if (!returnsElement)
            {
                return null;
            }

            var newReplacements = new Dictionary<string, ExpressionSyntax>();

            var parameters = methodDecl.ParameterList.Parameters;

            for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
            {
                var arg = invocation.ArgumentList.Arguments[i];

                var argExpr = arg.Expression;

                if (argExpr is IdentifierNameSyntax id && parameterReplacements != null && parameterReplacements.TryGetValue(id.Identifier.Text, out var parentExpr))
                {
                    argExpr = parentExpr;
                }

                if (arg.NameColon != null)
                {
                    var name = arg.NameColon.Name.Identifier.Text;

                    newReplacements[name] = argExpr;
                }
                else if (i < parameters.Count)
                {
                    var name = parameters[i].Identifier.Text;

                    newReplacements[name] = argExpr;
                }
            }

            ExpressionSyntax? bodyExpression = null;

            if (methodDecl.ExpressionBody != null)
            {
                bodyExpression = methodDecl.ExpressionBody.Expression;
            }
            else if (methodDecl.Body != null)
            {
                var returnStatement = methodDecl.Body.Statements
                                                        .OfType<ReturnStatementSyntax>()
                                                        .FirstOrDefault();

                if (returnStatement != null)
                {
                    bodyExpression = returnStatement.Expression;
                }
            }

            if (bodyExpression == null)
            {
                return null;
            }

            var nextExpanding = expandingMethods == null ? new HashSet<string>() : new HashSet<string>(expandingMethods);

            nextExpanding.Add(methodName);

            return ParseExpression(bodyExpression, classDecl, newReplacements, nextExpanding);
        }

        /// <summary>
        /// Parses a with expression syntax.
        /// </summary>
        /// <param name="withExpr">The with expression syntax node.</param>
        /// <param name="classDecl">The class declaration context.</param>
        /// <param name="parameterReplacements">Parameter replacement mapping.</param>
        /// <param name="expandingMethods">Set of currently expanding methods.</param>
        /// <returns>The populated AstElement, or null.</returns>
        private static AstElement? ParseWithExpression(
            WithExpressionSyntax withExpr,
            ClassDeclarationSyntax? classDecl = null,
            Dictionary<string, ExpressionSyntax>? parameterReplacements = null,
            HashSet<string>? expandingMethods = null)
        {
            var baseElem = ParseExpression(withExpr.Expression, classDecl, parameterReplacements, expandingMethods);

            if (baseElem != null)
            {
                if (withExpr.Initializer != null)
                {
                    foreach (var expression in withExpr.Initializer.Expressions)
                    {
                        if (expression is AssignmentExpressionSyntax assignment)
                        {
                            var propName = assignment.Left.ToString().Trim();

                            var propValueExpr = assignment.Right;

                            if (propValueExpr is IdentifierNameSyntax id && parameterReplacements != null && parameterReplacements.TryGetValue(id.Identifier.Text, out var replaced))
                            {
                                propValueExpr = replaced;
                            }

                            if (TryGetConstantString(propValueExpr, out var constString, parameterReplacements))
                            {
                                baseElem.Properties[propName] = constString;
                            }
                            else
                            {
                                baseElem.Properties[propName] = propValueExpr.ToString().Trim();
                            }
                        }
                    }
                }

                return baseElem;
            }

            return null;
        }

        /// <summary>
        /// Parses the arguments of an invocation element.
        /// </summary>
        /// <param name="elem">The AST element being populated.</param>
        /// <param name="arguments">The syntax list of arguments.</param>
        /// <param name="classDecl">The class declaration context.</param>
        /// <param name="parameterReplacements">Parameter replacement mapping.</param>
        /// <param name="expandingMethods">Set of currently expanding methods.</param>
        private static void ParseArguments(
            AstElement elem,
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            ClassDeclarationSyntax? classDecl = null,
            Dictionary<string, ExpressionSyntax>? parameterReplacements = null,
            HashSet<string>? expandingMethods = null)
        {
            for (int i = 0; i < arguments.Count; i++)
            {
                var arg = arguments[i];

                var argExpr = arg.Expression;

                var argName = arg.NameColon?.Name.Identifier.Text;

                if (IsTextArgument(elem.Name, argName, i, argExpr))
                {
                    ParseTextArgument(elem, argExpr, classDecl, parameterReplacements, expandingMethods);
                }
                else
                {
                    ParseNonTextArgument(elem, argExpr, argName, i, classDecl, parameterReplacements, expandingMethods);
                }
            }
        }

        /// <summary>
        /// Parses an argument identified as the main text/content argument.
        /// </summary>
        /// <param name="elem">The AST element being populated.</param>
        /// <param name="argExpr">The argument expression syntax.</param>
        /// <param name="classDecl">The class declaration context.</param>
        /// <param name="parameterReplacements">Parameter replacement mapping.</param>
        /// <param name="expandingMethods">Set of currently expanding methods.</param>
        private static void ParseTextArgument(
            AstElement elem,
            ExpressionSyntax argExpr,
            ClassDeclarationSyntax? classDecl = null,
            Dictionary<string, ExpressionSyntax>? parameterReplacements = null,
            HashSet<string>? expandingMethods = null)
        {
            if (argExpr is IdentifierNameSyntax id && parameterReplacements != null && parameterReplacements.TryGetValue(id.Identifier.Text, out var replaced))
            {
                argExpr = replaced;
            }

            var child = ParseExpression(argExpr, classDecl, parameterReplacements, expandingMethods);

            if (child != null)
            {
                elem.Children.Add(child);
            }
            else
            {
                if (TryGetConstantString(argExpr, out var constString, parameterReplacements))
                {
                    elem.Content = constString;
                }
                else
                {
                    elem.Content = argExpr.ToString();
                }
            }
        }

        /// <summary>
        /// Parses an argument that is not the main text/content argument.
        /// </summary>
        /// <param name="elem">The AST element being populated.</param>
        /// <param name="argExpr">The argument expression syntax.</param>
        /// <param name="argName">The parameter name, if named.</param>
        /// <param name="index">The position of the argument.</param>
        /// <param name="classDecl">The class declaration context.</param>
        /// <param name="parameterReplacements">Parameter replacement mapping.</param>
        /// <param name="expandingMethods">Set of currently expanding methods.</param>
        private static void ParseNonTextArgument(
            AstElement elem,
            ExpressionSyntax argExpr,
            string? argName,
            int index,
            ClassDeclarationSyntax? classDecl = null,
            Dictionary<string, ExpressionSyntax>? parameterReplacements = null,
            HashSet<string>? expandingMethods = null)
        {
            if (argExpr is IdentifierNameSyntax id && parameterReplacements != null && parameterReplacements.TryGetValue(id.Identifier.Text, out var replaced))
            {
                argExpr = replaced;
            }

            var child = ParseExpression(argExpr, classDecl, parameterReplacements, expandingMethods);

            if (child != null)
            {
                elem.Children.Add(child);
            }
            else
            {
                var isSectionHeaderDesc = elem.Name.Equals("SectionHeader", StringComparison.OrdinalIgnoreCase) && 
                    ((argName != null && argName.Equals("description", StringComparison.OrdinalIgnoreCase)) || (argName == null && index == 1));

                var isToggleSwitchState = elem.Name.Equals("ToggleSwitch", StringComparison.OrdinalIgnoreCase) &&
                    ((argName != null && argName.Equals("isOn", StringComparison.OrdinalIgnoreCase)) || (argName == null && index == 0));

                var isStackSpacing = (elem.Name.Contains("Stack") || elem.Name.Contains("VStack") || elem.Name.Contains("HStack") || elem.Name.Contains("WrapGrid") || elem.Name.Contains("Flex")) &&
                    argName == null && index == 0 && argExpr is LiteralExpressionSyntax numLit && numLit.Token.IsKind(SyntaxKind.NumericLiteralToken);

                if (isSectionHeaderDesc)
                {
                    if (TryGetConstantString(argExpr, out var constString, parameterReplacements))
                    {
                        elem.Properties["Description"] = constString;
                    }
                    else
                    {
                        elem.Properties["Description"] = argExpr.ToString();
                    }
                }
                else if (isToggleSwitchState)
                {
                    elem.Properties["IsOn"] = argExpr.ToString().Trim();
                }
                else if (isStackSpacing)
                {
                    elem.Properties["Spacing"] = argExpr.ToString().Trim();
                }
                else if (argName != null)
                {
                    if (TryGetConstantString(argExpr, out var constString, parameterReplacements))
                    {
                        elem.Properties[argName] = constString;
                    }
                    else
                    {
                        elem.Properties[argName] = argExpr.ToString();
                    }
                }
                else
                {
                    elem.Children.Add(new AstElement
                    {
                        Name = "CodeExpressionPlaceholder",
                        Content = argExpr.ToString().Trim()
                    });
                }
            }
        }

        /// <summary>
        /// Attempts to evaluate a string expression into a single constant string if it consists entirely of string literals.
        /// </summary>
        /// <param name="expr">The syntax expression to evaluate.</param>
        /// <param name="value">The evaluated constant string.</param>
        /// <param name="parameterReplacements">Parameter replacement mapping.</param>
        /// <returns>True if the expression is a compile-time constant string; otherwise, false.</returns>
        private static bool TryGetConstantString(
            ExpressionSyntax expr,
            out string value,
            Dictionary<string, ExpressionSyntax>? parameterReplacements = null)
        {
            if (expr is IdentifierNameSyntax id && parameterReplacements != null && parameterReplacements.TryGetValue(id.Identifier.Text, out var replaced))
            {
                return TryGetConstantString(replaced, out value, parameterReplacements);
            }

            if (expr is LiteralExpressionSyntax literal && literal.Token.IsKind(SyntaxKind.StringLiteralToken))
            {
                value = literal.Token.ValueText;

                return true;
            }

            if (expr is BinaryExpressionSyntax binary && binary.OperatorToken.IsKind(SyntaxKind.PlusToken))
            {
                if (TryGetConstantString(binary.Left, out var left, parameterReplacements) && TryGetConstantString(binary.Right, out var right, parameterReplacements))
                {
                    value = left + right;

                    return true;
                }
            }

            if (expr is ParenthesizedExpressionSyntax parenthesized)
            {
                return TryGetConstantString(parenthesized.Expression, out value, parameterReplacements);
            }

            value = string.Empty;

            return false;
        }
    }
}
