using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Onity.Editor.AssistantGuidance
{
    /// <summary>
    /// Editor commands that copy the Onity assistant skill into the project and check the installed copy.
    /// Nothing is written until the user confirms a dialog that lists every file, and nothing runs on import.
    /// </summary>
    public static class OnityAssistantGuidanceMenu
    {
        private const string k_installMenuPath = "Onity/AI/Install Assistant Guidance...";
        private const string k_checkMenuPath = "Onity/AI/Check Assistant Guidance";
        private const string k_dialogTitle = "Onity Assistant Guidance";
        private const string k_logPrefix = "[Onity Assistant Guidance]";
        private const int k_installPriority = 2300;
        private const int k_checkPriority = 2301;
        private const int k_cancelChoice = 1;
        private const int k_installWithInstructionsChoice = 2;

        private static readonly UTF8Encoding s_utf8WithoutBom = new UTF8Encoding(false);

        [MenuItem(k_installMenuPath, false, k_installPriority)]
        private static void InstallMenu()
        {
            if (TryGetPackage(out string packageRoot, out string packageVersion) == false)
            {
                return;
            }

            string projectRoot = GetProjectRoot();
            List<PlannedWrite> skillWrites = new List<PlannedWrite>(6);
            string planError = PlanSkillWrites(projectRoot, packageRoot, packageVersion, skillWrites);

            if (planError != null)
            {
                EditorUtility.DisplayDialog(k_dialogTitle, planError, "OK");
                return;
            }

            List<PlannedWrite> instructionWrites = new List<PlannedWrite>(2);
            List<string> instructionSkips = new List<string>(2);
            PlanInstructionWrites(projectRoot, instructionWrites, instructionSkips);

            if (HasChanges(skillWrites) == false && HasChanges(instructionWrites) == false && instructionSkips.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    k_dialogTitle,
                    "The assistant guidance in this project already matches package " + packageVersion + ". Nothing to write.",
                    "OK");
                return;
            }

            string message = BuildConfirmationMessage(
                projectRoot,
                packageVersion,
                skillWrites,
                instructionWrites,
                instructionSkips);

            int choice = EditorUtility.DisplayDialogComplex(
                k_dialogTitle,
                message,
                "Install skill only",
                "Cancel",
                "Skill + AGENTS.md / CLAUDE.md");

            if (choice == k_cancelChoice)
            {
                return;
            }

            try
            {
                int written = WriteChanged(projectRoot, skillWrites);

                if (choice == k_installWithInstructionsChoice)
                {
                    written += WriteChanged(projectRoot, instructionWrites);
                }

                Debug.Log($"{k_logPrefix} Wrote {written} file(s) for package {packageVersion} under {projectRoot}.");
                EditorUtility.DisplayDialog(k_dialogTitle, $"Wrote {written} file(s). Run the check command to verify.", "OK");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    k_dialogTitle,
                    "Writing failed, so the install may be incomplete: " + exception.Message +
                    "\n\nRun the check command to see what is in place.",
                    "OK");
            }
        }

        [MenuItem(k_checkMenuPath, false, k_checkPriority)]
        private static void CheckMenu()
        {
            if (TryGetPackage(out string packageRoot, out string packageVersion) == false)
            {
                return;
            }

            string projectRoot = GetProjectRoot();
            StringBuilder report = new StringBuilder(512);
            report.Append("Package ").Append(packageVersion).Append('\n');
            report.Append("Project ").Append(projectRoot).Append("\n\n");
            bool allCurrent = true;
            IReadOnlyList<string> skillRoots = OnityAssistantGuidance.SkillRoots;

            for (int i = 0; i < skillRoots.Count; i++)
            {
                GuidanceStatus status = ReadSkillStatus(
                    projectRoot,
                    packageRoot,
                    packageVersion,
                    skillRoots[i],
                    out string installedVersion);
                allCurrent &= status == GuidanceStatus.UpToDate;
                report.Append(skillRoots[i]).Append(": ")
                    .Append(OnityAssistantGuidance.Describe(status, installedVersion, packageVersion)).Append('\n');
            }

            report.Append('\n');
            string section = OnityAssistantGuidance.BuildMarkedSection();
            IReadOnlyList<string> instructionFiles = OnityAssistantGuidance.InstructionFiles;

            for (int i = 0; i < instructionFiles.Count; i++)
            {
                string fullPath = GetFullPath(projectRoot, instructionFiles[i]);
                string text = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
                GuidanceSectionStatus status = OnityAssistantGuidance.GetSectionStatus(text, section);
                report.Append(instructionFiles[i]).Append(": ")
                    .Append(OnityAssistantGuidance.DescribeSection(status)).Append('\n');
            }

            report.Append(allCurrent
                ? "\nThe installed skill matches this package version."
                : "\nRun " + k_installMenuPath + " to install or update the skill.");
            Debug.Log(k_logPrefix + "\n" + report);
            EditorUtility.DisplayDialog(k_dialogTitle, report.ToString(), "OK");
        }

        private static bool TryGetPackage(out string packageRoot, out string packageVersion)
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(OnityAssistantGuidanceMenu).Assembly);

            if (packageInfo == null ||
                string.IsNullOrEmpty(packageInfo.resolvedPath) ||
                string.IsNullOrWhiteSpace(packageInfo.version))
            {
                packageRoot = null;
                packageVersion = null;
                EditorUtility.DisplayDialog(
                    k_dialogTitle,
                    "Onity is not installed as a Unity package, so its documentation folder cannot be located.",
                    "OK");
                return false;
            }

            packageRoot = packageInfo.resolvedPath;
            packageVersion = packageInfo.version;
            return true;
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string GetFullPath(string root, string relativePath)
        {
            return Path.GetFullPath(Path.Combine(root, relativePath));
        }

        private static string PlanSkillWrites(
            string projectRoot,
            string packageRoot,
            string packageVersion,
            List<PlannedWrite> writes)
        {
            IReadOnlyList<string> skillRoots = OnityAssistantGuidance.SkillRoots;

            for (int rootIndex = 0; rootIndex < skillRoots.Count; rootIndex++)
            {
                IReadOnlyList<GuidanceCopy> copies = OnityAssistantGuidance.GetSkillCopies(skillRoots[rootIndex]);

                for (int copyIndex = 0; copyIndex < copies.Count; copyIndex++)
                {
                    GuidanceCopy copy = copies[copyIndex];
                    string sourcePath = GetFullPath(packageRoot, copy.PackageSource);

                    if (File.Exists(sourcePath) == false)
                    {
                        return "The installed Onity package has no " + copy.PackageSource +
                            ". Update Onity to a version that ships its documentation.";
                    }

                    writes.Add(PlanWrite(projectRoot, copy.ProjectTarget, File.ReadAllText(sourcePath, Encoding.UTF8)));
                }

                // The stamp goes last in each folder, so an interrupted install never reads as complete.
                writes.Add(PlanWrite(
                    projectRoot,
                    OnityAssistantGuidance.GetVersionStampPath(skillRoots[rootIndex]),
                    OnityAssistantGuidance.FormatVersionStamp(packageVersion)));
            }

            return null;
        }

        private static void PlanInstructionWrites(string projectRoot, List<PlannedWrite> writes, List<string> skips)
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();
            IReadOnlyList<string> instructionFiles = OnityAssistantGuidance.InstructionFiles;

            for (int i = 0; i < instructionFiles.Count; i++)
            {
                string fullPath = GetFullPath(projectRoot, instructionFiles[i]);
                string existing = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;

                if (OnityAssistantGuidance.TryUpsertMarkedSection(existing, section, out string updated, out string error) == false)
                {
                    skips.Add(instructionFiles[i] + ": " + error);
                    continue;
                }

                writes.Add(new PlannedWrite(
                    instructionFiles[i],
                    updated,
                    existing != null,
                    existing == null || OnityAssistantGuidance.HaveSameContent(existing, updated) == false));
            }
        }

        private static PlannedWrite PlanWrite(string projectRoot, string relativePath, string text)
        {
            string fullPath = GetFullPath(projectRoot, relativePath);
            bool exists = File.Exists(fullPath);
            bool changed = exists == false ||
                OnityAssistantGuidance.HaveSameContent(File.ReadAllText(fullPath), text) == false;
            return new PlannedWrite(relativePath, text, exists, changed);
        }

        private static bool HasChanges(List<PlannedWrite> writes)
        {
            for (int i = 0; i < writes.Count; i++)
            {
                if (writes[i].Changed)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildConfirmationMessage(
            string projectRoot,
            string packageVersion,
            List<PlannedWrite> skillWrites,
            List<PlannedWrite> instructionWrites,
            List<string> instructionSkips)
        {
            StringBuilder builder = new StringBuilder(1024);
            builder.Append("Onity will copy AI assistant guidance into this project.\n");
            builder.Append("Package ").Append(packageVersion).Append(", project ").Append(projectRoot).Append("\n\n");
            builder.Append("Skill files, written by both install choices:\n");
            AppendWrites(builder, skillWrites);
            builder.Append("\nProject instruction files, written only by \"Skill + AGENTS.md / CLAUDE.md\".\n");
            builder.Append("Only the text between the Onity markers is added or replaced:\n");
            AppendWrites(builder, instructionWrites);

            for (int i = 0; i < instructionSkips.Count; i++)
            {
                builder.Append("  skip    ").Append(instructionSkips[i]).Append('\n');
            }

            builder.Append("\nNothing else is changed. Files marked \"update\" are overwritten; \"same\" files are left alone.");
            return builder.ToString();
        }

        private static void AppendWrites(StringBuilder builder, List<PlannedWrite> writes)
        {
            for (int i = 0; i < writes.Count; i++)
            {
                PlannedWrite write = writes[i];
                string action = write.Changed ? (write.Exists ? "update" : "create") : "same  ";
                builder.Append("  ").Append(action).Append("  ").Append(write.RelativePath).Append('\n');
            }
        }

        private static int WriteChanged(string projectRoot, List<PlannedWrite> writes)
        {
            int count = 0;

            for (int i = 0; i < writes.Count; i++)
            {
                PlannedWrite write = writes[i];

                if (write.Changed == false)
                {
                    continue;
                }

                string fullPath = GetFullPath(projectRoot, write.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, write.Text, s_utf8WithoutBom);
                count++;
            }

            return count;
        }

        private static GuidanceStatus ReadSkillStatus(
            string projectRoot,
            string packageRoot,
            string packageVersion,
            string skillRoot,
            out string installedVersion)
        {
            IReadOnlyList<GuidanceCopy> copies = OnityAssistantGuidance.GetSkillCopies(skillRoot);
            bool anyPresent = false;
            bool allPresent = true;
            bool matchesPackage = true;

            for (int i = 0; i < copies.Count; i++)
            {
                string targetPath = GetFullPath(projectRoot, copies[i].ProjectTarget);

                if (File.Exists(targetPath) == false)
                {
                    allPresent = false;
                    continue;
                }

                anyPresent = true;
                string sourcePath = GetFullPath(packageRoot, copies[i].PackageSource);

                if (File.Exists(sourcePath) == false ||
                    OnityAssistantGuidance.HaveSameContent(File.ReadAllText(targetPath), File.ReadAllText(sourcePath)) == false)
                {
                    matchesPackage = false;
                }
            }

            string stampPath = GetFullPath(projectRoot, OnityAssistantGuidance.GetVersionStampPath(skillRoot));
            string stampText = File.Exists(stampPath) ? File.ReadAllText(stampPath) : null;
            installedVersion = OnityAssistantGuidance.ParseVersionStamp(stampText);
            return OnityAssistantGuidance.Classify(stampText, packageVersion, anyPresent, allPresent, matchesPackage);
        }

        private readonly struct PlannedWrite
        {
            public readonly string RelativePath;
            public readonly string Text;
            public readonly bool Exists;
            public readonly bool Changed;

            public PlannedWrite(string relativePath, string text, bool exists, bool changed)
            {
                RelativePath = relativePath;
                Text = text;
                Exists = exists;
                Changed = changed;
            }
        }
    }
}
