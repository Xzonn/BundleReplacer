using AssetsTools.NET.Extra;
using BundleReplacer.Helper;
using Mono.Options;
using static BundleReplacer.Helper.BundleReplaceHelper;

namespace BundleReplacer.Commands;

public class ImportCommand : Command
{
    private string? bundlePath;
    private string? replaceDir;
    private string? outputPath;
    private string? filterStr;

    public ImportCommand() : base("import", "Import into a bundle file")
    {
        Options = new OptionSet
        {
            {"p|path=", "The path to the original bundle file", v => bundlePath = v},
            {"r|replace=", "The path to the json files to replace", v => replaceDir = v},
            {"o|output=", "The output path", v => outputPath = v},
            {"f|filter=", "Filter, including: MonoBehaviour, TextAsset, Texture2D, Sprite", v => filterStr = v },
        };
    }

    public override int Invoke(IEnumerable<string> arguments)
    {
        Options.Parse(arguments);

        if (string.IsNullOrWhiteSpace(bundlePath) || string.IsNullOrWhiteSpace(replaceDir) || string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Missing required arguments");
        }
        ImportBundle(bundlePath, replaceDir, outputPath, filterStr ?? "");

        return 0;
    }

    public static void ImportBundle(string bundlePath, string replaceDir, string outputPath, string filterStr) => ImportBundle(bundlePath, replaceDir, outputPath, ParseFilter(filterStr));

    internal static void ImportBundle(string bundlePath, string replaceDir, string outputPath, Filter filter)
    {
        var manager = new AssetsManager();
        var bundle = manager.LoadBundleFile(bundlePath);
        var sprites = filter.Sprite ? new Sprite(manager, bundle) : null;
        var resourceStreams = new Dictionary<string, StreamWrapper>();

        bool changed = false;

        for (int index = 0; index < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; index++)
        {
            var assetsFile = manager.LoadAssetsFileFromBundle(bundle, index);
            if (assetsFile is null) { continue; }
            foreach (var info in assetsFile.file.Metadata.AssetInfos)
            {
                switch (info.TypeId)
                {
                    case (int)AssetClassID.MonoBehaviour:
                        if (filter.MonoBehaviour) { changed = MonoBehaviour.Import(index, replaceDir, manager, bundle, assetsFile, info) || changed; }
                        break;
                    case (int)AssetClassID.TextAsset:
                        if (filter.TextAsset) { changed = TextAsset.Import(index, replaceDir, manager, bundle, assetsFile, info) || changed; }
                        break;
                    case (int)AssetClassID.Texture2D:
                        if (filter.Texture2D) { changed = Texture2D.Import(index, replaceDir, manager, bundle, assetsFile, info, resourceStreams) || changed; }
                        break;
                    case (int)AssetClassID.Sprite:
                        if (filter.Sprite) { sprites!.QueueImport(index, replaceDir, assetsFile, info); }
                        break;
                    case (int)AssetClassID.VideoClip:
                        if (filter.VideoClip) { changed = VideoClip.Import(index, replaceDir, manager, bundle, assetsFile, info, resourceStreams) || changed; }
                        break;
                }
            }
        }

        if (filter.Sprite) { changed = sprites!.Import(outputPath, resourceStreams) || changed; }
        if (!changed) { return; }

        SaveBundle(outputPath, manager, bundle, resourceStreams);
    }
}
