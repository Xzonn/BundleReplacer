using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BundleReplacer.Helper;

internal static class Texture2D
{
    public static bool Export(int index, string outputDir, AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance asset, AssetFileInfo texture2D)
    {

        var texture2DInfo = manager.GetBaseField(asset, texture2D);
        var pngPath = $"{outputDir}/{GetFileName(index, texture2D, texture2DInfo)}";

        using var image = GetImage(manager, bundle, asset, texture2D);
        if (image is null) { return false; }
        Directory.CreateDirectory(outputDir);
        image.SaveAsPng(pngPath);
        return true;
    }

    public static Image<Rgba32>? GetImage(AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance asset, AssetFileInfo texture2D, Dictionary<string, StreamWrapper>? resourceStreams = null)
    {
        var texture2DInfo = manager.GetBaseField(asset, texture2D);
        var texFile = TextureFile.ReadTextureFile(texture2DInfo);
        if (texFile.m_Width == 0 && texFile.m_Height == 0)
        {
            return null;
        }
        var streamInfo = texFile.m_StreamData;
        if (resourceStreams is not null && resourceStreams.TryGetValue(Path.GetFileName(streamInfo.path), out var stream))
        {
            stream.Stream.Position = (long)streamInfo.offset;
            texFile.pictureData = new byte[streamInfo.size];
            stream.Stream.ReadExactly(texFile.pictureData);
        }
        else
        {
            TextureHelper.GetResSTexture(texFile, bundle);
        }
        var bytes = texFile.pictureData;
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        var width = texFile.m_Width;
        var height = texFile.m_Height;
        var format = (TextureFormat)texFile.m_TextureFormat;

        var platformBlob = TextureHelper.GetPlatformBlob(texture2DInfo);
        var platform = asset.file.Metadata.TargetPlatform;
        if (platform == 38 && platformBlob is not null && platformBlob.Length != 0)
        {
            int originalWidth = width;
            int originalHeight = height;

            format = TextureHelper.GetCorrectedSwitchTextureFormat(format);
            int gobsPerBlock = Texture2DSwitchDeswizzler.GetSwitchGobsPerBlock(platformBlob);
            Size blockSize = Texture2DSwitchDeswizzler.TextureFormatToBlockSize(format);
            Size newSize = Texture2DSwitchDeswizzler.GetPaddedTextureSize(width, height, blockSize.Width, blockSize.Height, gobsPerBlock);
            width = newSize.Width;
            height = newSize.Height;

            byte[] decData = TextureEncoderDecoder.Decode(bytes, width, height, format);
            if (decData is null) { return null; }

            using Image<Rgba32> decoded = Image.LoadPixelData<Rgba32>(decData, width, height);
            Image<Rgba32> image = Texture2DSwitchDeswizzler.SwitchUnswizzle(decoded, blockSize, gobsPerBlock);
            if (originalWidth != width || originalHeight != height)
            {
                image.Mutate(i => i.Crop(originalWidth, originalHeight));
            }
            image.Mutate(i => i.Flip(FlipMode.Vertical));
            return image;
        }
        else
        {
            byte[] decData = TextureEncoderDecoder.Decode(bytes, width, height, format);
            if (decData is null) { return null; }

            Image<Rgba32> image = Image.LoadPixelData<Rgba32>(decData, width, height);
            image.Mutate(i => i.Flip(FlipMode.Vertical));
            return image;
        }
    }

    public static bool Import(int index, string replaceDir, AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance asset, AssetFileInfo texture2D, Dictionary<string, StreamWrapper> resourceStreams)
    {
        var texture2DInfo = manager.GetBaseField(asset, texture2D);
        var pngPath = $"{replaceDir}/{GetFileName(index, texture2D, texture2DInfo)}";

        if (!File.Exists(pngPath)) { return false; }
        using Image<Rgba32> image = Image.Load<Rgba32>(pngPath);
        return ImportImage(image, manager, bundle, asset, texture2D, resourceStreams);
    }

    public static bool ImportImage(Image<Rgba32> image, AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance asset, AssetFileInfo texture2D, Dictionary<string, StreamWrapper> resourceStreams)
    {
        var texture2DInfo = manager.GetBaseField(asset, texture2D);
        var width = image.Width;
        var height = image.Height;

        var format = (TextureFormat)texture2DInfo["m_TextureFormat"].AsInt;
        var platformBlob = TextureHelper.GetPlatformBlob(texture2DInfo);
        var platform = asset.file.Metadata.TargetPlatform;

        int mips = texture2DInfo["m_MipCount"].IsDummy ? 1 : texture2DInfo["m_MipCount"].AsInt;
        byte[] encData = [];
        if (platform == 38 && platformBlob != null && platformBlob.Length != 0)
        {
            int paddedWidth, paddedHeight;

            format = TextureHelper.GetCorrectedSwitchTextureFormat(format);
            int gobsPerBlock = Texture2DSwitchDeswizzler.GetSwitchGobsPerBlock(platformBlob);
            Size blockSize = Texture2DSwitchDeswizzler.TextureFormatToBlockSize(format);
            Size newSize = Texture2DSwitchDeswizzler.GetPaddedTextureSize(width, height, blockSize.Width, blockSize.Height, gobsPerBlock);
            paddedWidth = newSize.Width;
            paddedHeight = newSize.Height;

            image.Mutate(i => i.Resize(new ResizeOptions()
            {
                Mode = ResizeMode.BoxPad,
                Position = AnchorPositionMode.BottomLeft,
                PadColor = Color.Fuchsia, // full alpha?
                Size = newSize
            }).Flip(FlipMode.Vertical));

            using Image<Rgba32> swizzledImage = Texture2DSwitchDeswizzler.SwitchSwizzle(image, blockSize, gobsPerBlock);

            encData = TextureEncoderDecoder.Encode(swizzledImage, paddedWidth, paddedHeight, format);
        }
        else
        {
            // can't make mipmaps from this image
            if (mips > 1 && (width != height || !TextureHelper.IsPo2(width)))
            {
                mips = 1;
            }

            image.Mutate(i => i.Flip(FlipMode.Vertical));
            encData = TextureEncoderDecoder.Encode(image, width, height, format, 5, mips);
        }

        if (!texture2DInfo["m_MipCount"].IsDummy) { texture2DInfo["m_MipCount"].AsInt = mips; }

        texture2DInfo["m_TextureFormat"].AsInt = (int)format;
        // todo: size for multi image textures
        texture2DInfo["m_CompleteImageSize"].AsInt = encData.Length;

        texture2DInfo["m_Width"].AsInt = width;
        texture2DInfo["m_Height"].AsInt = height;

        var m_StreamData = texture2DInfo["m_StreamData"];

        if (string.IsNullOrEmpty(m_StreamData["path"].AsString))
        {
            var image_data = texture2DInfo["image data"];
            image_data.Value.ValueType = AssetValueType.ByteArray;
            image_data.TemplateField.ValueType = AssetValueType.ByteArray;
            image_data.AsByteArray = encData;
        }
        else
        {
            var resourceFileName = Path.GetFileName(m_StreamData["path"].AsString);

            var offset = ResourceReplaceHelper.Replace(resourceFileName, bundle, resourceStreams, m_StreamData["size"].AsInt, m_StreamData["offset"].AsInt, encData);
            m_StreamData["offset"].AsInt = (int)offset;
            m_StreamData["size"].AsInt = encData.Length;
        }


        var bytes = texture2DInfo.WriteToByteArray();
        texture2D.Replacer = new ContentReplacerFromBuffer(bytes);

        return true;
    }

    public static string GetFileName(int index, AssetFileInfo texture2D, AssetTypeValueField texture2DInfo)
    {
        var name = texture2DInfo["m_Name"].AsString;

        var id = texture2D.PathId;
        var pngPath = $"{BundleReplaceHelper.EscapeFileName(name)}-{(index == 0 ? "" : $"{index}_")}{id:X16}.png";

        return pngPath;
    }
}
