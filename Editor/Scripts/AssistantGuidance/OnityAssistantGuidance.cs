using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Onity.Editor.AssistantGuidance
{
    /// <summary>
    /// One file the install command copies from the package into the project.
    /// </summary>
    public readonly struct GuidanceCopy
    {
        /// <summary>
        /// Creates a copy description.
        /// </summary>
        /// <param name="packageSource">Source path relative to the package root.</param>
        /// <param name="projectTarget">Target path relative to the project root.</param>
        public GuidanceCopy(string packageSource, string projectTarget)
        {
            PackageSource = packageSource;
            ProjectTarget = projectTarget;
        }

        /// <summary>
        /// Source path relative to the package root, with forward slashes.
        /// </summary>
        public string PackageSource { get; }

        /// <summary>
        /// Target path relative to the project root, with forward slashes.
        /// </summary>
        public string ProjectTarget { get; }
    }

    /// <summary>
    /// How an installed skill folder compares with the installed package.
    /// </summary>
    public enum GuidanceStatus
    {
        /// <summary>Neither the skill files nor a version stamp exist.</summary>
        NotInstalled,

        /// <summary>A skill file or the version stamp is missing.</summary>
        Incomplete,

        /// <summary>The version stamp does not hold a valid version.</summary>
        UnknownVersion,

        /// <summary>The skill was installed from an older package version.</summary>
        Outdated,

        /// <summary>The skill was installed from a newer package version than the one in the project.</summary>
        NewerThanPackage,

        /// <summary>The versions match, but a skill file differs from the package copy.</summary>
        Modified,

        /// <summary>The versions match and every skill file equals the package copy.</summary>
        UpToDate
    }

    /// <summary>
    /// State of the Onity section inside a project instruction file such as <c>AGENTS.md</c>.
    /// </summary>
    public enum GuidanceSectionStatus
    {
        /// <summary>The instruction file does not exist.</summary>
        FileMissing,

        /// <summary>The file has no Onity section.</summary>
        NoSection,

        /// <summary>The Onity section equals the text this package version writes.</summary>
        Current,

        /// <summary>The Onity section differs from the text this package version writes.</summary>
        Different,

        /// <summary>The section markers are damaged, so the section cannot be replaced safely.</summary>
        Damaged
    }

    /// <summary>
    /// Pure logic behind the <c>Onity/AI</c> menu commands: where the assistant skill is installed, how the
    /// project instruction files are patched, and how an installed copy is compared with the package.
    /// Nothing here touches the file system or the Unity API, so plain EditMode tests cover it.
    /// </summary>
    public static class OnityAssistantGuidance
    {
        /// <summary>
        /// First line of the section Onity owns inside a project instruction file.
        /// </summary>
        public const string BeginMarker = "<!-- onity:begin -->";

        /// <summary>
        /// Last line of the section Onity owns inside a project instruction file.
        /// </summary>
        public const string EndMarker = "<!-- onity:end -->";

        /// <summary>
        /// Path of the skill file inside the package. The package ships it under a folder Unity ignores.
        /// </summary>
        public const string PackageSkillPath = "Documentation~/AI/skills/onity-use/SKILL.md";

        /// <summary>
        /// Path of the usage guide inside the package. It is installed as a reference next to the skill.
        /// </summary>
        public const string PackageGuidePath = "Documentation~/AI/Onity-AI-Usage-Guide.md";

        private const string k_skillFileName = "SKILL.md";
        private const string k_guideTargetName = "references/Onity-AI-Usage-Guide.md";
        private const string k_versionStampFileName = ".onity-version";
        private const string k_sectionTitle = "## Onity";
        private const char k_byteOrderMark = (char)0xFEFF;

        private static readonly IReadOnlyList<string> s_skillRoots = Array.AsReadOnly(new[]
        {
            ".claude/skills/onity-use",
            ".agents/skills/onity-use"
        });

        private static readonly IReadOnlyList<string> s_instructionFiles = Array.AsReadOnly(new[]
        {
            "AGENTS.md",
            "CLAUDE.md"
        });

        private static readonly Regex s_versionPattern = new Regex(
            @"^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)" +
            @"(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?" +
            @"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Project-relative folders that receive the skill, with forward slashes. Claude Code reads the first,
        /// Codex and other agents read the second.
        /// </summary>
        public static IReadOnlyList<string> SkillRoots
        {
            get { return s_skillRoots; }
        }

        /// <summary>
        /// Project-root files that can receive the marked Onity section.
        /// </summary>
        public static IReadOnlyList<string> InstructionFiles
        {
            get { return s_instructionFiles; }
        }

        /// <summary>
        /// Gets the files copied into one skill folder: the skill itself and the usage guide as a reference.
        /// </summary>
        /// <param name="skillRoot">A project-relative skill folder from <see cref="SkillRoots"/>.</param>
        /// <returns>The copies, in install order.</returns>
        public static IReadOnlyList<GuidanceCopy> GetSkillCopies(string skillRoot)
        {
            if (string.IsNullOrEmpty(skillRoot))
            {
                throw new ArgumentException("A skill folder is required.", nameof(skillRoot));
            }

            return new[]
            {
                new GuidanceCopy(PackageSkillPath, skillRoot + "/" + k_skillFileName),
                new GuidanceCopy(PackageGuidePath, skillRoot + "/" + k_guideTargetName)
            };
        }

        /// <summary>
        /// Gets the project-relative path of the version stamp inside one skill folder.
        /// </summary>
        /// <param name="skillRoot">A project-relative skill folder from <see cref="SkillRoots"/>.</param>
        /// <returns>The stamp path, with forward slashes.</returns>
        public static string GetVersionStampPath(string skillRoot)
        {
            if (string.IsNullOrEmpty(skillRoot))
            {
                throw new ArgumentException("A skill folder is required.", nameof(skillRoot));
            }

            return skillRoot + "/" + k_versionStampFileName;
        }

        /// <summary>
        /// Builds the text of the stamp that records which package version installed a skill.
        /// </summary>
        /// <param name="packageVersion">The package version from <c>package.json</c>.</param>
        /// <returns>The stamp text.</returns>
        public static string FormatVersionStamp(string packageVersion)
        {
            if (string.IsNullOrWhiteSpace(packageVersion))
            {
                throw new ArgumentException("A package version is required.", nameof(packageVersion));
            }

            return packageVersion.Trim() + "\n";
        }

        /// <summary>
        /// Reads the package version out of stamp text.
        /// </summary>
        /// <param name="stampText">The stamp file text, or null when the file does not exist.</param>
        /// <returns>The first non-empty line, trimmed, or null when there is none.</returns>
        public static string ParseVersionStamp(string stampText)
        {
            if (string.IsNullOrEmpty(stampText))
            {
                return null;
            }

            string[] lines = stampText.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim(k_byteOrderMark, ' ', '\t', '\r');

                if (line.Length > 0)
                {
                    return line;
                }
            }

            return null;
        }

        /// <summary>
        /// Compares two semantic versions (<c>major.minor.patch</c> with optional pre-release and build parts).
        /// Build metadata is ignored and a pre-release sorts before its release.
        /// </summary>
        /// <param name="left">The first version.</param>
        /// <param name="right">The second version.</param>
        /// <param name="comparison">Negative when left is older, zero when equal, positive when newer.</param>
        /// <returns>False when either text is not a valid version.</returns>
        public static bool TryCompareVersions(string left, string right, out int comparison)
        {
            comparison = 0;

            if (TryParseVersion(left, out ParsedVersion parsedLeft) == false ||
                TryParseVersion(right, out ParsedVersion parsedRight) == false)
            {
                return false;
            }

            comparison = CompareVersions(parsedLeft, parsedRight);
            return true;
        }

        /// <summary>
        /// Classifies an installed skill folder.
        /// </summary>
        /// <param name="stampText">The text of the version stamp, or null when it does not exist.</param>
        /// <param name="packageVersion">The version of the installed package.</param>
        /// <param name="anySkillFilePresent">True when at least one skill file exists.</param>
        /// <param name="allSkillFilesPresent">True when every skill file exists.</param>
        /// <param name="skillFilesMatchPackage">True when every present skill file equals the package copy.</param>
        /// <returns>The status.</returns>
        public static GuidanceStatus Classify(
            string stampText,
            string packageVersion,
            bool anySkillFilePresent,
            bool allSkillFilesPresent,
            bool skillFilesMatchPackage)
        {
            string installedVersion = ParseVersionStamp(stampText);

            if (installedVersion == null && anySkillFilePresent == false)
            {
                return GuidanceStatus.NotInstalled;
            }

            if (installedVersion == null || allSkillFilesPresent == false)
            {
                return GuidanceStatus.Incomplete;
            }

            if (TryCompareVersions(installedVersion, packageVersion, out int comparison) == false)
            {
                return GuidanceStatus.UnknownVersion;
            }

            if (comparison < 0)
            {
                return GuidanceStatus.Outdated;
            }

            if (comparison > 0)
            {
                return GuidanceStatus.NewerThanPackage;
            }

            return skillFilesMatchPackage ? GuidanceStatus.UpToDate : GuidanceStatus.Modified;
        }

        /// <summary>
        /// Describes a status for the check report.
        /// </summary>
        /// <param name="status">The status to describe.</param>
        /// <param name="installedVersion">The installed version from the stamp, or null.</param>
        /// <param name="packageVersion">The version of the installed package.</param>
        /// <returns>One short sentence fragment.</returns>
        public static string Describe(GuidanceStatus status, string installedVersion, string packageVersion)
        {
            switch (status)
            {
                case GuidanceStatus.NotInstalled:
                    return "not installed";
                case GuidanceStatus.Incomplete:
                    return "incomplete (a skill file or the version stamp is missing)";
                case GuidanceStatus.UnknownVersion:
                    return $"unreadable version stamp '{installedVersion}'";
                case GuidanceStatus.Outdated:
                    return $"outdated (installed {installedVersion}, package {packageVersion})";
                case GuidanceStatus.NewerThanPackage:
                    return $"installed from a newer package (installed {installedVersion}, package {packageVersion})";
                case GuidanceStatus.Modified:
                    return $"version {installedVersion} matches, but a file differs from the package copy";
                default:
                    return $"up to date ({installedVersion})";
            }
        }

        /// <summary>
        /// Describes a section status for the check report.
        /// </summary>
        /// <param name="status">The status to describe.</param>
        /// <returns>One short sentence fragment.</returns>
        public static string DescribeSection(GuidanceSectionStatus status)
        {
            switch (status)
            {
                case GuidanceSectionStatus.FileMissing:
                    return "file not found";
                case GuidanceSectionStatus.NoSection:
                    return "no Onity section";
                case GuidanceSectionStatus.Different:
                    return "Onity section differs from this package version";
                case GuidanceSectionStatus.Damaged:
                    return "Onity section markers are damaged";
                default:
                    return "Onity section is current";
            }
        }

        /// <summary>
        /// Compares two texts while ignoring the line-ending style, so a Git checkout that converts line
        /// endings does not read as a modification.
        /// </summary>
        /// <param name="left">The first text; null counts as empty.</param>
        /// <param name="right">The second text; null counts as empty.</param>
        /// <returns>True when the texts hold the same lines.</returns>
        public static bool HaveSameContent(string left, string right)
        {
            return string.Equals(NormalizeNewlines(left), NormalizeNewlines(right), StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds the section written into project instruction files, including both markers.
        /// It does not depend on the package version, so re-running the install changes nothing
        /// until this text changes.
        /// </summary>
        /// <returns>The section, with line feeds and no trailing newline.</returns>
        public static string BuildMarkedSection()
        {
            string skill = SkillRoots[0] + "/" + k_skillFileName;
            string agentsSkill = SkillRoots[1] + "/" + k_skillFileName;
            string guide = SkillRoots[0] + "/" + k_guideTargetName;

            string[] lines =
            {
                BeginMarker,
                k_sectionTitle,
                string.Empty,
                "This project uses the Onity package (`com.onity.framework`) for dependency injection, reactive state,",
                "typed messaging and `OnityTask` async. Before you write or change Onity code, read the installed skill:",
                string.Empty,
                $"- `{skill}` (`{agentsSkill}` for Codex and other agents)",
                $"- `{guide}`: API rules, recipes and the module index (a copy sits next to each skill)",
                string.Empty,
                "Do not invent Onity APIs from Zenject, VContainer, R3, UniRx or UniTask analogies; the installed",
                "package source and its tests are the authority. The full guides ship inside the package under",
                "`Documentation~/` (`Packages/com.onity.framework` or `Library/PackageCache/com.onity.framework@<hash>`).",
                string.Empty,
                "Onity maintains the text between its begin and end markers. Run `Onity/AI/Install Assistant Guidance...`",
                "in the Unity Editor to refresh it; text outside the markers is never changed.",
                EndMarker
            };

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Inserts or replaces the marked section in an instruction file. Text outside the markers is kept
        /// exactly as it is, the file's line-ending style is used for the section, and running it again on
        /// its own result changes nothing.
        /// </summary>
        /// <param name="existingText">The current file text, or null when the file does not exist.</param>
        /// <param name="section">The section to write, from <see cref="BuildMarkedSection"/>.</param>
        /// <param name="result">The new file text; the unchanged input when the call fails.</param>
        /// <param name="error">Why the markers cannot be used, or null on success.</param>
        /// <returns>False when the existing markers are damaged, in which case nothing should be written.</returns>
        public static bool TryUpsertMarkedSection(string existingText, string section, out string result, out string error)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            string text = existingText ?? string.Empty;
            result = text;
            error = null;

            string trimmedSection = section.Trim('\r', '\n');

            if (trimmedSection.StartsWith(BeginMarker, StringComparison.Ordinal) == false ||
                trimmedSection.EndsWith(EndMarker, StringComparison.Ordinal) == false)
            {
                throw new ArgumentException("The section must start with the begin marker and end with the end marker.", nameof(section));
            }

            string newline = DetectNewline(text);
            string block = ConvertNewlines(trimmedSection, newline);
            int begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
            int end = text.IndexOf(EndMarker, StringComparison.Ordinal);

            if (begin < 0 && end < 0)
            {
                result = AppendSection(text, block, newline);
                return true;
            }

            if (begin < 0)
            {
                error = "the end marker has no begin marker before it";
                return false;
            }

            if (end < 0)
            {
                error = "the begin marker has no end marker after it";
                return false;
            }

            if (end < begin)
            {
                error = "the end marker comes before the begin marker";
                return false;
            }

            if (text.IndexOf(BeginMarker, begin + BeginMarker.Length, StringComparison.Ordinal) >= 0 ||
                text.IndexOf(EndMarker, end + EndMarker.Length, StringComparison.Ordinal) >= 0)
            {
                error = "the file holds more than one Onity section";
                return false;
            }

            result = text.Substring(0, begin) + block + text.Substring(end + EndMarker.Length);
            return true;
        }

        /// <summary>
        /// Compares the Onity section inside an instruction file with the text this package version writes.
        /// </summary>
        /// <param name="existingText">The file text, or null when the file does not exist.</param>
        /// <param name="section">The section from <see cref="BuildMarkedSection"/>.</param>
        /// <returns>The section status.</returns>
        public static GuidanceSectionStatus GetSectionStatus(string existingText, string section)
        {
            if (existingText == null)
            {
                return GuidanceSectionStatus.FileMissing;
            }

            bool hasBegin = existingText.IndexOf(BeginMarker, StringComparison.Ordinal) >= 0;
            bool hasEnd = existingText.IndexOf(EndMarker, StringComparison.Ordinal) >= 0;

            if (hasBegin == false && hasEnd == false)
            {
                return GuidanceSectionStatus.NoSection;
            }

            if (TryUpsertMarkedSection(existingText, section, out string updated, out string error) == false)
            {
                return GuidanceSectionStatus.Damaged;
            }

            return HaveSameContent(existingText, updated) ? GuidanceSectionStatus.Current : GuidanceSectionStatus.Different;
        }

        private static string AppendSection(string text, string block, string newline)
        {
            string trimmed = text.TrimEnd('\r', '\n');

            if (trimmed.Length == 0)
            {
                return block + newline;
            }

            return trimmed + newline + newline + block + newline;
        }

        private static string DetectNewline(string text)
        {
            int index = text.IndexOf('\n');
            return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
        }

        private static string ConvertNewlines(string text, string newline)
        {
            string normalized = NormalizeNewlines(text);
            return newline == "\n" ? normalized : normalized.Replace("\n", newline);
        }

        private static string NormalizeNewlines(string text)
        {
            return text == null ? string.Empty : text.Replace("\r\n", "\n");
        }

        private static bool TryParseVersion(string text, out ParsedVersion version)
        {
            version = default;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Match match = s_versionPattern.Match(text.Trim());

            if (match.Success == false ||
                int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) == false ||
                int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor) == false ||
                int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch) == false)
            {
                return false;
            }

            Group preRelease = match.Groups["pre"];
            string[] identifiers = preRelease.Success ? preRelease.Value.Split('.') : Array.Empty<string>();
            version = new ParsedVersion(major, minor, patch, identifiers);
            return true;
        }

        private static int CompareVersions(ParsedVersion left, ParsedVersion right)
        {
            int result = left.Major.CompareTo(right.Major);

            if (result == 0)
            {
                result = left.Minor.CompareTo(right.Minor);
            }

            if (result == 0)
            {
                result = left.Patch.CompareTo(right.Patch);
            }

            if (result != 0)
            {
                return Math.Sign(result);
            }

            bool leftIsRelease = left.PreRelease.Length == 0;
            bool rightIsRelease = right.PreRelease.Length == 0;

            if (leftIsRelease || rightIsRelease)
            {
                return leftIsRelease == rightIsRelease ? 0 : (leftIsRelease ? 1 : -1);
            }

            int shared = Math.Min(left.PreRelease.Length, right.PreRelease.Length);

            for (int i = 0; i < shared; i++)
            {
                int identifierResult = ComparePreReleaseIdentifiers(left.PreRelease[i], right.PreRelease[i]);

                if (identifierResult != 0)
                {
                    return identifierResult;
                }
            }

            return Math.Sign(left.PreRelease.Length.CompareTo(right.PreRelease.Length));
        }

        private static int ComparePreReleaseIdentifiers(string left, string right)
        {
            bool leftIsNumeric = IsNumeric(left);
            bool rightIsNumeric = IsNumeric(right);

            if (leftIsNumeric && rightIsNumeric)
            {
                string leftDigits = left.TrimStart('0');
                string rightDigits = right.TrimStart('0');

                if (leftDigits.Length != rightDigits.Length)
                {
                    return leftDigits.Length < rightDigits.Length ? -1 : 1;
                }

                return Math.Sign(string.CompareOrdinal(leftDigits, rightDigits));
            }

            if (leftIsNumeric || rightIsNumeric)
            {
                return leftIsNumeric ? -1 : 1;
            }

            return Math.Sign(string.CompareOrdinal(left, right));
        }

        private static bool IsNumeric(string identifier)
        {
            for (int i = 0; i < identifier.Length; i++)
            {
                if (identifier[i] < '0' || identifier[i] > '9')
                {
                    return false;
                }
            }

            return identifier.Length > 0;
        }

        private readonly struct ParsedVersion
        {
            public readonly int Major;
            public readonly int Minor;
            public readonly int Patch;
            public readonly string[] PreRelease;

            public ParsedVersion(int major, int minor, int patch, string[] preRelease)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                PreRelease = preRelease;
            }
        }
    }
}
