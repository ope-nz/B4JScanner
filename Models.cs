using System;
using System.Collections.Generic;

namespace B4JScanner
{
    class B4JProject
    {
        public string ProjectFile { get; set; }
        public string ProjectFolder { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string JavaPackage { get; set; }
        public List<string> Libraries { get; set; }
        public List<string> Modules { get; set; }

        public List<string> AdditionalJars { get; set; }

        public B4JProject()
        {
            Libraries      = new List<string>();
            Modules        = new List<string>();
            AdditionalJars = new List<string>();
        }
    }

    class ResolvedLibrary
    {
        public string LibraryName { get; set; }
        public string JarPath { get; set; }
        public string XmlPath { get; set; }
        public string B4xlibPath { get; set; }
        public bool IsAdditionalJar { get; set; }
        public bool Found { get { return JarPath != null || B4xlibPath != null; } }
        public LibraryInfo Info { get; set; }
    }

    class LibraryInfo
    {
        public string DisplayName { get; set; }
        public string Version { get; set; }
        public string Vendor { get; set; }
        public string Description { get; set; }
        public string VersionSource { get; set; }
        public string JavaClass { get; set; }
        public List<string> Dependencies { get; set; }
        public MavenCoords Maven { get; set; }
        public List<ResolvedDependency> ResolvedDeps { get; set; }

        public LibraryInfo()
        {
            Dependencies = new List<string>();
            ResolvedDeps = new List<ResolvedDependency>();
        }
    }

    class MavenCoords
    {
        public string GroupId { get; set; }
        public string ArtifactId { get; set; }
        public string Version { get; set; }
        public string Note { get; set; }  // set when coords are partial/ambiguous

        public string ToPurl()
        {
            if (GroupId == null || ArtifactId == null) return null;
            return "pkg:maven/" + Uri.EscapeDataString(GroupId)
                 + "/" + Uri.EscapeDataString(ArtifactId)
                 + "@" + Uri.EscapeDataString(Version ?? "unknown");
        }
    }

    class ResolvedDependency
    {
        public string Name { get; set; }
        public string JarPath { get; set; }
        public MavenCoords Maven { get; set; }
    }

    class JavaSourceFile
    {
        public string FilePath { get; set; }
        public string ClassName { get; set; }
        public string Package { get; set; }
        public List<string> Imports { get; set; }

        public JavaSourceFile()
        {
            Imports = new List<string>();
        }
    }

    class OsvPackageResult
    {
        public string PackageName { get; set; }
        public string Version { get; set; }
        public string Ecosystem { get; set; }
        public List<OsvVuln> Vulns { get; set; }

        public OsvPackageResult()
        {
            Vulns = new List<OsvVuln>();
        }
    }

    class OsvVuln
    {
        public string Id { get; set; }
        public string Summary { get; set; }
        public string Severity { get; set; }
        public string FixedVersion { get; set; }
        public List<string> Aliases { get; set; }

        public OsvVuln()
        {
            Aliases = new List<string>();
        }
    }

    class OsvScanOutput
    {
        public List<OsvPackageResult> Packages { get; set; }
        public string ErrorText { get; set; }

        public OsvScanOutput()
        {
            Packages = new List<OsvPackageResult>();
        }
    }
}
