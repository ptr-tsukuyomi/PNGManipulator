using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PNGManipulator
{
    public class SliceStream : System.IO.Stream
    {
        private System.IO.Stream _stream = null;
        private long _length = 0;
        private long _begin = 0;
        public SliceStream(System.IO.Stream stream, long length)
        {
            _stream = stream;
            _length = length;
            _begin = _stream.Position;
        }
        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _length;

        public override long Position { get => _stream.Position - _begin; set => _stream.Position = _begin + value; }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var numberToRead = System.Math.Min(count, Length - Position);
            return _stream.Read(buffer, offset, (int)numberToRead);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset + _begin, origin);
        }

        public override void SetLength(long value)
        {
            _length = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
        }
    }

    public abstract class PNGChunk
    {
        private byte[] _chunkData = null;

        protected UInt32 _length = 0;
        public String ChunkType = null;
        public UInt32 CRC = 0;
        public bool CRCCheck = true;

        protected System.IO.Stream _stream = null;
        protected BinaryTools.BinaryReader _binaryReader = null;
        protected long _offset = 0;

        public void SetOffset(long x) { _offset = x;  }

        public PNGChunk()
        {
        }

        private static async Task<UInt32> CalcCRC(System.IO.Stream stream, long begin, long length)
        {
            stream.Seek(begin, SeekOrigin.Begin);
            var sliceStream = new SliceStream(stream, length);

            var calculator = System.Data.HashFunction.CRC.CRCFactory.Instance.Create(System.Data.HashFunction.CRC.CRCConfig.CRC32);
            var calculatedCRC = await calculator.ComputeHashAsync(sliceStream);

            if (calculatedCRC.BitLength != 32) throw new Exception("CRC BitLength != 32");

            return BitConverter.ToUInt32(calculatedCRC.Hash, 0);
        }

        protected virtual async Task Initialize()
        {
            if (CRCCheck)
            {
                var oldPosition = _stream.Position;
                // calc CRC
                var calculated = await CalcCRC(_stream, _offset + 4, _length + 4);

                // read CRC
                _stream.Seek(_offset + 8 + _length, SeekOrigin.Begin);
                CRC = await _binaryReader.ReadUInt32Async();

                if (calculated != CRC) throw new InvalidPNGException(String.Format("CRC Error, expected: {0}, calculated: {1}", CRC, calculated));

                _stream.Position = oldPosition;
            }
        }

        protected virtual async Task<Stream> FinalizeBinary(Stream payload)
        {
            var memoryStream = new MemoryStream();
            var binaryWriter = new BinaryTools.BinaryWriter(memoryStream, false);

            var length = (UInt32)payload.Length;
            await binaryWriter.WriteUInt32Async(length);

            var chunkType = System.Text.Encoding.ASCII.GetBytes(ChunkType);
            if (chunkType.Length != 4) throw new Exception("ChunkType: wrong number of characters.");
            await binaryWriter.WriteAsync(chunkType, 0, chunkType.Length);

            payload.Seek(0, SeekOrigin.Begin);
            await payload.CopyToAsync(memoryStream);

            var crc = await CalcCRC(memoryStream, 4, length + 4);

            memoryStream.Seek(length + 4 + 4, SeekOrigin.Begin);
            await binaryWriter.WriteUInt32Async(crc);

            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        }

        public abstract Task<Stream> CreateBinary();

        /// <summary>
        /// Load PNGChunk from stream and seek stream to begin of the next chunk.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="checkCRC"></param>
        /// <returns></returns>
        public static async Task<PNGChunk> LoadFromStream(Stream stream, bool checkCRC = true)
        {
            var offset = stream.Position;
            var binaryReader = new BinaryTools.BinaryReader(stream, false);
            var length = await binaryReader.ReadUInt32Async();
            var chunkTypeByte = new Byte[4];
            await binaryReader.ReadAsync(chunkTypeByte, 0, chunkTypeByte.Length);
            var chunkType = System.Text.Encoding.ASCII.GetString(chunkTypeByte);

            // switch by chunkType.ToUpper()
            // select class to make instance

            PNGChunk result;
            switch (chunkType.ToUpper())
            {
                case "IHDR":
                    result = new IHDRChunk();
                    break;
                case "IEND":
                    result = new IENDChunk();
                    break;
                case "PLTE":
                    result = new PLTEChunk();
                    break;
                case "IDAT":
                    result = new IDATChunk();
                    break;
                case "TEXT":
                    result = new TEXTChunk();
                    break;
                case "ZTXT":
                    result = new ZTXTChunk();
                    break;
                case "ITXT":
                    result = new ITXTChunk();
                    break;
                case "EXIF":
                    result = new EXIFChunk();
                    break;
                default:
                    result = new UnknownChunk();
                    break;
            }

            result._length = length;
            result.ChunkType = chunkType;
            result._stream = stream;
            result._binaryReader = binaryReader;
            result._offset = offset;
            result.CRCCheck = checkCRC;

            await result.Initialize();

            stream.Seek(offset + 8 + length + 4, SeekOrigin.Begin);

            return result;
        }

        protected async Task<byte[]> _GetChunkData()
        {
            if (_chunkData != null) return _chunkData;

            _stream.Seek(_offset + 8, System.IO.SeekOrigin.Begin);

            _chunkData = new byte[_length];
            var memoryStream = new System.IO.MemoryStream(_chunkData);

            var chunkDataStream = new SliceStream(_stream, _length);

            await chunkDataStream.CopyToAsync(memoryStream);

            return _chunkData;
        }

        protected void _SetChunkData(byte[] data)
        {
            _chunkData = data;
            _length = (uint)data.Length;
        }
    }

    public class UnknownChunk : PNGChunk
    {
        public override async Task<Stream> CreateBinary()
        {
            var chunkData = await _GetChunkData();
            var memoryStream = new MemoryStream(chunkData);

            return await base.FinalizeBinary(memoryStream);
        }

        public async Task<byte[]> GetChunkData()
        {
            return await _GetChunkData();
        }

        public void SetChunkData(byte[] data)
        {
            _SetChunkData(data);
        }
    }

    public class IHDRChunk : PNGChunk
    {
        public IHDRChunk() : base() { ChunkType = "IHDR"; }

        public UInt32 Width;
        public UInt32 Height;
        public byte BitDepth;
        public byte ColorType;
        public byte CompressionMethod;
        public byte FilterMethod;
        public byte InterlaceMethod;

        protected override async Task Initialize()
        {
            await base.Initialize();

            Width = await _binaryReader.ReadUInt32Async();
            Height = await _binaryReader.ReadUInt32Async();

            BitDepth = await _binaryReader.ReadByteAsync();
            ColorType = await _binaryReader.ReadByteAsync();
            CompressionMethod = await _binaryReader.ReadByteAsync();
            FilterMethod = await _binaryReader.ReadByteAsync();
            InterlaceMethod = await _binaryReader.ReadByteAsync();
        }

        public async override Task<Stream> CreateBinary()
        {
            var memoryStream = new MemoryStream();
            var binaryWriter = new BinaryTools.BinaryWriter(memoryStream, false);

            await binaryWriter.WriteUInt32Async(Width);
            await binaryWriter.WriteUInt32Async(Height);
            await binaryWriter.WriteByteAsync(BitDepth);
            await binaryWriter.WriteByteAsync(ColorType);
            await binaryWriter.WriteByteAsync(CompressionMethod);
            await binaryWriter.WriteByteAsync(FilterMethod);
            await binaryWriter.WriteByteAsync(InterlaceMethod);

            return await base.FinalizeBinary(memoryStream);
        }
    }

    public class PLTEChunk : PNGChunk
    {
        public PLTEChunk() : base() { ChunkType = "PLTE"; }

        public struct PaletteEntry
        {
            public byte Red;
            public byte Green;
            public byte Blue;
        }

        private PaletteEntry[] _paletteEntries;

        public async Task<PaletteEntry[]> GetPaletteEntries()
        {
            var chunkData = await _GetChunkData();
            var numOfPalettes = chunkData.Length / 3;
            _paletteEntries = new PaletteEntry[numOfPalettes];

            foreach(var i in Enumerable.Range(0, numOfPalettes))
            {
                _paletteEntries[i].Red = chunkData[i * 3 + 0];
                _paletteEntries[i].Green = chunkData[i * 3 + 1];
                _paletteEntries[i].Blue = chunkData[i * 3 + 1];
            }

            return _paletteEntries;
        }

        protected override async Task Initialize()
        {
            await base.Initialize();
        }

        public override async Task<Stream> CreateBinary()
        {
            var memoryStream = new MemoryStream();
            var binaryWriter = new BinaryTools.BinaryWriter(memoryStream, false);

            foreach (var e in _paletteEntries)
            {
                await binaryWriter.WriteByteAsync(e.Red);
                await binaryWriter.WriteByteAsync(e.Green);
                await binaryWriter.WriteByteAsync(e.Blue);
            }

            return await base.FinalizeBinary(memoryStream);
        }
    }

    public class IDATChunk : PNGChunk
    {
        public IDATChunk() : base()
        {
            ChunkType = "IDAT";
        }

        public override async Task<Stream> CreateBinary()
        {
            var chunkData = await _GetChunkData();
            var memoryStream = new MemoryStream(chunkData);

            return await base.FinalizeBinary(memoryStream);
        }

        protected override async Task Initialize()
        {
            await base.Initialize();
        }

        public async Task<byte[]> GetData()
        {
            return await _GetChunkData();
        }

        public void SetData(byte[] data)
        {
            _SetChunkData(data);
        }
    }

    public class IENDChunk : PNGChunk
    {
        public IENDChunk() : base() { ChunkType = "IEND"; }

        public override async Task<Stream> CreateBinary()
        {
            var memoryStream = new MemoryStream();

            return await base.FinalizeBinary(memoryStream);
        }

        protected override Task Initialize()
        {
            return Task.CompletedTask;
        }
    }

    public class TEXTChunk : PNGChunk
    {
        public string Keyword;
        //public string Text;

        private byte[] _rawText;
        private long _positionTextBegin;
        private long _textLength;

        public async Task<byte[]> GetRawText()
        {
            if (_rawText == null)
            {
                _rawText = new byte[_textLength];
                _stream.Position = _positionTextBegin;
                await _stream.ReadAsync(_rawText, 0, _rawText.Length);
            }
            return _rawText;
        }

        public async Task<string> GetText()
        {
            return System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(await GetRawText());
        }

        public void SetRawText(byte[] data)
        {
            _rawText = data;
        }

        public void SetText(string str)
        {
            _rawText = System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(str);
        }

        public TEXTChunk() : base() { ChunkType = "tEXt"; }

        protected override async Task Initialize()
        {
            await base.Initialize();

            var positionKeywordBegin = _stream.Position;
            Keyword = await _binaryReader.ReadStringAsync(System.Text.Encoding.ASCII);
            _positionTextBegin = _stream.Position;
            _textLength = _length - (_positionTextBegin - positionKeywordBegin);
        }

        public override async Task<Stream> CreateBinary()
        {
            var memoryStream = new MemoryStream();
            var binaryWriter = new BinaryTools.BinaryWriter(memoryStream, false);
            await binaryWriter.WriteStringAsync(Keyword, System.Text.Encoding.ASCII);

            var rawText = await GetRawText();
            await memoryStream.WriteAsync(rawText, 0, rawText.Length);

            return await base.FinalizeBinary(memoryStream);
        }
    }

    public class ZTXTChunk : PNGChunk
    {
        public string Keyword;

        private long compressedTextBegin = 0;
        private long compressedTextLength = 0;

        private byte[] compressedText = null;
        private string text = null;
        
        public ZTXTChunk() : base()
        {
            ChunkType = "zTXt";
        }

        protected override async Task Initialize()
        {
            await base.Initialize();

            var keywordBegin = _stream.Position;
            Keyword = await _binaryReader.ReadStringAsync(System.Text.Encoding.ASCII);
            /*var compressionMethod = */
            await _binaryReader.ReadByteAsync();
            compressedTextBegin = _stream.Position;
            compressedTextLength = _length - (compressedTextBegin - keywordBegin);
        }

        public async Task<byte[]> GetCompressedText()
        {
            if (compressedText != null) return compressedText;

            compressedText = new byte[compressedTextLength];

            _stream.Position = compressedTextBegin;
            await _stream.ReadAsync(compressedText, 0, compressedText.Length);

            return compressedText;
        }

        public async Task<byte[]> GetRawText()
        {
            var compressed = await GetCompressedText();
            var compressedMemoryStream = new MemoryStream(compressed);
            var zstream = new Ionic.Zlib.ZlibStream(compressedMemoryStream, Ionic.Zlib.CompressionMode.Decompress);
            var decompressedMemoryStream = new MemoryStream();
            await zstream.CopyToAsync(decompressedMemoryStream);

            var rawText = new byte[decompressedMemoryStream.Length];
            decompressedMemoryStream.Seek(0, SeekOrigin.Begin);
            await decompressedMemoryStream.ReadAsync(rawText, 0, rawText.Length);

            return rawText;
        }

        public async Task<string> GetText()
        {
            if (text != null) return text;

            var compressed = await GetCompressedText();
            var memoryStream = new MemoryStream(compressed);
            var zstream = new Ionic.Zlib.ZlibStream(memoryStream, Ionic.Zlib.CompressionMode.Decompress);
            var streamReader = new System.IO.StreamReader(
                zstream,
                System.Text.Encoding.GetEncoding("ISO-8859-1")
                );
            text = await streamReader.ReadToEndAsync();
            return text;
        }

        public void SetCompressedText(byte[] data)
        {
            compressedText = data;
        }

        public async Task SetRawTextAsync(byte[] data)
        {
            var rawMemoryStream = new MemoryStream(data);
            var zstream = new Ionic.Zlib.ZlibStream(rawMemoryStream, Ionic.Zlib.CompressionMode.Compress);
            var compressedMemoryStream = new MemoryStream();
            await zstream.CopyToAsync(compressedMemoryStream);

            var compressedText = new byte[compressedMemoryStream.Length];
            compressedMemoryStream.Seek(0, SeekOrigin.Begin);
            await compressedMemoryStream.ReadAsync(compressedText, 0, compressedText.Length);

            SetCompressedText(compressedText);
        }

        public async Task SetTextAsync(string str)
        {
            await SetRawTextAsync(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(str));
        }

        public override async Task<Stream> CreateBinary()
        {
            var memoryStream = new MemoryStream();
            var binaryWriter = new BinaryTools.BinaryWriter(memoryStream, false);
            await binaryWriter.WriteStringAsync(Keyword, System.Text.Encoding.ASCII);
            await binaryWriter.WriteByteAsync(0); // compression method

            var rawText = await GetCompressedText();
            await memoryStream.WriteAsync(rawText, 0, rawText.Length);

            return await base.FinalizeBinary(memoryStream);
        }
    }

    public class ITXTChunk : PNGChunk
    {
        public string Keyword = null;

        public byte CompressionFlag { get; private set; } = 0;
        public string LanguageTag = null;
        public string TranslatedKeyword = null;

        private byte[] _rawText = null;

        private long _textBegin = 0;
        private long _textLength = 0;

        private byte _originalCompressionFlag = 0;

        public ITXTChunk() : base()
        {
            ChunkType = "iTXt";
        }

        protected async override Task Initialize()
        {
            await base.Initialize();

            var keywordBegin = _stream.Position;
            Keyword = await _binaryReader.ReadStringAsync(System.Text.Encoding.ASCII);
            CompressionFlag = await _binaryReader.ReadByteAsync();
            _originalCompressionFlag = CompressionFlag;
            /*CompressionMethod = */await _binaryReader.ReadByteAsync();
            LanguageTag = await _binaryReader.ReadStringAsync(System.Text.Encoding.ASCII);
            TranslatedKeyword = await _binaryReader.ReadStringAsync(System.Text.Encoding.UTF8);

            _textBegin = _stream.Position;
            _textLength = _length - (_textBegin - keywordBegin);
        }

        public async Task<byte[]> GetRawText()
        {
            if (_rawText == null && _stream != null)
            {
                _rawText = new byte[_textLength];
                _stream.Position = _textBegin;
                await _stream.ReadAsync(_rawText, 0, _rawText.Length);

                CompressionFlag = _originalCompressionFlag;
            }

            return _rawText;
        }

        public async Task<byte[]> GetTextByteArray()
        {
            var rawText = await GetRawText();

            if (CompressionFlag != 0)
            {
                var compressedMemoryStream = new MemoryStream(rawText);
                var stream = new Ionic.Zlib.ZlibStream(compressedMemoryStream, Ionic.Zlib.CompressionMode.Decompress);
                var decompressedMemoryStream = new MemoryStream();
                await stream.CopyToAsync(decompressedMemoryStream);

                decompressedMemoryStream.Position = 0;
                var decompressedRawText = new byte[decompressedMemoryStream.Length];
                await decompressedMemoryStream.ReadAsync(decompressedRawText, 0, decompressedRawText.Length);
                return decompressedRawText;
            }
            else
            {
                return rawText;
            }
        }

        public async Task<string> GetText()
        {
            var rawText = await GetTextByteArray();

            var stream = new MemoryStream(rawText);
            var streamReader = new System.IO.StreamReader(
                stream,
                System.Text.Encoding.UTF8
                );
            return await streamReader.ReadToEndAsync();
        }

        public void SetRawText(byte[] data, bool isCompressed = false)
        {
            CompressionFlag = (byte)(isCompressed ? 1 : 0);
            _rawText = data;
        }

        public async Task SetTextAsync(byte[] data, bool compress = false)
        {
            if (compress)
            {
                var rawMemoryStream = new MemoryStream(data);
                var compressedMemoryStream = new MemoryStream();
                var stream = new Ionic.Zlib.ZlibStream(compressedMemoryStream, Ionic.Zlib.CompressionMode.Compress);
                stream.FlushMode = Ionic.Zlib.FlushType.Finish;

                await rawMemoryStream.CopyToAsync(stream);

                compressedMemoryStream.Position = 0;
                var compressedRawText = new byte[compressedMemoryStream.Length];
                await compressedMemoryStream.ReadAsync(compressedRawText, 0, compressedRawText.Length);
                SetRawText(compressedRawText, true);
            } else
            {
                SetRawText(data, false);
            }
        }

        public async Task SetTextAsync(string str, bool compress = false)
        {
            await SetTextAsync(System.Text.Encoding.UTF8.GetBytes(str), compress);
        }

        public override async Task<Stream> CreateBinary()
        {
            var memoryStream = new MemoryStream();
            var binaryWriter = new BinaryTools.BinaryWriter(memoryStream, false);
            await binaryWriter.WriteStringAsync(Keyword, System.Text.Encoding.ASCII);
            await binaryWriter.WriteByteAsync(CompressionFlag); 
            await binaryWriter.WriteByteAsync(0); // compression method
            await binaryWriter.WriteStringAsync(LanguageTag, System.Text.Encoding.ASCII);
            await binaryWriter.WriteStringAsync(TranslatedKeyword, System.Text.Encoding.UTF8);

            await GetRawText();
            var rawText = _rawText;
            await memoryStream.WriteAsync(rawText, 0, rawText.Length);

            return await base.FinalizeBinary(memoryStream);
        }
    }

    public class EXIFChunk : PNGChunk
    {
        public EXIFChunk() : base() { ChunkType = "zTXt"; }

        public override async Task<Stream> CreateBinary()
        {
            var chunkData = await _GetChunkData();
            var memoryStream = new MemoryStream(chunkData);

            return await base.FinalizeBinary(memoryStream);
        }

        protected override async Task Initialize()
        {
            await base.Initialize();
        }

        public async Task<byte[]> GetExifBinary()
        {
            return await _GetChunkData();
        }

        public void SetExifBinary(byte[] data)
        {
            _SetChunkData(data);
        }
    }

}
