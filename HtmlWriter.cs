using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace B4JScanner
{
    static class HtmlWriter
    {
        const string ToolVersion = "1.0";

        public static string Write(B4JProject project, List<ResolvedLibrary> libraries,
            List<JavaSourceFile> javaFiles, string outputPath,
            List<OsvPackageResult> osvResults = null)
        {
            int b4xFound = 0, b4xNotFound = 0;
            var b4xLibs  = new List<ResolvedLibrary>();
            var javaDeps = new List<ResolvedLibrary>();
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

            // Build a set of B4X library names so we can suppress deps that are themselves B4X wrappers
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

            int mavenCount = 0, nonMavenCount = 0;
            foreach (var lib in javaDeps)
            {
                bool hc = lib.Info != null && lib.Info.Maven != null && lib.Info.Maven.GroupId != null;
                if (hc) mavenCount++; else nonMavenCount++;
            }
            foreach (var dep in mavenDeps)
            {
                bool hc = dep.Maven != null && dep.Maven.GroupId != null;
                if (hc) mavenCount++; else nonMavenCount++;
            }

            int totalVulns = 0, critCount = 0, highCount = 0;
            string worstSev = null;
            if (osvResults != null)
            {
                foreach (var p in osvResults)
                {
                    totalVulns += p.Vulns.Count;
                    foreach (var v in p.Vulns)
                    {
                        worstSev = WorstSev(worstSev, v.Severity);
                        if (!string.IsNullOrEmpty(v.Severity))
                        {
                            switch (v.Severity.ToUpperInvariant())
                            {
                                case "CRITICAL": critCount++; break;
                                case "HIGH":     highCount++; break;
                            }
                        }
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"UTF-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.AppendLine("<title>SBOM: " + H(project.Name) + "</title>");
            sb.AppendLine("<style>");
            sb.Append(Css());
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"wrap\">");

            // Header
            sb.AppendLine("<header>");
            sb.AppendLine("<h1>SBOM Report: " + H(project.Name) + "</h1>");
            sb.AppendLine("<div class=\"meta\">Generated " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
                + " UTC &nbsp;&middot;&nbsp; B4JScanner v" + ToolVersion + "</div>");
            sb.AppendLine("</header>");

            // Summary cards
            sb.AppendLine("<div class=\"cards\">");
            sb.AppendLine(Card("blue",  b4xLibs.Count.ToString(),  "B4X Libraries"));
            sb.AppendLine(Card("green", b4xFound.ToString(),        "Found"));
            if (b4xNotFound > 0)
                sb.AppendLine(Card("red", b4xNotFound.ToString(), "Not Found"));
            sb.AppendLine(Card("blue", mavenCount.ToString(), "Maven Deps"));
            if (nonMavenCount > 0)
                sb.AppendLine(Card("gray", nonMavenCount.ToString(), "Non-Maven Deps"));
            if (osvResults == null)
                sb.AppendLine(Card("gray",  "?",                    "Vulnerabilities"));
            else if (totalVulns == 0)
                sb.AppendLine(Card("green", "0",                    "Vulnerabilities"));
            else
                sb.AppendLine(Card(SevCardColor(worstSev), totalVulns.ToString(), "Vulnerabilities"));
            if (critCount > 0)
                sb.AppendLine(Card("purple", critCount.ToString(), "Critical"));
            if (highCount > 0)
                sb.AppendLine(Card("red",    highCount.ToString(), "High"));
            sb.AppendLine("</div>");

            // Project info
            sb.AppendLine("<h2>Project</h2>");
            sb.AppendLine("<table><tbody>");
            sb.AppendLine(InfoRow("Name",    project.Name));
            sb.AppendLine(InfoRow("Version", project.Version ?? "unknown"));
            if (!string.IsNullOrEmpty(project.JavaPackage))
                sb.AppendLine(InfoRow("Package", project.JavaPackage));
            sb.AppendLine(InfoRow("B4J File", project.ProjectFile));
            sb.AppendLine("</tbody></table>");

            // B4X Libraries
            sb.AppendLine("<h2>B4X Libraries</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr>"
                + "<th>Library</th><th>Version</th><th>Type</th>"
                + "<th style=\"text-align:right\">Deps</th><th>Status</th>"
                + "</tr></thead>");
            sb.AppendLine("<tbody>");
            foreach (var lib in b4xLibs)
            {
                var info = lib.Info;
                string ver   = info != null && !string.IsNullOrEmpty(info.Version) ? info.Version : "unknown";
                int depCount  = info != null ? info.ResolvedDeps.Count : 0;
                string typeTag = lib.XmlPath != null
                    ? "<span class=\"bge b4xjar\">B4X Jar</span>"
                    : "<span class=\"bge b4xlib\">b4xlib</span>";
                string statusTag = lib.Found
                    ? "<span class=\"bge ok\">Found</span>"
                    : "<span class=\"bge miss\">Missing</span>";
                string pkg = info != null ? PackageOf(info.JavaClass) : null;
                string nameCell = pkg != null
                    ? H(lib.LibraryName) + "<div class=\"lib-pkg\">" + H(pkg) + "</div>"
                    : H(lib.LibraryName);

                sb.AppendLine("<tr>"
                    + "<td>" + nameCell + "</td>"
                    + "<td><code>" + H(ver) + "</code></td>"
                    + "<td>" + typeTag + "</td>"
                    + "<td style=\"text-align:right\">" + (depCount > 0 ? depCount.ToString() : "-") + "</td>"
                    + "<td>" + statusTag + "</td>"
                    + "</tr>");
            }
            sb.AppendLine("</tbody></table>");

            // Maven dependencies
            // Separate deps into those with Maven coords and those without
            var mavenRows    = new List<object[]>(); // [name, gId, aId, ver, srcTag, purl]
            var nonMavenRows = new List<object[]>(); // [name, ver, srcTag]

            foreach (var lib in javaDeps)
            {
                var info = lib.Info;
                string ver  = info != null && !string.IsNullOrEmpty(info.Version) ? info.Version : "unknown";
                bool hasCoords = info != null && info.Maven != null && info.Maven.GroupId != null;
                string srcTag = lib.IsAdditionalJar
                    ? "<span class=\"bge aj\">AJ</span>"
                    : "<span class=\"bge b4xdep\">b4xlib dep</span>";
                if (hasCoords)
                {
                    string purl = "<code class=\"purl\">" + H(info.Maven.ToPurl()) + "</code>";
                    mavenRows.Add(new object[] { H(lib.LibraryName),
                        "<code>" + H(info.Maven.GroupId) + "</code>",
                        "<code>" + H(info.Maven.ArtifactId) + "</code>",
                        ver, srcTag, purl });
                }
                else
                {
                    nonMavenRows.Add(new object[] { H(lib.LibraryName), ver, srcTag });
                }
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
                string srcTag = "<span class=\"bge b4xdep\">B4X dep</span>";
                if (hasCoords)
                {
                    string purl = "<code class=\"purl\">" + H(dep.Maven.ToPurl()) + "</code>";
                    mavenRows.Add(new object[] { H(dep.Name),
                        "<code>" + H(dep.Maven.GroupId) + "</code>",
                        "<code>" + H(dep.Maven.ArtifactId) + "</code>",
                        dVer, srcTag, purl });
                }
                else
                {
                    nonMavenRows.Add(new object[] { H(dep.Name), dVer, srcTag });
                }
            }

            if (mavenRows.Count > 0)
            {
                sb.AppendLine("<h2>Maven Dependencies</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<thead><tr>"
                    + "<th>Name</th><th>Group ID</th><th>Artifact ID</th>"
                    + "<th>Version</th><th>Source</th><th>PURL</th>"
                    + "</tr></thead>");
                sb.AppendLine("<tbody>");
                foreach (var row in mavenRows)
                {
                    sb.AppendLine("<tr>"
                        + "<td>" + row[0] + "</td>"
                        + "<td>" + row[1] + "</td>"
                        + "<td>" + row[2] + "</td>"
                        + "<td><code>" + H((string)row[3]) + "</code></td>"
                        + "<td>" + row[4] + "</td>"
                        + "<td>" + row[5] + "</td>"
                        + "</tr>");
                }
                sb.AppendLine("</tbody></table>");
            }

            if (nonMavenRows.Count > 0)
            {
                sb.AppendLine("<h2>Non-Maven Dependencies</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<thead><tr>"
                    + "<th>Name</th><th>Version</th><th>Source</th>"
                    + "</tr></thead>");
                sb.AppendLine("<tbody>");
                foreach (var row in nonMavenRows)
                {
                    sb.AppendLine("<tr>"
                        + "<td>" + row[0] + "</td>"
                        + "<td><code>" + H((string)row[1]) + "</code></td>"
                        + "<td>" + row[2] + "</td>"
                        + "</tr>");
                }
                sb.AppendLine("</tbody></table>");
            }

            // Vulnerabilities
            sb.AppendLine("<h2>Vulnerabilities</h2>");
            if (osvResults == null)
            {
                sb.AppendLine("<div class=\"notice not-scanned\">"
                    + "OSV Scan has not been run. Click <strong>OSV Scan</strong> to check for known vulnerabilities."
                    + "</div>");
            }
            else if (totalVulns == 0)
            {
                sb.AppendLine("<div class=\"notice none-found\">&#10003;&nbsp; No known vulnerabilities found.</div>");
            }
            else
            {
                foreach (var pkg in osvResults)
                {
                    if (pkg.Vulns.Count == 0) continue;
                    sb.AppendLine("<table style=\"margin-bottom:14px\">");
                    sb.AppendLine("<thead><tr><th colspan=\"5\">"
                        + H(pkg.PackageName)
                        + "&nbsp;<code>" + H(pkg.Version ?? "") + "</code>"
                        + "</th></tr>"
                        + "<tr><th>ID</th><th>Aliases</th><th>Severity</th><th>Fix Version</th><th>Summary</th></tr>"
                        + "</thead><tbody>");
                    foreach (var v in pkg.Vulns)
                    {
                        string sevBadge = "<span class=\"bge " + SevClass(v.Severity) + "\">"
                            + H(v.Severity ?? "unknown") + "</span>";
                        string aliases = v.Aliases.Count > 0
                            ? "<code>" + H(string.Join(", ", v.Aliases.ToArray())) + "</code>"
                            : "<span class=\"dim\">-</span>";
                        string fixVer = !string.IsNullOrEmpty(v.FixedVersion)
                            ? "<code class=\"fix-ver\">" + H(v.FixedVersion) + "</code>"
                            : "<span class=\"dim\">-</span>";
                        sb.AppendLine("<tr>"
                            + "<td><code>" + H(v.Id) + "</code></td>"
                            + "<td>" + aliases + "</td>"
                            + "<td>" + sevBadge + "</td>"
                            + "<td>" + fixVer + "</td>"
                            + "<td>" + H(v.Summary ?? "") + "</td>"
                            + "</tr>");
                    }
                    sb.AppendLine("</tbody></table>");
                }
            }

            // Java import prefixes
            var prefixes = JavaSourceScanner.GetUniquePackagePrefixes(javaFiles);
            if (prefixes.Count > 0)
            {
                sb.AppendLine("<h2>Java Import Prefixes</h2>");
                sb.AppendLine("<p class=\"section-note\">Third-party package prefixes from generated <code>Objects/src</code> Java files.</p>");
                sb.AppendLine("<div class=\"prefix-list\">");
                foreach (var p in prefixes)
                    sb.AppendLine("<code class=\"prefix-tag\">" + H(p) + "</code>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</div>"); // .wrap
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
            return outputPath;
        }

        public static string WriteLibraryScan(List<ResolvedLibrary> jars,
            List<OsvPackageResult> osvResults, string outputPath)
        {
            var identified   = new List<ResolvedLibrary>();
            var unidentified = new List<ResolvedLibrary>();
            foreach (var jar in jars)
            {
                bool hasCoords = jar.Info != null && jar.Info.Maven != null && jar.Info.Maven.GroupId != null;
                if (hasCoords) identified.Add(jar); else unidentified.Add(jar);
            }

            identified.Sort((a, b) =>
            {
                int c = string.Compare(a.Info.Maven.GroupId, b.Info.Maven.GroupId, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.Info.Maven.ArtifactId, b.Info.Maven.ArtifactId, StringComparison.OrdinalIgnoreCase);
            });

            int totalVulns = 0, critCount = 0, highCount = 0;
            string worstSev = null;
            var highCritPkgs = new List<OsvPackageResult>();

            if (osvResults != null)
            {
                foreach (var p in osvResults)
                {
                    totalVulns += p.Vulns.Count;
                    int pkgCrit = 0, pkgHigh = 0;
                    foreach (var v in p.Vulns)
                    {
                        worstSev = WorstSev(worstSev, v.Severity);
                        if (!string.IsNullOrEmpty(v.Severity))
                        {
                            switch (v.Severity.ToUpperInvariant())
                            {
                                case "CRITICAL": critCount++; pkgCrit++; break;
                                case "HIGH":     highCount++; pkgHigh++; break;
                            }
                        }
                    }
                    if (pkgCrit > 0 || pkgHigh > 0) highCritPkgs.Add(p);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"UTF-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.AppendLine("<title>Library Scan Report</title>");
            sb.AppendLine("<style>");
            sb.Append(Css());
            sb.AppendLine(".wrap{max-width:90%}");
            sb.AppendLine("details{margin-bottom:4px}");
            sb.AppendLine("summary.sh{font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;padding-bottom:7px;border-bottom:2px solid #2563eb;color:#0f172a;cursor:pointer;list-style:none;display:flex;align-items:center;margin:28px 0 10px;user-select:none}");
            sb.AppendLine("summary.sh::-webkit-details-marker{display:none}");
            sb.AppendLine(".sh-count{font-weight:400;color:#64748b;font-size:11px;text-transform:none;letter-spacing:0;margin-left:6px}");
            sb.AppendLine("summary.sh::after{content:\"\\25B2\";font-size:9px;color:#94a3b8;margin-left:auto}");
            sb.AppendLine("details:not([open])>summary.sh::after{content:\"\\25BC\"}");
            sb.AppendLine("a.vlink{font-size:11px;color:#2563eb;text-decoration:none}");
            sb.AppendLine("a.card-link{text-decoration:none;color:inherit}");
            sb.AppendLine("a.card-link:hover .card{border-color:#93c5fd}");
            sb.AppendLine(".b4j-badge{font-size:10px;background:#dbeafe;color:#1d4ed8;border-radius:3px;padding:1px 5px;margin-left:5px;font-weight:600}");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body><div class=\"wrap\">");

            sb.AppendLine("<header>");
            sb.AppendLine("<h1>Library Scan Report</h1>");
            sb.AppendLine("<div class=\"meta\">Generated " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
                + " UTC &nbsp;&middot;&nbsp; B4JScanner v" + ToolVersion + "</div>");
            sb.AppendLine("</header>");

            // Summary cards
            sb.AppendLine("<div class=\"cards\">");
            sb.AppendLine(Card("blue",  jars.Count.ToString(),       "JARs Scanned"));
            sb.AppendLine(Card("green", identified.Count.ToString(),  "Identified",   "#sec-identified"));
            if (unidentified.Count > 0)
                sb.AppendLine(Card("gray",  unidentified.Count.ToString(), "Unidentified", "#sec-unidentified"));
            if (osvResults == null)
                sb.AppendLine(Card("gray",   "?",                     "Vulnerabilities", "#sec-vulns"));
            else if (totalVulns == 0)
                sb.AppendLine(Card("green",  "0",                     "Vulnerabilities", "#sec-vulns"));
            else
                sb.AppendLine(Card(SevCardColor(worstSev), totalVulns.ToString(), "Vulnerabilities", "#sec-vulns"));
            if (critCount > 0)
                sb.AppendLine(Card("purple", critCount.ToString(), "Critical", "#sec-hc"));
            if (highCount > 0)
                sb.AppendLine(Card("red",    highCount.ToString(), "High",     "#sec-hc"));
            sb.AppendLine("</div>");

            // High / Critical findings summary
            if (highCritPkgs.Count > 0)
            {
                sb.AppendLine("<details id=\"sec-hc\" open>");
                sb.AppendLine("<summary class=\"sh\">High / Critical Findings"
                    + "<span class=\"sh-count\">" + highCritPkgs.Count + " package"
                    + (highCritPkgs.Count == 1 ? "" : "s") + "</span></summary>");
                sb.AppendLine("<table style=\"margin-bottom:14px\">");
                sb.AppendLine("<thead><tr>"
                    + "<th>Package</th><th>Version</th>"
                    + "<th style=\"width:80px\">Critical</th><th style=\"width:80px\">High</th>"
                    + "<th style=\"width:60px\"></th>"
                    + "</tr></thead><tbody>");
                foreach (var p in highCritPkgs)
                {
                    int pkgCrit = 0, pkgHigh = 0;
                    foreach (var v in p.Vulns)
                    {
                        if (!string.IsNullOrEmpty(v.Severity))
                        {
                            switch (v.Severity.ToUpperInvariant())
                            {
                                case "CRITICAL": pkgCrit++; break;
                                case "HIGH":     pkgHigh++; break;
                            }
                        }
                    }
                    string aid = "vuln-" + AnchorId(p.PackageName);
                    sb.AppendLine("<tr>"
                        + "<td><code>" + H(p.PackageName) + "</code></td>"
                        + "<td><code>" + H(p.Version ?? "") + "</code></td>"
                        + "<td>" + (pkgCrit > 0 ? "<span class=\"bge critical\">" + pkgCrit + "</span>" : "<span class=\"dim\">-</span>") + "</td>"
                        + "<td>" + (pkgHigh > 0 ? "<span class=\"bge high\">" + pkgHigh + "</span>" : "<span class=\"dim\">-</span>") + "</td>"
                        + "<td><a class=\"vlink\" href=\"#" + aid + "\">&#8595;&nbsp;view</a></td>"
                        + "</tr>");
                }
                sb.AppendLine("</tbody></table>");
                sb.AppendLine("</details>");
            }

            // Identified JARs
            if (identified.Count > 0)
            {
                sb.AppendLine("<details id=\"sec-identified\" open>");
                sb.AppendLine("<summary class=\"sh\">Identified JARs"
                    + "<span class=\"sh-count\">" + identified.Count + "</span></summary>");
                sb.AppendLine("<table>");
                sb.AppendLine("<thead><tr>"
                    + "<th style=\"width:22%\">Name</th><th style=\"width:20%\">Group ID</th><th style=\"width:20%\">Artifact ID</th>"
                    + "<th style=\"width:10%\">Version</th><th style=\"width:28%\">PURL</th>"
                    + "</tr></thead><tbody>");
                for (int i = 0; i < identified.Count; i++)
                {
                    var jar = identified[i];
                    string ver  = jar.Info.Maven.Version ?? jar.Info.Version ?? "unknown";
                    string purl = jar.Info.Maven.ToPurl();
                    sb.AppendLine("<tr id=\"jar-" + i + "\">"
                        + "<td>" + H(jar.LibraryName) + "</td>"
                        + "<td><code>" + H(jar.Info.Maven.GroupId) + "</code></td>"
                        + "<td><code>" + H(jar.Info.Maven.ArtifactId) + "</code></td>"
                        + "<td><code>" + H(ver) + "</code></td>"
                        + "<td><code class=\"purl\">" + H(purl) + "</code></td>"
                        + "</tr>");
                }
                sb.AppendLine("</tbody></table>");
                sb.AppendLine("</details>");
            }

            // Unidentified JARs
            if (unidentified.Count > 0)
            {
                sb.AppendLine("<details id=\"sec-unidentified\" open>");
                sb.AppendLine("<summary class=\"sh\">Unidentified JARs"
                    + "<span class=\"sh-count\">" + unidentified.Count + "</span></summary>");
                sb.AppendLine("<p class=\"section-note\">These JARs have no embedded Maven metadata and could not be identified via Maven Central.</p>");
                sb.AppendLine("<table>");
                sb.AppendLine("<thead><tr><th>Name</th><th>Version</th></tr></thead><tbody>");
                for (int i = 0; i < unidentified.Count; i++)
                {
                    var jar = unidentified[i];
                    string ver   = jar.Info != null && !string.IsNullOrEmpty(jar.Info.Version) ? jar.Info.Version : "unknown";
                    string badge = jar.XmlPath != null ? "<span class=\"b4j-badge\">B4J</span>" : "";
                    sb.AppendLine("<tr id=\"ujar-" + i + "\">"
                        + "<td>" + H(jar.LibraryName) + badge + "</td>"
                        + "<td><code>" + H(ver) + "</code></td>"
                        + "</tr>");
                }
                sb.AppendLine("</tbody></table>");
                sb.AppendLine("</details>");
            }

            // Vulnerabilities
            sb.AppendLine("<details id=\"sec-vulns\" open>");
            sb.AppendLine("<summary class=\"sh\">Vulnerabilities"
                + "<span class=\"sh-count\">" + (osvResults == null ? "?" : totalVulns.ToString()) + "</span></summary>");
            if (osvResults == null)
            {
                sb.AppendLine("<div class=\"notice not-scanned\">OSV scan did not run.</div>");
            }
            else if (totalVulns == 0)
            {
                sb.AppendLine("<div class=\"notice none-found\">&#10003;&nbsp; No known vulnerabilities found.</div>");
            }
            else
            {
                foreach (var pkg in osvResults)
                {
                    if (pkg.Vulns.Count == 0) continue;
                    string aid = "vuln-" + AnchorId(pkg.PackageName);
                    sb.AppendLine("<table id=\"" + aid + "\" style=\"margin-bottom:14px\">");
                    sb.AppendLine("<thead><tr><th colspan=\"5\">"
                        + H(pkg.PackageName) + "&nbsp;<code>" + H(pkg.Version ?? "") + "</code>"
                        + "</th></tr>"
                        + "<tr><th>ID</th><th>Aliases</th><th>Severity</th><th>Fix Version</th><th>Summary</th></tr>"
                        + "</thead><tbody>");
                    foreach (var v in pkg.Vulns)
                    {
                        string sevBadge = "<span class=\"bge " + SevClass(v.Severity) + "\">" + H(v.Severity ?? "unknown") + "</span>";
                        string aliases  = v.Aliases.Count > 0
                            ? "<code>" + H(string.Join(", ", v.Aliases.ToArray())) + "</code>"
                            : "<span class=\"dim\">-</span>";
                        string fixVer = !string.IsNullOrEmpty(v.FixedVersion)
                            ? "<code class=\"fix-ver\">" + H(v.FixedVersion) + "</code>"
                            : "<span class=\"dim\">-</span>";
                        sb.AppendLine("<tr>"
                            + "<td><code>" + H(v.Id) + "</code></td>"
                            + "<td>" + aliases + "</td>"
                            + "<td>" + sevBadge + "</td>"
                            + "<td>" + fixVer + "</td>"
                            + "<td>" + H(v.Summary ?? "") + "</td>"
                            + "</tr>");
                    }
                    sb.AppendLine("</tbody></table>");
                }
            }
            sb.AppendLine("</details>");

            sb.AppendLine("</div></body></html>");
            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
            return outputPath;
        }

        // --- Helpers ---

        static bool IsB4X(ResolvedLibrary lib)
        {
            return lib.XmlPath != null || lib.B4xlibPath != null;
        }

        static string Card(string colorClass, string val, string label, string href = null)
        {
            string inner = "<div class=\"card " + colorClass + "\">"
                         + "<div class=\"val\">" + val + "</div>"
                         + "<div class=\"lbl\">" + H(label) + "</div>"
                         + "</div>";
            if (href != null)
                return "<a href=\"" + href + "\" class=\"card-link\">" + inner + "</a>";
            return inner;
        }

        static string InfoRow(string label, string value)
        {
            return "<tr>"
                 + "<th>" + H(label) + "</th>"
                 + "<td><code>" + H(value ?? "") + "</code></td>"
                 + "</tr>";
        }

        static string SevClass(string sev)
        {
            if (string.IsNullOrEmpty(sev)) return "unknown";
            switch (sev.ToUpperInvariant())
            {
                case "CRITICAL": return "critical";
                case "HIGH":     return "high";
                case "MEDIUM":   return "medium";
                case "LOW":      return "low";
                default:         return "unknown";
            }
        }

        static string SevCardColor(string sev)
        {
            if (string.IsNullOrEmpty(sev)) return "amber";
            switch (sev.ToUpperInvariant())
            {
                case "CRITICAL": return "purple";
                case "HIGH":     return "red";
                case "MEDIUM":   return "amber";
                case "LOW":      return "green";
                default:         return "amber";
            }
        }

        static string WorstSev(string current, string candidate)
        {
            return SevRank(candidate) > SevRank(current) ? candidate : current;
        }

        static int SevRank(string s)
        {
            if (s == null) return 0;
            switch (s.ToUpperInvariant())
            {
                case "CRITICAL": return 4;
                case "HIGH":     return 3;
                case "MEDIUM":   return 2;
                case "LOW":      return 1;
                default:         return 0;
            }
        }

        static string PackageOf(string className)
        {
            if (string.IsNullOrEmpty(className)) return null;
            int dot = className.LastIndexOf('.');
            return dot > 0 ? className.Substring(0, dot) : null;
        }

        static string AnchorId(string s)
        {
            if (string.IsNullOrEmpty(s)) return "item";
            var result = new StringBuilder();
            bool lastHyphen = false;
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    result.Append(c);
                    lastHyphen = false;
                }
                else if (!lastHyphen && result.Length > 0)
                {
                    result.Append('-');
                    lastHyphen = true;
                }
            }
            string r = result.ToString().TrimEnd('-');
            return r.Length > 0 ? r : "item";
        }

        static string H(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        static string Css()
        {
            return
@"*{box-sizing:border-box;margin:0;padding:0}
body{font:14px/1.6 system-ui,-apple-system,BlinkMacSystemFont,sans-serif;background:#f1f5f9;color:#1e293b}
.wrap{max-width:1100px;margin:0 auto;padding:24px}
header{background:linear-gradient(135deg,#1e3a8a,#2563eb);color:#fff;padding:20px 28px;border-radius:10px;margin-bottom:20px}
header h1{font-size:22px;font-weight:700;letter-spacing:-.3px}
header .meta{font-size:12px;opacity:.8;margin-top:5px}
h2{font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;margin:28px 0 10px;padding-bottom:7px;border-bottom:2px solid #2563eb;color:#0f172a}
.cards{display:flex;flex-wrap:wrap;gap:10px;margin-bottom:6px}
.card{background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:14px 18px;min-width:130px;border-top:3px solid #94a3b8}
.card.blue{border-top-color:#2563eb}.card.green{border-top-color:#16a34a}
.card.red{border-top-color:#dc2626}.card.amber{border-top-color:#d97706}
.card.purple{border-top-color:#7c3aed}.card.gray{border-top-color:#94a3b8}
.card .val{font-size:26px;font-weight:800;color:#0f172a;line-height:1}
.card .lbl{font-size:11px;color:#64748b;text-transform:uppercase;letter-spacing:.05em;margin-top:5px}
table{width:100%;border-collapse:collapse;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e2e8f0;font-size:13px}
thead th{background:#f8fafc;padding:8px 12px;text-align:left;font-weight:600;color:#475569;border-bottom:1px solid #e2e8f0;font-size:11px;text-transform:uppercase;letter-spacing:.05em}
tbody th{background:#f8fafc;padding:8px 12px;text-align:left;font-weight:600;color:#475569;width:130px;border-bottom:1px solid #f1f5f9;font-size:13px;text-transform:none;letter-spacing:0}
td{padding:8px 12px;border-bottom:1px solid #f1f5f9;vertical-align:middle}
tr:last-child td,tr:last-child th{border-bottom:none}
.bge{display:inline-block;padding:2px 9px;border-radius:99px;font-size:11px;font-weight:700;letter-spacing:.02em;white-space:nowrap}
.bge.ok{background:#dcfce7;color:#166534}
.bge.miss{background:#fee2e2;color:#991b1b}
.bge.aj{background:#dbeafe;color:#1e40af}
.bge.b4xjar{background:#ede9fe;color:#5b21b6}
.bge.b4xlib{background:#fce7f3;color:#9d174d}
.bge.b4xdep{background:#e0f2fe;color:#075985}
.bge.critical{background:#ede9fe;color:#5b21b6}
.bge.high{background:#fee2e2;color:#991b1b}
.bge.medium{background:#fef3c7;color:#92400e}
.bge.low{background:#d1fae5;color:#065f46}
.bge.unknown{background:#f1f5f9;color:#64748b}
code{background:#f1f5f9;padding:1px 6px;border-radius:3px;font-family:ui-monospace,Consolas,monospace;font-size:12px;color:#0f172a}
code.purl{font-size:11px;color:#475569;overflow-wrap:break-word;word-break:break-word}
code.fix-ver{background:#dcfce7;color:#166534;font-weight:600}
.dim{color:#94a3b8}
.maven-note{color:#b45309;font-style:italic;font-size:12px}
.lib-pkg{font-size:11px;color:#94a3b8;margin-top:2px;font-family:ui-monospace,Consolas,monospace}
.notice{padding:13px 18px;border-radius:8px;font-size:13px;border:1px solid}
.notice.none-found{background:#f0fdf4;border-color:#86efac;color:#166534}
.notice.not-scanned{background:#f8fafc;border-color:#cbd5e1;color:#64748b}
.section-note{font-size:12px;color:#64748b;margin-bottom:10px}
.prefix-list{display:flex;flex-wrap:wrap;gap:6px}
.prefix-tag{background:#f1f5f9;padding:4px 10px;border-radius:5px;border:1px solid #e2e8f0;font-size:12px}
";
        }
    }
}
