using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace B4JScanner
{
    static class SbomWriter
    {
        const string ToolVersion = "1.0";

        public static string Write(B4JProject project, List<ResolvedLibrary> libraries,
            List<JavaSourceFile> javaFiles, string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"bomFormat\": \"CycloneDX\",");
            sb.AppendLine("  \"specVersion\": \"1.5\",");
            sb.AppendLine("  \"serialNumber\": \"urn:uuid:" + Guid.NewGuid() + "\",");
            sb.AppendLine("  \"version\": 1,");

            // metadata
            sb.AppendLine("  \"metadata\": {");
            sb.AppendLine("    \"timestamp\": \"" + DateTime.UtcNow.ToString("O") + "\",");
            sb.AppendLine("    \"tools\": [");
            sb.AppendLine("      {");
            sb.AppendLine("        \"vendor\": \"B4JScanner\",");
            sb.AppendLine("        \"name\": \"B4JScanner\",");
            sb.AppendLine("        \"version\": \"" + ToolVersion + "\"");
            sb.AppendLine("      }");
            sb.AppendLine("    ],");
            sb.AppendLine("    \"component\": {");
            sb.AppendLine("      \"type\": \"application\",");
            sb.AppendLine("      \"name\": " + Json(project.Name) + ",");
            sb.AppendLine("      \"version\": " + Json(project.Version ?? "unknown"));
            if (!string.IsNullOrEmpty(project.JavaPackage))
                sb.AppendLine("      ,\"group\": " + Json(project.JavaPackage));
            sb.AppendLine("    }");
            sb.AppendLine("  },");

            // components
            sb.AppendLine("  \"components\": [");

            bool firstComp = true;
            foreach (var lib in libraries)
            {
                if (!firstComp) sb.AppendLine(",");
                firstComp = false;

                var info = lib.Info ?? new LibraryInfo { Version = "unknown" };
                string displayName = string.IsNullOrEmpty(info.DisplayName) ? lib.LibraryName : info.DisplayName;
                string version = string.IsNullOrEmpty(info.Version) ? "unknown" : info.Version;
                // Use real Maven PURL if we found coords in the JAR, otherwise label as b4j wrapper
                string purl = info.Maven != null
                    ? info.Maven.ToPurl()
                    : "pkg:maven/b4j/" + Uri.EscapeDataString(displayName)
                      + "@" + Uri.EscapeDataString(version);

                sb.AppendLine("    {");
                sb.AppendLine("      \"type\": \"library\",");
                sb.AppendLine("      \"name\": " + Json(displayName) + ",");
                sb.AppendLine("      \"version\": " + Json(version) + ",");
                sb.AppendLine("      \"purl\": " + Json(purl) + ",");

                if (!string.IsNullOrEmpty(info.Vendor))
                    sb.AppendLine("      \"publisher\": " + Json(info.Vendor) + ",");
                if (!string.IsNullOrEmpty(info.Description))
                    sb.AppendLine("      \"description\": " + Json(info.Description) + ",");

                // properties
                sb.AppendLine("      \"properties\": [");
                var props = new List<KeyValuePair<string, string>>();
                props.Add(new KeyValuePair<string, string>("b4j:libraryName", lib.LibraryName));
                props.Add(new KeyValuePair<string, string>("b4j:found", lib.Found ? "true" : "false"));
                if (lib.JarPath != null)    props.Add(new KeyValuePair<string, string>("b4j:jarPath", lib.JarPath));
                if (lib.XmlPath != null)    props.Add(new KeyValuePair<string, string>("b4j:xmlPath", lib.XmlPath));
                if (lib.B4xlibPath != null) props.Add(new KeyValuePair<string, string>("b4j:b4xlibPath", lib.B4xlibPath));
                if (!string.IsNullOrEmpty(info.VersionSource))
                    props.Add(new KeyValuePair<string, string>("b4j:versionSource", info.VersionSource));
                foreach (var dep in info.Dependencies)
                    props.Add(new KeyValuePair<string, string>("b4j:dependsOn", dep));

                for (int i = 0; i < props.Count; i++)
                {
                    string comma = i < props.Count - 1 ? "," : "";
                    sb.AppendLine("        { \"name\": " + Json(props[i].Key)
                                + ", \"value\": " + Json(props[i].Value) + " }" + comma);
                }
                sb.AppendLine("      ]");
                sb.Append("    }");
            }

            // Dependency components — deduplicated by PURL, with real pkg:maven coords
            var seenPurls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lib in libraries)
            {
                if (lib.Info == null) continue;
                foreach (var dep in lib.Info.ResolvedDeps)
                {
                    if (dep.Maven == null) continue;
                    string depPurl = dep.Maven.ToPurl();
                    if (!seenPurls.Add(depPurl)) continue;

                    if (!firstComp) sb.AppendLine(",");
                    firstComp = false;

                    sb.AppendLine("    {");
                    sb.AppendLine("      \"type\": \"library\",");
                    sb.AppendLine("      \"group\": " + Json(dep.Maven.GroupId) + ",");
                    sb.AppendLine("      \"name\": " + Json(dep.Maven.ArtifactId) + ",");
                    sb.AppendLine("      \"version\": " + Json(dep.Maven.Version ?? "unknown") + ",");
                    sb.AppendLine("      \"purl\": " + Json(depPurl) + ",");
                    sb.AppendLine("      \"properties\": [");
                    sb.AppendLine("        { \"name\": \"b4j:depName\", \"value\": " + Json(dep.Name) + " }");
                    if (dep.JarPath != null)
                        sb.AppendLine("       ,{ \"name\": \"b4j:jarPath\", \"value\": " + Json(dep.JarPath) + " }");
                    sb.AppendLine("      ]");
                    sb.Append("    }");
                }
            }

            if (!firstComp) sb.AppendLine();
            sb.AppendLine("  ],");

            // Java source import prefixes as external references
            var prefixes = JavaSourceScanner.GetUniquePackagePrefixes(javaFiles);
            sb.AppendLine("  \"externalReferences\": [");
            bool firstRef = true;
            foreach (var prefix in prefixes)
            {
                if (!firstRef) sb.AppendLine(",");
                firstRef = false;
                sb.Append("    { \"type\": \"other\", \"url\": " + Json("pkg:" + prefix)
                         + ", \"comment\": \"Java import prefix from Objects/src\" }");
            }
            if (!firstRef) sb.AppendLine();
            sb.AppendLine("  ]");

            sb.AppendLine("}");

            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
            return outputPath;
        }

        public static string WriteLibraryScan(List<ResolvedLibrary> jars, string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"bomFormat\": \"CycloneDX\",");
            sb.AppendLine("  \"specVersion\": \"1.5\",");
            sb.AppendLine("  \"serialNumber\": \"urn:uuid:" + Guid.NewGuid() + "\",");
            sb.AppendLine("  \"version\": 1,");
            sb.AppendLine("  \"metadata\": {");
            sb.AppendLine("    \"timestamp\": \"" + DateTime.UtcNow.ToString("O") + "\",");
            sb.AppendLine("    \"tools\": [");
            sb.AppendLine("      {");
            sb.AppendLine("        \"vendor\": \"B4JScanner\",");
            sb.AppendLine("        \"name\": \"B4JScanner\",");
            sb.AppendLine("        \"version\": \"" + ToolVersion + "\"");
            sb.AppendLine("      }");
            sb.AppendLine("    ],");
            sb.AppendLine("    \"component\": {");
            sb.AppendLine("      \"type\": \"application\",");
            sb.AppendLine("      \"name\": \"B4J Libraries\"");
            sb.AppendLine("    }");
            sb.AppendLine("  },");
            sb.AppendLine("  \"components\": [");

            bool firstComp = true;
            var seenPurls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var jar in jars)
            {
                if (jar.Info == null || jar.Info.Maven == null) continue;
                string purl = jar.Info.Maven.ToPurl();
                if (purl == null) continue;
                if (!seenPurls.Add(purl)) continue;

                if (!firstComp) sb.AppendLine(",");
                firstComp = false;

                string version = string.IsNullOrEmpty(jar.Info.Version) ? "unknown" : jar.Info.Version;
                sb.AppendLine("    {");
                sb.AppendLine("      \"type\": \"library\",");
                if (jar.Info.Maven.GroupId != null)
                    sb.AppendLine("      \"group\": " + Json(jar.Info.Maven.GroupId) + ",");
                sb.AppendLine("      \"name\": " + Json(jar.Info.Maven.ArtifactId ?? jar.LibraryName) + ",");
                sb.AppendLine("      \"version\": " + Json(jar.Info.Maven.Version ?? version) + ",");
                sb.AppendLine("      \"purl\": " + Json(purl) + ",");
                sb.AppendLine("      \"properties\": [");
                sb.AppendLine("        { \"name\": \"b4j:jarPath\", \"value\": " + Json(jar.JarPath) + " }");
                sb.AppendLine("      ]");
                sb.Append("    }");
            }

            if (!firstComp) sb.AppendLine();
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
            return outputPath;
        }

        static string Json(string value)
        {
            if (value == null) return "null";
            return "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                + "\"";
        }
    }
}
