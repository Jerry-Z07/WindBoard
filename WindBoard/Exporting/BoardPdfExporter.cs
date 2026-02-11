using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using WindBoard.Board.Persistence;
using WindBoard.Localization;

namespace WindBoard.Exporting
{
    /// <summary>
    /// PDF 导出（v1：位图嵌入）。
    /// </summary>
    internal static class BoardPdfExporter
    {
        public static void Export(IReadOnlyList<BoardPageSnapshot> pages, string filePath, BoardPdfExportOptions options, CancellationToken cancellationToken = default)
        {
            if (pages is null)
            {
                throw new ArgumentNullException(nameof(pages));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(L10n.Get("Export_PathEmpty_Message"), nameof(filePath));
            }

            if (pages.Count == 0)
            {
                throw new ArgumentException(L10n.Get("Export_Pdf_AtLeastOnePage_Message"), nameof(pages));
            }

            cancellationToken.ThrowIfCancellationRequested();

            int totalObjects = 2 + pages.Count * 3;
            var objectOffsets = new long[totalObjects + 1];

            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            var writer = new PdfWriter(fileStream, objectOffsets);

            writer.WriteHeader();

            int catalogObj = 1;
            int pagesObj = 2;

            var imageObjNums = new int[pages.Count];
            var contentObjNums = new int[pages.Count];
            var pageObjNums = new int[pages.Count];

            for (int i = 0; i < pages.Count; i++)
            {
                int baseObj = 3 + i * 3;
                imageObjNums[i] = baseObj;
                contentObjNums[i] = baseObj + 1;
                pageObjNums[i] = baseObj + 2;
            }

            // Catalog
            writer.BeginObject(catalogObj);
            writer.WriteRaw("<< /Type /Catalog /Pages ");
            writer.WriteRaw(Ref(pagesObj));
            writer.WriteRaw(" >>\n");
            writer.EndObject();

            // Pages tree
            writer.BeginObject(pagesObj);
            writer.WriteRaw("<< /Type /Pages /Count ");
            writer.WriteRaw(pages.Count.ToString(CultureInfo.InvariantCulture));
            writer.WriteRaw(" /Kids [ ");
            for (int i = 0; i < pageObjNums.Length; i++)
            {
                writer.WriteRaw(Ref(pageObjNums[i]));
                writer.WriteRaw(" ");
            }

            writer.WriteRaw("] >>\n");
            writer.EndObject();

            // Page content
            using var raster = new BoardRasterExporter();
            BoardRasterExportOptions rasterOptions = options.RasterOptions;

            for (int i = 0; i < pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                RasterizedRgbPage rgbPage = raster.RenderRgbPage(pages[i], rasterOptions, cancellationToken);

                // PDF 以 points（1/72 inch）作为单位：points = pixels / dpi * 72
                double pageWidthPoints = rgbPage.PixelWidth * 72.0 / Math.Max(1.0, rgbPage.Dpi);
                double pageHeightPoints = rgbPage.PixelHeight * 72.0 / Math.Max(1.0, rgbPage.Dpi);

                byte[] compressed = CompressZlib(rgbPage.RgbBytes);

                // Image XObject
                writer.WriteImageObject(
                    objectNumber: imageObjNums[i],
                    pixelWidth: rgbPage.PixelWidth,
                    pixelHeight: rgbPage.PixelHeight,
                    compressedRgbData: compressed);

                // Contents：将图片铺满整页
                string w = FormatNumber(pageWidthPoints);
                string h = FormatNumber(pageHeightPoints);
                string content =
                    "q\n"
                    + w + " 0 0 " + h + " 0 0 cm\n"
                    + "/Im0 Do\n"
                    + "Q\n";

                writer.WriteStreamObject(contentObjNums[i], Encoding.ASCII.GetBytes(content));

                // Page
                writer.BeginObject(pageObjNums[i]);
                writer.WriteRaw("<< /Type /Page /Parent ");
                writer.WriteRaw(Ref(pagesObj));
                writer.WriteRaw(" /MediaBox [0 0 ");
                writer.WriteRaw(w);
                writer.WriteRaw(" ");
                writer.WriteRaw(h);
                writer.WriteRaw("] ");
                writer.WriteRaw("/Resources << /XObject << /Im0 ");
                writer.WriteRaw(Ref(imageObjNums[i]));
                writer.WriteRaw(" >> >> ");
                writer.WriteRaw("/Contents ");
                writer.WriteRaw(Ref(contentObjNums[i]));
                writer.WriteRaw(" >>\n");
                writer.EndObject();
            }

            writer.WriteXrefAndTrailer(catalogObj);
        }

        private static byte[] CompressZlib(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(data, 0, data.Length);
            }

            return ms.ToArray();
        }

        private static string Ref(int objectNumber)
        {
            return objectNumber.ToString(CultureInfo.InvariantCulture) + " 0 R";
        }

        private static string FormatNumber(double value)
        {
            // PDF 数值必须使用 '.' 作为小数点，因此强制 InvariantCulture。
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private sealed class PdfWriter
        {
            private static readonly byte[] PdfHeader = Encoding.ASCII.GetBytes("%PDF-1.4\n");
            private static readonly byte[] PdfBinaryComment = { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' };

            private readonly Stream _stream;
            private readonly long[] _objectOffsets;

            public PdfWriter(Stream stream, long[] objectOffsets)
            {
                _stream = stream ?? throw new ArgumentNullException(nameof(stream));
                _objectOffsets = objectOffsets ?? throw new ArgumentNullException(nameof(objectOffsets));
            }

            public void WriteHeader()
            {
                _stream.Write(PdfHeader, 0, PdfHeader.Length);
                _stream.Write(PdfBinaryComment, 0, PdfBinaryComment.Length);
            }

            public void BeginObject(int objectNumber)
            {
                _objectOffsets[objectNumber] = _stream.Position;
                WriteRaw(objectNumber.ToString(CultureInfo.InvariantCulture));
                WriteRaw(" 0 obj\n");
            }

            public void EndObject()
            {
                WriteRaw("endobj\n");
            }

            public void WriteStreamObject(int objectNumber, byte[] streamBytes)
            {
                BeginObject(objectNumber);
                WriteRaw("<< /Length ");
                WriteRaw(streamBytes.Length.ToString(CultureInfo.InvariantCulture));
                WriteRaw(" >>\n");
                WriteRaw("stream\n");
                _stream.Write(streamBytes, 0, streamBytes.Length);
                WriteRaw("\nendstream\n");
                EndObject();
            }

            public void WriteImageObject(int objectNumber, int pixelWidth, int pixelHeight, byte[] compressedRgbData)
            {
                BeginObject(objectNumber);
                WriteRaw("<< /Type /XObject /Subtype /Image ");
                WriteRaw("/Width ");
                WriteRaw(pixelWidth.ToString(CultureInfo.InvariantCulture));
                WriteRaw(" /Height ");
                WriteRaw(pixelHeight.ToString(CultureInfo.InvariantCulture));
                WriteRaw(" /ColorSpace /DeviceRGB /BitsPerComponent 8 ");
                WriteRaw("/Filter /FlateDecode ");
                WriteRaw("/Length ");
                WriteRaw(compressedRgbData.Length.ToString(CultureInfo.InvariantCulture));
                WriteRaw(" >>\n");
                WriteRaw("stream\n");
                _stream.Write(compressedRgbData, 0, compressedRgbData.Length);
                WriteRaw("\nendstream\n");
                EndObject();
            }

            public void WriteXrefAndTrailer(int catalogObjectNumber)
            {
                long xrefStart = _stream.Position;

                int totalObjects = _objectOffsets.Length - 1;

                WriteRaw("xref\n");
                WriteRaw("0 ");
                WriteRaw((totalObjects + 1).ToString(CultureInfo.InvariantCulture));
                WriteRaw("\n");

                // 对象 0 固定为 free entry。
                WriteRaw("0000000000 65535 f \n");

                for (int i = 1; i <= totalObjects; i++)
                {
                    long offset = _objectOffsets[i];
                    WriteRaw(offset.ToString("0000000000", CultureInfo.InvariantCulture));
                    WriteRaw(" 00000 n \n");
                }

                WriteRaw("trailer\n");
                WriteRaw("<< /Size ");
                WriteRaw((totalObjects + 1).ToString(CultureInfo.InvariantCulture));
                WriteRaw(" /Root ");
                WriteRaw(BoardPdfExporter.Ref(catalogObjectNumber));
                WriteRaw(" >>\n");
                WriteRaw("startxref\n");
                WriteRaw(xrefStart.ToString(CultureInfo.InvariantCulture));
                WriteRaw("\n%%EOF\n");
            }

            public void WriteRaw(string text)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(text);
                _stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
