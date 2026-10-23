using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Skua.Core;

/// <summary>Preserves legacy source syntax while binding toolkit globals to the running session.</summary>
internal sealed class ScriptGlobalRewriter(SemanticModel model) : CSharpSyntaxRewriter
{
    public static CSharpCompilation Apply(CSharpCompilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees.ToArray())
        {
            if (!tree.GetText().ToString().Contains("Default", StringComparison.Ordinal)) continue;
            var root = tree.GetRoot();
            var rewritten = new ScriptGlobalRewriter(compilation.GetSemanticModel(tree)).Visit(root)!;
            if (rewritten != root) compilation = compilation.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(rewritten, tree.Options));
        }
        return compilation;
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (node.Name.Identifier.ValueText == "Default" && model.GetSymbolInfo(node.Expression).Symbol is INamedTypeSymbol type)
        {
            string? property = type.ToDisplayString() switch
            {
                "CommunityToolkit.Mvvm.DependencyInjection.Ioc" => "Ioc",
                "CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger" => "StrongMessenger",
                "CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger" => "WeakMessenger",
                _ => null
            };
            if (property is not null)
                return SyntaxFactory.ParseExpression("global::Skua.Core.Interfaces.ScriptSessionGlobals." + property).WithTriviaFrom(node);
        }
        return base.VisitMemberAccessExpression(node);
    }
}
