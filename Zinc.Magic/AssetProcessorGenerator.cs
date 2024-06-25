using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Zinc.Magic;

[Generator]
public class AssetProcessorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Add the marker attribute to the compilation.
        // context.RegisterPostInitializationOutput(ctx => ctx.AddSource(
        //     "ReportAttribute.g.cs",
        //     SourceText.From(AttributeSourceCode, Encoding.UTF8)));

        // Filter classes annotated with the [Report] attribute. Only filtered Syntax Nodes can trigger code generation.
        var provider = context.SyntaxProvider
            .CreateSyntaxProvider(
                (s, _) => s is ClassDeclarationSyntax,
                (ctx, _) => GetClassDeclarationForSourceGen(ctx))
            .Where(t => t.reportAttributeFound)
            .Select((t, _) => t.Item1);

        // Generate the source code.
        context.RegisterSourceOutput(context.CompilationProvider.Combine(provider.Collect()),
            ((ctx, t) => GenerateCode(ctx, t.Left, t.Right)));
    }

    /// <summary>
    /// Checks whether the Node is annotated with the [Report] attribute and maps syntax context to the specific node type (ClassDeclarationSyntax).
    /// </summary>
    /// <param name="context">Syntax context, based on CreateSyntaxProvider predicate</param>
    /// <returns>The specific cast and whether the attribute was found.</returns>
    private static (ClassDeclarationSyntax, bool reportAttributeFound) GetClassDeclarationForSourceGen(
        GeneratorSyntaxContext context)
    {
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

        // Go through all attributes of the class.
        foreach (AttributeListSyntax attributeListSyntax in classDeclarationSyntax.AttributeLists)
        foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
        {
            if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                continue; // if we can't get the symbol, ignore it

            string attributeName = attributeSymbol.ContainingType.ToDisplayString();

            // Check the full name of the [Report] attribute.
            // if (attributeName == $"{Namespace}.{AttributeName}")
            if (attributeName == "Zinc.Magic.Sample.AssetTargetAttribute")
                return (classDeclarationSyntax, true);
        }

        return (classDeclarationSyntax, false);
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // Assume we have a way to get the asset paths (this could be through AdditionalFiles, or some other mechanism)
        // var assetPaths = context.AdditionalFiles.Select(f => f.Path).ToList();
        var assetPaths = new List<string> { "file1.sample" };

        var compilation = context.Compilation;
        // var assetProcessorSymbol = compilation.GetTypeByMetadataName("AssetProcessor");
        var assetProcessorSymbol = FindAssetProcessorSymbol(compilation);

        if (assetProcessorSymbol == null)
        {
            return;
        }

        var processorsByExtension = new Dictionary<string, INamedTypeSymbol>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var classDeclarations = syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var classDeclaration in classDeclarations)
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

                if (classSymbol != null && classSymbol.BaseType != null && classSymbol.BaseType.Equals(assetProcessorSymbol, SymbolEqualityComparer.Default))
                {
                    var attribute = classSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "AssetTargetAttribute");
                    if (attribute != null && attribute.ConstructorArguments.Length > 0)
                    {
                        var extension = attribute.ConstructorArguments[0].Value?.ToString();
                        if (!string.IsNullOrEmpty(extension))
                        {
                            processorsByExtension[extension] = classSymbol;
                        }
                    }
                }
            }
        }

        foreach (var assetPath in assetPaths)
        {
            var extension = Path.GetExtension(assetPath);
            if (processorsByExtension.TryGetValue(extension, out var processorSymbol))
            {
                var instance = Activator.CreateInstance(processorSymbol.GetType());
                instance.GetType().GetMethod("ProcessAsset").Invoke(instance, new object[] { assetPath });
                // var processAssetMethod = processorSymbol.GetMembers("ProcessAsset").FirstOrDefault() as IMethodSymbol;
                
                // if (processAssetMethod != null && instance != null)
                // {
                //     processAssetMethod.meth.Invoke(instance, new object[] { assetPath });
                // }
            }
        }
    }

    private INamedTypeSymbol? FindAssetProcessorSymbol(Compilation compilation)
    {
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            var assetProcessorSymbol = FindAssetProcessorInAssembly(assembly.GlobalNamespace);
            if (assetProcessorSymbol != null)
            {
                return assetProcessorSymbol;
            }
        }

        // Also check in the current compilation
        return FindAssetProcessorInAssembly(compilation.GlobalNamespace);
    }

    private INamedTypeSymbol? FindAssetProcessorInAssembly(INamespaceSymbol namespaceSymbol)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamedTypeSymbol typeSymbol && typeSymbol.Name == "AssetProcessor")
            {
                return typeSymbol;
            }
            else if (member is INamespaceSymbol nestedNamespace)
            {
                var result = FindAssetProcessorInAssembly(nestedNamespace);
                if (result != null)
                {
                    return result;
                }
            }
        }

        return null;
    }
}