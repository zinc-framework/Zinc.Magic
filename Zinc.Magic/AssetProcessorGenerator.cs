using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Zinc.Magic
{
    [Generator(LanguageNames.CSharp)]
    public class GeneratorEntry : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Get the project directory
            IncrementalValueProvider<string?> projectDir = context.AnalyzerConfigOptionsProvider
                .Select((provider, _) => provider.GlobalOptions.TryGetValue("build_property.projectdir", out var dir) ? dir : null);

            // Get all additional files
            IncrementalValuesProvider<AdditionalText> additionalFiles = context.AdditionalTextsProvider;

            // Combine project directory and additional files
            IncrementalValuesProvider<(string? ProjectDir, AdditionalText File)> resFiles = 
                additionalFiles.Select((file, _) => (ProjectDir: (string?)null, File: file))
                               .Combine(projectDir)
                               .Select((tuple, _) => (tuple.Right, tuple.Left.File));

            // Register the source output
            context.RegisterSourceOutput(resFiles, (spc, tuple) => GenerateSource(spc, tuple.ProjectDir, tuple.File));
        }

        private void GenerateSource(SourceProductionContext context, string? projectDir, AdditionalText additionalFile)
        {
            if (string.IsNullOrEmpty(projectDir) || additionalFile == null)
                return;

            var resPath = new DirectoryInfo(Path.Combine(projectDir, "res"));

            if (!resPath.ContainsPath(additionalFile.Path))
                return;

            var ext = Path.GetExtension(additionalFile.Path);
            if (string.IsNullOrEmpty(ext))
                return;

            var cw = new Utils.CodeWriter();
            cw.OpenScope(@"
using static Zinc.Core.Assets;

namespace Res;

public static partial class Assets");

            AssetRouter.RouteAsset(context, cw, additionalFile, ext);

            cw.CloseScope();

            context.AddSource($"Res_{Path.GetFileNameWithoutExtension(additionalFile.Path)}.g.cs", cw.ToString());
        }
    }

    public static class AssetRouter
    {
        public static void RouteAsset(SourceProductionContext context, Utils.CodeWriter cw, AdditionalText t, string ext)
        {
            var compileFriendlyName = File.SanitizeFilename(Path.GetFileNameWithoutExtension(t.Path));
            switch (ext)
            {
                case ".png":
                case ".jpeg":
                case ".jpg":
                    cw.AddLine($"public static TextureAsset {compileFriendlyName} = new(@\"{t.Path}\");");
                    break;
                case ".aseprite":
                case ".tmx":
                case ".ldtk":
                case ".cue":
                default:
                    break;
            }
        }
    }
}