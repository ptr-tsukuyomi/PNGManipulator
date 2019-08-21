using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PNGManipulator
{
    public class ChunkIndexEntry
    {
        public String Type;
        public Int64 OffsetFromHeadOfFile;
        public PNGChunk Instance;
    }

    public class PNGImage
    {
        public const int PNGSignatureLength = 8;
        public byte[] PNGSignature = new byte[PNGSignatureLength] { 137, 80, 78, 71, 13, 10, 26, 10 };

        public List<PNGChunk> Chunks;

        private Stream _stream = null;


        public PNGImage()
        {

        }

        private async Task _LoadChunks(bool checkCRC = true, bool exploreBeyondIEND = false)
        {
            _stream.Position = PNGSignatureLength;
            Chunks = new List<PNGChunk>();

            while (true)
            {
                try
                {
                    var offset = _stream.Position;
                    var chunk = await PNGChunk.LoadFromStream(_stream, checkCRC);
                    Chunks.Add(chunk);

                    if (!exploreBeyondIEND && chunk.GetType() == typeof(IENDChunk)) break;
                }
                catch (InvalidPNGException)
                {
                    throw;
                }
                catch (BinaryTools.DataLengthNotEnoughException)
                {
                    break;
                }
            }
        }

        public async Task Load(System.IO.Stream stream, bool checkCRC = true, bool exploreBeyondIEND = false)
        {
            _stream = stream;
            byte[] signature = new byte[PNGSignatureLength];
            
            var read = await _stream.ReadAsync(signature, 0, signature.Length);

            if (read != signature.Length || !signature.SequenceEqual(PNGSignature))
            {
                throw new InvalidPNGException("PNG Signature is invalid.");
            }

            await _LoadChunks(checkCRC, exploreBeyondIEND);
        }

        public async Task WriteImage(System.IO.Stream stream = null)
        {
            using (var memoryStream = new MemoryStream())
            {
                await memoryStream.WriteAsync(PNGSignature, 0, PNGSignature.Length);

                // create and join chunk binaries
                foreach (var c in Chunks)
                {
                    var chunkBinary = await c.CreateBinary();
                    var offset = memoryStream.Position;
                    await chunkBinary.CopyToAsync(memoryStream);
                    if (stream == null)
                    {
                        c.SetOffset(offset);
                    }
                }

                // actual write
                var outputStream = stream != null ? stream : _stream;
                outputStream.Seek(0, SeekOrigin.Begin);
                memoryStream.Seek(0, SeekOrigin.Begin);

                await memoryStream.CopyToAsync(outputStream);

                var length = outputStream.Position;
                outputStream.SetLength(length);
                outputStream.Seek(0, SeekOrigin.Begin);
            }
        }
    }
}
