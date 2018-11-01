﻿using System.Collections.Generic;
using System.IO;

namespace IxMilia.Dwg
{
    public class DwgDrawing
    {
        public DwgFileHeader FileHeader { get; private set; }
        public DwgHeaderVariables Variables { get; private set; }
        public IList<DwgClassDefinition> Classes { get; private set; }

        internal DwgObjectMap ObjectMap { get; private set; }

        public DwgDrawing()
        {
            FileHeader = new DwgFileHeader(DwgVersionId.Default, 0, 0, 0);
            Variables = new DwgHeaderVariables();
            Classes = new List<DwgClassDefinition>();
            ObjectMap = new DwgObjectMap();
        }

#if HAS_FILESYSTEM_ACCESS
        public static DwgDrawing Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open))
            {
                return Load(stream);
            }
        }
#endif

        public static DwgDrawing Load(Stream stream)
        {
            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            return Load(buffer);
        }

        public static DwgDrawing Load(byte[] data)
        {
            var reader = new BitReader(data);
            var drawing = new DwgDrawing();
            drawing.FileHeader = DwgFileHeader.Parse(reader);
            drawing.Variables = DwgHeaderVariables.Parse(reader.FromOffset(drawing.FileHeader.HeaderVariablesLocator.Pointer), drawing.FileHeader.Version);
            drawing.Classes = DwgClasses.Parse(reader.FromOffset(drawing.FileHeader.ClassSectionLocator.Pointer), drawing.FileHeader.Version);
            drawing.ObjectMap = DwgObjectMap.Parse(reader.FromOffset(drawing.FileHeader.ObjectMapLocator.Pointer));
            // don't read the R13C3 and later unknown section
            drawing.FileHeader.ValidateSecondHeader(reader, drawing.Variables);

            return drawing;
        }

#if HAS_FILESYSTEM_ACCESS
        public void Save(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            {
                Save(fs);
            }
        }
#endif

        public void Save(Stream stream)
        {
            // write the file header; this will be re-written again once the pointers have been calculated
            var writer = new BitWriter(stream);
            var fileHeaderLocation = writer.Position;
            FileHeader.Write(writer);

            var variablesStart = writer.Position;
            Variables.Write(writer, FileHeader.Version);
            FileHeader.HeaderVariablesLocator = DwgFileHeader.DwgSectionLocator.HeaderVariablesLocator(variablesStart - fileHeaderLocation, writer.Position - variablesStart);

            var classesStart = writer.Position;
            DwgClasses.Write(Classes, writer);
            FileHeader.ClassSectionLocator = DwgFileHeader.DwgSectionLocator.ClassSectionLocator(classesStart - fileHeaderLocation, writer.Position - classesStart);

            var paddingStart = writer.Position;
            writer.WriteBytes(new byte[0x200]); // may contain the MEASUREMENT variable as the first 4 bytes, but not required
            FileHeader.UnknownSection_PaddingLocator = DwgFileHeader.DwgSectionLocator.UnknownSection_PaddingLocator(paddingStart - fileHeaderLocation, writer.Position - paddingStart);

            var objectDataStart = writer.Position;
            writer.WriteBytes(new byte[0]); // TODO
            // no pointer to set

            var objectMapStart = writer.Position;
            ObjectMap.Write(writer);
            FileHeader.ObjectMapLocator = DwgFileHeader.DwgSectionLocator.ObjectMapLocator(objectMapStart - fileHeaderLocation, writer.Position - objectMapStart);

            var unknownR13C3Start = writer.Position;
            DwgUnknownSectionR13C3.Write(writer);
            FileHeader.UnknownSection_R13C3AndLaterLocator = DwgFileHeader.DwgSectionLocator.UnknownSection_R13C3AndLaterLocator(unknownR13C3Start - fileHeaderLocation, writer.Position - unknownR13C3Start);

            var secondHeaderStart = writer.Position;
            FileHeader.WriteSecondHeader(writer, Variables, secondHeaderStart - fileHeaderLocation);

            var imageDataStart = writer.Position;
            writer.WriteBytes(new byte[0]); // TODO
            FileHeader.ImagePointer = imageDataStart - fileHeaderLocation;

            // re-write the file header now that the pointer values have been set
            var endPos = writer.Position;
            writer.BaseStream.Seek(fileHeaderLocation, SeekOrigin.Begin);
            FileHeader.Write(writer);
            writer.BaseStream.Seek(endPos, SeekOrigin.Begin);
        }
    }
}
