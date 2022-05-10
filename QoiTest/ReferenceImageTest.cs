using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Qoi;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Pbm;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace QoiTest;

public class ReferenceImageTest
{
    private static readonly QoiDecoder QoiDecoder = new();

    private static readonly Configuration Configuration = new(new BmpConfigurationModule(), new PngConfigurationModule(), new PbmConfigurationModule());

    private const string TestDataDirectory = "qoi_test_images";

    public static TheoryData<string, string> TestCases
    {
        get
        {
            var matcher = new Matcher();
            matcher.AddInclude($"*.qoi");
            var imageFilePairs = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(TestDataDirectory)))
                .Files
                .Select(match => Path.Combine(TestDataDirectory, match.Path))
                .Select(p => new
                {
                    Qoi = p,
                    Png = Path.ChangeExtension(p, "png"),
                });

            var data = new TheoryData<string, string>();
            foreach (var pair in imageFilePairs) data.Add(pair.Qoi, pair.Png);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(TestCases))]
    public void DecodeCorrectly(string qoiPath, string pngPath)
    {
        var png = Image.Load(Configuration, File.OpenRead(pngPath)).CloneAs<Rgba32>();
        var qoi = QoiDecoder.Decode(Configuration, File.OpenRead(qoiPath), CancellationToken.None).CloneAs<Rgba32>();

        Assert.Equal(ImageBin(png), ImageBin(qoi));
    }

    private static string ImageBin(Image<Rgba32> image)
    {
        var buffer = new byte[image.Height * image.Width * 4];
        image.CopyPixelDataTo(buffer);
        return Convert.ToBase64String(buffer);
    }
}
