using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace B4JScanner
{
    static class LibraryResolver
    {
        public static ResolvedLibrary Resolve(string libraryName, string libsPath, string addLibsPath)
        {
            var result = new ResolvedLibrary { LibraryName = libraryName };

            // Try JAR + XML in each library directory (multiple strategies per dir)
            string jar = FindJar(libraryName, libsPath) 
                ?? FindJar(libraryName, addLibsPath) 
                ?? FindJar(libraryName, Path.Combine(addLibsPath, "b4j"));

            if (jar != null)
            {
                result.JarPath = jar;
                string xmlCandidate = Path.ChangeExtension(jar, ".xml");
                if (File.Exists(xmlCandidate))
                    result.XmlPath = xmlCandidate;
            }
            else
            {
                // Try B4XLib
                string b4xlib = FindFile(libraryName + ".b4xlib", libsPath)
                             ?? FindFile(libraryName + ".b4xlib", addLibsPath)
                             ?? FindFile(libraryName + ".b4xlib", Path.Combine(addLibsPath, "b4j"))
                             ?? FindFile(libraryName + ".b4xlib", Path.Combine(addLibsPath, "b4x"));
                result.B4xlibPath = b4xlib;
            }

            return result;
        }

        static string FindJar(string libraryName, string directory)
        {
            if (!Directory.Exists(directory)) return null;

            // 1. Exact match: Json.jar (case-insensitive)
            string exact = FindFile(libraryName + ".jar", directory);
            if (exact != null) return exact;

            // 2. Versioned subdirectory: jserver-11.0.21 -> Libraries\jserver\jserver-11.0.21.jar
            string baseName = StripVersion(libraryName);
            if (!string.IsNullOrEmpty(baseName) && baseName != libraryName)
            {
                string subDir = Path.Combine(directory, baseName);
                string inSub = FindFile(libraryName + ".jar", subDir);
                if (inSub != null) return inSub;

                // Also try exact base name in subdir
                inSub = FindFile(baseName + ".jar", subDir);
                if (inSub != null) return inSub;
            }

            // 3. Partial/prefix match: library name starts the JAR filename
            // e.g. hikaricp matches HikariCP-2.4.6.jar
            try
            {
                var jars = Directory.GetFiles(directory, "*.jar", SearchOption.TopDirectoryOnly);
                string match = jars.FirstOrDefault(j =>
                    Path.GetFileNameWithoutExtension(j)
                        .StartsWith(libraryName, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;

                // Also search one level of subdirectories (for jserver/ etc.)
                foreach (var sub in Directory.GetDirectories(directory))
                {
                    var subJars = Directory.GetFiles(sub, "*.jar", SearchOption.TopDirectoryOnly);
                    match = subJars.FirstOrDefault(j =>
                        Path.GetFileNameWithoutExtension(j)
                            .StartsWith(libraryName, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }
            catch { }

            return null;
        }

        public static string FindFile(string fileName, string directory)
        {
            if (!Directory.Exists(directory)) return null;
            try
            {
                return Directory.GetFiles(directory, fileName, SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        public static void ResolveDependencyJars(LibraryInfo info, string libsPath, string addLibsPath)
        {
            foreach (string depName in info.Dependencies)
            {
                string jarFile = depName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                    ? depName : depName + ".jar";

                string found = FindDepJar(jarFile, libsPath)
                            ?? FindDepJar(jarFile, addLibsPath)
                            ?? FindDepJar(jarFile, Path.Combine(addLibsPath, "b4j"));

                var dep = new ResolvedDependency { Name = depName, JarPath = found };
                if (found != null)
                    dep.Maven = JarAnalyzer.TryReadMavenCoords(found);
                info.ResolvedDeps.Add(dep);
            }
        }

        static string FindDepJar(string jarFile, string baseDir)
        {
            if (!Directory.Exists(baseDir)) return null;
            try
            {
                // Try the path as given (handles "jserver-11.0.21/jetty-server-11.0.21.jar")
                string full = Path.Combine(baseDir, jarFile);
                if (File.Exists(full)) return full;
                // Also try just the filename in the base dir
                string name = Path.GetFileName(jarFile);
                if (name != jarFile)
                {
                    string flat = Path.Combine(baseDir, name);
                    if (File.Exists(flat)) return flat;
                }
            }
            catch { }
            return null;
        }

        // Strip trailing version: "jserver-11.0.21" -> "jserver", "commons-codec-1.16" -> "commons-codec"
        static string StripVersion(string name)
        {
            int idx = name.LastIndexOf('-');
            if (idx < 1) return null;
            string suffix = name.Substring(idx + 1);
            if (suffix.Length > 0 && char.IsDigit(suffix[0]))
                return name.Substring(0, idx);
            return null;
        }
    }
}
