using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace B4JScanner
{
    static class B4JProjectParser
    {
        static readonly Regex _libraryRe       = new Regex(@"^Library\d+=(.+)$",      RegexOptions.IgnoreCase);
        static readonly Regex _moduleRe        = new Regex(@"^Module\d+=(.+?)(\||$)", RegexOptions.IgnoreCase);
        static readonly Regex _build1Re        = new Regex(@"^Build1=\S+?,(.+)$",     RegexOptions.IgnoreCase);
        static readonly Regex _versionRe       = new Regex(@"^Version=(.+)$",         RegexOptions.IgnoreCase);
        static readonly Regex _additionalJarRe = new Regex(@"^\s*#AdditionalJar:\s*(.+?)\s*$", RegexOptions.IgnoreCase);

        public static B4JProject Parse(string path, string libsPath, string addLibsPath)
        {
            string projectFile;
            string projectFolder;

            if (Directory.Exists(path))
            {
                projectFolder = path;
                var b4jFiles = Directory.GetFiles(path, "*.b4j");
                if (b4jFiles.Length == 0)
                    throw new Exception("No .b4j file found in: " + path);
                projectFile = b4jFiles[0];
            }
            else if (File.Exists(path))
            {
                projectFile = path;
                projectFolder = Path.GetDirectoryName(path);
            }
            else
            {
                throw new Exception("Path not found: " + path);
            }

            var project = new B4JProject
            {
                ProjectFile   = projectFile,
                ProjectFolder = projectFolder,
                Name          = Path.GetFileNameWithoutExtension(projectFile)
            };

            // Tracks all seen library names to prevent duplicates from b4xlib expansion
            var seenLibs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenJars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool inCode = false;
            foreach (var line in File.ReadLines(projectFile))
            {
                if (!inCode)
                {
                    if (line == "@EndOfDesignText@") { inCode = true; continue; }

                    Match m;
                    m = _libraryRe.Match(line);
                    if (m.Success)
                    {
                        ExpandLibrary(m.Groups[1].Value.Trim(), libsPath, addLibsPath,
                            project.Libraries, seenLibs);
                        continue;
                    }

                    m = _moduleRe.Match(line);
                    if (m.Success) { project.Modules.Add(m.Groups[1].Value.Trim()); continue; }

                    m = _build1Re.Match(line);
                    if (m.Success) { project.JavaPackage = m.Groups[1].Value.Trim(); continue; }

                    m = _versionRe.Match(line);
                    if (m.Success) { project.Version = m.Groups[1].Value.Trim(); continue; }
                }
                else
                {
                    CollectAdditionalJar(line, project, seenJars);
                }
            }

            // Scan all .bas module files in the project folder
            try
            {
                foreach (var basFile in Directory.GetFiles(projectFolder, "*.bas", SearchOption.AllDirectories))
                {
                    foreach (var line in File.ReadLines(basFile))
                        CollectAdditionalJar(line, project, seenJars);
                }
            }
            catch { }

            return project;
        }

        // Adds libName to the library list and, if it is a b4xlib, recursively
        // expands its DependsOn entries. seenLibs prevents duplicates and cycles.
        static void ExpandLibrary(string libName, string libsPath, string addLibsPath,
            List<string> libraries, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(libName) || !seen.Add(libName)) return;

            libraries.Add(libName);

            string b4xlibPath = LibraryResolver.FindFile(libName + ".b4xlib", libsPath)
                             ?? LibraryResolver.FindFile(libName + ".b4xlib", addLibsPath)
                             ?? LibraryResolver.FindFile(libName + ".b4xlib", Path.Combine(addLibsPath, "b4j"))
                             ?? LibraryResolver.FindFile(libName + ".b4xlib", Path.Combine(addLibsPath, "b4x"));

            if (b4xlibPath == null) return;

            foreach (string dep in ReadB4XLibDeps(b4xlibPath))
                ExpandLibrary(dep, libsPath, addLibsPath, libraries, seen);
        }

        // Reads the DependsOn line from a b4xlib manifest.txt and returns dep names.
        static List<string> ReadB4XLibDeps(string b4xlibPath)
        {
            var deps = new List<string>();
            try
            {
                using (var zip = ZipFile.OpenRead(b4xlibPath))
                {
                    var entry = zip.GetEntry("manifest.txt");
                    if (entry == null) return deps;

                    using (var reader = new StreamReader(entry.Open()))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            bool isDependsOn = line.StartsWith("B4J.DependsOn=", StringComparison.OrdinalIgnoreCase) 
                                || line.StartsWith("DependsOn=", StringComparison.OrdinalIgnoreCase);
                            if (!isDependsOn)
                                continue;

                            string val = line.Substring(line.IndexOf('=') + 1).Trim();
                            foreach (string dep in val.Split(','))
                            {
                                string d = dep.Trim();
                                // Strip .b4xlib extension if present
                                if (d.EndsWith(".b4xlib", StringComparison.OrdinalIgnoreCase))
                                    d = d.Substring(0, d.Length - 7);
                                if (!string.IsNullOrEmpty(d))
                                    deps.Add(d);
                            }
                            break;
                        }
                    }
                }
            }
            catch { }
            return deps;
        }

        static void CollectAdditionalJar(string line, B4JProject project, HashSet<string> seen)
        {
            var m = _additionalJarRe.Match(line.Trim());
            if (!m.Success) return;
            string jar = m.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(jar) && seen.Add(jar))
                project.AdditionalJars.Add(jar);
        }
    }
}
