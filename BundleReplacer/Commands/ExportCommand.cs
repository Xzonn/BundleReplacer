using AssetsTools.NET.Extra;
using BundleReplacer.Helper;
using Mono.Options;
using static BundleReplacer.Helper.BundleReplaceHelper;

namespace BundleReplacer.Commands;

public class ExportCommand : Command
{
    private string? bundlePath;
    private string? outputDir;
    private string? filterStr;

    public ExportCommand() : base("export", "Export from a bundle file")
    {
        Options = new OptionSet
        {
            {"p|path=", "The path to the bundle file", v => bundlePath = v},
            {"o|output=", "The output directory", v => outputDir = v},
            {"f|filter=", "Filter, including: MonoBehaviour, TextAsset, Texture2D, Sprite", v => filterStr = v },
        };
    }


    public override int Invoke(IEnumerable<string> arguments)
    {
        Options.Parse(arguments);

        if (string.IsNullOrWhiteSpace(bundlePath) || string.IsNullOrWhiteSpace(outputDir))
        {
            throw new ArgumentException("Missing required arguments");
        }
        ExtractBundle(bundlePath, outputDir, filterStr ?? "");

        return 0;
    }

    public static void ExtractBundle(string bundlePath, string outputDir, string filterStr) => ExtractBundle(bundlePath, outputDir, ParseFilter(filterStr));

    internal static void ExtractBundle(string bundlePath, string outputDir, Filter filter)
    {
        var manager = new AssetsManager();
        var bundle = manager.LoadBundleFile(bundlePath);
        var sprites = filter.Sprite ? new Sprite(manager, bundle) : null;

        for (int index = 0; index < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; index++)
        {
            var assetsFile = manager.LoadAssetsFileFromBundle(bundle, index);
            if (assetsFile is null) { continue; }

            foreach (var info in assetsFile.file.Metadata.AssetInfos)
            {
                switch (info.TypeId)
                {
                    case (int)AssetClassID.MonoBehaviour:
                        if (filter.MonoBehaviour) { MonoBehaviour.Export(index, outputDir, manager, bundle, assetsFile, info); }
                        break;
                    case (int)AssetClassID.TextAsset:
                        if (filter.TextAsset) { TextAsset.Export(index, outputDir, manager, bundle, assetsFile, info); }
                        break;
                    case (int)AssetClassID.Texture2D:
                        if (filter.Texture2D) { Texture2D.Export(index, outputDir, manager, bundle, assetsFile, info); }
                        break;
                    case (int)AssetClassID.Sprite:
                        if (filter.Sprite) { sprites!.Export(index, outputDir, assetsFile, info); }
                        break;
                    case (int)AssetClassID.VideoClip:
                        if (filter.VideoClip) { VideoClip.Export(index, outputDir, manager, bundle, assetsFile, info); }
                        break;
                }
            }
        }
    }
}
