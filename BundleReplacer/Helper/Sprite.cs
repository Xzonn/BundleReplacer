using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;

namespace BundleReplacer.Helper;

internal class Sprite(AssetsManager manager, BundleFileInstance bundle)
{
    private readonly Dictionary<string, (string Path, int Index)> dependencies = [];
    private readonly List<(SpriteData Data, string Path)> replacements = [];
    private bool indexedDependencies;

    private record SpriteData(AssetTypeValueField Field, AssetTypeValueField RenderData, AssetExternal Texture);

    private AssetExternal Resolve(AssetsFileInstance asset, AssetTypeValueField pointer)
    {
        var result = manager.GetExtAsset(asset, pointer);
        if (result.info is not null || pointer["m_PathID"].AsLong == 0) { return result; }

        if (!indexedDependencies)
        {
            foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(bundle.path)!, $"*{Path.GetExtension(bundle.path)}"))
            {
                var candidate = new BundleFileInstance(File.OpenRead(path), false);
                for (int index = 0; index < candidate.file.BlockAndDirInfo.DirectoryInfos.Count; index++)
                {
                    var entry = candidate.file.BlockAndDirInfo.DirectoryInfos[index];
                    dependencies[Path.GetFileName(entry.Name)] = (path, index);
                }
                candidate.file.Close();
            }
            indexedDependencies = true;
        }

        var name = Path.GetFileName(asset.file.Metadata.Externals[pointer["m_FileID"].AsInt - 1].PathName);
        var dependency = dependencies[name];
        var dependencyBundle = manager.LoadBundleFile(dependency.Path);
        manager.LoadAssetsFileFromBundle(dependencyBundle, dependency.Index);
        return manager.GetExtAsset(asset, pointer);
    }

    private static (uint, uint, uint, uint, long) GetKey(AssetTypeValueField key)
    {
        var guid = key["first"];
        return (guid["data[0]"].AsUInt, guid["data[1]"].AsUInt, guid["data[2]"].AsUInt,
            guid["data[3]"].AsUInt, key["second"].AsLong);
    }

    private SpriteData GetData(AssetsFileInstance asset, AssetFileInfo info)
    {
        var field = manager.GetBaseField(asset, info);
        var renderData = field["m_RD"];
        var textureAsset = asset;
        var atlasPointer = field["m_SpriteAtlas"];
        if (!atlasPointer.IsDummy && atlasPointer["m_PathID"].AsLong != 0)
        {
            var atlas = Resolve(asset, atlasPointer);
            var key = GetKey(field["m_RenderDataKey"]);
            renderData = atlas.baseField["m_RenderDataMap"]["Array"].Children
                .First(entry => GetKey(entry["first"]) == key)["second"];
            textureAsset = atlas.file;
        }
        return new SpriteData(field, renderData, Resolve(textureAsset, renderData["texture"]));
    }

    private static Rectangle GetRectangle(AssetTypeValueField field)
    {
        int x = (int)Math.Floor(field["x"].AsFloat);
        int y = (int)Math.Floor(field["y"].AsFloat);
        return new Rectangle(x, y, (int)Math.Ceiling(field["x"].AsFloat + field["width"].AsFloat) - x,
            (int)Math.Ceiling(field["y"].AsFloat + field["height"].AsFloat) - y);
    }

    private static void Rotate(Image<Rgba32> image, uint settings, bool inverse)
    {
        if ((settings & 1) == 0) { return; }
        switch ((settings >> 2) & 15)
        {
            case 1: image.Mutate(i => i.Flip(FlipMode.Horizontal)); break;
            case 2: image.Mutate(i => i.Flip(FlipMode.Vertical)); break;
            case 3: image.Mutate(i => i.Rotate(180)); break;
            case 4: image.Mutate(i => i.Rotate(inverse ? 90 : 270)); break;
        }
    }

    private static bool[] GetMask(SpriteData data, int width, int height)
    {
        var mask = new bool[width * height];
        uint settings = data.RenderData["settingsRaw"].AsUInt;
        if (((settings >> 1) & 1) != 0 && ((settings >> 6) & 1) == 0)
        {
            Array.Fill(mask, true);
            return mask;
        }

        var vertexData = data.Field["m_RD"]["m_VertexData"];
        var channels = vertexData["m_Channels"]["Array"].Children;
        var position = channels[0];
        int stream = position["stream"].AsInt;
        int count = vertexData["m_VertexCount"].AsInt;
        int streamOffset = 0;
        int stride = 0;
        for (int index = 0; index <= stream; index++)
        {
            stride = 0;
            foreach (var channel in channels.Where(channel => channel["stream"].AsInt == index))
            {
                int size = channel["format"].AsInt switch
                {
                    0 or 10 or 11 => 4,
                    1 or 4 or 5 or 8 or 9 => 2,
                    _ => 1,
                };
                stride += size * (channel["dimension"].AsInt & 15);
            }
            if (index < stream) { streamOffset = (streamOffset + count * stride + 15) & ~15; }
        }

        var vertices = new Vector2[count];
        var bytes = vertexData["m_DataSize"].AsByteArray;
        var pivot = data.Field["m_Pivot"];
        var rect = data.Field["m_Rect"];
        var offset = data.RenderData["textureRectOffset"];
        float scale = data.Field["m_PixelsToUnits"].AsFloat;
        for (int index = 0; index < count; index++)
        {
            int start = streamOffset + index * stride + position["offset"].AsInt;
            vertices[index] = new Vector2(
                BitConverter.ToSingle(bytes, start) * scale + rect["width"].AsFloat * pivot["x"].AsFloat - offset["x"].AsFloat,
                BitConverter.ToSingle(bytes, start + 4) * scale + rect["height"].AsFloat * pivot["y"].AsFloat - offset["y"].AsFloat);
        }

        var indices = data.Field["m_RD"]["m_IndexBuffer"]["Array"].AsByteArray;
        foreach (var mesh in data.Field["m_RD"]["m_SubMeshes"]["Array"].Children)
        {
            int first = mesh["firstByte"].AsInt;
            for (int index = 0; index < mesh["indexCount"].AsInt; index += 3)
            {
                var a = vertices[BitConverter.ToUInt16(indices, first + index * 2)];
                var b = vertices[BitConverter.ToUInt16(indices, first + index * 2 + 2)];
                var c = vertices[BitConverter.ToUInt16(indices, first + index * 2 + 4)];
                int left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
                int right = Math.Min(width, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
                int bottom = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
                int top = Math.Min(height, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
                for (int y = bottom; y < top; y++)
                {
                    for (int x = left; x < right; x++)
                    {
                        var point = new Vector2(x + 0.5f, y + 0.5f);
                        float ab = Cross(b - a, point - a);
                        float bc = Cross(c - b, point - b);
                        float ca = Cross(a - c, point - c);
                        if ((ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0))
                        {
                            mask[y * width + x] = true;
                        }
                    }
                }
            }
        }
        return mask;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    public bool Export(int index, string outputDir, AssetsFileInstance asset, AssetFileInfo info)
    {
        var data = GetData(asset, info);
        var texture = data.Texture;
        using var image = Texture2D.GetImage(manager, texture.file.parentBundle, texture.file, texture.info);
        if (image is null) { return false; }
        var rect = GetRectangle(data.RenderData["textureRect"]);
        image.Mutate(i => i.Flip(FlipMode.Vertical));
        using var cropped = image.Clone(i => i.Crop(rect));
        Rotate(cropped, data.RenderData["settingsRaw"].AsUInt, false);
        var mask = GetMask(data, cropped.Width, cropped.Height);
        var size = data.Field["m_Rect"];
        using var output = new Image<Rgba32>((int)size["width"].AsFloat, (int)size["height"].AsFloat);
        int offsetX = (int)Math.Floor(data.RenderData["textureRectOffset"]["x"].AsFloat);
        int offsetY = (int)Math.Floor(data.RenderData["textureRectOffset"]["y"].AsFloat);
        for (int y = 0; y < cropped.Height; y++)
        {
            for (int x = 0; x < cropped.Width; x++)
            {
                if (mask[y * cropped.Width + x] && x + offsetX < output.Width && y + offsetY < output.Height)
                {
                    output[x + offsetX, output.Height - 1 - y - offsetY] = cropped[x, y];
                }
            }
        }
        Directory.CreateDirectory(outputDir);
        output.SaveAsPng($"{outputDir}/{Texture2D.GetFileName(index, info, data.Field)}");
        return true;
    }

    public void QueueImport(int index, string replaceDir, AssetsFileInstance asset, AssetFileInfo info)
    {
        var field = manager.GetBaseField(asset, info);
        var path = $"{replaceDir}/{Texture2D.GetFileName(index, info, field)}";
        if (File.Exists(path)) { replacements.Add((GetData(asset, info), path)); }
    }

    public bool Import(string outputPath, Dictionary<string, StreamWrapper> resourceStreams)
    {
        var changedBundles = new Dictionary<BundleFileInstance, Dictionary<string, StreamWrapper>>();
        foreach (var group in replacements.GroupBy(item => (item.Data.Texture.file, item.Data.Texture.info.PathId)))
        {
            var texture = group.First().Data.Texture;
            var textureBundle = texture.file.parentBundle;
            if (!changedBundles.TryGetValue(textureBundle, out var streams))
            {
                streams = textureBundle == bundle ? resourceStreams : [];
            }
            using var image = Texture2D.GetImage(manager, textureBundle, texture.file, texture.info, streams);
            if (image is null) { continue; }
            bool changed = false;
            image.Mutate(i => i.Flip(FlipMode.Vertical));
            foreach (var item in group)
            {
                using var replacement = Image.Load<Rgba32>(item.Path);
                var size = item.Data.Field["m_Rect"];
                if (replacement.Width != (int)size["width"].AsFloat || replacement.Height != (int)size["height"].AsFloat)
                {
                    throw new InvalidDataException($"Sprite dimensions must remain unchanged: {item.Path}");
                }
                var rect = GetRectangle(item.Data.RenderData["textureRect"]);
                using var cropped = image.Clone(i => i.Crop(rect));
                uint settings = item.Data.RenderData["settingsRaw"].AsUInt;
                Rotate(cropped, settings, false);
                var mask = GetMask(item.Data, cropped.Width, cropped.Height);
                int offsetX = (int)Math.Floor(item.Data.RenderData["textureRectOffset"]["x"].AsFloat);
                int offsetY = (int)Math.Floor(item.Data.RenderData["textureRectOffset"]["y"].AsFloat);
                for (int y = 0; y < cropped.Height; y++)
                {
                    for (int x = 0; x < cropped.Width; x++)
                    {
                        if (mask[y * cropped.Width + x] && x + offsetX < replacement.Width && y + offsetY < replacement.Height)
                        {
                            var pixel = replacement[x + offsetX, replacement.Height - 1 - y - offsetY];
                            changed = cropped[x, y] != pixel || changed;
                            cropped[x, y] = pixel;
                        }
                    }
                }
                Rotate(cropped, settings, true);
                for (int y = 0; y < cropped.Height; y++)
                {
                    for (int x = 0; x < cropped.Width; x++) { image[rect.X + x, rect.Y + y] = cropped[x, y]; }
                }
            }
            if (!changed) { continue; }
            image.Mutate(i => i.Flip(FlipMode.Vertical));
            Texture2D.ImportImage(image, manager, textureBundle, texture.file, texture.info, streams);
            changedBundles[textureBundle] = streams;
        }
        foreach (var entry in changedBundles.Where(entry => entry.Key != bundle))
        {
            BundleReplaceHelper.SaveBundle($"{Path.GetDirectoryName(Path.GetFullPath(outputPath))}/{entry.Key.name}",
                new AssetsManager(), entry.Key, entry.Value);
        }
        return changedBundles.Count != 0;
    }
}
