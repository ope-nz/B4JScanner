using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace B4JScanner
{
    static class MdWriter
    {
        const string ToolVersion = "1.0";

        public static string Write(B4JProject project, List<ResolvedLibrary> libraries,
            List<JavaSourceFile> javaFiles, string outputPath)
        {
            int b4xFound = 0, b4xNotFound = 0;
            var b4xLibs   = new List<ResolvedLibrary>();
            var javaDeps  = new List<ResolvedLibrary>();
            var mavenDeps = new List<ResolvedDependency>();
            var seenPurls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var lib in libraries)
            {
                if (!IsB4X(lib))
                {
                    javaDeps.Add(lib);
                    continue;
                }
                b4xLibs.Add(lib);
                if (lib.Found) b4xFound++; else b4xNotFound++;
            }

            var b4xLibNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lib in b4xLibs)
                b4xLibNames.Add(lib.LibraryName);

            foreach (var lib in b4xLibs)
            {
                if (lib.Info == null) continue;
                foreach (var dep in lib.Info.ResolvedDeps)
                {
                    if (b4xLibNames.Contains(dep.Name)) continue;
                    string depPurl = dep.Maven != null ? dep.Maven.ToPurl() : null;
                    string dedupKey = depPurl ?? ("name:" + dep.Name.ToLowerInvariant());
                    if (seenPurls.Add(dedupKey))
                        mavenDeps.Add(dep);
                }
            }

            int totalMavenDeps = javaDeps.Count + mavenDeps.Count;

            var sb = new StringBuilder();

            // Title
            sb.AppendLine("# SBOM Report: " + project.Name);
            sb.AppendLine();
            sb.AppendLine("> Generated " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC"
                        + " by B4JScanner v" + ToolVersion);
            sb.AppendLine();

            // Project info
            sb.AppendLine("## Project");
            sb.AppendLine();
            sb.AppendLine("| Property | Value |");
            sb.AppendLine("|----------|-------|");
            sb.AppendLine("| Name | " + Md(project.Name) + " |");
            sb.AppendLine("| Version | " + Md(project.Version ?? "unknown") + " |");
            if (!string.IsNullOrEmpty(project.JavaPackage))
                sb.AppendLine("| Package | `" + project.JavaPackage + "` |");
            sb.AppendLine("| B4J File | `" + project.ProjectFile + "` |");
            sb.AppendLine();

            // Summary
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| | Count |");
            sb.AppendLine("|---|------:|");
            sb.AppendLine("| B4X libraries | " + b4xLibs.Count + " |");
            sb.AppendLine("| Found | " + b4xFound + " |");
            if (b4xNotFound > 0)
                sb.AppendLine("| **Not found** | **" + b4xNotFound + "** |");
            sb.AppendLine("| Maven dependencies | " + totalMavenDeps + " |");
            sb.AppendLine("| Java source files scanned | " + javaFiles.Count + " |");
            sb.AppendLine();

            // B4X Libraries table
            sb.AppendLine("## B4X Libraries");
            sb.AppendLine();
            sb.AppendLine("| Library | Package | Type | Version | Deps |");
            sb.AppendLine("|---------|---------|------|---------|-----:|");

            foreach (var lib in b4xLibs)
            {
                string typeLabel = lib.XmlPath != null ? "B4X Jar" : "b4xlib";
                string status = lib.Found ? "" : " ⚠";
                var info = lib.Info;
                string ver   = info != null && !string.IsNullOrEmpty(info.Version) ? info.Version : "unknown";
                int depCount = info != null ? info.ResolvedDeps.Count : 0;
                string deps = depCount > 0 ? depCount.ToString() : "-";
                string pkg   = info != null ? PackageOf(info.JavaClass) : null;

                sb.AppendLine("| " + Md(lib.LibraryName) + status
                            + " | " + (pkg != null ? "`" + Md(pkg) + "`" : "-")
                            + " | " + typeLabel
                            + " | " + Md(ver)
                            + " | " + deps + " |");
            }
            sb.AppendLine();

            // Split deps into Maven (have GroupId) and non-Maven
            var mavenRows    = new List<string[]>(); // name, gId, aId, ver, src, purl
            var nonMavenRows = new List<string[]>(); // name, ver, src

            foreach (var lib in javaDeps)
            {
                var info = lib.Info;
                string ver  = info != null && !string.IsNullOrEmpty(info.Version) ? info.Version : "unknown";
                bool hasCoords = info != null && info.Maven != null && info.Maven.GroupId != null;
                string src  = lib.IsAdditionalJar ? "AJ" : "b4xlib dep";
                if (hasCoords)
                    mavenRows.Add(new string[] { Md(lib.LibraryName),
                        "`" + Md(info.Maven.GroupId) + "`",
                        "`" + Md(info.Maven.ArtifactId) + "`",
                        Md(ver), src, "`" + info.Maven.ToPurl() + "`" });
                else
                    nonMavenRows.Add(new string[] { Md(lib.LibraryName), Md(ver), src });
            }

            mavenDeps.Sort((a, b) =>
            {
                string ag = a.Maven != null ? a.Maven.GroupId ?? "" : "";
                string bg = b.Maven != null ? b.Maven.GroupId ?? "" : "";
                string aa = a.Maven != null ? a.Maven.ArtifactId ?? "" : "";
                string ba = b.Maven != null ? b.Maven.ArtifactId ?? "" : "";
                int c = string.Compare(ag, bg, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(aa, ba, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var dep in mavenDeps)
            {
                bool hasCoords = dep.Maven != null && dep.Maven.GroupId != null;
                string dVer = dep.Maven != null ? dep.Maven.Version ?? "unknown" : "unknown";
                if (hasCoords)
                    mavenRows.Add(new string[] { Md(dep.Name),
                        "`" + Md(dep.Maven.GroupId) + "`",
                        "`" + Md(dep.Maven.ArtifactId) + "`",
                        Md(dVer), "B4X dep", "`" + dep.Maven.ToPurl() + "`" });
                else
                    nonMavenRows.Add(new string[] { Md(dep.Name), Md(dVer), "B4X dep" });
            }

            if (mavenRows.Count > 0)
            {
                sb.AppendLine("## Maven Dependencies");
                sb.AppendLine();
                sb.AppendLine("Underlying Java libraries from b4xlib dependencies, `#AdditionalJar` directives, and B4X `<dependsOn>` metadata.");
                sb.AppendLine("Run OSV Scan to check these for known vulnerabilities.");
                sb.AppendLine();
                sb.AppendLine("| Name | Group ID | Artifact ID | Version | Source | PURL |");
                sb.AppendLine("|------|----------|-------------|---------|--------|------|");
                foreach (var row in mavenRows)
                    sb.AppendLine("| " + row[0] + " | " + row[1] + " | " + row[2]
                                + " | " + row[3] + " | " + row[4] + " | " + row[5] + " |");
                sb.AppendLine();
            }

            if (nonMavenRows.Count > 0)
            {
                sb.AppendLine("## Non-Maven Dependencies");
                sb.AppendLine();
                sb.AppendLine("| Name | Version | Source |");
                sb.AppendLine("|------|---------|--------|");
                foreach (var row in nonMavenRows)
                    sb.AppendLine("| " + row[0] + " | " + row[1] + " | " + row[2] + " |");
                sb.AppendLine();
            }

            // Java import prefixes (if any)
            var prefixes = JavaSourceScanner.GetUniquePackagePrefixes(javaFiles);
            if (prefixes.Count > 0)
            {
                sb.AppendLine("## Java Source Import Prefixes");
                sb.AppendLine();
                sb.AppendLine("Third-party package prefixes found in generated `Objects/src` Java files.");
                sb.AppendLine();
                foreach (var p in prefixes)
                    sb.AppendLine("- `" + p + "`");
                sb.AppendLine();
            }

            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
            return outputPath;
        }

        static bool IsB4X(ResolvedLibrary lib)
        {
            return lib.XmlPath != null || lib.B4xlibPath != null;
        }

        static string PackageOf(string className)
        {
            if (string.IsNullOrEmpty(className)) return null;
            int dot = className.LastIndexOf('.');
            return dot > 0 ? className.Substring(0, dot) : null;
        }

        // Escape pipe characters so they don't break Markdown tables
        static string Md(string value)
        {
            if (value == null) return "";
            return value.Replace("|", "\\|").Replace("[", "\\[").Replace("]", "\\]");
        }
    }
}
