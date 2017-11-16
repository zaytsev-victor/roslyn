﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Editor.GoToDefinition
{
    internal abstract class AbstractGoToDefinitionSymbolService : IGoToDefinitionSymbolService
    {
        protected abstract ISymbol FindRelatedExplicitlyDeclaredSymbol(ISymbol symbol, Compilation compilation);

        public async Task<(ISymbol, TextSpan)> GetSymbolAndBoundSpanAsync(Document document, int position, CancellationToken cancellationToken)
        {
            var workspace = document.Project.Solution.Workspace;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var token = await GetToken(semanticModel, workspace, position, cancellationToken).ConfigureAwait(false);
            if (token == default)
            {
                return EmptyResult;
            }

            var semanticInfo = semanticModel.GetSemanticInfo(token, workspace, cancellationToken);

            // prefer references to declarations.  It's more likely that the user is attempting to 
            // go to a definition at some other location, rather than the definition they're on.  
            // This can happen when a token is at a location that is both a reference and a definition.
            // For example, on an anonymous type member declaration.
            var symbol = semanticInfo.AliasSymbol ??
                         semanticInfo.ReferencedSymbols.FirstOrDefault() ??
                         semanticInfo.DeclaredSymbol ??
                         semanticInfo.Type;

            // Disabled navigation if token is a part of declaration syntax
            // or if it is a part of partial method implementation.
            if (symbol != null &&
            	IsNotPartialMethodDefinitionPart(symbol) &&
                TokenIsPartOfDeclaringSyntax(token, symbol))
            {
                return EmptyResult;
            }

            return (FindRelatedExplicitlyDeclaredSymbol(symbol, semanticModel.Compilation), semanticInfo.Span);
        }

        private async Task<SyntaxToken> GetToken(SemanticModel semanticModel, Workspace workspace, int position, CancellationToken cancellationToken)
        {
            var syntaxTree = semanticModel.SyntaxTree;
            var syntaxFacts = workspace.Services.GetLanguageServices(semanticModel.Language).GetService<ISyntaxFactsService>();
            var token = await syntaxTree.GetTouchingTokenAsync(position, syntaxFacts.IsBindableToken, cancellationToken, findInsideTrivia: true).ConfigureAwait(false);

            if (token != default &&
                token.Span.IntersectsWith(position))
            {
                return token;
            }

            return default;
        }

        private static bool IsNotPartialMethodDefinitionPart(ISymbol symbol)
            => !(symbol is IMethodSymbol methodSymbol && methodSymbol.IsPartialMethodDefinitionPart());

        private static bool TokenIsPartOfDeclaringSyntax(SyntaxToken token, ISymbol symbol)
        {
            // For partial classes symbol can contain more than one declaring syntax reference
            // and we won't change behavior for that cases (user should be able to navigate to other class declarations).
            return symbol.DeclaringSyntaxReferences.Length == 1 && symbol.DeclaringSyntaxReferences[0].GetSyntax().Equals(token.Parent);
        }

        private static readonly (ISymbol, TextSpan) EmptyResult = (default, default);
    }
}
