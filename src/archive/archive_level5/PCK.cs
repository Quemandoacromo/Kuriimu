using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using Kontract.Interface;
using Kontract.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Text;

namespace archive_level5.PCK
{
    public sealed class PCK
    {
        public List<PckFileInfo> Files;
        Stream _stream = null;

        public T FromXmlString<T>(string xml)
        {
            using (var sr = new StringReader(xml))
            {
                return (T)new XmlSerializer(typeof(T)).Deserialize(sr);
            }
        }

        public PCK(Stream input, string filename)
        {
            _stream = input;

            List<Dict> dicts = null;
            if (File.Exists(".\\plugins\\pck_dicts.xml"))
                dicts = FromXmlString<DictXmlClass>(File.ReadAllText(".\\plugins\\pck_dicts.xml")).dict;

            using (var br = new BinaryReaderX(input, true))
            {
                var entries = br.ReadMultiple<Entry>(br.ReadInt32()).ToList();

                var dict = (dicts != null) ? dicts.Where(d => d.pckName == Path.GetFileNameWithoutExtension(filename)).ToList() : new List<Dict>();
                Files = entries.Select(entry =>
                {
                    br.BaseStream.Position = entry.fileOffset;
                    var hashes = (br.ReadInt16() == 0x64) ? br.ReadMultiple<uint>(br.ReadInt16()).ToList() : null;
                    int blockOffset = hashes?.Count + 1 ?? 0;

                    return new PckFileInfo
                    {
                        FileData = new SubStream(
                            input,
                            entry.fileOffset + blockOffset * 4,
                            entry.fileLength - blockOffset * 4),
                        FileName = (dict.Count > 0) ?
                                    (dict[0].keyValuePairs.Find(kvp => kvp.key == entry.hash) != null ?
                                        dict[0].keyValuePairs.Find(kvp => kvp.key == entry.hash).value :
                                        $"0x{entry.hash:X8}.bin") :
                                    $"0x{entry.hash:X8}.bin",
                        State = ArchiveFileState.Archived,
                        Entry = entry,
                        Hashes = hashes
                    };
                }).ToList();
            }
        }

        public void Save(Stream output)
        {
            using (var bw = new BinaryWriterX(output))
            {
                bw.Write(Files.Count);

                // entryList
                int dataPos = 4 + Files.Count * 0xc;
                foreach (var afi in Files)
                {
                    var entry = new Entry
                    {
                        hash = afi.Entry.hash,
                        fileOffset = dataPos,
                        fileLength = 4 * (afi.Hashes?.Count + 1 ?? 0) + (int)afi.FileSize
                    };
                    dataPos += entry.fileLength;
                    bw.WriteStruct(entry);
                }

                // data
                foreach (var afi in Files)
                {
                    if (afi.Hashes != null)
                    {
                        bw.Write((short)0x64);
                        bw.Write((short)afi.Hashes.Count);
                        foreach (var hash in afi.Hashes)
                            bw.Write(hash);
                    }
                    afi.FileData.CopyTo(bw.BaseStream);
                }
            }
        }

        public void Close()
        {
            _stream?.Close();
            _stream = null;
        }
    }
}
