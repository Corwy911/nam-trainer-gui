// Self-extracting installer stub for NAM Trainer.
//
// The installer file is:  [this program][file data ...][index (text)][32-byte trailer]
// Built by build.py (packer). At run time the program
//   1. reads the index from the end of its own file,
//   2. extracts every file into <folder of the exe>\_setup\stage, checking each SHA-256,
//   3. runs stage\setup\Installa.ps1 (Python venv, wheels, program files), then removes _setup.
// Nothing is written outside the folder the exe sits in (except by Installa.ps1 for the
// optional VC++ runtime, which asks for administrator rights only if it is missing).
//
// Targets C# 5 / .NET Framework 4.x (what csc.exe ships with Windows): no newer syntax.
//
// Language: the first question is "Language / Lingua" (English or Italian; the Windows display language is the default).
//           /LANG=en|it (or NAM_INSTALL_LANG) skips it. The choice is passed on to Installa.ps1 (-Lang) and stored for the
//           program's own scripts (nam-web\hardware.json, "language").
//
// Command line:  /S  silent (no questions, no final pause)     /verifica  only check the file's integrity
//                /D=<folder>  install there instead of next to the exe (tests)   /soloestrai  extract only
//                /VARIANTE=nvidia|amd|cpu  skip the hardware detection     /rileva  only show what would be installed
// Test hook: NAM_FAKE_ADAPTERS="10DE|NVIDIA GeForce RTX 4070;1002|AMD Radeon RX 7900 XTX" replaces the WMI query.
//
// The data file: "<exe name>.dat" next to the exe, or - when it was downloaded from GitHub, where a release file cannot
// exceed 2 GiB - its parts "<exe name>.dat.001", ".002", ... in the same folder (read as if they were one file).
//
// The package holds one PyTorch build per hardware variant (setup/wheelhouse-nvidia, -amd, -cpu). Only the one that
// matches this PC is copied (AMD also copies -cpu: it is the fallback if the Radeon turns out not to be usable).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace NamInstaller
{
    static class Program
    {
        const string Magic = "NAMINST1";
        const int TrailerSize = 32;
        const int BufferSize = 4 * 1024 * 1024;

        sealed class Entry
        {
            public string Path;
            public long Size;
            public long Offset;
            public string Hash;
        }

        static bool silent;
        static bool interactiveOut;
        static readonly string[] Variants = new string[] { "nvidia", "amd", "cpu" };
        static string lang = "en";   // "it" | "en": language of everything this program (and Installa.ps1) prints

        // The two texts of every message, Italian first. Usage: L("testo italiano", "english text").
        static string L(string it, string en)
        {
            return lang == "it" ? it : en;
        }

        // Language of the PC's user interface (Windows display language): Italian PCs default to Italian, the rest to English.
        static string SystemLanguage()
        {
            try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() == "it" ? "it" : "en"; }
            catch (Exception) { return "en"; }
        }

        static void Say(string message)
        {
            Console.WriteLine(message);
        }

        static string Size(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            return (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB";
        }

        static bool Confirm(string question, bool defaultYes)
        {
            if (silent) return true;
            string hint = lang == "it" ? (defaultYes ? " [S/n] " : " [s/N] ") : (defaultYes ? " [Y/n] " : " [y/N] ");
            Console.Write(question + hint);
            string answer;
            if (Console.IsInputRedirected) answer = (Console.ReadLine() ?? "").Trim();
            else
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                Console.WriteLine(key.KeyChar);
                answer = key.Key == ConsoleKey.Enter ? "" : key.KeyChar.ToString();
            }
            if (answer.Length == 0) return defaultYes;
            answer = answer.ToLowerInvariant();
            return answer == "s" || answer == "si" || answer == "y" || answer == "yes";
        }

        static void Pause()
        {
            if (silent || Console.IsInputRedirected) return;
            Say("");
            Console.Write(L("Premi un tasto per chiudere...", "Press any key to close..."));
            Console.ReadKey(true);
        }

        static int Fail(string message)
        {
            Say("");
            Console.ForegroundColor = ConsoleColor.Red;
            Say(L("ERRORE: ", "ERROR: ") + message);
            Console.ResetColor();
            Pause();
            return 1;
        }

        // Several files read as one (the parts of "Installa NAM.dat.001", ".002", ...). Read-only, seekable.
        sealed class PartsStream : Stream
        {
            readonly string[] paths;
            readonly long[] starts;
            readonly long length;
            FileStream current;
            int currentIndex = -1;
            long position;

            public PartsStream(string[] paths)
            {
                this.paths = paths;
                starts = new long[paths.Length];
                long sum = 0;
                for (int i = 0; i < paths.Length; i++) { starts[i] = sum; sum += new FileInfo(paths[i]).Length; }
                length = sum;
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return length; } }
            public override long Position { get { return position; } set { position = value; } }
            public override void Flush() { }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (origin == SeekOrigin.Begin) position = offset;
                else if (origin == SeekOrigin.Current) position += offset;
                else position = length + offset;
                return position;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (position >= length || count <= 0) return 0;
                int index = Array.BinarySearch(starts, position);
                if (index < 0) index = ~index - 1;
                if (index != currentIndex)
                {
                    if (current != null) current.Dispose();
                    current = new FileStream(paths[index], FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                    currentIndex = index;
                }
                current.Seek(position - starts[index], SeekOrigin.Begin);
                int n = current.Read(buffer, offset, (int)Math.Min(count, current.Length - (position - starts[index])));
                position += n;
                return n;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && current != null) { current.Dispose(); current = null; }
                base.Dispose(disposing);
            }
        }

        // Where the package is: one .dat file, its numbered parts, or (old style) appended to the exe itself.
        static string[] FindData(string self)
        {
            string dir = Path.GetDirectoryName(self);
            string stem = Path.GetFileNameWithoutExtension(self);
            string single = Path.Combine(dir, stem + ".dat");
            if (File.Exists(single)) return new string[] { single };
            List<string> parts = new List<string>();
            for (int i = 1; i < 1000; i++)
            {
                string part = Path.Combine(dir, stem + ".dat." + i.ToString("000"));
                if (!File.Exists(part)) break;
                parts.Add(part);
            }
            return parts.Count > 0 ? parts.ToArray() : new string[] { self };
        }

        static Stream OpenData(string[] paths)
        {
            if (paths.Length == 1) return new FileStream(paths[0], FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
            return new PartsStream(paths);
        }

        static void ReadIndex(Stream file, out List<Entry> files, out List<string> dirs, out long needBytes, out Dictionary<string, long> needByVariant)
        {
            files = new List<Entry>();
            dirs = new List<string>();
            needBytes = 0;
            needByVariant = new Dictionary<string, long>();
            if (file.Length < TrailerSize) throw new InvalidDataException(L("file troppo corto", "file too short"));
            byte[] trailer = new byte[TrailerSize];
            file.Seek(-TrailerSize, SeekOrigin.End);
            ReadExactly(file, trailer, TrailerSize);
            if (Encoding.ASCII.GetString(trailer, 0, 8) != Magic)
                throw new InvalidDataException(L("il file non contiene un pacchetto di installazione (copia incompleta?)", "the file does not contain an installation package (incomplete copy?)"));
            long indexOffset = BitConverter.ToInt64(trailer, 8);
            long indexLength = BitConverter.ToInt64(trailer, 16);
            if (indexOffset < 0 || indexLength <= 0 || indexOffset + indexLength > file.Length - TrailerSize)
                throw new InvalidDataException(L("indice non valido (copia incompleta?)", "invalid index (incomplete copy?)"));
            byte[] raw = new byte[indexLength];
            file.Seek(indexOffset, SeekOrigin.Begin);
            ReadExactly(file, raw, (int)indexLength);
            string[] lines = Encoding.UTF8.GetString(raw).Split('\n');
            foreach (string lineRaw in lines)
            {
                string line = lineRaw.TrimEnd('\r');
                if (line.Length == 0) continue;
                string[] p = line.Split('\t');
                if (p[0] == "F" && p.Length == 5)
                {
                    Entry e = new Entry();
                    e.Size = long.Parse(p[1], CultureInfo.InvariantCulture);
                    e.Offset = long.Parse(p[2], CultureInfo.InvariantCulture);
                    e.Hash = p[3];
                    e.Path = p[4];
                    if (e.Offset < 0 || e.Size < 0 || e.Offset + e.Size > indexOffset) throw new InvalidDataException(L("voce dell'indice fuori dal file", "index entry outside the file"));
                    files.Add(e);
                }
                else if (p[0] == "D" && p.Length == 2) dirs.Add(p[1]);
                else if (p[0] == "N" && p.Length == 2) needBytes = long.Parse(p[1], CultureInfo.InvariantCulture);
                else if (p[0] == "V" && p.Length == 3) needByVariant[p[1]] = long.Parse(p[2], CultureInfo.InvariantCulture);
            }
            if (files.Count == 0) throw new InvalidDataException(L("il pacchetto e vuoto", "the package is empty"));
        }

        static void ReadExactly(Stream s, byte[] buffer, int count)
        {
            int done = 0;
            while (done < count)
            {
                int n = s.Read(buffer, done, count - done);
                if (n <= 0) throw new EndOfStreamException();
                done += n;
            }
        }

        // Relative path from the index -> full path under root; refuses anything that could escape root.
        static string SafePath(string root, string rel)
        {
            if (rel.Length == 0 || rel.StartsWith("/") || rel.Contains(":") || rel.Contains("\\"))
                throw new InvalidDataException(L("percorso non valido nel pacchetto: ", "invalid path in the package: ") + rel);
            foreach (string part in rel.Split('/'))
                if (part == ".." || part.Length == 0) throw new InvalidDataException(L("percorso non valido nel pacchetto: ", "invalid path in the package: ") + rel);
            string full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', '\\')));
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L("percorso fuori dalla cartella di destinazione: ", "path outside the destination folder: ") + rel);
            return full;
        }

        sealed class Progress
        {
            readonly long total;
            long done;
            int lastPercent = -1;
            DateTime lastPrint = DateTime.MinValue;
            readonly string label;

            public Progress(string label, long total) { this.label = label; this.total = total; }

            public void Add(long n, string current)
            {
                done += n;
                int percent = total > 0 ? (int)(done * 100 / total) : 100;
                DateTime now = DateTime.UtcNow;
                if (percent == lastPercent && (now - lastPrint).TotalSeconds < 2) return;
                if (!interactiveOut && percent != lastPercent && percent % 10 != 0 && percent != 100) return;
                lastPercent = percent;
                lastPrint = now;
                string text = string.Format("{0}: {1,3}%  ({2} / {3})", label, percent, Size(done), Size(total));
                if (interactiveOut)
                {
                    string extra = current.Length > 50 ? "..." + current.Substring(current.Length - 47) : current;
                    Console.Write("\r" + (text + "  " + extra).PadRight(Math.Max(20, Console.WindowWidth - 1)).Substring(0, Math.Max(20, Console.WindowWidth - 1)));
                }
                else Console.WriteLine(text);
            }

            public void Finish() { if (interactiveOut) Console.WriteLine(); }
        }

        // Copies every file out of the installer (or, with root == null, just verifies them).
        static void ExtractOrVerify(string[] dataPaths, List<Entry> files, string root)
        {
            long total = 0;
            foreach (Entry e in files) total += e.Size;
            Progress progress = new Progress(root == null ? L("Verifica", "Verifying") : L("Estrazione", "Extracting"), total);
            byte[] buffer = new byte[BufferSize];
            using (Stream src = OpenData(dataPaths))
            {
                foreach (Entry e in files)
                {
                    string dest = root == null ? null : SafePath(root, e.Path);
                    FileStream output = null;
                    if (dest != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);
                    }
                    try
                    {
                        using (SHA256 sha = SHA256.Create())
                        {
                            src.Seek(e.Offset, SeekOrigin.Begin);
                            long left = e.Size;
                            while (left > 0)
                            {
                                int want = (int)Math.Min(buffer.Length, left);
                                int n = src.Read(buffer, 0, want);
                                if (n <= 0) throw new EndOfStreamException(L("il file di installazione e incompleto", "the installation file is incomplete"));
                                sha.TransformBlock(buffer, 0, n, null, 0);
                                if (output != null) output.Write(buffer, 0, n);
                                left -= n;
                                progress.Add(n, e.Path);
                            }
                            sha.TransformFinalBlock(buffer, 0, 0);
                            string hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                            if (hash != e.Hash) throw new InvalidDataException(L("file danneggiato (SHA-256 diverso): ", "damaged file (different SHA-256): ") + e.Path);
                        }
                    }
                    finally { if (output != null) output.Dispose(); }
                }
            }
            progress.Finish();
        }

        // ------------------------------------------------------------------ hardware

        sealed class Adapter
        {
            public string Name;
            public string Vendor;   // hex vendor id from the PNP id: 10DE NVIDIA, 1002 AMD, 8086 Intel, ...
        }

        static List<Adapter> DetectAdapters()
        {
            List<Adapter> list = new List<Adapter>();
            string fake = Environment.GetEnvironmentVariable("NAM_FAKE_ADAPTERS");
            if (fake != null)
            {
                foreach (string item in fake.Split(';'))
                {
                    string[] p = item.Split(new char[] { '|' }, 2);
                    if (p.Length == 2) { Adapter a = new Adapter(); a.Vendor = p[0].Trim().ToUpperInvariant(); a.Name = p[1].Trim(); list.Add(a); }
                }
                return list;
            }
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject o in results)
                    {
                        string pnp = Convert.ToString(o["PNPDeviceID"]) ?? "";
                        Match m = Regex.Match(pnp, "VEN_([0-9A-Fa-f]{4})");
                        Adapter a = new Adapter();
                        a.Name = (Convert.ToString(o["Name"]) ?? "").Trim();
                        a.Vendor = m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
                        list.Add(a);
                    }
                }
            }
            catch (Exception) { /* WMI unavailable: no adapters -> CPU */ }
            return list;
        }

        static string ProcessorName()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                using (ManagementObjectCollection results = searcher.Get())
                    foreach (ManagementBaseObject o in results) return Regex.Replace((Convert.ToString(o["Name"]) ?? "").Trim(), "\\s+", " ");
            }
            catch (Exception) { }
            return "";
        }

        // Radeons that PyTorch ROCm on Windows officially supports (AMD's compatibility list: RX 9000, RX 7700/7800/7900,
        // Radeon PRO W7700/W7800/W7900, Radeon AI PRO, and the Ryzen AI APUs 8050S/8060S/890M/880M).
        static bool AmdOfficiallySupported(string name)
        {
            return Regex.IsMatch(name, @"RX\s*(9\d{3}|7[789]\d{2})\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(name, @"PRO\s*(W7[789]\d{2}|R9\d{3})\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(name, @"\b(8050S|8060S|890M|880M)\b", RegexOptions.IgnoreCase);
        }

        // Discrete Radeons (RX / PRO series) and the big Ryzen AI iGPUs are worth trying with ROCm; generic "Radeon Graphics"
        // (Vega / older iGPU) would just download ~2 GB to end up on the CPU anyway.
        static bool AmdCandidate(string name)
        {
            return Regex.IsMatch(name, @"Radeon.*\b(RX|PRO|AI PRO)\b", RegexOptions.IgnoreCase)
                || AmdOfficiallySupported(name);
        }

        // NVIDIA wins over AMD (a laptop with an AMD iGPU and an NVIDIA GPU trains on the NVIDIA one).
        static string ChooseVariant(List<Adapter> adapters, out string gpuName, out string reason)
        {
            gpuName = ""; reason = "";
            foreach (Adapter a in adapters)
                if (a.Vendor == "10DE") { gpuName = a.Name; reason = L("scheda NVIDIA rilevata", "NVIDIA card detected"); return "nvidia"; }
            string amdOther = "";
            foreach (Adapter a in adapters)
            {
                if (a.Vendor != "1002") continue;
                if (AmdCandidate(a.Name))
                {
                    if (Environment.OSVersion.Version.Build < 22000)
                    {
                        amdOther = a.Name + L(" (l'accelerazione AMD richiede Windows 11)", " (AMD acceleration needs Windows 11)");
                        continue;
                    }
                    gpuName = a.Name;
                    reason = AmdOfficiallySupported(a.Name)
                        ? L("scheda AMD supportata dall'accelerazione ROCm", "AMD card supported by ROCm acceleration")
                        : L("scheda AMD non nell'elenco ufficiale ROCm: provo comunque; se non e utilizzabile si ripiega sulla CPU",
                            "AMD card not on the official ROCm list: trying anyway; if it is not usable the CPU version is used");
                    return "amd";
                }
                amdOther = a.Name;
            }
            reason = amdOther.Length > 0
                ? L("scheda AMD non adatta all'accelerazione ROCm (", "AMD card not suited to ROCm acceleration (") + amdOther + L("): uso la CPU", "): using the CPU")
                : L("nessuna scheda NVIDIA o AMD adatta: uso la CPU", "no suitable NVIDIA or AMD card: using the CPU");
            return "cpu";
        }

        static string VariantLabel(string variant)
        {
            if (variant == "nvidia") return "NVIDIA (CUDA)";
            if (variant == "amd") return "AMD (ROCm)";
            return L("solo CPU", "CPU only");
        }

        // Which variant a package file belongs to ("" = common): setup/wheelhouse-<variant>/... and setup/requirements-<variant>.lock
        static string FileVariant(string rel)
        {
            foreach (string v in Variants)
                if (rel.StartsWith("setup/wheelhouse-" + v + "/", StringComparison.Ordinal) || rel == "setup/requirements-" + v + ".lock") return v;
            return "";
        }

        static bool Wanted(string rel, string variant)
        {
            string v = FileVariant(rel);
            return v.Length == 0 || v == variant || (variant == "amd" && v == "cpu");
        }

        static List<Entry> SelectFiles(List<Entry> files, string variant)
        {
            List<Entry> chosen = new List<Entry>();
            foreach (Entry e in files) if (Wanted(e.Path, variant)) chosen.Add(e);
            return chosen;
        }

        static long TotalSize(List<Entry> files)
        {
            long total = 0;
            foreach (Entry e in files) total += e.Size;
            return total;
        }

        // Free space the install needs for this variant (the packer stores the estimate); a rough guess if it is missing.
        static long NeedFor(string variant, Dictionary<string, long> needByVariant, long needBytes, long chosenTotal)
        {
            long need;
            if (needByVariant.TryGetValue(variant, out need) && need > 0) return need;
            return needBytes > 0 ? needBytes : chosenTotal * 3;
        }
        static void DeleteDir(string path)
        {
            for (int attempt = 0; attempt < 6 && Directory.Exists(path); attempt++)
            {
                try { Directory.Delete(path, true); }
                catch (Exception) { Thread.Sleep(1000); }
            }
        }

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch (Exception) { }
            
            interactiveOut = !Console.IsOutputRedirected;
            string self = Process.GetCurrentProcess().MainModule.FileName;
            string target = Path.GetDirectoryName(self);
            bool verifyOnly = false, extractOnly = false, detectOnly = false;
            string forcedVariant = "";
            silent = Environment.GetEnvironmentVariable("NAM_INSTALL_SILENT") == "1";
            string chosenLang = (Environment.GetEnvironmentVariable("NAM_INSTALL_LANG") ?? "").Trim().ToLowerInvariant();
            foreach (string a in args)
            {
                if (a.Equals("/S", StringComparison.OrdinalIgnoreCase)) silent = true;
                else if (a.Equals("/verifica", StringComparison.OrdinalIgnoreCase)) verifyOnly = true;
                else if (a.Equals("/soloestrai", StringComparison.OrdinalIgnoreCase)) extractOnly = true;
                else if (a.Equals("/rileva", StringComparison.OrdinalIgnoreCase)) detectOnly = true;
                else if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)) target = a.Substring(3).Trim('"');
                else if (a.StartsWith("/LANG=", StringComparison.OrdinalIgnoreCase)) chosenLang = a.Substring(6).Trim('"').ToLowerInvariant();
                else if (a.StartsWith("/VARIANTE=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("/VARIANT=", StringComparison.OrdinalIgnoreCase))
                    forcedVariant = a.Substring(a.IndexOf('=') + 1).Trim('"').ToLowerInvariant();
            }

            // ---- language: first thing, so that everything after is in it
            lang = SystemLanguage();
            if (chosenLang == "en" || chosenLang == "it") lang = chosenLang;
            else if (!silent && !detectOnly && !verifyOnly)
            {
                Console.Write("Language / Lingua:  1 = English   2 = Italiano   [" + (lang == "it" ? "2" : "1") + "] ");
                string answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                if (answer == "1" || answer == "en" || answer == "e") lang = "en";
                else if (answer == "2" || answer == "it" || answer == "i") lang = "it";
                Say("");
            }
            try { Console.Title = L("Installazione NAM Trainer", "NAM Trainer setup"); } catch (Exception) { }
            if (forcedVariant.Length > 0 && Array.IndexOf(Variants, forcedVariant) < 0)
                return Fail(L("variante sconosciuta: ", "unknown variant: ") + forcedVariant + L(" (usa nvidia, amd o cpu)", " (use nvidia, amd or cpu)"));

            Say("=====================================================");
            Say(L("  NAM Trainer - installazione", "  NAM Trainer - setup"));
            Say("=====================================================");
            Say("");

            // The data normally sits in "<this exe's name>.dat" next to the exe (Windows cannot run an .exe larger than
            // 4 GiB, and the package with all hardware variants is bigger). A package appended to the exe still works.
            string[] data = FindData(self);
            string sibling = Path.Combine(Path.GetDirectoryName(self), Path.GetFileNameWithoutExtension(self) + ".dat");

            List<Entry> files;
            List<string> dirs;
            long needBytes;
            Dictionary<string, long> needByVariant;
            try
            {
                using (Stream f = OpenData(data))
                    ReadIndex(f, out files, out dirs, out needBytes, out needByVariant);
            }
            catch (Exception e)
            {
                return Fail(L("pacchetto non leggibile: ", "cannot read the package: ") + e.Message + (data.Length == 1 && data[0] == self
                    ? L("\nManca il file dati \"", "\nThe data file \"") + Path.GetFileName(sibling)
                        + L("\" (o le sue parti .001, .002, ...): copialo nella stessa cartella di questo programma e rilancia.", "\" (or its parts .001, .002, ...) is missing: copy it into the same folder as this program and run it again.")
                    : (data.Length > 1 || data[0].EndsWith(".001", StringComparison.OrdinalIgnoreCase))
                        ? L("\nControlla di aver scaricato TUTTE le parti (", "\nMake sure you downloaded ALL the parts (") + data.Length + L(" trovate, da .001 in poi senza buchi).", " found, from .001 on with no gaps).")
                        : ""));
            }

            long total = 0;
            foreach (Entry e in files) total += e.Size;

            if (verifyOnly)
            {
                Say(L("Verifico l'integrita del file di installazione (", "Checking the integrity of the installation file (") + files.Count + L(" file, ", " files, ") + Size(total) + ")...");
                try { ExtractOrVerify(data, files, null); }
                catch (Exception e) { return Fail(e.Message); }
                Say(L("Tutto in ordine: il file di installazione e integro.", "All good: the installation file is intact."));
                Pause();
                return 0;
            }

            // ---- hardware: which PyTorch build goes on this PC
            List<Adapter> adapters = DetectAdapters();
            string gpuName, reason;
            string variant = ChooseVariant(adapters, out gpuName, out reason);
            if (forcedVariant.Length > 0)
            {
                variant = forcedVariant;
                reason = L("scelta forzata da riga di comando", "chosen on the command line");
                foreach (Adapter a in adapters)
                {
                    if ((variant == "nvidia" && a.Vendor == "10DE") || (variant == "amd" && a.Vendor == "1002")) { gpuName = a.Name; break; }
                }
            }
            Say(L("Hardware rilevato:", "Detected hardware:"));
            string cpu = ProcessorName();
            if (cpu.Length > 0) Say(L("    Processore:     ", "    Processor:      ") + cpu);
            if (adapters.Count == 0) Say(L("    Scheda video:   (nessuna rilevata)", "    Graphics card:  (none detected)"));
            foreach (Adapter a in adapters) Say(L("    Scheda video:   ", "    Graphics card:  ") + (a.Name.Length > 0 ? a.Name : L("(sconosciuta)", "(unknown)")));
            Say(L("    Versione da installare:  ", "    Version to install:  ") + VariantLabel(variant) + "  (" + reason + ")");
            Say("");
            if (detectOnly) { Say("VARIANTE=" + variant); return 0; }

            target = Path.GetFullPath(target).TrimEnd('\\');
            if (target.Length <= 2) return Fail(L("scegli una cartella, non la radice del disco (es. C:\\NAM).", "choose a folder, not the root of a drive (e.g. C:\\NAM)."));
            Say(L("Cartella di installazione: ", "Installation folder:      ") + target);
            List<Entry> chosen = SelectFiles(files, variant);
            long chosenTotal = TotalSize(chosen);
            Say(L("Contenuto del pacchetto:    ", "Package contents:          ") + files.Count + L(" file, ", " files, ") + Size(total) + L(" (comprese le versioni per altri tipi di PC)", " (including the versions for other kinds of PC)"));
            Say(L("Da copiare per questo PC:   ", "To copy for this PC:      ") + chosen.Count + L(" file, ", " files, ") + Size(chosenTotal));
            Say(L("Spazio libero necessario:   circa ", "Free space needed:        about ") + Size(NeedFor(variant, needByVariant, needBytes, chosenTotal)));
            Say("");

            // Questions that must come BEFORE gigabytes are copied. Installa.ps1 gets -Confirmed and does not repeat them.
            bool existing = File.Exists(Path.Combine(target, "nam-web", "app.py")) || File.Exists(Path.Combine(target, "venv", "Scripts", "python.exe"));
            List<string> others = new List<string>();
            if (!existing && Directory.Exists(target))
            {
                foreach (string entry in Directory.GetFileSystemEntries(target))
                {
                    string name = Path.GetFileName(entry);
                    if (name.Equals("_setup", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith(Path.GetFileNameWithoutExtension(self), StringComparison.OrdinalIgnoreCase)) continue;   // the installer's own files (.exe, .dat, .sha256.txt)
                    others.Add(name);
                }
            }
            bool confirmed;
            if (existing)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Say(L("Trovata un'installazione esistente in questa cartella: verranno aggiornati il programma e l'ambiente Python.", "An existing installation was found in this folder: the program and the Python environment will be updated."));
                Console.ResetColor();
                Say(L("Restano INTATTI: NAM Generati, File WAV caricati, Backup, i trainer (anche se aggiornati dalla pagina web) e le impostazioni della pagina.", "These stay UNTOUCHED: NAM Generati (models), File WAV caricati (WAV files), Backup, the trainers (even if updated from the web page) and the page settings."));
                Say("");
                confirmed = Confirm(L("Aggiornare l'installazione esistente?", "Update the existing installation?"), true);
            }
            else if (others.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Say(L("ATTENZIONE: la cartella non e vuota. Contiene:", "WARNING: the folder is not empty. It contains:"));
                Console.ResetColor();
                for (int i = 0; i < others.Count && i < 8; i++) Say("    " + others[i]);
                if (others.Count > 8) Say(L("    ... e altri ", "    ... and ") + (others.Count - 8) + L("", " more"));
                Say(L("I file esistenti non vengono toccati, ma di solito conviene una cartella vuota.", "Existing files are not touched, but an empty folder is usually better."));
                Say("");
                confirmed = Confirm(L("Installare NAM Trainer qui comunque?", "Install NAM Trainer here anyway?"), false);
            }
            else confirmed = Confirm(L("Installare NAM Trainer in questa cartella?", "Install NAM Trainer in this folder?"), true);
            if (!confirmed) { Say(L("Annullato.", "Cancelled.")); Pause(); return 2; }

            // The detection can be wrong (e.g. a PC with two graphics cards): let the user pick another build.
            if (!silent && forcedVariant.Length == 0)
            {
                Console.Write(L("Versione ", "Version ") + VariantLabel(variant) + L(": Invio per confermare, oppure 1 = NVIDIA, 2 = AMD, 3 = solo CPU: ", ": press Enter to confirm, or 1 = NVIDIA, 2 = AMD, 3 = CPU only: "));
                string pick = (Console.ReadLine() ?? "").Trim();
                string picked = pick == "1" ? "nvidia" : pick == "2" ? "amd" : pick == "3" ? "cpu" : variant;
                if (picked != variant)
                {
                    variant = picked;
                    gpuName = "";
                    foreach (Adapter a in adapters)
                        if ((variant == "nvidia" && a.Vendor == "10DE") || (variant == "amd" && a.Vendor == "1002")) { gpuName = a.Name; break; }
                    chosen = SelectFiles(files, variant);
                    chosenTotal = TotalSize(chosen);
                    Say(L("Scelta: ", "Chosen: ") + VariantLabel(variant) + " - " + chosen.Count + L(" file, ", " files, ") + Size(chosenTotal) + L("; spazio libero necessario circa ", "; free space needed about ") + Size(NeedFor(variant, needByVariant, needBytes, chosenTotal)));
                }
            }
            if (variant == "amd" && target.Length > 76)
                return Fail(L("il percorso \"", "the path \"") + target + L("\" e troppo lungo per l'accelerazione AMD (", "\" is too long for AMD acceleration (") + target.Length
                    + L(" caratteri, massimo 76): i file di ROCm hanno nomi lunghissimi e Windows limita i percorsi a 260 caratteri.\nScegli una cartella piu corta (es. C:\\NAM), oppure lancia con /VARIANTE=cpu.",
                        " characters, 76 at most): ROCm files have very long names and Windows limits paths to 260 characters.\nChoose a shorter folder (e.g. C:\\NAM), or run with /VARIANT=cpu."));

            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(target));
                long need = NeedFor(variant, needByVariant, needBytes, chosenTotal);
                if (drive.AvailableFreeSpace < need)
                    return Fail(string.Format(L("spazio insufficiente su {0}: liberi {1}, servono circa {2}.", "not enough space on {0}: {1} free, about {2} needed."), drive.Name, Size(drive.AvailableFreeSpace), Size(need)));
            }
            catch (Exception) { /* network path etc.: let the extraction fail by itself if needed */ }

            string setupDir = Path.Combine(target, "_setup");
            string stage = Path.Combine(setupDir, "stage");
            try
            {
                Directory.CreateDirectory(target);
                DeleteDir(setupDir);
                Directory.CreateDirectory(stage);
                foreach (string d in dirs) if (Wanted(d + "/", variant)) Directory.CreateDirectory(SafePath(stage, d));
                Say("");
                Say(L("Copio e verifico i file (puo richiedere qualche minuto)...", "Copying and checking the files (this can take a few minutes)..."));
                ExtractOrVerify(data, chosen, stage);
            }
            catch (Exception e)
            {
                return Fail(e.Message + L("\nLa cartella _setup e stata lasciata in ", "\nThe _setup folder was left in ") + target + L(": puoi eliminarla e rilanciare l'installazione.", ": you can delete it and run the installer again."));
            }
            if (extractOnly) { Say(L("Estrazione completata in ", "Extraction completed in ") + stage); return 0; }

            string script = Path.Combine(stage, "setup", "Installa.ps1");
            if (!File.Exists(script)) return Fail(L("manca lo script di installazione nel pacchetto.", "the installation script is missing from the package."));
            Say("");
            Say(L("File estratti e verificati. Preparo l'ambiente...", "Files extracted and checked. Setting up the environment..."));
            Say("");
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            psi.Arguments = string.Format("-NoProfile -ExecutionPolicy Bypass -File \"{0}\" -Target \"{1}\" -Variant {2} -GpuName \"{3}\" -Lang {4} -Confirmed{5}",
                script, target, variant, gpuName.Replace("\"", ""), lang, silent ? " -Silent" : "");
            psi.UseShellExecute = false;
            psi.WorkingDirectory = target;
            int code;
            try
            {
                using (Process p = Process.Start(psi)) { p.WaitForExit(); code = p.ExitCode; }
            }
            catch (Exception e) { return Fail(L("impossibile avviare PowerShell: ", "cannot start PowerShell: ") + e.Message); }

            if (code != 0)
            {
                Say("");
                Console.ForegroundColor = ConsoleColor.Red;
                Say(L("L'installazione non e andata a buon fine (codice ", "The installation did not succeed (code ") + code + ").");
                Console.ResetColor();
                Say(L("Il registro e in: ", "The log is in: ") + Path.Combine(setupDir, "install.log"));
                Say(L("Dopo aver risolto, rilancia questo programma (i file gia estratti verranno rifatti).", "After fixing the problem, run this program again (the files already extracted will be redone)."));
                Pause();
                return code;
            }
            DeleteDir(setupDir);
            Pause();
            return 0;
        }
    }
}
