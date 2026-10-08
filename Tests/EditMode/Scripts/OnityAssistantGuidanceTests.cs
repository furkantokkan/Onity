using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Editor.AssistantGuidance;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the pure logic behind the Onity/AI menu commands: where the skill is installed, how the
    /// marked section in AGENTS.md and CLAUDE.md is upserted, and how an installed copy is compared
    /// with the package. The Editor commands themselves only add dialogs and file writes around it.
    /// </summary>
    [TestFixture]
    public sealed class OnityAssistantGuidanceTests
    {
        private const string k_begin = OnityAssistantGuidance.BeginMarker;
        private const string k_end = OnityAssistantGuidance.EndMarker;
        private const char k_byteOrderMark = (char)0xFEFF;

        // Path mapping

        [Test]
        public void SkillRoots_NameTheClaudeAndAgentsSkillFolders()
        {
            Assert.That(
                OnityAssistantGuidance.SkillRoots,
                Is.EqualTo(new[] { ".claude/skills/onity-use", ".agents/skills/onity-use" }));
        }

        [Test]
        public void InstructionFiles_AreAgentsAndClaude()
        {
            Assert.That(OnityAssistantGuidance.InstructionFiles, Is.EqualTo(new[] { "AGENTS.md", "CLAUDE.md" }));
        }

        [Test]
        public void GetSkillCopies_MapsSkillAndGuideIntoTheSkillFolder()
        {
            IReadOnlyList<GuidanceCopy> copies = OnityAssistantGuidance.GetSkillCopies(".claude/skills/onity-use");

            Assert.That(copies.Count, Is.EqualTo(2));
            Assert.That(copies[0].PackageSource, Is.EqualTo("Documentation~/AI/skills/onity-use/SKILL.md"));
            Assert.That(copies[0].ProjectTarget, Is.EqualTo(".claude/skills/onity-use/SKILL.md"));
            Assert.That(copies[1].PackageSource, Is.EqualTo("Documentation~/AI/Onity-AI-Usage-Guide.md"));
            Assert.That(
                copies[1].ProjectTarget,
                Is.EqualTo(".claude/skills/onity-use/references/Onity-AI-Usage-Guide.md"));
        }

        [Test]
        public void GetSkillCopies_EveryRoot_ReadsFromDocumentationAndUsesForwardSlashes()
        {
            foreach (string root in OnityAssistantGuidance.SkillRoots)
            {
                foreach (GuidanceCopy copy in OnityAssistantGuidance.GetSkillCopies(root))
                {
                    Assert.That(copy.PackageSource, Does.StartWith("Documentation~/AI/"));
                    Assert.That(copy.ProjectTarget, Does.StartWith(root + "/"));
                    Assert.That(copy.PackageSource, Does.Not.Contain("\\"));
                    Assert.That(copy.ProjectTarget, Does.Not.Contain("\\"));
                }
            }
        }

        [TestCase(null)]
        [TestCase("")]
        public void GetSkillCopies_MissingRoot_Throws(string root)
        {
            Assert.Throws<ArgumentException>(() => OnityAssistantGuidance.GetSkillCopies(root));
        }

        [Test]
        public void GetVersionStampPath_PlacesTheStampInsideTheSkillFolder()
        {
            Assert.That(
                OnityAssistantGuidance.GetVersionStampPath(".agents/skills/onity-use"),
                Is.EqualTo(".agents/skills/onity-use/.onity-version"));
        }

        // Version stamp

        [TestCase("0.5.0", "0.5.0\n")]
        [TestCase("  0.5.0-preview.1  ", "0.5.0-preview.1\n")]
        public void FormatVersionStamp_TrimsAndEndsWithANewline(string version, string expected)
        {
            Assert.That(OnityAssistantGuidance.FormatVersionStamp(version), Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("  ")]
        public void FormatVersionStamp_BlankVersion_Throws(string version)
        {
            Assert.Throws<ArgumentException>(() => OnityAssistantGuidance.FormatVersionStamp(version));
        }

        [Test]
        public void ParseVersionStamp_RoundTripsAFormattedStamp()
        {
            string stamp = OnityAssistantGuidance.FormatVersionStamp("1.2.3-preview.4");

            Assert.That(OnityAssistantGuidance.ParseVersionStamp(stamp), Is.EqualTo("1.2.3-preview.4"));
        }

        [Test]
        public void ParseVersionStamp_ReadsTheFirstNonEmptyLine()
        {
            Assert.That(OnityAssistantGuidance.ParseVersionStamp("\r\n  0.5.0 \r\nignored\r\n"), Is.EqualTo("0.5.0"));
        }

        [Test]
        public void ParseVersionStamp_IgnoresAByteOrderMark()
        {
            Assert.That(OnityAssistantGuidance.ParseVersionStamp(k_byteOrderMark + "0.5.0\n"), Is.EqualTo("0.5.0"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \r\n \n")]
        public void ParseVersionStamp_EmptyText_ReturnsNull(string stamp)
        {
            Assert.That(OnityAssistantGuidance.ParseVersionStamp(stamp), Is.Null);
        }

        // Version compare

        [TestCase("0.5.0", "0.5.0", 0)]
        [TestCase("0.4.9", "0.5.0", -1)]
        [TestCase("0.5.1", "0.5.0", 1)]
        [TestCase("0.10.0", "0.9.0", 1)]
        [TestCase("1.0.0", "0.99.99", 1)]
        [TestCase("0.5.0-preview.1", "0.5.0", -1)]
        [TestCase("0.5.0", "0.5.0-preview.1", 1)]
        [TestCase("0.5.0-preview.2", "0.5.0-preview.10", -1)]
        [TestCase("0.5.0-alpha", "0.5.0-alpha.1", -1)]
        [TestCase("0.5.0-alpha.1", "0.5.0-beta", -1)]
        [TestCase("0.5.0-1", "0.5.0-alpha", -1)]
        [TestCase("0.5.0-rc.1", "0.5.0-rc.1", 0)]
        [TestCase("0.5.0+build.7", "0.5.0", 0)]
        [TestCase(" 0.5.0 ", "0.5.0", 0)]
        public void TryCompareVersions_ValidVersions_OrdersBySemanticVersioning(string left, string right, int expected)
        {
            bool compared = OnityAssistantGuidance.TryCompareVersions(left, right, out int comparison);

            Assert.That(compared, Is.True);
            Assert.That(comparison, Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("0.5")]
        [TestCase("v0.5.0")]
        [TestCase("0.5.0.1")]
        [TestCase("01.2.3")]
        [TestCase("a.b.c")]
        [TestCase("0.5.0-")]
        [TestCase("99999999999.0.0")]
        public void TryCompareVersions_InvalidVersion_ReturnsFalse(string invalid)
        {
            Assert.That(OnityAssistantGuidance.TryCompareVersions(invalid, "0.5.0", out _), Is.False);
            Assert.That(OnityAssistantGuidance.TryCompareVersions("0.5.0", invalid, out _), Is.False);
        }

        // Status

        [Test]
        public void Classify_NoStampAndNoFiles_IsNotInstalled()
        {
            Assert.That(
                OnityAssistantGuidance.Classify(null, "0.5.0", false, false, true),
                Is.EqualTo(GuidanceStatus.NotInstalled));
        }

        [Test]
        public void Classify_FilesWithoutAStamp_IsIncomplete()
        {
            Assert.That(
                OnityAssistantGuidance.Classify(null, "0.5.0", true, true, true),
                Is.EqualTo(GuidanceStatus.Incomplete));
        }

        [Test]
        public void Classify_StampWithAMissingFile_IsIncomplete()
        {
            Assert.That(
                OnityAssistantGuidance.Classify("0.5.0\n", "0.5.0", true, false, true),
                Is.EqualTo(GuidanceStatus.Incomplete));
        }

        [Test]
        public void Classify_StampWithoutAnyFile_IsIncomplete()
        {
            Assert.That(
                OnityAssistantGuidance.Classify("0.5.0\n", "0.5.0", false, false, true),
                Is.EqualTo(GuidanceStatus.Incomplete));
        }

        [Test]
        public void Classify_UnreadableStamp_IsUnknownVersion()
        {
            Assert.That(
                OnityAssistantGuidance.Classify("not a version\n", "0.5.0", true, true, true),
                Is.EqualTo(GuidanceStatus.UnknownVersion));
        }

        [Test]
        public void Classify_OlderStamp_IsOutdated()
        {
            Assert.That(
                OnityAssistantGuidance.Classify("0.4.0\n", "0.5.0", true, true, true),
                Is.EqualTo(GuidanceStatus.Outdated));
        }

        [Test]
        public void Classify_NewerStamp_IsNewerThanPackage()
        {
            Assert.That(
                OnityAssistantGuidance.Classify("0.6.0\n", "0.5.0", true, true, true),
                Is.EqualTo(GuidanceStatus.NewerThanPackage));
        }

        [Test]
        public void Classify_SameVersionWithADifferingFile_IsModified()
        {
            Assert.That(
                OnityAssistantGuidance.Classify("0.5.0\n", "0.5.0", true, true, false),
                Is.EqualTo(GuidanceStatus.Modified));
        }

        [Test]
        public void Classify_SameVersionAndIdenticalFiles_IsUpToDate()
        {
            Assert.That(
                OnityAssistantGuidance.Classify("0.5.0\r\n", "0.5.0", true, true, true),
                Is.EqualTo(GuidanceStatus.UpToDate));
        }

        [Test]
        public void Describe_OutdatedStatus_NamesBothVersions()
        {
            string description = OnityAssistantGuidance.Describe(GuidanceStatus.Outdated, "0.4.0", "0.5.0");

            Assert.That(description, Does.Contain("0.4.0"));
            Assert.That(description, Does.Contain("0.5.0"));
        }

        [Test]
        public void HaveSameContent_IgnoresTheLineEndingStyle()
        {
            Assert.That(OnityAssistantGuidance.HaveSameContent("a\r\nb\r\n", "a\nb\n"), Is.True);
            Assert.That(OnityAssistantGuidance.HaveSameContent("a\nb\n", "a\nc\n"), Is.False);
            Assert.That(OnityAssistantGuidance.HaveSameContent(null, string.Empty), Is.True);
        }

        // Marked section

        [Test]
        public void BuildMarkedSection_IsWrappedInMarkersAndUsesLineFeeds()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();

            Assert.That(section, Does.StartWith(k_begin + "\n"));
            Assert.That(section, Does.EndWith("\n" + k_end));
            Assert.That(section, Does.Not.Contain("\r"));
            Assert.That(section, Does.Not.EndWith("\n"));
        }

        [Test]
        public void BuildMarkedSection_PointsAtBothInstalledSkillsAndTheGuide()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();

            Assert.That(section, Does.Contain(".claude/skills/onity-use/SKILL.md"));
            Assert.That(section, Does.Contain(".agents/skills/onity-use/SKILL.md"));
            Assert.That(section, Does.Contain("references/Onity-AI-Usage-Guide.md"));
        }

        [Test]
        public void BuildMarkedSection_ContainsExactlyOneBeginAndOneEndMarker()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();

            Assert.That(section.IndexOf(k_begin, StringComparison.Ordinal), Is.EqualTo(0));
            Assert.That(section.IndexOf(k_begin, k_begin.Length, StringComparison.Ordinal), Is.EqualTo(-1));
            Assert.That(section.IndexOf(k_end, StringComparison.Ordinal), Is.EqualTo(section.Length - k_end.Length));
        }

        [TestCase(null)]
        [TestCase("")]
        public void TryUpsertMarkedSection_MissingFile_CreatesTheSectionWithATrailingNewline(string existing)
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();

            bool upserted = OnityAssistantGuidance.TryUpsertMarkedSection(existing, section, out string result, out string error);

            Assert.That(upserted, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(result, Is.EqualTo(section + "\n"));
        }

        [Test]
        public void TryUpsertMarkedSection_AppendsAfterExistingContentWithABlankLine()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();

            OnityAssistantGuidance.TryUpsertMarkedSection("# Notes\nhello\n", section, out string result, out _);

            Assert.That(result, Is.EqualTo("# Notes\nhello\n\n" + section + "\n"));
        }

        [Test]
        public void TryUpsertMarkedSection_AppendCollapsesTrailingBlankLinesAndAddsAMissingNewline()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();

            OnityAssistantGuidance.TryUpsertMarkedSection("keep\n\n\n", section, out string withBlankLines, out _);
            OnityAssistantGuidance.TryUpsertMarkedSection("keep", section, out string withoutNewline, out _);

            Assert.That(withBlankLines, Is.EqualTo("keep\n\n" + section + "\n"));
            Assert.That(withoutNewline, Is.EqualTo("keep\n\n" + section + "\n"));
        }

        [Test]
        public void TryUpsertMarkedSection_ExistingSection_ReplacesOnlyTheMarkedRegion()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();
            string existing = "# Project rules\n\nUse tabs.\n\n" + k_begin + "\nold text\nmore old text\n" + k_end + "\n\n## Other\nKeep me.\n";

            bool upserted = OnityAssistantGuidance.TryUpsertMarkedSection(existing, section, out string result, out _);

            Assert.That(upserted, Is.True);
            Assert.That(result, Is.EqualTo("# Project rules\n\nUse tabs.\n\n" + section + "\n\n## Other\nKeep me.\n"));
            Assert.That(result, Does.Not.Contain("old text"));
        }

        [Test]
        public void TryUpsertMarkedSection_MarkersInsideALine_KeepTheSurroundingText()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();
            string existing = "before " + k_begin + "x" + k_end + " after";

            OnityAssistantGuidance.TryUpsertMarkedSection(existing, section, out string result, out _);

            Assert.That(result, Is.EqualTo("before " + section + " after"));
        }

        [Test]
        public void TryUpsertMarkedSection_RunTwice_ChangesNothingTheSecondTime()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();
            string[] inputs = { null, "", "plain\n", "plain", "a\r\nb\r\n", k_begin + "\nstale\n" + k_end };

            foreach (string input in inputs)
            {
                OnityAssistantGuidance.TryUpsertMarkedSection(input, section, out string first, out _);
                OnityAssistantGuidance.TryUpsertMarkedSection(first, section, out string second, out _);

                Assert.That(second, Is.EqualTo(first), "Input: " + (input ?? "<null>"));
            }
        }

        [Test]
        public void TryUpsertMarkedSection_CrLfFile_WritesTheSectionWithCrLf()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();

            OnityAssistantGuidance.TryUpsertMarkedSection("line one\r\nline two\r\n", section, out string appended, out _);

            Assert.That(appended.Replace("\r\n", string.Empty), Does.Not.Contain("\n"));
            Assert.That(appended, Does.StartWith("line one\r\nline two\r\n\r\n" + k_begin + "\r\n"));
            Assert.That(appended, Does.EndWith(k_end + "\r\n"));

            string stale = "head\r\n" + k_begin + "\r\nold\r\n" + k_end + "\r\ntail\r\n";
            OnityAssistantGuidance.TryUpsertMarkedSection(stale, section, out string replaced, out _);

            Assert.That(replaced.Replace("\r\n", string.Empty), Does.Not.Contain("\n"));
            Assert.That(replaced, Does.StartWith("head\r\n" + k_begin + "\r\n"));
            Assert.That(replaced, Does.EndWith(k_end + "\r\ntail\r\n"));
        }

        [TestCase("text <!-- onity:begin --> without an end")]
        [TestCase("text without a begin <!-- onity:end -->")]
        [TestCase("<!-- onity:end --> before <!-- onity:begin -->")]
        [TestCase("<!-- onity:begin -->a<!-- onity:end --> and <!-- onity:begin -->b<!-- onity:end -->")]
        [TestCase("<!-- onity:begin -->a<!-- onity:begin -->b<!-- onity:end -->")]
        public void TryUpsertMarkedSection_DamagedMarkers_FailsAndLeavesTheTextUntouched(string existing)
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();

            bool upserted = OnityAssistantGuidance.TryUpsertMarkedSection(existing, section, out string result, out string error);

            Assert.That(upserted, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(result, Is.EqualTo(existing));
        }

        [Test]
        public void TryUpsertMarkedSection_SectionWithoutMarkers_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => OnityAssistantGuidance.TryUpsertMarkedSection("text", "no markers", out _, out _));
            Assert.Throws<ArgumentNullException>(
                () => OnityAssistantGuidance.TryUpsertMarkedSection("text", null, out _, out _));
        }

        [Test]
        public void GetSectionStatus_ReportsEachState()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();
            OnityAssistantGuidance.TryUpsertMarkedSection("# Rules\n", section, out string current, out _);

            Assert.That(OnityAssistantGuidance.GetSectionStatus(null, section), Is.EqualTo(GuidanceSectionStatus.FileMissing));
            Assert.That(OnityAssistantGuidance.GetSectionStatus("# Rules\n", section), Is.EqualTo(GuidanceSectionStatus.NoSection));
            Assert.That(OnityAssistantGuidance.GetSectionStatus(current, section), Is.EqualTo(GuidanceSectionStatus.Current));
            Assert.That(
                OnityAssistantGuidance.GetSectionStatus(current.Replace("Do not invent", "Please invent"), section),
                Is.EqualTo(GuidanceSectionStatus.Different));
            Assert.That(
                OnityAssistantGuidance.GetSectionStatus("text " + k_begin, section),
                Is.EqualTo(GuidanceSectionStatus.Damaged));
        }

        [Test]
        public void GetSectionStatus_CurrentSectionInACrLfFile_StaysCurrent()
        {
            string section = OnityAssistantGuidance.BuildMarkedSection();
            OnityAssistantGuidance.TryUpsertMarkedSection("# Rules\r\n", section, out string current, out _);

            Assert.That(OnityAssistantGuidance.GetSectionStatus(current, section), Is.EqualTo(GuidanceSectionStatus.Current));
        }
    }
}
