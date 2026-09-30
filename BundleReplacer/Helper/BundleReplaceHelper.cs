using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace BundleReplacer.Helper;

internal static class BundleReplaceHelper
{
    public class Filter
    {
        public bool MonoBehaviour = false;
        public bool TextAsset = false;
        public bool Texture2D = false;
        public bool Sprite = false;
        public bool VideoClip = false;
    }

    public static void CompressBundle(string outputPath, AssetsManager manager, BundleFileInstance bundle)
    {

        try { Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!); } catch (Exception) { }
        using (var writer = new AssetsFileWriter(outputPath + ".tmp"))
        {
            bundle.file.Write(writer);
        }
        bundle = manager.LoadBundleFile(outputPath + ".tmp", true);
        using (var writer = new AssetsFileWriter(outputPath))
        {
            bundle.file.Pack(writer, AssetBundleCompressionType.LZ4);
        }
        manager.UnloadAll();
        File.Delete(outputPath + ".tmp");
    }

    public static void SaveBundle(string outputPath, AssetsManager manager, BundleFileInstance bundle, Dictionary<string, StreamWrapper> resourceStreams)
    {
        foreach (var info in bundle.file.BlockAndDirInfo.DirectoryInfos)
        {
            if (resourceStreams.TryGetValue(info.Name, out var stream))
            {
                foreach (var block in stream.BlankBlocks)
                {
                    stream.Stream.Position = block.Start;
                    stream.Stream.Write(Enumerable.Repeat((byte)0, block.Length).ToArray());
                }
                info.SetNewData(stream.Stream.ToArray());
                stream.Stream.Dispose();
            }
            else
            {
                var asset = bundle.loadedAssetsFiles.FirstOrDefault(asset => asset.name == info.Name);
                if (asset is not null) { info.SetNewData(asset.file); }
            }
        }
        CompressBundle(outputPath, manager, bundle);
    }

    public static Filter ParseFilter(string filterStr)
    {
        if (string.IsNullOrWhiteSpace(filterStr))
        {
            return new Filter()
            {
                MonoBehaviour = true,
                TextAsset = true,
                Texture2D = true,
                Sprite = true,
                VideoClip = true,
            };
        }
        var list = filterStr.Split(',');
        var filter = new Filter();
        foreach (var item in list)
        {
            switch (item.Trim())
            {
                case "MonoBehaviour":
                case "json":
                    filter.MonoBehaviour = true;
                    break;
                case "TextAsset":
                case "txt":
                case "bin":
                    filter.TextAsset = true;
                    break;
                case "Texture2D":
                case "png":
                    filter.Texture2D = true;
                    break;
                case "Sprite":
                    filter.Sprite = true;
                    break;
                case "VideoClip":
                case "mp4":
                case "mov":
                    filter.VideoClip = true;
                    break;
            }
        }
        return filter;
    }

    public static string EscapeFileName(string name)
    {
        return name.Replace('|', '_').Replace('/', '_').Replace('\\', '_').Replace(':', '_')
            .Replace('*', '_').Replace('?', '_').Replace('"', '_').Replace('<', '_')
            .Replace('>', '_').Replace(' ', '_');
    }
}
