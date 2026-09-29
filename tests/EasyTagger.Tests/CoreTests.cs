using EasyTagger.Core;

namespace EasyTagger.Tests;

public class RenamerTests
{
    [Fact]
    public void FinalNameDropsDateAndSuffix()
    {
        var result = Renamer.BuildNewStem("Vanessa Hu_260712214720_Hyb.mp4", "Realistic");
        Assert.True(result.MatchedPattern);
        Assert.Equal("Vanessa Hu_Realistic", result.NewStem);
    }

    [Fact]
    public void UnknownNameStillAppendsModel()
    {
        var result = Renamer.BuildNewStem("irgendwas.mp4", "ModelX");
        Assert.False(result.MatchedPattern);
        Assert.Equal("irgendwas_ModelX", result.NewStem);
    }

    [Fact]
    public void TitleIsCutAtTwentyCharacters()
    {
        var result = Renamer.BuildNewStem(
            "Ein ausgesprochen langer Videotitel_260712214720_Hyb.mp4",
            "[Clip] [Hund] [Sitzen]");
        Assert.Equal("Ein ausgesprochen la_[Clip] [Hund] [Sitzen]", result.NewStem);
    }

    [Fact]
    public void IntermediateFileIsNotFinal()
    {
        Assert.False(Renamer.IsFinalOutputName("260712214720_Pro.mp4"));
        Assert.True(Renamer.IsFinalOutputName("Vanessa Hu_260712214720_Hyb.mp4"));
    }

    [Fact]
    public void AlreadyTaggedWhenEveryBracketIsPresent()
    {
        Assert.True(Renamer.IsTaggedWith("clip_[Clip] [Hund] [Sitzen].mp4", "[Clip] [Hund] [Sitzen]"));
        Assert.False(Renamer.IsTaggedWith("clip_[Clip] [Hund].mp4", "[Clip] [Hund] [Sitzen]"));
    }

    [Fact]
    public void MoveRenamesIntoTheModelFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "easytagger-tests", Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "in");
        var destDir = Path.Combine(root, "out");
        Directory.CreateDirectory(sourceDir);
        var source = Path.Combine(sourceDir, "Clip_260712214720_Hyb.mp4");
        File.WriteAllText(source, "video");
        var outcome = Renamer.ApplyTag(source, new FaceModel { Name = "[Hund]", Folder = destDir }, "move");
        Assert.False(File.Exists(source));
        Assert.Equal(Path.Combine(destDir, "Clip_[Hund].mp4"), outcome.TargetPath);
        var parked = outcome.TargetPath;
        Assert.Throws<InvalidOperationException>(() =>
            Renamer.ApplyTag(parked, new FaceModel { Name = "[Hund]", Folder = "" }, "move"));
        Assert.True(File.Exists(outcome.TargetPath));
        var restored = Renamer.UndoTag(outcome.SourcePath, outcome.TargetPath, outcome.Action);
        Assert.Equal(source, restored);
        Assert.True(File.Exists(source));
    }
}

public class ModelGroupTests
{
    [Fact]
    public void NameTagKeepsItsChipLabel()
    {
        var groups = ModelGroups.AvailableGroups(
            [new FaceModel { Name = "[Hund] [Studio]" }],
            [],
            "Sonstige");
        var studio = Assert.Single(groups, chip => chip.Id == "studio");
        Assert.Equal("Studio", studio.Label);
    }

    [Fact]
    public void EmptyCategoryStaysVisible()
    {
        var catalog = new List<ModelCategory>();
        ModelGroups.AddCategory(catalog, "Familie");
        var groups = ModelGroups.AvailableGroups([], catalog, "Other");
        Assert.Equal("familie", Assert.Single(groups).Id);
        Assert.Equal(0, groups[0].Count);
    }

    [Fact]
    public void AssignmentWithoutFilenameChangeKeepsTheName()
    {
        var model = new FaceModel { Name = "[Hund]", Folder = @"D:\Videos" };
        ModelGroups.ApplyAssignment(model, [
            new AssignmentRow { Id = "familie", Label = "Familie", Selected = true, InName = false },
        ]);
        Assert.Equal("[Hund]", model.Name);
        Assert.Equal(["familie"], model.Categories);
    }

    [Fact]
    public void FilenameHookAppendsTheCategory()
    {
        var model = new FaceModel { Name = "[Hund]" };
        ModelGroups.ApplyAssignment(model, [
            new AssignmentRow { Id = "familie", Label = "Familie", Selected = true, InName = true },
        ]);
        Assert.Equal("[Hund] [Familie]", model.Name);
    }

    [Fact]
    public void TagTypedIntoTheNameIsKept()
    {
        var model = new FaceModel { Name = "[Hund] [Katze] [Studio]" };
        ModelGroups.ApplyAssignment(model, ModelGroups.AssignmentRows([], new FaceModel { Name = "[Hund] [Katze]" }));
        Assert.Equal("[Hund] [Katze] [Studio]", model.Name);
    }

    [Fact]
    public void TurningTheFilenameHookOffRemovesOnlyThatTag()
    {
        var rows = ModelGroups.AssignmentRows([], new FaceModel { Name = "[Hund] [Katze]" });
        rows[0].InName = false;
        rows[0].Selected = false;
        var model = new FaceModel { Name = "[Hund] [Katze] [Studio]" };
        ModelGroups.ApplyAssignment(model, rows);
        Assert.Equal("[Hund] [Studio]", model.Name);
    }

    [Fact]
    public void HundAndHundeShareOneChip()
    {
        var groups = ModelGroups.AvailableGroups([
            new FaceModel { Name = "[ExampleModel] [Hund]" },
            new FaceModel { Name = "[Katze] [Hunde]" },
        ], [], "Other");
        var hund = Assert.Single(groups, chip => chip.Id == "hund");
        Assert.Equal("Hund", hund.Label);
        Assert.Equal(2, hund.Count);
    }
}

public class DownloadTests
{
    [Fact]
    public void SelectionComposesNameAndFolder()
    {
        var picker = DownloadPicker.CreateDefault();
        picker.Types[0].BaseFolder = @"D:\Videos\Downloads\Clips";
        Assert.Equal("[Clip] [Hund] [Sitzen]", DownloadTags.SelectionName(picker));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(@"D:\Videos\Downloads\Clips", "hund")),
            DownloadTags.SelectionFolder(picker));
    }

    [Fact]
    public void NonVideoSkipsReencode()
    {
        var path = Path.Combine(Path.GetTempPath(), "note.txt");
        var config = TagConfig.CreateDefault();
        config.ReencodeEnabled = true;
        Assert.Equal(path, Encoder.MaybeReencode(path, config));
    }

    [Fact]
    public void DuplicateTagIsRejected()
    {
        var picker = DownloadPicker.CreateDefault();
        Assert.Throws<InvalidOperationException>(() => DownloadTags.AddItem(picker, "contents", "Sitzen"));
    }

    [Fact]
    public void EditingATargetChangesItsNameAndFolder()
    {
        var picker = DownloadPicker.CreateDefault();
        picker.Types[0].BaseFolder = @"D:\Videos";
        DownloadTags.UpdateItem(picker, "targets", "hund", "Welpe", "welpen");
        Assert.Equal("welpe", picker.LastSelection.Target);
        Assert.Equal("[Clip] [Welpe] [Sitzen]", DownloadTags.SelectionName(picker));
        Assert.Equal(Path.GetFullPath(Path.Combine(@"D:\Videos", "welpen")), DownloadTags.SelectionFolder(picker));
    }
}

public class ConfigTests
{
    [Fact]
    public void ProfileSwitchKeepsEachList()
    {
        var config = TagConfig.CreateDefault();
        config.Models[0].Name = "[ExampleModel]";
        config.SwitchProfile(TagConfig.ProfileDownloads);
        Assert.Equal(TagConfig.ProfileDownloads, config.ActiveProfile);
        Assert.Empty(config.Models);
        Assert.Equal("[Clip] [Hund] [Sitzen]", DownloadTags.SelectionName(config.DownloadPicker));
        config.SwitchProfile(TagConfig.ProfileModels);
        Assert.Equal("[ExampleModel]", config.Models[0].Name);
    }

    [Fact]
    public void SaveRoundTripUsesItsOwnFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "easytagger-tests", Guid.NewGuid().ToString("N"), "config.json");
        var config = TagConfig.CreateDefault();
        config.Hotkey = "Ctrl+Alt+E";
        ConfigStore.Save(path, config);
        var loaded = ConfigStore.LoadOrCreate(path);
        Assert.Equal("Ctrl+Alt+E", loaded.Hotkey);
        Assert.Equal("[ExampleModel]", loaded.Models[0].Name);
    }

    [Fact]
    public void ABrokenFileStartsWithDefaultsAndStaysAsBackup()
    {
        var path = Path.Combine(Path.GetTempPath(), "easytagger-tests", Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"models\": [ this is not json");
        var loaded = ConfigStore.LoadOrCreate(path);
        Assert.Equal("[ExampleModel]", loaded.Models[0].Name);
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public void BatchSettingsStayWhenTheProfileChanges()
    {
        var config = TagConfig.CreateDefault();
        config.BatchRoot = @"D:\Library";
        config.BatchRules.Add(new BatchRule { Match = "Hund", Tags = "hund" });
        config.SwitchProfile(TagConfig.ProfileDownloads);
        Assert.Equal(@"D:\Library", config.BatchRoot);
        Assert.Single(config.BatchRules);
    }
}

public class ModelFolderTests
{
    [Fact]
    public void FolderPlanUsesTheModelNameAndDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "easytagger-tests", Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "in");
        var destination = Path.Combine(root, "out");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "clip.mp4"), "video");
        File.WriteAllText(Path.Combine(sourceDir, "clip_[Hund].mp4"), "video");
        var config = TagConfig.CreateDefault();
        config.WatchOnlyFinalNames = true;
        var plans = ModelFolder.Plan(sourceDir, new FaceModel { Name = "[Hund]", Folder = destination }, "move", config);
        var fresh = Assert.Single(plans, plan => !plan.AlreadyTagged);
        Assert.Equal(Path.Combine(destination, "clip_[Hund].mp4"), fresh.TargetPath);
        Assert.Single(plans, plan => plan.AlreadyTagged);
    }

    [Fact]
    public void FolderNameBecomesATagWhenAsked()
    {
        var root = Path.Combine(Path.GetTempPath(), "easytagger-tests", Guid.NewGuid().ToString("N"));
        var schule = Path.Combine(root, "Clips", "Schule");
        Directory.CreateDirectory(schule);
        File.WriteAllText(Path.Combine(schule, "clip.mp4"), "video");
        var config = TagConfig.CreateDefault();
        config.ModelBatchFolderTag = true;
        var model = ModelFolder.WithFolderTag(new FaceModel { Name = "[Hund] [Katze]", Folder = root }, schule, true);
        var plans = ModelFolder.Plan(schule, model, "rename_only", config);
        var fresh = Assert.Single(plans);
        Assert.Equal(Path.Combine(schule, "clip_[Hund] [Katze] [Schule].mp4"), fresh.TargetPath);
    }
}

public class BatchRuleTests
{
    [Fact]
    public void NewFileGetsBracketTagsAfterTheTitle()
    {
        Assert.Equal("Clip -- [hund] [sitzen]", BatchRules.BuildTaggedStem("Clip.mp4", ["hund", "sitzen"]));
        Assert.Equal("Clip -- [hund] [sitzen]", BatchRules.BuildTaggedStem("Clip -- [hund].mp4", ["sitzen", "hund"]));
        Assert.Equal("ABCDEFGHIJKLMNOPQRST -- [hund]", BatchRules.BuildTaggedStem("ABCDEFGHIJKLMNOPQRSTUVWXYZ.mp4", ["hund"]));
    }

    [Fact]
    public void SubfoldersBecomeTagsAndRulesRenameInPlace()
    {
        var root = Path.Combine(Path.GetTempPath(), "easytagger-tests", Guid.NewGuid().ToString("N"));
        var hund = Path.Combine(root, "clips", "Hund");
        Directory.CreateDirectory(hund);
        var source = Path.Combine(hund, "one.mp4");
        File.WriteAllText(source, "video");
        var preview = BatchRules.Preview(root, [], [".mp4"], useFolderNames: true);
        var item = Assert.Single(preview);
        Assert.Equal("one -- [clips] [hund].mp4", item.NewName);
        var target = BatchRules.Apply(item);
        Assert.Equal(Path.Combine(hund, "one -- [clips] [hund].mp4"), target);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void ALockedFileIsReportedInUse()
    {
        var path = Path.Combine(Path.GetTempPath(), "easytagger-tests", Guid.NewGuid().ToString("N") + ".mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            stream.WriteByte(1);
            Assert.True(FileScan.IsFileInUse(path));
        }
        Assert.False(FileScan.IsFileInUse(path));
    }
}
