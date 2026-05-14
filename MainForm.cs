using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace B4JScanner
{
    class MainForm : Form
    {
        TextBox txtProject, txtLibs, txtAddLibs;
        Button btnScan, btnOpenSbom, btnOpenReport, btnLibScan;
        RichTextBox txtLog;
        ToolStripStatusLabel statusLabel;
        string _lastSbomPath;
        string _lastHtmlPath;
        B4JProject _lastProject;
        List<ResolvedLibrary> _lastResolved;
        List<JavaSourceFile> _lastJavaFiles;
        AppConfig _config;

        public MainForm()
        {
            _config = AppConfig.Load();
            BuildUI();
            PopulateFromConfig();
        }

        void BuildUI()
        {
            SuspendLayout();

            Text = "B4JScanner";
            Size = new Size(740, 560);
            MinimumSize = new Size(620, 460);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            FormClosing += (s, e) => SaveConfig();

            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            // --- Status strip ---
            statusLabel = new ToolStripStatusLabel("Ready")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(statusLabel);

            // --- Folder selector table ---
            txtProject = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
            txtLibs    = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
            txtAddLibs = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };

            var btnBrowseProject = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            var btnBrowseLibs    = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            var btnBrowseAddLibs = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };

            btnBrowseProject.Click += (s, e) => BrowseFolder("Select B4J project folder", txtProject);
            btnBrowseLibs.Click    += (s, e) => BrowseFolder("Select B4J Libraries folder", txtLibs);
            btnBrowseAddLibs.Click += (s, e) => BrowseFolder("Select AdditionalLibraries folder", txtAddLibs);

            var folderTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                Padding = new Padding(10, 8, 10, 4)
            };
            folderTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155f));
            folderTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            folderTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
            folderTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
            folderTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
            folderTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33.4f));

            folderTable.Controls.Add(FolderLabel("Project Folder:"),    0, 0);
            folderTable.Controls.Add(txtProject,                        1, 0);
            folderTable.Controls.Add(btnBrowseProject,                  2, 0);
            folderTable.Controls.Add(FolderLabel("Libraries:"),         0, 1);
            folderTable.Controls.Add(txtLibs,                           1, 1);
            folderTable.Controls.Add(btnBrowseLibs,                     2, 1);
            folderTable.Controls.Add(FolderLabel("Add. Libraries:"),    0, 2);
            folderTable.Controls.Add(txtAddLibs,                        1, 2);
            folderTable.Controls.Add(btnBrowseAddLibs,                  2, 2);

            // --- Button strip ---
            btnScan = new Button
            {
                Text = "Project Dependency Scan",
                Width = 168, Height = 28,
                Margin = new Padding(0, 0, 8, 0)
            };
            btnOpenReport = new Button
            {
                Text = "Open Project Report",
                Width = 138, Height = 28,
                Enabled = false,
                Margin = new Padding(0, 0, 8, 0)
            };
            btnOpenSbom = new Button
            {
                Text = "Open Project SBOM",
                Width = 132, Height = 28,
                Enabled = false,
                Margin = new Padding(0, 0, 8, 0)
            };
            btnLibScan = new Button
            {
                Text = "Library Scan",
                Width = 110, Height = 28,
                Margin = new Padding(0)
            };
            btnScan.Click       += OnScan;
            btnOpenSbom.Click   += OnOpenSbom;
            btnOpenReport.Click += OnOpenReport;
            btnLibScan.Click    += OnLibraryScan;

            var btnLeftFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            btnLeftFlow.Controls.Add(btnScan);
            btnLeftFlow.Controls.Add(btnOpenReport);
            btnLeftFlow.Controls.Add(btnOpenSbom);

            // Right-docked holder keeps btnLibScan at its natural height
            // WinForms always processes Right before Fill so add order doesn't matter
            var libScanHolder = new Panel { Dock = DockStyle.Right, Width = 110 };
            libScanHolder.Controls.Add(btnLibScan);

            var btnPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 6, 10, 4)
            };
            btnPanel.Controls.Add(libScanHolder);
            btnPanel.Controls.Add(btnLeftFlow);

            // --- Log area ---
            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            var logGroup = new GroupBox
            {
                Text = "Output",
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 4, 6, 6)
            };
            logGroup.Controls.Add(txtLog);

            var logWrapper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 6) };
            logWrapper.Controls.Add(logGroup);

            // --- Main layout ---
            var mainTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0)
            };
            mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f));
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            mainTable.Controls.Add(folderTable,  0, 0);
            mainTable.Controls.Add(btnPanel,     0, 1);
            mainTable.Controls.Add(logWrapper,   0, 2);

            Controls.Add(mainTable);
            Controls.Add(statusStrip);

            ResumeLayout();
        }

        static Label FolderLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 4, 0)
            };
        }

        void PopulateFromConfig()
        {
            txtProject.Text = _config.ProjectFolder ?? "";
            txtLibs.Text    = _config.LibrariesPath ?? "";
            txtAddLibs.Text = _config.AdditionalLibrariesPath ?? "";
        }

        void BrowseFolder(string description, TextBox target)
        {
            string initial = target.Text.Trim();
            using (var dlg = new FolderBrowserDialog
            {
                Description  = description,
                SelectedPath = Directory.Exists(initial) ? initial : ""
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    target.Text = dlg.SelectedPath;
            }
        }

        async void OnScan(object sender, EventArgs e)
        {
            string projectPath = txtProject.Text.Trim();
            string libsPath    = txtLibs.Text.Trim();
            string addLibsPath = txtAddLibs.Text.Trim();

            if (string.IsNullOrEmpty(projectPath))
            {
                MessageBox.Show("Please select a B4J project folder.", "B4JScanner",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Directory.Exists(libsPath) && !Directory.Exists(addLibsPath))
            {
                MessageBox.Show("Please set at least one valid library folder path.", "B4JScanner",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_config.MavenSearchEnabled.HasValue)
            {
                var ans = MessageBox.Show(
                    "B4JScanner can query Maven Central (search.maven.org) to identify JAR files " +
                    "that don't include embedded Maven metadata.\n\n" +
                    "This sends the JAR filename and a file checksum to search.maven.org.\n\n" +
                    "Allow Maven Central lookups?",
                    "Maven Central Lookup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                _config.MavenSearchEnabled = (ans == DialogResult.Yes);
                try { _config.Save(); } catch { }
            }
            JarAnalyzer.MavenSearchEnabled = _config.MavenSearchEnabled.GetValueOrDefault(false);

            txtLog.Clear();
            Log("Maven Central lookup: " + (JarAnalyzer.MavenSearchEnabled ? "enabled" : "disabled"));
            btnScan.Enabled       = false;
            btnLibScan.Enabled    = false;
            btnOpenSbom.Enabled   = false;
            btnOpenReport.Enabled = false;
            _lastSbomPath         = null;
            _lastHtmlPath         = null;
            _lastProject          = null;
            statusLabel.Text      = "Scanning...";
            Application.DoEvents();

            SaveConfig();

            string sbomPath = null;
            string htmlPath = null;

            try
            {
                Log("Parsing project: " + projectPath);
                var project = B4JProjectParser.Parse(projectPath, libsPath, addLibsPath);
                Log("  Name: "      + project.Name
                  + "  Version: "   + (project.Version ?? "?")
                  + "  Libraries: " + project.Libraries.Count
                  + "  Modules: "   + project.Modules.Count);

                sbomPath = Path.Combine(project.ProjectFolder, project.Name + ".cdx.json");

                Log("");
                Log("Resolving " + project.Libraries.Count + " libraries...");

                var resolved = new List<ResolvedLibrary>();
                int found = 0, notFound = 0;

                foreach (var lib in project.Libraries)
                {
                    var r = LibraryResolver.Resolve(lib, libsPath, addLibsPath);
                    r.Info = JarAnalyzer.Analyze(r);
                    LibraryResolver.ResolveDependencyJars(r.Info, libsPath, addLibsPath);
                    resolved.Add(r);

                    if (r.Found)
                    {
                        found++;
                        string depNote = r.Info.Dependencies.Count > 0
                            ? "  (" + r.Info.Dependencies.Count + " deps)"
                            : "";
                        Log("  [OK] " + lib.PadRight(30) + " v" + r.Info.Version + depNote);
                    }
                    else
                    {
                        notFound++;
                        Log("  [??] " + lib + "  (not found)");
                    }
                }

                if (project.AdditionalJars.Count > 0)
                {
                    Log("");
                    Log("Resolving " + project.AdditionalJars.Count + " #AdditionalJar entries...");
                    foreach (var jar in project.AdditionalJars)
                    {
                        var r = LibraryResolver.Resolve(jar, libsPath, addLibsPath);
                        r.IsAdditionalJar = true;
                        r.Info = JarAnalyzer.Analyze(r);
                        LibraryResolver.ResolveDependencyJars(r.Info, libsPath, addLibsPath);
                        resolved.Add(r);

                        if (r.Found)
                        {
                            found++;
                            Log("  [AJ] " + jar.PadRight(30) + " v" + r.Info.Version);
                        }
                        else
                        {
                            notFound++;
                            Log("  [??] " + jar + "  (additional JAR not found)");
                        }
                    }
                }

                Log("");
                Log("Scanning Objects\\src...");
                var javaFiles = JavaSourceScanner.Scan(project.ProjectFolder);
                var prefixes  = JavaSourceScanner.GetUniquePackagePrefixes(javaFiles);
                Log("  " + javaFiles.Count + " .java files, " + prefixes.Count + " unique import prefixes");

                Log("");
                Log("Writing SBOM...");
                SbomWriter.Write(project, resolved, javaFiles, sbomPath);
                Log("  " + sbomPath);

                string mdPath = Path.ChangeExtension(sbomPath, ".md");
                MdWriter.Write(project, resolved, javaFiles, mdPath);
                Log("  " + mdPath);

                htmlPath = Path.ChangeExtension(sbomPath, ".html");
                HtmlWriter.Write(project, resolved, javaFiles, htmlPath);
                Log("  " + htmlPath);

                _lastSbomPath  = sbomPath;
                _lastHtmlPath  = htmlPath;
                _lastProject   = project;
                _lastResolved  = resolved;
                _lastJavaFiles = javaFiles;

                Log("");
                string summary = "Libraries: " + found + " found, " + notFound + " not found.";
                Log(summary);
                statusLabel.Text = "Scanning for vulnerabilities...";

                // OSV vulnerability scan
                Log("");
                Log("--- OSV vulnerability scan ---");
                Application.DoEvents();

                var osvResult = await Task.Run(() => RunOsvScanner(sbomPath));

                if (!string.IsNullOrEmpty(osvResult.ErrorText))
                    Log(osvResult.ErrorText);

                if (osvResult.Packages.Count > 0)
                {
                    int total = 0;
                    foreach (var p in osvResult.Packages) total += p.Vulns.Count;
                    Log(total + " vulnerabilities found across " + osvResult.Packages.Count + " package(s):");
                    foreach (var p in osvResult.Packages)
                    {
                        Log("  " + p.PackageName + " " + (p.Version ?? "") + "  (" + p.Vulns.Count + " vulns)");
                        foreach (var v in p.Vulns)
                            Log("    " + (v.Id ?? "?").PadRight(20)
                                + " [" + (v.Severity ?? "?") + "]"
                                + (v.FixedVersion != null ? "  fix: " + v.FixedVersion : "")
                                + (v.Summary != null ? "  " + v.Summary : ""));
                    }
                }
                else if (string.IsNullOrEmpty(osvResult.ErrorText))
                {
                    Log("No vulnerabilities found.");
                }

                HtmlWriter.Write(project, resolved, javaFiles, htmlPath, osvResult.Packages);
                Log("--- OSV scan complete ---");

                int vulnCount = 0;
                foreach (var p in osvResult.Packages) vulnCount += p.Vulns.Count;
                statusLabel.Text = summary + "  Vulnerabilities: " + vulnCount + ".";

                btnOpenSbom.Enabled   = true;
                btnOpenReport.Enabled = true;
            }
            catch (Exception ex)
            {
                Log("");
                Log("ERROR: " + ex.Message);
                statusLabel.Text = "Scan failed.";
            }
            finally
            {
                btnScan.Enabled    = true;
                btnLibScan.Enabled = true;
            }
        }

        void OnOpenSbom(object sender, EventArgs e)
        {
            if (_lastSbomPath != null && File.Exists(_lastSbomPath))
            {
                try { Process.Start(_lastSbomPath); }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not open file:\n" + ex.Message, "B4JScanner",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void OnOpenReport(object sender, EventArgs e)
        {
            if (_lastHtmlPath != null && File.Exists(_lastHtmlPath))
            {
                try { Process.Start(_lastHtmlPath); }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not open file:\n" + ex.Message, "B4JScanner",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }


        static string FindOsvScanner()
        {
            string appDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            string[] matches = Directory.GetFiles(appDir, "osv-scanner*");
            if (matches.Length > 0)
                return matches[0];
            return "osv-scanner";
        }

        static OsvScanOutput RunOsvScanner(string sbomPath)
        {
            var output = new OsvScanOutput();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = FindOsvScanner(),
                    Arguments              = "--format json -L \"" + sbomPath + "\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    string trimmed = stdout == null ? "" : stdout.Trim();
                    if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                    {
                        output.Packages = OsvResultParser.Parse(trimmed);
                        if (!string.IsNullOrWhiteSpace(stderr))
                            output.ErrorText = stderr.TrimEnd();
                    }
                    else
                    {
                        string combined = stdout;
                        if (!string.IsNullOrWhiteSpace(stderr))
                            combined += (combined.Length > 0 ? "\n" : "") + stderr;
                        output.ErrorText = string.IsNullOrWhiteSpace(combined)
                            ? "(no output)" : combined.TrimEnd();
                    }
                }
            }
            catch (Win32Exception)
            {
                output.ErrorText = "osv-scanner not found.\n\n"
                    + "Place osv-scanner.exe in the same folder as B4JScanner.exe or add it to your PATH.\n"
                    + "Download from: https://github.com/google/osv-scanner/releases";
            }
            catch (Exception ex)
            {
                output.ErrorText = "Error running osv-scanner: " + ex.Message;
            }
            return output;
        }

        async void OnLibraryScan(object sender, EventArgs e)
        {
            string libsPath    = txtLibs.Text.Trim();
            string addLibsPath = txtAddLibs.Text.Trim();

            if (!Directory.Exists(libsPath) && !Directory.Exists(addLibsPath))
            {
                MessageBox.Show("Please set at least one valid library folder path.", "B4JScanner",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_config.MavenSearchEnabled.HasValue)
            {
                var ans = MessageBox.Show(
                    "B4JScanner can query Maven Central (search.maven.org) to identify JAR files " +
                    "that don't include embedded Maven metadata.\n\n" +
                    "This sends the JAR filename and a file checksum to search.maven.org.\n\n" +
                    "Allow Maven Central lookups?",
                    "Maven Central Lookup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                _config.MavenSearchEnabled = (ans == DialogResult.Yes);
                try { _config.Save(); } catch { }
            }
            JarAnalyzer.MavenSearchEnabled = _config.MavenSearchEnabled.GetValueOrDefault(false);

            txtLog.Clear();
            Log("Maven Central lookup: " + (JarAnalyzer.MavenSearchEnabled ? "enabled" : "disabled"));
            btnScan.Enabled    = false;
            btnLibScan.Enabled = false;
            statusLabel.Text   = "Scanning library folders...";
            Application.DoEvents();

            string appDir  = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string sbomPath = Path.Combine(appDir, "library-scan.cdx.json");
            string htmlPath = Path.Combine(appDir, "library-scan.html");

            var progress = new Progress<string>(msg => Log(msg));
            var jars = await Task.Run(() => ScanLibraryFolders(libsPath, addLibsPath, progress));

            int identified = 0;
            foreach (var j in jars)
                if (j.Info != null && j.Info.Maven != null && j.Info.Maven.GroupId != null) identified++;
            Log("");
            Log(jars.Count + " JARs scanned, " + identified + " identified.");

            Log("");
            Log("Writing SBOM...");
            try
            {
                SbomWriter.WriteLibraryScan(jars, sbomPath);
                Log("  " + sbomPath);
            }
            catch (Exception ex)
            {
                Log("ERROR writing SBOM: " + ex.Message);
                btnScan.Enabled    = true;
                btnLibScan.Enabled = true;
                statusLabel.Text   = "Library scan failed.";
                return;
            }

            Log("");
            Log("--- OSV vulnerability scan ---");
            statusLabel.Text = "Running OSV scan...";
            Application.DoEvents();

            var osvResult = await Task.Run(() => RunOsvScanner(sbomPath));

            if (!string.IsNullOrEmpty(osvResult.ErrorText))
                Log(osvResult.ErrorText);

            if (osvResult.Packages.Count > 0)
            {
                int total = 0;
                foreach (var p in osvResult.Packages) total += p.Vulns.Count;
                Log(total + " vulnerabilities found across " + osvResult.Packages.Count + " package(s):");
                foreach (var p in osvResult.Packages)
                {
                    Log("  " + p.PackageName + " " + (p.Version ?? "") + "  (" + p.Vulns.Count + " vulns)");
                    foreach (var v in p.Vulns)
                        Log("    " + (v.Id ?? "?").PadRight(20)
                            + " [" + (v.Severity ?? "?") + "]"
                            + (v.FixedVersion != null ? "  fix: " + v.FixedVersion : ""));
                }
            }
            else if (string.IsNullOrEmpty(osvResult.ErrorText))
            {
                Log("No vulnerabilities found.");
            }
            Log("--- OSV scan complete ---");

            Log("");
            Log("Writing HTML report...");
            try
            {
                HtmlWriter.WriteLibraryScan(jars, osvResult.Packages, htmlPath);
                Log("  " + htmlPath);
            }
            catch (Exception ex)
            {
                Log("ERROR writing report: " + ex.Message);
            }

            int vulnCount = 0;
            foreach (var p in osvResult.Packages) vulnCount += p.Vulns.Count;
            statusLabel.Text = "Library scan complete. "
                + jars.Count + " JARs, " + vulnCount + " vulnerabilities.";
            btnScan.Enabled    = true;
            btnLibScan.Enabled = true;

            try { System.Diagnostics.Process.Start(htmlPath); } catch { }
        }

        static List<ResolvedLibrary> ScanLibraryFolders(string libsPath, string addLibsPath,
            IProgress<string> progress)
        {
            var result    = new List<ResolvedLibrary>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in new[] { libsPath, addLibsPath })
            {
                if (!Directory.Exists(folder)) continue;
                string[] jars;
                try { jars = Directory.GetFiles(folder, "*.jar", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (string jar in jars)
                {
                    string fname = Path.GetFileName(jar);
                    if (!seenNames.Add(fname)) continue;

                    var r = new ResolvedLibrary
                    {
                        LibraryName = Path.GetFileNameWithoutExtension(jar),
                        JarPath     = jar
                    };
                    r.Info = JarAnalyzer.Analyze(r);

                    // Flag as B4J library for display if a sidecar XML exists
                    // (don't pass to Analyze so JAR version is preserved)
                    if (r.XmlPath == null)
                    {
                        string xmlCandidate = Path.ChangeExtension(jar, ".xml");
                        if (File.Exists(xmlCandidate)) r.XmlPath = xmlCandidate;
                    }

                    bool hasCoords = r.Info.Maven != null && r.Info.Maven.GroupId != null;
                    if (progress != null)
                        progress.Report((hasCoords ? "[OK] " : "[??] ") + fname);

                    result.Add(r);
                }
            }

            return result;
        }

        void Log(string message)
        {
            txtLog.AppendText(message + Environment.NewLine);
        }

        void SaveConfig()
        {
            _config.ProjectFolder           = txtProject.Text.Trim();
            _config.LibrariesPath           = txtLibs.Text.Trim();
            _config.AdditionalLibrariesPath = txtAddLibs.Text.Trim();
            try { _config.Save(); } catch { }
        }
    }
}
