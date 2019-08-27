using Microsoft.VisualStudio.TestTools.UnitTesting;
using PNGManipulator;
using System.Linq;

namespace PNGManipulatorTest
{
    [TestClass]
    public class PNGTest
    {
        [TestMethod]
        //[DeploymentItem(@"²°Ý;;.PNG")]
        public void TestLoadPNG()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                System.Console.WriteLine(string.Format("Begin {0}", System.DateTime.Now));

                var path = @"²°Ý;;.PNG";
                using (var file = System.IO.File.OpenRead(path))
                {
                    var img = new PNGImage();
                    await img.Load(file, true);

                    foreach(var current in img.Chunks)
                    {
                        switch (current)
                        {
                            case ZTXTChunk c:
                                {
                                    var text = await c.GetText();
                                    System.Console.WriteLine(
                                        string.Format("{0} {1} {2}", "ZTXT", c.Keyword, text));
                                }
                                break;
                            case ITXTChunk c:
                                {
                                    var text = await c.GetText();
                                    System.Console.WriteLine(
                                        string.Format("{0} {1} {2}", "ITXT", c.Keyword, text));
                                }
                                break;
                            case TEXTChunk c:
                                {
                                    var text = await c.GetText();
                                    System.Console.WriteLine(
                                        string.Format("{0} {1} {2}", "TEXT", c.Keyword, text));
                                }
                                break;
                            case EXIFChunk c:
                                {
                                    await c.GetExifBinary();
                                    System.Console.WriteLine(string.Format("{0}", "EXIF"));
                                }
                                break;
                            case UnknownChunk u:
                                System.Console.WriteLine(
                                    string.Format("{0} {1}", u.ChunkType, (await u.GetChunkData()).Length)
                                    );
                                break;
                            default:
                                System.Console.WriteLine("{0}", current.ChunkType);
                                break;
                        }
                    }
                }
            }).Wait();
        }

        [TestMethod]
        public void TestLoadAndWritePNG()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                System.Console.WriteLine(string.Format("Begin {0}", System.DateTime.Now));

                var path = @"²°Ý;;.PNG";
                using (var file = System.IO.File.OpenRead(path))
                {
                    var img = new PNGManipulator.PNGImage();
                    await img.Load(file);

                    var memoryStream = new System.IO.MemoryStream();
                    await img.WriteImage(memoryStream);

                    using (var outputFile = System.IO.File.OpenWrite("output.png"))
                    {
                        await memoryStream.CopyToAsync(outputFile);
                    }
                }
            }).Wait();
        }

        [TestMethod]
        public void TestModifyAndWritePNG()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                System.Console.WriteLine(string.Format("Begin {0}", System.DateTime.Now));

                var path = @"²°Ý;;.PNG";
                using (var file = System.IO.File.OpenRead(path))
                {
                    var img = new PNGManipulator.PNGImage();
                    await img.Load(file);

                    img.Chunks = img.Chunks.Where((v) =>
                    {
                        switch (v)
                        {
                            case ZTXTChunk _:
                            case ITXTChunk _:
                                return false;
                            default:
                                return true;
                        }
                    }).ToList();

                    var newChunk = new ITXTChunk();
                    newChunk.ChunkType = "iTXt";
                    newChunk.Keyword = "sample";
                    newChunk.LanguageTag = "";
                    newChunk.TranslatedKeyword = "";
                    await newChunk.SetTextAsync("Hello World!", true);

                    img.Chunks.Insert(1, newChunk);

                    var memoryStream = new System.IO.MemoryStream();
                    await img.WriteImage(memoryStream);

                    using (var outputFile = System.IO.File.OpenWrite("output2.png"))
                    {
                        await memoryStream.CopyToAsync(outputFile);
                        outputFile.SetLength(outputFile.Position);
                    }

                    memoryStream.Position = 0;

                    var img2 = new PNGManipulator.PNGImage();
                    await img2.Load(memoryStream);

                    foreach(var current in img2.Chunks)
                    {
                        switch (current)
                        {
                            case ZTXTChunk c:
                                {
                                    var text = await c.GetText();
                                    System.Console.WriteLine(
                                        string.Format("{0} {1} {2}", "ZTXT", c.Keyword, text));
                                }
                                break;
                            case ITXTChunk c:
                                {
                                    var text = await c.GetText();
                                    System.Console.WriteLine(
                                        string.Format("{0} {1} {2}", "ITXT", c.Keyword, text));
                                }
                                break;
                            case TEXTChunk c:
                                {
                                    var text = await c.GetText();
                                    System.Console.WriteLine(
                                        string.Format("{0} {1} {2}", "TEXT", c.Keyword, text));
                                }
                                break;
                            case EXIFChunk c:
                                {
                                    await c.GetExifBinary();
                                    System.Console.WriteLine(string.Format("{0}", "EXIF"));
                                }
                                break;
                            case UnknownChunk u:
                                System.Console.WriteLine(
                                    string.Format("{0} {1}", u.ChunkType, (await u.GetChunkData()).Length)
                                    );
                                break;
                            default:
                                System.Console.WriteLine("{0}", current.ChunkType);
                                break;
                        }
                    }
                }
            }).Wait();
        }

        [TestMethod]
        public void TestWriteBackPNG()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                System.Console.WriteLine(string.Format("Begin {0}", System.DateTime.Now));

                var path = @"²°Ý;;.PNG";
                using (var file = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.Read))
                {
                    var img = new PNGManipulator.PNGImage();
                    await img.Load(file);

                    img.Chunks = img.Chunks.Where((v) =>
                    {
                        switch (v)
                        {
                            case ZTXTChunk _:
                            case ITXTChunk _:
                                return false;
                            default:
                                return true;
                        }
                    }).ToList();

                    var newChunk = new ITXTChunk();
                    newChunk.ChunkType = "iTXt";
                    newChunk.Keyword = "sample";
                    newChunk.LanguageTag = "";
                    newChunk.TranslatedKeyword = "";
                    await newChunk.SetTextAsync("Hello World!", true);

                    img.Chunks.Insert(1, newChunk);

                    await img.WriteImage();
                }
            }).Wait();
        }
    }
}
