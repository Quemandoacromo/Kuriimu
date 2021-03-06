using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
//using Cetera.Image;
using Kontract.Compression;
using Kontract.Image;
using Kontract.Image.Swizzle;
using Kontract.IO;

namespace image_level5.imgv
{
    public class IMGV
    {
        public Bitmap Image;
        public ImageSettings settings;

        public Header header;
        public byte[] entryStart = null;

        bool editMode = false;
        bool editMode2 = false;
        Level5.Method tableComp;
        Level5.Method picComp;

        public IMGV(Stream input)
        {
            using (var br = new BinaryReaderX(input))
            {
                //Header
                header = br.ReadStruct<Header>();

                //get tile table
                br.BaseStream.Position = header.tableDataOffset;
                var tableC = br.ReadBytes(header.tableSize1);
                tableComp = (Level5.Method)(tableC[0] & 0x7);
                byte[] table = Level5.Decompress(new MemoryStream(tableC));

                //get image data
                br.BaseStream.Position = header.tableDataOffset + header.tableSize2;
                var texC = br.ReadBytes(header.imgDataSize);
                picComp = (Level5.Method)(texC[0] & 0x7);
                byte[] tex = Level5.Decompress(new MemoryStream(texC));

                //order pic blocks by table
                byte[] pic = Order(new MemoryStream(table), new MemoryStream(tex), header.bitDepth);

                //File.WriteAllBytes(@"D:\Users\Kirito\Desktop\xi_data.bin", pic);

                //return finished image
                var isBlockCompression = Support.Format[header.imageFormat].FormatName.Contains("DXT");
                settings = new ImageSettings
                {
                    Width = header.width,
                    Height = header.height,
                    Format = Support.Format[header.imageFormat],
                    Swizzle = isBlockCompression ? new Support.BlockSwizzle(header.width, header.height) : null
                };
                Image = Kontract.Image.Common.Load(pic, settings);
            }
        }

        public byte[] Order(MemoryStream tableStream, MemoryStream texStream, int bitDepth)
        {
            using (var table = new BinaryReaderX(tableStream))
            using (var tex = new BinaryReaderX(texStream))
            {
                int tableLength = (int)table.BaseStream.Length;

                var tmp = table.ReadUInt16();
                table.BaseStream.Position = 0;
                var entryLength = 2;
                if (tmp == 0x453)
                {
                    entryStart = table.ReadBytes(8);
                    entryLength = 4;
                }

                var ms = new MemoryStream();
                for (int i = (int)table.BaseStream.Position; i < tableLength; i += entryLength)
                {
                    uint entry = (entryLength == 2) ? table.ReadUInt16() : table.ReadUInt32();
                    if (entry == 0xFFFF || entry == 0xFFFFFFFF)
                    {
                        for (int j = 0; j < 64 * bitDepth / 8; j++)
                        {
                            ms.WriteByte(0);
                        }
                    }
                    else
                    {
                        if (entry * (64 * bitDepth / 8) < tex.BaseStream.Length)
                        {
                            tex.BaseStream.Position = entry * (64 * bitDepth / 8);
                            for (int j = 0; j < 64 * bitDepth / 8; j++)
                            {
                                ms.WriteByte(tex.ReadByte());
                            }
                        }
                    }
                }
                return ms.ToArray();
            }
        }

        public void Save(string filename)
        {
            int width = (Image.Width + 0x7) & ~0x7;
            int height = (Image.Height + 0x7) & ~0x7;
            var isBlockCompression = Support.Format[header.imageFormat].FormatName.Contains("DXT");
            var settings = new ImageSettings
            {
                Width = width,
                Height = height,
                Format = Support.Format[header.imageFormat],
                Swizzle = isBlockCompression ? new Support.BlockSwizzle(header.width, header.height) : null
            };
            byte[] pic = Kontract.Image.Common.Save(Image, settings);

            using (var bw = new BinaryWriterX(File.Create(filename)))
            {
                //Header
                header.width = (short)Image.Width;
                header.height = (short)Image.Height;

                //tile table
                var table = new MemoryStream();
                byte[] importPic = Deflate(pic, Support.Format[header.imageFormat].BitDepth, out table);

                //Table
                bw.BaseStream.Position = 0x48;
                var comp = Level5.Compress(table, tableComp);
                bw.Write(comp);
                header.tableSize1 = comp.Length;
                header.tableSize2 = (header.tableSize1 + 3) & ~3;

                //Image
                bw.BaseStream.Position = 0x48 + header.tableSize2;
                comp = Level5.Compress(new MemoryStream(importPic), picComp);
                bw.Write(comp);
                bw.WriteAlignment(4);
                header.imgDataSize = comp.Length;

                //Header
                bw.BaseStream.Position = 0;
                bw.WriteStruct(header);
            }
        }

        public byte[] Deflate(byte[] pic, int bpp, out MemoryStream table)
        {
            table = new MemoryStream();
            using (var tableB = new BinaryWriterX(table, true))
            {
                if (entryStart != null) tableB.Write(entryStart);

                List<byte[]> parts = new List<byte[]>();

                using (var picB = new BinaryReaderX(new MemoryStream(pic)))
                    while (picB.BaseStream.Position < picB.BaseStream.Length)
                    {
                        byte[] part = picB.ReadBytes(64 * bpp / 8);

                        if (parts.Find(x => x.SequenceEqual(part)) != null)
                            if (entryStart != null) tableB.Write(parts.FindIndex(x => x.SequenceEqual(part))); else tableB.Write((short)parts.FindIndex(x => x.SequenceEqual(part)));
                        else
                        {
                            if (entryStart != null) tableB.Write(parts.Count); else tableB.Write((short)parts.Count);
                            parts.Add(part);
                        }
                    }

                return parts.SelectMany(x => x.SelectMany(b => new[] { b })).ToArray();
            }
        }
    }
}
