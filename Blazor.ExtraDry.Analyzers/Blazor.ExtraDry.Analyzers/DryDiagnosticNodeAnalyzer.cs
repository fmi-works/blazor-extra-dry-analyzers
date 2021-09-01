﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Blazor.ExtraDry.Analyzers
{
    public abstract class DryDiagnosticNodeAnalyzer : DiagnosticAnalyzer
    {
        protected DryDiagnosticNodeAnalyzer(
            SyntaxKind kind,
            int code,
            DryAnalyzerCategory category,
            DiagnosticSeverity severity,
            string title,
            string message,
            string description)
        {
            Kind = kind;
            if(rules.ContainsKey(code)) {
                Rule = rules[code];
            } else {
                Rule = new DiagnosticDescriptor($"DRY{code}", title, message, category.ToString(), severity,
                    isEnabledByDefault: true, description: description);
                rules.TryAdd(code, Rule);
            }
        }

        /// <summary>
        /// Diagnostics are run in parallel for performance reasons, so our caching mechanism must be thread safe.
        /// </summary>
        private static readonly ConcurrentDictionary<int, DiagnosticDescriptor> rules = new ConcurrentDictionary<int, DiagnosticDescriptor>();

        protected DiagnosticDescriptor Rule { get; set; }

        protected SyntaxKind Kind { get; set; }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics {
            get { return ImmutableArray.Create(Rule); }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeNode, Kind);
        }

        public abstract void AnalyzeNode(SyntaxNodeAnalysisContext context);

        protected bool HasAttribute(SyntaxNodeAnalysisContext context, ClassDeclarationSyntax _class, string attributeName, out AttributeSyntax attribute)
        {
            return HasAnyAttribute(context, _class, out attribute, attributeName);
        }

        protected bool HasAnyAttribute(SyntaxNodeAnalysisContext context, ClassDeclarationSyntax _class, out AttributeSyntax attribute, params string[] attributeNames)
        {
            if(attributeNames == null || !attributeNames.Any() || _class == null) {
                attribute = null;
                return false;
            }
            var fullNames = attributeNames.Select(e => e.EndsWith("Attribute") ? e : $"{e}Attribute");
            var attributes = _class.AttributeLists.SelectMany(e => e.Attributes) ?? Array.Empty<AttributeSyntax>();
            return AnyAttributeMatches(context, out attribute, fullNames, attributes);
        }

        protected bool HasAttribute(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method, string attributeName, out AttributeSyntax attribute)
        {
            return HasAnyAttribute(context, method, out attribute, attributeName);
        }

        protected bool HasAnyAttribute(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method, out AttributeSyntax attribute, params string[] attributeNames)
        {
            var fullNames = attributeNames.Select(e => e.EndsWith("Attribute") ? e : $"{e}Attribute");
            var attributes = method.AttributeLists.SelectMany(e => e.Attributes);
            return AnyAttributeMatches(context, out attribute, fullNames, attributes);
        }

        private static bool AnyAttributeMatches(SyntaxNodeAnalysisContext context, out AttributeSyntax attribute, IEnumerable<string> fullNames, IEnumerable<AttributeSyntax> attributes)
        {
            foreach(var attr in attributes) {
                var attrSymbol = context.SemanticModel?.GetTypeInfo(attr).Type;
                var inherits = fullNames?.Any(e => Inherits(attrSymbol, e)) ?? false;
                if(inherits) {
                    attribute = attr;
                    return true;
                }
            }
            attribute = null;
            return false;
        }

        protected static bool HasVisibility(MethodDeclarationSyntax method, Visibility visibility)
        {
            var kind = SyntaxKind.PublicKeyword;
            switch(visibility) {
                case Visibility.Public:
                    kind = SyntaxKind.PublicKeyword;
                    break;
                case Visibility.Private:
                    kind = SyntaxKind.PrivateKeyword;
                    break;
                case Visibility.Protected:
                    kind = SyntaxKind.ProtectedKeyword;
                    break;
                case Visibility.Internal:
                    kind = SyntaxKind.InternalKeyword;
                    break;
            };
            return method.ChildTokens()?.Any(e => e.Kind() == kind) ?? false;
        }

        protected static bool IsStatic(MethodDeclarationSyntax method)
        {
            return method.ChildTokens()?.Any(e => e.Kind() == SyntaxKind.StaticKeyword) ?? false;
        }

        protected bool InheritsFrom(SyntaxNodeAnalysisContext context, ClassDeclarationSyntax _class, string baseName)
        {
            var symbol = context.SemanticModel.GetDeclaredSymbol(_class);
            return Inherits(symbol, baseName);
        }

        private static bool Inherits(ITypeSymbol symbol, string baseName)
        {
            if(symbol?.Name == baseName) {
                return true;
            }
            while(symbol?.BaseType != null) {
                if(symbol?.Name == baseName) {
                    return true;
                }
                symbol = symbol?.BaseType;
            }
            return false;
        }

    }
}
