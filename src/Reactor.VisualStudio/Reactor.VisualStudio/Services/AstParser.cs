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
            HashSet<string>? expandingMethods = null,
            HashSet<string>? expandingParameters = null)
        {
            if (expr == null)
            {
                return null;
            }

            if (expr is IdentifierNameSyntax exprId)
            {
                var idText = exprId.Identifier.Text;
                if (parameterReplacements != null && parameterReplacements.TryGetValue(idText, out var replacedExpr))
                {
                    var parameterName = idText;

                    if (expandingParameters != null && expandingParameters.Contains(parameterName))
                    {
                        return null;
                    }

                    var nextExpandingParameters = expandingParameters == null
                        ? new HashSet<string>(StringComparer.Ordinal)
                        : new HashSet<string>(expandingParameters, StringComparer.Ordinal);

                    nextExpandingParameters.Add(parameterName);

                    return ParseExpression(replacedExpr, classDecl, parameterReplacements, expandingMethods, nextExpandingParameters);
                }

                if (classDecl != null)
                {
                    var prop = classDecl.Members.OfType<PropertyDeclarationSyntax>()
                                         .FirstOrDefault(p => p.Identifier.Text == idText);
                    if (prop?.Initializer != null)
                    {
                        return ParseExpression(prop.Initializer.Value, classDecl, parameterReplacements, expandingMethods, expandingParameters);
                    }

                    var field = classDecl.Members.OfType<FieldDeclarationSyntax>()
                                          .FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.Text == idText));
                    var variable = field?.Declaration.Variables.FirstOrDefault(v => v.Identifier.Text == idText);
                    if (variable?.Initializer != null)
                    {
                        return ParseExpression(variable.Initializer.Value, classDecl, parameterReplacements, expandingMethods, expandingParameters);
                    }
                }
            }

            if (expr is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is SimpleNameSyntax simpleName)
                {
                    if (simpleName.Identifier.Text == "Component" && simpleName is GenericNameSyntax genericName && genericName.TypeArgumentList.Arguments.Count > 0)
                    {
                        var subComponentName = genericName.TypeArgumentList.Arguments[0].ToString();
                        var customComponent = FindClassDeclaration(classDecl?.Parent ?? classDecl, subComponentName);
                        if (customComponent != null && IsComponentClass(customComponent))
                        {
                            var renderedSub = ParseSubComponentRender(customComponent, new Dictionary<string, ExpressionSyntax>(), expandingMethods, expandingParameters);
                            if (renderedSub != null)
                            {
                                var compElem = new AstElement { Name = "Component" };
                                compElem.Properties["GenericType"] = subComponentName;
                                compElem.Children.Add(renderedSub);
                                return compElem;
                            }
                        }
                    }

                    var resolvedMethodElement = ParseClassMethodInvocation(invocation, simpleName, classDecl, parameterReplacements, expandingMethods, expandingParameters);

                    if (resolvedMethodElement != null)
                    {
                        return resolvedMethodElement;
                    }

                    var elem = new AstElement { Name = simpleName.Identifier.Text };

                    if (simpleName is GenericNameSyntax genericName2)
                    {
                        var typeArgs = genericName2.TypeArgumentList.Arguments
                                                   .Select(a => a.ToString())
                                                   .ToList();

                        if (typeArgs.Count > 0)
                        {
                            elem.Properties["GenericType"] = string.Join(", ", typeArgs);
                        }
                    }

                    ParseArguments(elem, invocation.ArgumentList.Arguments, classDecl, parameterReplacements, expandingMethods, expandingParameters);

                    return elem;
                }

                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var methodName = memberAccess.Name.Identifier.Text;
                    if (methodName == "ToArray" || methodName == "ToList")
                    {
                        return ParseExpression(memberAccess.Expression, classDecl, parameterReplacements, expandingMethods, expandingParameters);
                    }

                    if (methodName == "Select")
                    {
                        var sourceExpr = memberAccess.Expression;
                        if (invocation.ArgumentList.Arguments.Count > 0 && 
                            invocation.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax lambda)
                        {
                            string? lambdaParamName = null;
                            if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
                            {
                                lambdaParamName = simpleLambda.Parameter.Identifier.Text;
                            }
                            else if (lambda is ParenthesizedLambdaExpressionSyntax parenLambda)
                            {
                                lambdaParamName = parenLambda.ParameterList.Parameters.FirstOrDefault()?.Identifier.Text;
                            }

                            if (lambdaParamName != null)
                            {
                                ExpressionSyntax? lambdaBodyExpr = null;
                                if (lambda.Body is ExpressionSyntax bodyExpr)
                                {
                                    lambdaBodyExpr = bodyExpr;
                                }
                                else if (lambda.Body is BlockSyntax block)
                                {
                                    var returnStmt = block.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
                                    if (returnStmt != null)
                                    {
                                        lambdaBodyExpr = returnStmt.Expression;
                                    }
                                }

                                if (lambdaBodyExpr != null)
                                {
                                    var resolvedSourceExpr = sourceExpr;
                                    while (resolvedSourceExpr is IdentifierNameSyntax id && parameterReplacements != null && parameterReplacements.TryGetValue(id.Identifier.Text, out var replaced))
                                    {
                                        resolvedSourceExpr = replaced;
                                    }

                                    var itemExpressions = new List<ExpressionSyntax>();

                                    if (resolvedSourceExpr is CollectionExpressionSyntax collectionExpr)
                                    {
                                        foreach (var element in collectionExpr.Elements)
                                        {
                                            if (element is ExpressionElementSyntax exprElem)
                                            {
                                                itemExpressions.Add(exprElem.Expression);
                                            }
                                        }
                                    }
                                    else if (resolvedSourceExpr is ArrayCreationExpressionSyntax arrayExpr && arrayExpr.Initializer != null)
                                    {
                                        itemExpressions.AddRange(arrayExpr.Initializer.Expressions);
                                    }
                                    else if (resolvedSourceExpr is ImplicitArrayCreationExpressionSyntax implicitArrayExpr && implicitArrayExpr.Initializer != null)
                                    {
                                        itemExpressions.AddRange(implicitArrayExpr.Initializer.Expressions);
                                    }
                                    else if (resolvedSourceExpr is ObjectCreationExpressionSyntax objectCreation && objectCreation.Initializer != null)
                                    {
                                        itemExpressions.AddRange(objectCreation.Initializer.Expressions);
                                    }

                                    if (itemExpressions.Count == 0)
                                    {
                                        for (int i = 1; i <= 3; i++)
                                        {
                                            var dummyText = $"Item {i}";
                                            var dummyLit = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.LiteralExpression(
                                                Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
                                                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Literal(dummyText));
                                            itemExpressions.Add(dummyLit);
                                        }
                                    }

                                    var collectionGroup = new AstElement { Name = "$$CollectionGroup$$" };
                                    foreach (var itemExpr in itemExpressions)
                                    {
                                        var newReplacements = parameterReplacements == null 
                                            ? new Dictionary<string, ExpressionSyntax>() 
                                            : new Dictionary<string, ExpressionSyntax>(parameterReplacements);
                                        newReplacements[lambdaParamName] = itemExpr;

                                        var parsedItem = ParseExpression(lambdaBodyExpr, classDecl, newReplacements, expandingMethods, expandingParameters);
                                        if (parsedItem != null)
                                        {
                                            collectionGroup.Children.Add(parsedItem);
                                        }
                                    }

                                    return collectionGroup;
                                }
                            }
                        }
                    }

                    var baseElem = ParseExpression(memberAccess.Expression, classDecl, parameterReplacements, expandingMethods, expandingParameters);

                    if (baseElem != null)
                    {
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

            if (expr is ObjectCreationExpressionSyntax objCreation)
            {
                var typeName = objCreation.Type.ToString();
                string name = typeName;
                string? genericTypeArgs = null;

                if (objCreation.Type is GenericNameSyntax genericName)
                {
                    name = genericName.Identifier.Text;
                    genericTypeArgs = string.Join(", ", genericName.TypeArgumentList.Arguments.Select(a => a.ToString()));
                }

                var customComponent = FindClassDeclaration(classDecl?.Parent ?? classDecl, name);
                if (customComponent != null && IsComponentClass(customComponent))
                {
                    var subReplacements = new Dictionary<string, ExpressionSyntax>();

                    if (objCreation.Initializer != null)
                    {
                        foreach (var initExpr in objCreation.Initializer.Expressions)
                        {
                            if (initExpr is AssignmentExpressionSyntax assignment)
                            {
                                var propName = assignment.Left.ToString().Trim();
                                var propValueExpr = assignment.Right;

                                if (propValueExpr is IdentifierNameSyntax id && parameterReplacements != null && parameterReplacements.TryGetValue(id.Identifier.Text, out var replaced))
                                {
                                    propValueExpr = replaced;
                                }

                                subReplacements[propName] = propValueExpr;
                            }
                        }
                    }

                    var renderedSub = ParseSubComponentRender(customComponent, subReplacements, expandingMethods, expandingParameters);
                    if (renderedSub != null)
                    {
                        var compElem = new AstElement { Name = "Component" };
                        if (genericTypeArgs != null)
                        {
                            compElem.Properties["GenericType"] = genericTypeArgs;
                        }
                        else
                        {
                            compElem.Properties["GenericType"] = name;
                        }
                        compElem.Children.Add(renderedSub);
                        return compElem;
                    }
                }

                var elem = new AstElement { Name = name };
                if (genericTypeArgs != null)
                {
                    elem.Properties["GenericType"] = genericTypeArgs;
                }

                if (objCreation.ArgumentList != null)
                {
                    ParseArguments(elem, objCreation.ArgumentList.Arguments, classDecl, parameterReplacements, expandingMethods, expandingParameters);
                }

                if (objCreation.Initializer != null)
                {
                    foreach (var expression in objCreation.Initializer.Expressions)
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
                                elem.Properties[propName] = constString;
                            }
                            else
                            {
                                elem.Properties[propName] = propValueExpr.ToString().Trim();
                            }
                        }
                    }
                }

                return elem;
            }

            if (expr is ParenthesizedExpressionSyntax parenthesized)
            {
                return ParseExpression(parenthesized.Expression, classDecl, parameterReplacements, expandingMethods, expandingParameters);
            }

            if (expr is WithExpressionSyntax withExpr)
            {
                return ParseWithExpression(withExpr, classDecl, parameterReplacements, expandingMethods, expandingParameters);
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
            HashSet<string>? expandingMethods,
            HashSet<string>? expandingParameters)
        {
            var methodName = simpleName.Identifier.Text;

            var methodDecl = classDecl?.Members
                                        .OfType<MethodDeclarationSyntax>()
                                        .FirstOrDefault(m => m.Identifier.Text == methodName);

            if (methodDecl == null)
            {
                var localFunc = classDecl?.DescendantNodes()
                                           .OfType<LocalFunctionStatementSyntax>()
                                           .FirstOrDefault(m => m.Identifier.Text == methodName);

                if (localFunc != null)
                {
                    if (expandingMethods != null && expandingMethods.Contains(methodName))
                    {
                        return null;
                    }

                    var localReturnTypeName = localFunc.ReturnType.ToString();
                    bool localReturnsElement = localReturnTypeName.Contains("Element") || localReturnTypeName.Contains("VisualNode");

                    if (!localReturnsElement)
                    {
                        return null;
                    }

                    var localReplacements = new Dictionary<string, ExpressionSyntax>();
                    var localParameters = localFunc.ParameterList.Parameters;

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
                            localReplacements[name] = argExpr;
                        }
                        else if (i < localParameters.Count)
                        {
                            var name = localParameters[i].Identifier.Text;
                            localReplacements[name] = argExpr;
                        }
                    }

                    ExpressionSyntax? localBodyExpression = null;

                    if (localFunc.ExpressionBody != null)
                    {
                        localBodyExpression = localFunc.ExpressionBody.Expression;
                    }
                    else if (localFunc.Body != null)
                    {
                        var returnStatement = localFunc.Body.Statements
                                                                .OfType<ReturnStatementSyntax>()
                                                                .FirstOrDefault();

                        if (returnStatement != null)
                        {
                            localBodyExpression = returnStatement.Expression;
                        }
                    }

                    if (localBodyExpression == null)
                    {
                        return null;
                    }

                    var localNextExpanding = expandingMethods == null ? new HashSet<string>() : new HashSet<string>(expandingMethods);
                    localNextExpanding.Add(methodName);

                    return ParseExpression(localBodyExpression, classDecl, localReplacements, localNextExpanding, expandingParameters: null);
                }
            }

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

            return ParseExpression(bodyExpression, classDecl, newReplacements, nextExpanding, expandingParameters: null);
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
            HashSet<string>? expandingMethods = null,
            HashSet<string>? expandingParameters = null)
        {
            var baseElem = ParseExpression(withExpr.Expression, classDecl, parameterReplacements, expandingMethods, expandingParameters);

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

                            if (TryGetConstantString(propValueExpr, out var constString, parameterReplacements, classDecl: classDecl))
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
            HashSet<string>? expandingMethods = null,
            HashSet<string>? expandingParameters = null)
        {
            for (int i = 0; i < arguments.Count; i++)
            {
                var arg = arguments[i];

                var argExpr = arg.Expression;

                var argName = arg.NameColon?.Name.Identifier.Text;

                if (IsTextArgument(elem.Name, argName, i, argExpr))
                {
                    ParseTextArgument(elem, argExpr, classDecl, parameterReplacements, expandingMethods, expandingParameters);
                }
                else
                {
                    ParseNonTextArgument(elem, argExpr, argName, i, classDecl, parameterReplacements, expandingMethods, expandingParameters);
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
            HashSet<string>? expandingMethods = null,
            HashSet<string>? expandingParameters = null)
        {
            while (true)
            {
                if (argExpr is ParenthesizedExpressionSyntax parenthesized)
                {
                    argExpr = parenthesized.Expression;
                }
                else if (argExpr is ConditionalExpressionSyntax conditional)
                {
                    argExpr = conditional.WhenFalse;
                }
                else
                {
                    break;
                }
            }

            if (argExpr is IdentifierNameSyntax id && parameterReplacements != null && parameterReplacements.TryGetValue(id.Identifier.Text, out var replaced))
            {
                argExpr = replaced;
            }

            var child = ParseExpression(argExpr, classDecl, parameterReplacements, expandingMethods, expandingParameters);

            AddChildElement(elem, child);
            if (child == null)
            {
                if (TryGetConstantString(argExpr, out var constString, parameterReplacements, classDecl: classDecl))
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
            HashSet<string>? expandingMethods = null,
            HashSet<string>? expandingParameters = null)
        {
            if (argExpr is IdentifierNameSyntax id && parameterReplacements != null && parameterReplacements.TryGetValue(id.Identifier.Text, out var replaced))
            {
                argExpr = replaced;
            }

            var child = ParseExpression(argExpr, classDecl, parameterReplacements, expandingMethods, expandingParameters);

            AddChildElement(elem, child);
            if (child == null)
            {
                var isSectionHeaderDesc = elem.Name.Equals("SectionHeader", StringComparison.OrdinalIgnoreCase) && 
                    ((argName != null && argName.Equals("description", StringComparison.OrdinalIgnoreCase)) || (argName == null && index == 1));

                var isToggleSwitchState = elem.Name.Equals("ToggleSwitch", StringComparison.OrdinalIgnoreCase) &&
                    ((argName != null && argName.Equals("isOn", StringComparison.OrdinalIgnoreCase)) || (argName == null && index == 0));

                var isStackSpacing = (elem.Name.Contains("Stack") || elem.Name.Contains("VStack") || elem.Name.Contains("HStack") || elem.Name.Contains("WrapGrid") || elem.Name.Contains("Flex")) &&
                    argName == null && index == 0 && argExpr is LiteralExpressionSyntax numLit && numLit.Token.IsKind(SyntaxKind.NumericLiteralToken);

                if (isSectionHeaderDesc)
                {
                    if (TryGetConstantString(argExpr, out var constString, parameterReplacements, classDecl: classDecl))
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
                    if (TryGetConstantString(argExpr, out var constString, parameterReplacements, classDecl: classDecl))
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
            Dictionary<string, ExpressionSyntax>? parameterReplacements = null,
            HashSet<string>? expandingParameters = null,
            ClassDeclarationSyntax? classDecl = null)
        {
            if (expr is IdentifierNameSyntax id)
            {
                var idText = id.Identifier.Text;
                if (parameterReplacements != null && parameterReplacements.TryGetValue(idText, out var replaced))
                {
                    var parameterName = idText;

                    if (expandingParameters != null && expandingParameters.Contains(parameterName))
                    {
                        value = string.Empty;

                        return false;
                    }

                    var nextExpandingParameters = expandingParameters == null
                        ? new HashSet<string>(StringComparer.Ordinal)
                        : new HashSet<string>(expandingParameters, StringComparer.Ordinal);

                    nextExpandingParameters.Add(parameterName);

                    return TryGetConstantString(replaced, out value, parameterReplacements, nextExpandingParameters, classDecl);
                }

                if (classDecl != null)
                {
                    var prop = classDecl.Members.OfType<PropertyDeclarationSyntax>()
                                         .FirstOrDefault(p => p.Identifier.Text == idText);
                    if (prop?.Initializer != null)
                    {
                        return TryGetConstantString(prop.Initializer.Value, out value, parameterReplacements, expandingParameters, classDecl);
                    }

                    var field = classDecl.Members.OfType<FieldDeclarationSyntax>()
                                          .FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.Text == idText));
                    var variable = field?.Declaration.Variables.FirstOrDefault(v => v.Identifier.Text == idText);
                    if (variable?.Initializer != null)
                    {
                        return TryGetConstantString(variable.Initializer.Value, out value, parameterReplacements, expandingParameters, classDecl);
                    }
                }
            }

            if (expr is ConditionalExpressionSyntax conditional)
            {
                return TryGetConstantString(conditional.WhenFalse, out value, parameterReplacements, expandingParameters, classDecl);
            }

            if (expr is LiteralExpressionSyntax literal && literal.Token.IsKind(SyntaxKind.StringLiteralToken))
            {
                value = literal.Token.ValueText;

                return true;
            }

            if (expr is BinaryExpressionSyntax binary && binary.OperatorToken.IsKind(SyntaxKind.PlusToken))
            {
                if (TryGetConstantString(binary.Left, out var left, parameterReplacements, expandingParameters, classDecl) && TryGetConstantString(binary.Right, out var right, parameterReplacements, expandingParameters, classDecl))
                {
                    value = left + right;

                    return true;
                }
            }

            if (expr is InterpolatedStringExpressionSyntax interpolated)
            {
                var builder      = new System.Text.StringBuilder();
                bool allResolved = true;

                foreach (var content in interpolated.Contents)
                {
                    if (content is InterpolatedStringTextSyntax text)
                    {
                        builder.Append(text.TextToken.ValueText);
                    }
                    else if (content is InterpolationSyntax interpolation)
                    {
                        if (TryGetConstantString(interpolation.Expression, out var resolvedExpr, parameterReplacements, expandingParameters, classDecl))
                        {
                            builder.Append(resolvedExpr);
                        }
                        else
                        {
                            allResolved = false;

                            break;
                        }
                    }
                    else
                    {
                        allResolved = false;

                        break;
                    }
                }

                if (allResolved)
                {
                    value = builder.ToString();

                    return true;
                }
            }

            if (expr is ParenthesizedExpressionSyntax parenthesized)
            {
                return TryGetConstantString(parenthesized.Expression, out value, parameterReplacements, expandingParameters, classDecl);
            }

            value = string.Empty;

            return false;
        }

        private static void AddChildElement(AstElement parent, AstElement? child)
        {
            if (child == null)
            {
                return;
            }

            if (child.Name == "$$CollectionGroup$$")
            {
                foreach (var grandChild in child.Children)
                {
                    AddChildElement(parent, grandChild);
                }
            }
            else
            {
                parent.Children.Add(child);
            }
        }

        private static ClassDeclarationSyntax? FindClassDeclaration(SyntaxNode? node, string name)
        {
            if (node == null)
            {
                return null;
            }

            return node.SyntaxTree.GetRoot().DescendantNodes()
                       .OfType<ClassDeclarationSyntax>()
                       .FirstOrDefault(c => c.Identifier.Text == name);
        }

        private static bool IsComponentClass(ClassDeclarationSyntax classDecl)
        {
            return classDecl.BaseList != null && classDecl.BaseList.Types.Any(t => t.ToString().Contains("Component"));
        }

        private static AstElement? ParseSubComponentRender(
            ClassDeclarationSyntax classDecl,
            Dictionary<string, ExpressionSyntax> parameterReplacements,
            HashSet<string>? expandingMethods,
            HashSet<string>? expandingParameters)
        {
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
                return ParseExpression(bodyExpression, classDecl, parameterReplacements, expandingMethods, expandingParameters);
            }

            return null;
        }
    }
}
