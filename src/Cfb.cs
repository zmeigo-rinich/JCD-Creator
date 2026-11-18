using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace JcdBuilder
{
    /// <summary>Запись в каталоге OLE2-контейнера.</summary>
    public class CfbEntry
    {
        public string Name = "";
        public int Type;
        public uint Start;
        public uint Size;
    }

    /// <summary>Чтение OLE2 / Compound File Binary (только то, что нужно для .jcd).</summary>
    public class CfbReader
    {
        public List<uint> Fat = new List<uint>();
        public List<uint> MiniFat = new List<uint>();
        public byte[] MiniStream = new byte[0];
        public List<CfbEntry> Entries = new List<CfbEntry>();
        public int SectorSize = 512;
        public int MiniSectorSize = 64;
        public string Error = "";

        private uint miniCutoff = 4096;
        private byte[] data = new byte[0];

        public static CfbReader Open(string path)
        {
            CfbReader r = new CfbReader();
            r.Load(path);
            return r;
        }

        static uint U32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
        static ushort U16(byte[] b, int o) { return BitConverter.ToUInt16(b, o); }

        public void Load(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[65536];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                data = ms.ToArray();
            }

            if (data.Length < 512 || U32(data, 0) != 0xE011CFD0u)
            {
                Error = "Это не OLE2-контейнер (не файл .jcd)";
                return;
            }

            SectorSize = 1 << U16(data, 0x1E);
            MiniSectorSize = 1 << U16(data, 0x20);
            miniCutoff = U32(data, 0x38);
            int numFat = (int)U32(data, 0x2C);
            uint firstDir = U32(data, 0x30);
            uint firstMiniFat = U32(data, 0x3C);
            int numMiniFat = (int)U32(data, 0x40);
            uint firstDifat = U32(data, 0x44);
            int numDifat = (int)U32(data, 0x48);

            List<uint> fatSectors = new List<uint>();
            for (int i = 0; i < 109 && fatSectors.Count < numFat; i++)
            {
                uint v = U32(data, 0x4C + 4 * i);
                if (v != 0xFFFFFFFFu) fatSectors.Add(v);
            }
            uint next = firstDifat;
            int guard = 0;
            while (next != 0xFFFFFFFEu && next != 0xFFFFFFFFu && guard++ < 100000)
            {
                int off = SectorOffset(next);
                int cnt = SectorSize / 4 - 1;
                for (int i = 0; i < cnt && fatSectors.Count < numFat; i++)
                {
                    uint v = U32(data, off + 4 * i);
                    if (v != 0xFFFFFFFFu) fatSectors.Add(v);
                }
                next = U32(data, off + 4 * cnt);
            }

            for (int i = 0; i < fatSectors.Count; i++)
            {
                int off = SectorOffset(fatSectors[i]);
                for (int k = 0; k < SectorSize / 4; k++) Fat.Add(U32(data, off + 4 * k));
            }

            byte[] dir = ReadChain(firstDir, -1);
            for (int i = 0; i < dir.Length / 128; i++)
            {
                int o = i * 128;
                int nameLen = U16(dir, o + 0x40);
                CfbEntry e = new CfbEntry();
                e.Name = (nameLen >= 2 && nameLen <= 64) ? Encoding.Unicode.GetString(dir, o, nameLen - 2) : "";
                e.Type = dir[o + 0x42];
                e.Start = U32(dir, o + 0x74);
                e.Size = U32(dir, o + 0x78);
                Entries.Add(e);
            }

            byte[] mf = ReadChain(firstMiniFat, numMiniFat * SectorSize);
            for (int i = 0; i + 3 < mf.Length; i += 4) MiniFat.Add(BitConverter.ToUInt32(mf, i));

            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Type == 5) MiniStream = ReadChain(Entries[i].Start, (int)Entries[i].Size);
        }

        int SectorOffset(uint sector) { return (int)((sector + 1) * (uint)SectorSize); }

        byte[] ReadChain(uint start, int size)
        {
            MemoryStream ms = new MemoryStream();
            uint cur = start;
            int guard = 0;
            while (cur != 0xFFFFFFFEu && cur != 0xFFFFFFFFu && cur < (uint)Fat.Count && guard++ < 1000000)
            {
                int off = SectorOffset(cur);
                int take = SectorSize;
                if (size >= 0 && ms.Length + take > size) take = (int)(size - ms.Length);
                if (take <= 0) break;
                if (off + take > data.Length) take = data.Length - off;
                if (take <= 0) break;
                ms.Write(data, off, take);
                cur = Fat[(int)cur];
            }
            return ms.ToArray();
        }

        byte[] ReadMiniChain(uint start, int size)
        {
            MemoryStream ms = new MemoryStream();
            uint cur = start;
            int guard = 0;
            while (cur != 0xFFFFFFFEu && cur != 0xFFFFFFFFu && cur < (uint)MiniFat.Count && guard++ < 1000000)
            {
                int off = (int)(cur * (uint)MiniSectorSize);
                int take = MiniSectorSize;
                if (size >= 0 && ms.Length + take > size) take = (int)(size - ms.Length);
                if (take <= 0) break;
                if (off + take > MiniStream.Length) take = MiniStream.Length - off;
                if (take <= 0) break;
                ms.Write(MiniStream, off, take);
                cur = MiniFat[(int)cur];
            }
            return ms.ToArray();
        }

        public byte[] GetStream(CfbEntry e)
        {
            if (e.Size == 0) return new byte[0];
            if (e.Size < miniCutoff) return ReadMiniChain(e.Start, (int)e.Size);
            return ReadChain(e.Start, (int)e.Size);
        }

        public byte[] GetStream(string name)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Type == 2 && Entries[i].Name == name) return GetStream(Entries[i]);
            return null;
        }
    }

    /// <summary>Запись OLE2 / Compound File Binary: минимум, достаточный для .jcd.</summary>
    public static class CfbWriter
    {
        const int SS = 512;
        const int MINS = 64;
        const int CUTOFF = 4096;
        const uint FREE = 0xFFFFFFFFu;
        const uint END = 0xFFFFFFFEu;
        const uint FATSECT = 0xFFFFFFFDu;

        // CLSID корневого хранилища JCD
        static readonly byte[] RootClsid = new byte[]
        {
            0x3C, 0xEC, 0x8B, 0x13, 0xFF, 0x97, 0xD6, 0x11,
            0x8E, 0xDB, 0xE0, 0xF0, 0xAD, 0x8E, 0x02, 0x0A
        };

        public static void Write(string path, IList<KeyValuePair<string, byte[]>> streams)
        {
            // 1. Разделяем на маленькие (мини-поток) и большие.
            List<KeyValuePair<string, byte[]>> small = new List<KeyValuePair<string, byte[]>>();
            List<KeyValuePair<string, byte[]>> large = new List<KeyValuePair<string, byte[]>>();
            for (int i = 0; i < streams.Count; i++)
            {
                if (streams[i].Value.Length < CUTOFF) small.Add(streams[i]);
                else large.Add(streams[i]);
            }

            // 2. Собираем мини-поток и miniFAT.
            MemoryStream mini = new MemoryStream();
            List<int> miniStart = new List<int>();
            List<int> miniCount = new List<int>();
            int miniSectors = 0;
            for (int i = 0; i < small.Count; i++)
            {
                byte[] d = small[i].Value;
                int count = (d.Length + MINS - 1) / MINS;
                miniStart.Add(miniSectors);
                miniCount.Add(count);
                mini.Write(d, 0, d.Length);
                int pad = count * MINS - d.Length;
                for (int k = 0; k < pad; k++) mini.WriteByte(0);
                miniSectors += count;
            }
            byte[] miniStream = mini.ToArray();

            uint[] miniFat = new uint[Math.Max(1, miniSectors)];
            for (int i = 0; i < miniFat.Length; i++) miniFat[i] = FREE;
            for (int i = 0; i < small.Count; i++)
            {
                for (int k = 0; k < miniCount[i]; k++)
                {
                    int cur = miniStart[i] + k;
                    miniFat[cur] = (k == miniCount[i] - 1) ? END : (uint)(cur + 1);
                }
            }

            int miniFatSectors = (miniSectors * 4 + SS - 1) / SS;
            if (miniFatSectors == 0) miniFatSectors = 0;

            // 3. Считаем сектора.
            int miniStreamSectors = (miniStream.Length + SS - 1) / SS;
            int[] largeSectors = new int[large.Count];
            int largeTotal = 0;
            for (int i = 0; i < large.Count; i++)
            {
                largeSectors[i] = (large[i].Value.Length + SS - 1) / SS;
                largeTotal += largeSectors[i];
            }

            int dataSectors = 1 + miniFatSectors + miniStreamSectors + largeTotal;
            int numFat = 0;
            int totalSectors;
            while (true)
            {
                totalSectors = dataSectors + numFat;
                int need = (totalSectors + 127) / 128;
                if (need <= numFat) break;
                numFat = need;
            }
            if (numFat > 109) throw new InvalidOperationException("Слишком большой файл для упрощённого CFB-писателя");

            int dirSector = numFat;
            int miniFatStart = dirSector + 1;
            int miniStreamStart = miniFatStart + miniFatSectors;
            int largeStart = miniStreamStart + miniStreamSectors;

            // 4. FAT.
            uint[] fat = new uint[Math.Max(128, numFat * 128)];
            for (int i = 0; i < fat.Length; i++) fat[i] = FREE;
            for (int i = 0; i < numFat; i++) fat[i] = FATSECT;
            fat[dirSector] = END;
            Chain(fat, miniFatStart, miniFatSectors);
            Chain(fat, miniStreamStart, miniStreamSectors);
            int cursor = largeStart;
            for (int i = 0; i < large.Count; i++)
            {
                Chain(fat, cursor, largeSectors[i]);
                cursor += largeSectors[i];
            }

            // 5. Каталог.
            List<KeyValuePair<string, byte[]>> all = new List<KeyValuePair<string, byte[]>>();
            for (int i = 0; i < streams.Count; i++) all.Add(streams[i]);
            all.Sort(delegate(KeyValuePair<string, byte[]> a, KeyValuePair<string, byte[]> b)
            {
                if (a.Key.Length != b.Key.Length) return a.Key.Length.CompareTo(b.Key.Length);
                return string.Compare(a.Key.ToUpperInvariant(), b.Key.ToUpperInvariant(), StringComparison.Ordinal);
            });

            byte[] dir = new byte[SS];
            int rootStart = miniStream.Length > 0 ? miniStreamStart : unchecked((int)END);
            WriteDirEntry(dir, 0, "Root Entry", 5, -1, -1, all.Count > 0 ? 1 : -1,
                          RootClsid, rootStart, miniStream.Length);

            int sectorCursor = largeStart;
            for (int i = 0; i < all.Count; i++)
            {
                string name = all[i].Key;
                byte[] d = all[i].Value;
                bool isSmall = d.Length < CUTOFF;
                int start;
                if (isSmall)
                {
                    int idx = IndexOfStream(small, i, all);
                    start = miniStart[idx];
                }
                else
                {
                    int li = IndexOfLarge(large, all, i);
                    start = sectorCursor;
                    sectorCursor += largeSectors[li];
                }
                int right = -1;
                if (i + 1 < all.Count) right = i + 2; // +2 т.к. 0 — корень
                WriteDirEntry(dir, (i + 1) * 128, name, 2, -1, right, -1, null, start, d.Length);
            }

            // 6. Сборка файла.
            byte[] file = new byte[(totalSectors + 1) * SS];
            WriteHeader(file, numFat, dirSector, miniFatStart, miniFatSectors, miniStream.Length > 0);

            for (int i = 0; i < numFat; i++)
                for (int k = 0; k < 128; k++)
                    PutU32(file, (i + 1) * SS + k * 4, k < fat.Length ? fat[i * 128 + k] : FREE);

            Buffer.BlockCopy(dir, 0, file, (dirSector + 1) * SS, SS);

            if (miniFatSectors > 0)
            {
                byte[] mf = new byte[miniFatSectors * SS];
                for (int i = 0; i < miniFatSectors * SS / 4; i++)
                    PutU32(mf, i * 4, i < miniFat.Length ? miniFat[i] : FREE);
                Buffer.BlockCopy(mf, 0, file, (miniFatStart + 1) * SS, mf.Length);
            }

            if (miniStreamSectors > 0)
                Buffer.BlockCopy(miniStream, 0, file, (miniStreamStart + 1) * SS, miniStream.Length);

            for (int i = 0; i < large.Count; i++)
            {
                int start = largeStart;
                for (int k = 0; k < i; k++) start += largeSectors[k];
                int off = (start + 1) * SS;
                Buffer.BlockCopy(large[i].Value, 0, file, off, large[i].Value.Length);
            }

            File.WriteAllBytes(path, file);
        }

        static void Chain(uint[] fat, int start, int count)
        {
            for (int i = 0; i < count; i++)
                fat[start + i] = (i == count - 1) ? END : (uint)(start + i + 1);
        }

        static int IndexOfStream(List<KeyValuePair<string, byte[]>> small, int allIndex, List<KeyValuePair<string, byte[]>> all)
        {
            for (int i = 0; i < small.Count; i++)
                if (small[i].Key == all[allIndex].Key) return i;
            return 0;
        }

        static int IndexOfLarge(List<KeyValuePair<string, byte[]>> large, List<KeyValuePair<string, byte[]>> all, int allIndex)
        {
            for (int i = 0; i < large.Count; i++)
                if (large[i].Key == all[allIndex].Key) return i;
            return 0;
        }

        static void PutU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
        }

        static void PutU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)v; b[o + 1] = (byte)(v >> 8);
        }

        static void PutI32(byte[] b, int o, int v) { PutU32(b, o, (uint)v); }

        static void WriteDirEntry(byte[] dir, int off, string name, int type, int left, int right, int child,
                                  byte[] clsid, int start, int size)
        {
            byte[] nm = Encoding.Unicode.GetBytes(name);
            Buffer.BlockCopy(nm, 0, dir, off, nm.Length);
            PutU16(dir, off + 0x40, (ushort)(nm.Length + 2));
            dir[off + 0x42] = (byte)type;
            dir[off + 0x43] = 1; // black
            PutI32(dir, off + 0x44, left);
            PutI32(dir, off + 0x48, right);
            PutI32(dir, off + 0x4C, child);
            if (clsid != null) Buffer.BlockCopy(clsid, 0, dir, off + 0x50, 16);
            PutU32(dir, off + 0x74, (uint)start);
            PutU32(dir, off + 0x78, (uint)size);
            PutU32(dir, off + 0x7C, 0);
        }

        static void WriteHeader(byte[] file, int numFat, int dirSector, int miniFatStart, int miniFatSectors, bool hasMiniStream)
        {
            byte[] sig = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
            Buffer.BlockCopy(sig, 0, file, 0, 8);
            PutU16(file, 0x18, 0x003E);   // minor
            PutU16(file, 0x1A, 3);        // major
            PutU16(file, 0x1C, 0xFFFE);   // byte order
            PutU16(file, 0x1E, 9);        // sector shift
            PutU16(file, 0x20, 6);        // mini sector shift
            PutU32(file, 0x28, 0);        // number of directory sectors (v3)
            PutU32(file, 0x2C, (uint)numFat);
            PutU32(file, 0x30, (uint)dirSector);
            PutU32(file, 0x34, 0);
            PutU32(file, 0x38, CUTOFF);
            PutU32(file, 0x3C, hasMiniStream ? (uint)miniFatStart : END);
            PutU32(file, 0x40, (uint)miniFatSectors);
            PutU32(file, 0x44, END);
            PutU32(file, 0x48, 0);
            for (int i = 0; i < 109; i++) PutU32(file, 0x4C + 4 * i, i < numFat ? (uint)i : FREE);
        }
    }
}
