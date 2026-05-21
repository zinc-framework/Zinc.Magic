using System;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;

namespace Zinc.Magic
{
    [Generator(LanguageNames.CSharp)]
    public class GeneratorEntry : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Project directory (consuming project), used to locate the res/ folder.
            IncrementalValueProvider<string?> projectDir = context.AnalyzerConfigOptionsProvider
                .Select((provider, _) => provider.GlobalOptions.TryGetValue("build_property.projectdir", out var dir) ? dir : null);

            // Collect ALL additional files together: shader generation needs to read a
            // reflection .yaml alongside its sibling per-stage source files, so we can't
            // process one file in isolation. (Collect costs us per-file incrementality,
            // which is fine for an asset folder.)
            IncrementalValueProvider<ImmutableArray<AdditionalText>> allFiles =
                context.AdditionalTextsProvider.Collect();

            var combined = allFiles.Combine(projectDir);

            context.RegisterSourceOutput(combined, (spc, tuple) => GenerateAll(spc, tuple.Right, tuple.Left));
        }

        private void GenerateAll(SourceProductionContext context, string? projectDir, ImmutableArray<AdditionalText> files)
        {
            if (string.IsNullOrEmpty(projectDir))
                return;

            var resPath = new DirectoryInfo(Path.Combine(projectDir!, "res"));

            foreach (var file in files)
            {
                var path = file.Path;
                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext))
                    continue;

                // shdc reflection lands in the intermediate (obj) dir, not res/ — match it
                // by suffix wherever it is. It drives the build-time sg_shader factory.
                if (path.EndsWith("_reflection.yaml", StringComparison.OrdinalIgnoreCase))
                {
                    ShaderGen.EmitDesc(context, file, files);
                    continue;
                }

                if (!resPath.ContainsPath(path))
                    continue;

                // .glsl authoring: emit the typesafe stub + std140 uniform structs from the
                // source alone (no shdc), so the IDE has them even before a build.
                if (ext.Equals(".glsl", StringComparison.OrdinalIgnoreCase))
                {
                    ShaderGen.EmitStub(context, file);
                    continue;
                }

                // Existing typed-asset routing (textures, etc.)
                var cw = new Utils.CodeWriter();
                cw.OpenScope(@"
using static Zinc.Core.Assets;

namespace Res;

public static partial class Assets");

                AssetRouter.RouteAsset(context, cw, file, ext);

                cw.CloseScope();

                context.AddSource($"Res_{Path.GetFileNameWithoutExtension(path)}.g.cs", cw.ToString());
            }
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
