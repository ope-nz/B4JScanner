using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace B4JScanner
{
    static class JarAnalyzer
    {
        static readonly Regex _filenameVersionRe = new Regex(@"[-_](\d+[\.\d]*[\w\-]*)(?:\.jar)?$");

        public static bool MavenSearchEnabled { get; set; }

        public static LibraryInfo Analyze(ResolvedLibrary lib)
        {
            var info = new LibraryInfo { DisplayName = lib.LibraryName };

            // Extract Maven coords from the wrapper JAR (independent of version detection)
            if (lib.JarPath != null)
                info.Maven = TryReadMavenCoords(lib.JarPath);

            // 1. B4J XML descriptor
            if (lib.XmlPath != null)
            {
                TryReadXml(lib.XmlPath, info);
                if (!string.IsNullOrEmpty(info.Version))
                    return info;
            }

            // 1.5 B4XLib manifest.txt
            if (lib.B4xlibPath != null)
            {
                TryReadB4XLibManifest(lib.B4xlibPath, info);
                if (!string.IsNullOrEmpty(info.Version))
                    return info;
            }

            // 2. JAR MANIFEST.MF
            if (lib.JarPath != null)
            {
                TryReadManifest(lib.JarPath, info);
                if (!string.IsNullOrEmpty(info.Version))
                    return info;
            }

            // 3. Version from JAR filename
            if (lib.JarPath != null)
            {
                string name = Path.GetFileNameWithoutExtension(lib.JarPath);
                var m = _filenameVersionRe.Match(name);
                if (m.Success)
                {
                    info.Version = m.Groups[1].Value;
                    info.VersionSource = "filename";
                    return info;
                }
            }

            // 4. Version embedded in library name (e.g. jserver-11.0.21)
            {
                var m = _filenameVersionRe.Match(lib.LibraryName);
                if (m.Success)
                {
                    info.Version = m.Groups[1].Value;
                    info.VersionSource = "library-name";
                    return info;
                }
            }

            info.Version = "unknown";
            info.VersionSource = "none";
            return info;
        }

        static void TryReadB4XLibManifest(string b4xlibPath, LibraryInfo info)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(b4xlibPath))
                {
                    var entry = zip.GetEntry("manifest.txt");
                    if (entry == null) return;

                    using (var reader = new StreamReader(entry.Open()))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (!line.StartsWith("Version=", StringComparison.OrdinalIgnoreCase))
                                continue;
                            string val = line.Substring(line.IndexOf('=') + 1).Trim();
                            if (!string.IsNullOrEmpty(val))
                            {
                                info.Version       = val;
                                info.VersionSource = "b4xlib-manifest";
                            }
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        static void TryReadXml(string xmlPath, LibraryInfo info)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(xmlPath);
                var root = doc.DocumentElement;
                if (root == null) return;

                // <version> child element (not the doclet-version-NOT-library-version one)
                var verNode = root.SelectSingleNode("version");
                if (verNode != null && !string.IsNullOrEmpty(verNode.InnerText))
                {
                    info.Version = verNode.InnerText.Trim();
                    info.VersionSource = "xml";
                }

                // <class><name> gives the fully qualified Java class name
                var classNode = root.SelectSingleNode("class/name");
                if (classNode != null && !string.IsNullOrEmpty(classNode.InnerText))
                    info.JavaClass = classNode.InnerText.Trim();

                // <dependsOn> child elements list dependency JAR names
                foreach (XmlNode dep in root.SelectNodes("dependsOn"))
                {
                    string val = dep.InnerText.Trim();
                    if (!string.IsNullOrEmpty(val))
                        info.Dependencies.Add(val);
                }
            }
            catch { }
        }

        public static MavenCoords TryReadMavenCoords(string jarPath)
        {
            string bundleSymbolicName = null;
            string bundleVersion      = null;

            try
            {
                using (var zip = ZipFile.OpenRead(jarPath))
                {
                    foreach (var entry in zip.Entries)
                    {
                        // Priority 1: pom.properties (exact Maven coords embedded by Maven build)
                        if (entry.FullName.StartsWith("META-INF/maven/") && entry.Name == "pom.properties")
                        {
                            using (var reader = new StreamReader(entry.Open()))
                            {
                                var props = new Dictionary<string, string>();
                                string line;
                                while ((line = reader.ReadLine()) != null)
                                {
                                    if (line.StartsWith("#")) continue;
                                    int eq = line.IndexOf('=');
                                    if (eq < 1) continue;
                                    props[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                                }
                                string gId, aId, ver;
                                props.TryGetValue("groupId",    out gId);
                                props.TryGetValue("artifactId", out aId);
                                props.TryGetValue("version",    out ver);
                                if (!string.IsNullOrEmpty(gId) && !string.IsNullOrEmpty(aId))
                                    return new MavenCoords { GroupId = gId, ArtifactId = aId, Version = ver };
                            }
                        }

                        // Collect OSGi bundle info from MANIFEST.MF as fallback
                        if (entry.FullName == "META-INF/MANIFEST.MF")
                        {
                            using (var reader = new StreamReader(entry.Open()))
                            {
                                string line;
                                while ((line = reader.ReadLine()) != null)
                                {
                                    int colon = line.IndexOf(':');
                                    if (colon < 1) continue;
                                    string key = line.Substring(0, colon).Trim();
                                    string val = line.Substring(colon + 1).Trim();
                                    // Strip OSGi directives after semicolons (e.g. "com.h2database;singleton:=true")
                                    int semi = val.IndexOf(';');
                                    if (semi >= 0) val = val.Substring(0, semi).Trim();
                                    if (key == "Bundle-SymbolicName" && bundleSymbolicName == null && !string.IsNullOrEmpty(val))
                                        bundleSymbolicName = val;
                                    if (key == "Bundle-Version" && bundleVersion == null && !string.IsNullOrEmpty(val))
                                        bundleVersion = val;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Priority 2: Bundle-SymbolicName from MANIFEST.MF (OSGi JARs, e.g. H2 database)
            // Bundle-SymbolicName is the Maven groupId for many well-known OSGi-bundled libraries.
            if (!string.IsNullOrEmpty(bundleSymbolicName))
            {
                string fname     = Path.GetFileNameWithoutExtension(jarPath);
                var m            = _filenameVersionRe.Match(fname);
                string artifactId = m.Success ? fname.Substring(0, m.Index) : fname;
                if (!string.IsNullOrEmpty(artifactId))
                    return new MavenCoords { GroupId = bundleSymbolicName, ArtifactId = artifactId, Version = bundleVersion };
            }

            // Priority 3+: Maven Central lookup (network)
            return TryMavenCentralLookup(jarPath);
        }

        static MavenCoords TryMavenCentralLookup(string jarPath)
        {
            if (!MavenSearchEnabled) return null;

            // Ensure TLS 1.2 is available (required by Maven Central)
            try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072; }
            catch { }

            string fname = Path.GetFileNameWithoutExtension(jarPath);
            var m = _filenameVersionRe.Match(fname);

            string ambiguousNote = null;
            string errorNote     = null;

            // Try name+version search first (fast, no file hashing)
            // Only accept when unambiguous (exactly 1 match)
            if (m.Success)
            {
                string version    = m.Groups[1].Value;
                string artifactId = fname.Substring(0, m.Index);
                if (!string.IsNullOrEmpty(artifactId))
                {
                    int nameCount;
                    string nameError;
                    var byName = QueryMavenCentral(
                        "a:\"" + artifactId + "\" AND v:\"" + version + "\"",
                        true, out nameCount, out nameError);
                    if (byName != null) return byName;
                    if (nameCount > 1)
                        ambiguousNote = "ambiguous: " + nameCount + " matches on Maven Central";
                    else if (nameError != null)
                        errorNote = "Maven lookup failed: " + nameError;
                }
            }

            // Last resort: SHA1 fingerprint (exact, but requires hashing the file)
            string sha1 = ComputeSha1(jarPath);
            if (sha1 != null)
            {
                int dummy;
                string sha1Error;
                var bySha1 = QueryMavenCentral("1:" + sha1, false, out dummy, out sha1Error);
                if (bySha1 != null) return bySha1;
                if (sha1Error != null && errorNote == null)
                    errorNote = "Maven lookup failed: " + sha1Error;
            }

            if (ambiguousNote != null) return new MavenCoords { Note = ambiguousNote };
            if (errorNote     != null) return new MavenCoords { Note = errorNote };
            return null;
        }

        static MavenCoords QueryMavenCentral(string solrQuery, bool requireUnique,
            out int numFound, out string error)
        {
            numFound = 0;
            error    = null;
            try
            {
                string url = "https://search.maven.org/solrsearch/select?q="
                    + Uri.EscapeDataString(solrQuery) + "&rows=1&wt=json";
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout   = 8000;
                req.UserAgent = "B4JScanner/1.0";
                string json;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                    json = reader.ReadToEnd();

                var js = new JavaScriptSerializer();
                var root = js.Deserialize<Dictionary<string, object>>(json);
                object respObj;
                if (!root.TryGetValue("response", out respObj)) return null;
                var response = respObj as Dictionary<string, object>;
                if (response == null) return null;

                object numObj;
                response.TryGetValue("numFound", out numObj);
                numFound = Convert.ToInt32(numObj);
                if (numFound == 0) return null;
                if (requireUnique && numFound != 1) return null;

                object docsObj;
                response.TryGetValue("docs", out docsObj);
                var docs = docsObj as System.Collections.ArrayList;
                if (docs == null || docs.Count == 0) return null;
                var doc = docs[0] as Dictionary<string, object>;
                if (doc == null) return null;

                object gObj, aObj, vObj;
                doc.TryGetValue("g", out gObj);
                doc.TryGetValue("a", out aObj);
                doc.TryGetValue("v", out vObj);
                string g = gObj as string, a = aObj as string, v = vObj as string;
                if (!string.IsNullOrEmpty(g) && !string.IsNullOrEmpty(a))
                    return new MavenCoords { GroupId = g, ArtifactId = a, Version = v };
            }
            catch (Exception ex) { error = ex.Message; }
            return null;
        }

        static string ComputeSha1(string filePath)
        {
            try
            {
                using (var sha1 = SHA1.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha1.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch { return null; }
        }

        static void TryReadManifest(string jarPath, LibraryInfo info)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(jarPath))
                {
                    var entry = zip.GetEntry("META-INF/MANIFEST.MF");
                    if (entry == null) return;

                    using (var reader = new StreamReader(entry.Open()))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            int colon = line.IndexOf(':');
                            if (colon < 1) continue;
                            string key = line.Substring(0, colon).Trim();
                            string val = line.Substring(colon + 1).Trim();
                            if (string.IsNullOrEmpty(val)) continue;

                            switch (key)
                            {
                                case "Implementation-Version":
                                    if (string.IsNullOrEmpty(info.Version))
                                    { info.Version = val; info.VersionSource = "manifest:Implementation-Version"; }
                                    break;
                                case "Bundle-Version":
                                    if (string.IsNullOrEmpty(info.Version))
                                    { info.Version = val; info.VersionSource = "manifest:Bundle-Version"; }
                                    break;
                                case "Specification-Version":
                                    if (string.IsNullOrEmpty(info.Version))
                                    { info.Version = val; info.VersionSource = "manifest:Specification-Version"; }
                                    break;
                                case "Implementation-Vendor":
                                    if (string.IsNullOrEmpty(info.Vendor)) info.Vendor = val;
                                    break;
                                case "Implementation-Title":
                                    if (info.DisplayName == null || info.DisplayName == "") info.DisplayName = val;
                                    break;
                                case "Bundle-Description":
                                    if (string.IsNullOrEmpty(info.Description)) info.Description = val;
                                    break;
                                case "Bundle-Name":
                                    if (string.IsNullOrEmpty(info.DisplayName)) info.DisplayName = val;
                                    break;
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}
