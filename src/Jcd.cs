using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace JcdBuilder
{
    /// <summary>Чтение и запись формата .jcd.</summary>
    public static class JcdFormat
    {
        public const string ContentsName = "Contents";
        public const string SummaryName = "\u0005SummaryInformation";

        /// <summary>Постоянный хвост потока Contents: палитра, флаги, стиль Arial (230 байт).</summary>
        public static readonly byte[] Tail = new byte[230]
        {
            0x00, 0x00, 0x00, 0x00, 0x80, 0x80, 0x80, 0x00, 0xC0, 0xC0, 0xC0, 0x00, 0xB5, 0x00, 0x00, 0x00,
            0xFF, 0x00, 0x00, 0x00, 0x80, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00,
            0x00, 0xFF, 0x00, 0x00, 0x00, 0xB5, 0xB5, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0xB5, 0x00,
            0x00, 0x00, 0xFF, 0x00, 0xB5, 0x00, 0xB5, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFA, 0xF0, 0xF0, 0x00,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x05, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x8F, 0x92, 0xB8, 0x00, 0x02, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x80, 0x80, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0xF7, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x41, 0x72, 0x69, 0x61, 0x6C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0xC6, 0xCD, 0xE8, 0x00, 0xA9, 0xB9, 0xE2,
            0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x6A, 0x6A, 0x00, 0x47, 0x54, 0x96,
            0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x01
        };

        // ---------------------------------------------------------------- запись

        public static byte[] BuildContents(JcdPuzzle p)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);

            bw.Write(5u);
            bw.Write(0u);
            bw.Write(5u);
            bw.Write(5u);
            bw.Write((uint)p.Width);
            bw.Write((uint)p.Height);

            int runs = 0;
            for (int y = 0; y < p.Height; y++) runs += WriteClue(bw, LineRuns(p, true, y));
            for (int x = 0; x < p.Width; x++) runs += WriteClue(bw, LineRuns(p, false, x));

            bw.Write(runs > 0 ? 1u : 0u);
            if (runs > 0) bw.Write(p.Cells, 0, p.Cells.Length);

            for (int i = 0; i < 8; i++) bw.Write((byte)0);
            bw.Write(0u);
            bw.Write(0u);
            for (int i = 0; i < runs; i++) bw.Write((byte)0);

            bw.Write(Tail, 0, Tail.Length);
            bw.Flush();
            return ms.ToArray();
        }

        /// <summary>Числа одной строки/столбца: пары (длина, цвет).</summary>
        static List<int> LineRuns(JcdPuzzle p, bool row, int index)
        {
            List<int> res = new List<int>();
            int n = row ? p.Width : p.Height;
            byte cur = JcdPalette.White;
            int len = 0;
            for (int i = 0; i < n; i++)
            {
                byte v = row ? p.Cells[index * p.Width + i] : p.Cells[i * p.Width + index];
                if (v == JcdPalette.White)
                {
                    FlushRun(res, cur, len);
                    len = 0;
                }
                else if (len > 0 && v == cur)
                {
                    len++;
                }
                else
                {
                    FlushRun(res, cur, len);
                    cur = v;
                    len = 1;
                }
            }
            FlushRun(res, cur, len);
            return res;
        }

        static void FlushRun(List<int> res, byte color, int len)
        {
            if (len > 0)
            {
                res.Add(len);
                res.Add(color);
            }
        }

        static int WriteClue(BinaryWriter bw, List<int> runs)
        {
            int count = runs.Count / 2;
            bw.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                bw.Write((uint)runs[2 * i]);
                bw.Write((byte)runs[2 * i + 1]);
            }
            return count;
        }

        // ---------------------------------------------------------------- чтение

        public static JcdPuzzle ReadContents(byte[] c, out int runCount)
        {
            runCount = 0;
            if (c == null || c.Length < 0x18) throw new InvalidDataException("Поток Contents слишком короткий");

            int w = (int)ReadU32(c, 0x10);
            int h = (int)ReadU32(c, 0x14);
            if (w < 1 || h < 1 || w > 200 || h > 200)
                throw new InvalidDataException("Некорректный размер сетки: " + w + " x " + h);

            int pos = 0x18;
            for (int i = 0; i < w + h; i++)
            {
                if (pos + 2 > c.Length) throw new InvalidDataException("Обрыв в таблице чисел");
                int count = ReadU16(c, pos);
                pos += 2;
                if (pos + count * 5 > c.Length) throw new InvalidDataException("Обрыв в таблице чисел");
                pos += count * 5;
                runCount += count;
            }

            if (pos + 4 > c.Length) throw new InvalidDataException("Обрыв перед сеткой");
            uint sep = ReadU32(c, pos);
            pos += 4;

            JcdPuzzle puzzle = new JcdPuzzle(w, h);
            if (sep != 0 && pos + w * h <= c.Length)
            {
                Buffer.BlockCopy(c, pos, puzzle.Cells, 0, w * h);
                pos += w * h;
            }
            return puzzle;
        }

        public static JcdPuzzle Load(string path, out int runCount)
        {
            CfbReader cfb = CfbReader.Open(path);
            if (cfb.Error.Length != 0) throw new InvalidDataException(cfb.Error);
            byte[] contents = cfb.GetStream(ContentsName);
            if (contents == null)
                throw new InvalidDataException("В файле нет потока Contents — это не кроссворд JCD");
            return ReadContents(contents, out runCount);
        }

        public static JcdPuzzle Load(string path)
        {
            int runs;
            return Load(path, out runs);
        }

        static uint ReadU32(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        static int ReadU16(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8);
        }

        // ---------------------------------------------------------------- файл

        public static void Save(string path, JcdPuzzle puzzle, string title)
        {
            List<KeyValuePair<string, byte[]>> streams = new List<KeyValuePair<string, byte[]>>();
            streams.Add(new KeyValuePair<string, byte[]>(ContentsName, BuildContents(puzzle)));
            streams.Add(new KeyValuePair<string, byte[]>(SummaryName,
                BuildSummaryInformation(title, "Кроссворд JCD", "JCD Creator")));
            CfbWriter.Write(path, streams);
        }

        /// <summary>Минимальный, но корректный набор свойств \x05SummaryInformation.</summary>
        public static byte[] BuildSummaryInformation(string title, string subject, string author)
        {
            List<int> ids = new List<int>();
            List<byte[]> values = new List<byte[]>();

            ids.Add(1); values.Add(ValueI2(1251));
            if (title != null) { ids.Add(2); values.Add(ValueStr(title)); }
            if (subject != null) { ids.Add(3); values.Add(ValueStr(subject)); }
            if (author != null) { ids.Add(4); values.Add(ValueStr(author)); }
            ids.Add(18); values.Add(ValueStr("JCD Builder"));
            ids.Add(19); values.Add(ValueI4(2));

            int tableSize = 4 + 8 * ids.Count;
            int valuesSize = 0;
            for (int i = 0; i < values.Count; i++) valuesSize += values[i].Length;
            int sectionSize = 4 + tableSize + valuesSize;

            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);

            bw.Write((ushort)0xFFFE);
            bw.Write((ushort)0);
            bw.Write(0x00020006);
            for (int i = 0; i < 16; i++) bw.Write((byte)0);
            bw.Write(1);
            bw.Write(new byte[]
            {
                0xE0, 0x85, 0x9F, 0xF2, 0xF9, 0x4F, 0x68, 0x10,
                0xAB, 0x91, 0x08, 0x00, 0x2B, 0x27, 0xB3, 0xD9
            });
            bw.Write(48);

            bw.Write((uint)sectionSize);
            bw.Write((uint)ids.Count);

            int offset = 4 + tableSize;
            for (int i = 0; i < ids.Count; i++)
            {
                bw.Write((uint)ids[i]);
                bw.Write((uint)offset);
                offset += values[i].Length;
            }
            for (int i = 0; i < values.Count; i++) bw.Write(values[i]);

            bw.Flush();
            return ms.ToArray();
        }

        static byte[] ValueI2(int value)
        {
            byte[] b = new byte[8];
            PutU32(b, 0, 2);
            b[4] = (byte)value;
            b[5] = (byte)(value >> 8);
            return b;
        }

        static byte[] ValueI4(int value)
        {
            byte[] b = new byte[8];
            PutU32(b, 0, 3);
            PutU32(b, 4, (uint)value);
            return b;
        }

        static byte[] ValueStr(string text)
        {
            byte[] raw = Encoding.GetEncoding(1251).GetBytes(text);
            int len = raw.Length + 1;
            int total = 8 + len;
            int pad = (4 - (total % 4)) % 4;
            byte[] b = new byte[total + pad];
            PutU32(b, 0, 0x1E);
            PutU32(b, 4, (uint)len);
            Buffer.BlockCopy(raw, 0, b, 8, raw.Length);
            return b;
        }

        static void PutU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }
    }
}
