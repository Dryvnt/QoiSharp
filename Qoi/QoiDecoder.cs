using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace Qoi;

public class QoiDecoder : IImageDecoder
{
    public Image<TPixel> Decode<TPixel>(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        return DecodeFromHeader<TPixel>(new Header(stream), configuration, stream);
    }

    public Image Decode(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        var header = new Header(stream);

        return header.Channels switch
        {
            Header.ChannelsHeader.Rgb => DecodeFromHeader<Rgb24>(header, configuration, stream),
            Header.ChannelsHeader.Rgba => DecodeFromHeader<Rgba32>(header, configuration, stream),
            _ => throw new InvalidOperationException($"Invalid {nameof(header.Channels)}"),
        };
    }

    private static Image<TPixel> DecodeFromHeader<TPixel>(Header header, Configuration configuration, Stream stream)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        switch (header.Channels)
        {
            case Header.ChannelsHeader.Rgb when typeof(TPixel) != typeof(Rgb24):
                throw new InvalidImageContentException($"TPixel is {typeof(TPixel)}, expected {typeof(Rgb24)}");
            case Header.ChannelsHeader.Rgba when typeof(TPixel) != typeof(Rgba32):
                throw new InvalidImageContentException($"TPixel is {typeof(TPixel)}, expected {typeof(Rgba32)}");
            case var c when Enum.IsDefined(c):
                break;
            default:
                throw new InvalidOperationException($"Invalid {nameof(header.Channels)}");
        }

        var image = new Image<TPixel>(configuration, (int)header.Width, (int)header.Height);
        var state = new Decoder(new BinaryReader(stream));

        var p = new TPixel();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                p.FromRgba32(state.Next());
                image[x, y] = p;
            }
        }


        return image;
    }

    private sealed class Decoder
    {
        private readonly Rgba32[] _lookup = Enumerable.Repeat(new Rgba32(), 64).ToArray();
        private readonly BinaryReader _reader;
        private Rgba32 _previous;
        private int _runLength;

        public Decoder(BinaryReader reader)
        {
            _reader = reader;
            _previous.R = 0;
            _previous.G = 0;
            _previous.B = 0;
            _previous.A = 255;
        }

        public Rgba32 Next()
        {
            MoveNext();
            var index = (_previous.R * 3 + _previous.G * 5 + _previous.B * 7 + _previous.A * 11) % 64;
            _lookup[index] = _previous;
            return _previous;
        }

        private void MoveNext()
        {
            if (_runLength > 0)
            {
                _runLength--;
                return;
            }

            var op = _reader.ReadByte();
            switch (op)
            {
                case >= 0b0000_0000 and < 0b0100_0000: // QOI_OP_INDEX
                    var index = op & 0b0011_1111;
                    _previous.FromRgba32(_lookup[index]);
                    return;
                case >= 0b0100_0000 and < 0b1000_0000: // QOI_OP_DIFF
                    var dr = ((op & 0b0011_0000) >> 4) - 2;
                    var dg = ((op & 0b0000_1100) >> 2) - 2;
                    var db = (op & 0b0000_0011) - 2;
                    _previous.R += (byte) dr;
                    _previous.G += (byte) dg;
                    _previous.B += (byte) db;
                    return;
                case >= 0b1000_0000 and < 0b1100_0000: // QOI_OP_LUMA
                    var lumaRbByte = _reader.ReadByte();
                    var lumaDg = (op & 0b0011_1111) - 32;
                    var lumaDr = ((lumaRbByte & 0b1111_0000) >> 4) + lumaDg - 8;
                    var lumaDb = (lumaRbByte & 0b0000_1111) + lumaDg - 8;
                    _previous.R += (byte) lumaDr;
                    _previous.G += (byte) lumaDg;
                    _previous.B += (byte) lumaDb;
                    return;
                case >= 0b1100_0000 and < 0b1111_1110: // QOI_OP_RUN
                    Debug.Assert((op & 0b1100_0000) == 0b1100_0000);
                    Debug.Assert(op < 0b1111_1110);
                    _runLength = op & 0b0011_1111;
                    return;
                case 0b1111_1110 or 0b1111_1111: // QOI_OP_RGB or QOI_OP_RGBA
                    _previous.R = _reader.ReadByte();
                    _previous.G = _reader.ReadByte();
                    _previous.B = _reader.ReadByte();
                    if (op is 0b1111_1111) _previous.A = _reader.ReadByte();
                    return;
            }
        }
    }

    private sealed class Header
    {
        public enum ChannelsHeader : byte
        {
            Rgb = 3,
            Rgba = 4,
        }

        public enum ColorSpaceHeader : byte
        {
            SrgbWithLinearAlpha = 0,
            AllChannelsLinear = 1,
        }

        public Header(Stream input)
        {
            var reader = new BinaryReader(input);
            var magic = Encoding.UTF8.GetString(reader.ReadBytes(4));

            if (magic is not "qoif")
            {
                throw new InvalidImageContentException(
                    $"Wrong magic header. Expected 'qoif', got '{magic}'");
            }

            Width = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
            Height = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));

            Channels = (ChannelsHeader)reader.ReadByte();
            if (!Enum.IsDefined(Channels))
                throw new UnknownImageFormatException($"Unknown channels type {Channels}");

            ColorSpace = (ColorSpaceHeader)reader.ReadByte();
            if (!Enum.IsDefined(ColorSpace))
                throw new UnknownImageFormatException($"Unknown colorspace type {ColorSpace}");
        }

        public uint Width { get; }
        public uint Height { get; }
        public ChannelsHeader Channels { get; }
        public ColorSpaceHeader ColorSpace { get; }
    }
}
