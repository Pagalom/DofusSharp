using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class WeeklyRoutineTest
{
    [Fact]
    public void LegacyDefaultIdentitySurvivesRenamingDeletionAndRestore()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var original = WeeklyRoutinePlanner.CreateDefault();
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(original, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!;
        foreach (var activity in json["activities"]!.AsArray()) activity!.AsObject().Remove("defaultKey");
        try
        {
            string legacy = json.ToJsonString(); File.WriteAllText(path, legacy);
            var store = new WeeklyRoutineStore(path); var migrated = store.Read();
            Assert.Equal(legacy, File.ReadAllText(path));
            Assert.All(migrated.Activities, a => Assert.NotNull(a.DefaultKey));
            var first = migrated.Activities[0]; first.Title = "Renommée"; first.Deleted = true;
            migrated.Completions.Add(new(first.Id, new DateOnly(2026,11,5)));
            store.Save(migrated); var reloaded = store.Read();
            WeeklyRoutinePlanner.RestoreDefaultActivities(reloaded); store.Save(reloaded);
            Assert.Equal(original.Activities.Select(a => a.Id), reloaded.Activities.Select(a => a.Id));
            Assert.Equal(original.Activities[0].Title, reloaded.Activities[0].Title);
            Assert.False(reloaded.Activities[0].Deleted); Assert.Single(store.Read().Completions);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void RestoringDefaultsKeepsIdentityHistoryAndPersonalObjectives()
    {
        var routine = WeeklyRoutinePlanner.CreateDefault();
        routine.Name = "Mes objectifs";
        var original = routine.Activities[0];
        var custom = new RoutineActivity { Title = "Mon objectif", Notes = "À garder", EstimatedNetKamas = 42 };
        routine.Activities.Add(custom);
        routine.Completions.Add(new(original.Id, new DateOnly(2026,11,5)));
        routine.Completions.Add(new(custom.Id, new DateOnly(2026,11,6)));
        var history = routine.Completions.ToArray();
        original.Title = "Titre changé"; original.Deleted = true; original.Notes = "Erreur"; original.Priority = 3; original.EstimatedNetKamas = 99; original.Enabled = false;
        WeeklyRoutinePlanner.RestoreDefaultActivities(routine);
        var restored = routine.Activities.Single(a => a.Id == original.Id);
        Assert.Equal(1, restored.Priority); Assert.True(restored.Enabled); Assert.False(restored.Deleted); Assert.Null(restored.EstimatedNetKamas);
        Assert.Equal(WeeklyRoutinePlanner.CreateDefault().Activities[0].Notes, restored.Notes);
        Assert.Equal(history, routine.Completions); Assert.Equal("Mes objectifs", routine.Name);
        Assert.False(custom.Enabled); Assert.Equal("À garder", custom.Notes); Assert.Equal(42, custom.EstimatedNetKamas);
        WeeklyRoutinePlanner.Validate(routine);
        var ids = routine.Activities.Select(a => a.Id).ToArray();
        WeeklyRoutinePlanner.RestoreDefaultActivities(routine);
        Assert.Equal(ids, routine.Activities.Select(a => a.Id)); Assert.Equal(history, routine.Completions);
    }

    [Fact]
    public void CalendarUsesBerlinDateAcrossMidnightAndSummerTime()
    {
        Assert.Equal(new DateOnly(2026, 11, 6), WeeklyRoutinePlanner.Today(new(2026, 11, 5, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 7, 2), WeeklyRoutinePlanner.Today(new(2026, 7, 1, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 12, 31), WeeklyRoutinePlanner.Today(new(2026, 12, 31, 22, 59, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 1, 1), WeeklyRoutinePlanner.Today(new(2026, 12, 31, 23, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void EachNewDayHasFreshChecksAndKeepsEarlierCompletions()
    {
        var routine = WeeklyRoutinePlanner.CreateDefault(); var first = routine.Activities[0];
        DateOnly monday = new(2026, 11, 8), tuesday = monday.AddDays(1);
        WeeklyRoutinePlanner.Toggle(routine, first.Id, monday, monday);
        Assert.True(WeeklyRoutinePlanner.DoneOn(routine, first, monday));
        Assert.False(WeeklyRoutinePlanner.DoneOn(routine, first, tuesday));
        Assert.Single(routine.Completions);
        WeeklyRoutinePlanner.Toggle(routine, first.Id, tuesday, tuesday);
        Assert.Equal(2, routine.Completions.Count);
    }

    [Fact]
    public void DailyChecksArePerDateAndUndoDoesNotRemoveOtherDaysOrActivities()
    {
        var routine = WeeklyRoutinePlanner.CreateDefault(); var first = routine.Activities[0];
        DateOnly day = new(2026, 11, 5);
        WeeklyRoutinePlanner.Toggle(routine, first.Id, day, day);
        WeeklyRoutinePlanner.Toggle(routine, first.Id, day.AddDays(-1), day);
        Assert.True(WeeklyRoutinePlanner.DoneOn(routine, first, day));
        Assert.True(WeeklyRoutinePlanner.DoneOn(routine, first, day.AddDays(-1)));
        WeeklyRoutinePlanner.Toggle(routine, first.Id, day, day);
        Assert.False(WeeklyRoutinePlanner.DoneOn(routine, first, day)); Assert.Single(routine.Completions);
        Assert.Throws<ArgumentException>(() => WeeklyRoutinePlanner.Toggle(routine, first.Id, day.AddDays(1), day));
    }

    [Fact]
    public void NextTaskRespectsPriorityDeletionAndAlreadyDoneToday()
    {
        var routine = WeeklyRoutinePlanner.CreateDefault(); DateOnly today = new(2026, 11, 5);
        Assert.Equal(routine.Activities[0], WeeklyRoutinePlanner.Next(routine, today));
        routine.Activities[0].Enabled = false;
        routine.Activities[1].Deleted = true;
        Assert.Equal(routine.Activities[2], WeeklyRoutinePlanner.Next(routine, today));
        WeeklyRoutinePlanner.Toggle(routine, routine.Activities[2].Id, today, today);
        Assert.Equal(routine.Activities[3], WeeklyRoutinePlanner.Next(routine, today));
        Assert.Throws<ArgumentException>(() => WeeklyRoutinePlanner.Toggle(routine, routine.Activities[1].Id, today, today));
    }

    [Fact]
    public void DefaultsStayWithinEconomicToolsAndDoNotInventRevenueOrTrackQuestProgress()
    {
        var routine = WeeklyRoutinePlanner.CreateDefault();
        WeeklyRoutinePlanner.Validate(routine);
        Assert.All(routine.Activities, a => { Assert.NotNull(a.ModuleSlug); Assert.Null(a.EstimatedNetKamas); });
        Assert.Contains(routine.Activities, a => a.Title.Contains("Guildatons"));
        Assert.DoesNotContain(routine.Activities, a => a.Title.Contains("Tour du monde") || a.Title.Contains("Archimonstre"));
    }

    [Fact]
    public void StorePersistsCustomizationAndHistoryAndNeverReplacesADamagedFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "routine.json");
        try
        {
            var store = new WeeklyRoutineStore(path); var routine = store.Read();
            Assert.False(File.Exists(path)); routine.Name = "Pagalom · Tylezia";
            routine.Activities[0].EstimatedNetKamas = 12345;
            WeeklyRoutinePlanner.Toggle(routine, routine.Activities[0].Id, new(2026, 11, 5), new(2026, 11, 5));
            store.Save(routine);
            var restored = store.Read(); Assert.Equal(routine.Name, restored.Name);
            Assert.Equal(12345, restored.Activities[0].EstimatedNetKamas); Assert.Single(restored.Completions);
            File.WriteAllText(path, "{damaged");
            Assert.Throws<InvalidDataException>(() => store.Read());
            Assert.Throws<InvalidDataException>(() => store.Save(routine));
            Assert.Equal("{damaged", File.ReadAllText(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void LegacyWeeklyFileBecomesDailyWithoutInventingChecksOrLosingCustomization()
    {
        string folder=Path.Combine(Path.GetTempPath(),"routine-upgrade-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"routine.json");var id=Guid.NewGuid();DateOnly done=new(2026,11,5);
        string legacy=System.Text.Json.JsonSerializer.Serialize(new {
            version=1,name="Ma routine personnalisée",playerLevel=39,weekStartsOn=2,
            activities=new[]{new{id,title="Mon activité",notes="Mes notes",daily=false,enabled=true,priority=1,estimatedNetKamas=1234}},
            completions=new[]{new{activityId=id,date=done}}
        });
        try
        {
            File.WriteAllText(path,legacy);var store=new WeeklyRoutineStore(path);var routine=store.Read();
            Assert.Equal(2,routine.Version);Assert.Equal(legacy,File.ReadAllText(path));
            var activity=Assert.Single(routine.Activities, a => a.Id == id);Assert.Equal("Mes notes",activity.Notes);
            var soul=Assert.Single(routine.Activities, a => a.ModuleSlug == "pierres-ame");
            Assert.Equal(soul.Id, Assert.Single(store.Read().Activities, a => a.ModuleSlug == "pierres-ame").Id);
            soul.Deleted = true;
            store.Save(routine);
            Assert.True(Assert.Single(store.Read().Activities, a => a.ModuleSlug == "pierres-ame").Deleted);
            Assert.Equal(1234,activity.EstimatedNetKamas);
            Assert.Single(routine.Completions);Assert.True(WeeklyRoutinePlanner.DoneOn(routine,activity,done));
            Assert.False(WeeklyRoutinePlanner.DoneOn(routine,activity,done.AddDays(1)));
            WeeklyRoutinePlanner.Toggle(routine,id,done.AddDays(1),done.AddDays(1));store.Save(routine);
            var restored=store.Read();Assert.Equal(2,restored.Completions.Count);Assert.Equal("Ma routine personnalisée",restored.Name);
            string saved=File.ReadAllText(path);Assert.DoesNotContain("weekStartsOn",saved);Assert.DoesNotContain("\"daily\"",saved);
        }
        finally { Directory.Delete(folder,true); }
    }

    [Fact]
    public void InvalidAmountsAndUnsafeLinksDoNotReachTheStore()
    {
        var routine = WeeklyRoutinePlanner.CreateDefault(); routine.Activities[0].EstimatedNetKamas = long.MaxValue;
        Assert.Throws<ArgumentException>(() => WeeklyRoutinePlanner.Validate(routine));
        routine.Activities[0].EstimatedNetKamas = -100;
        WeeklyRoutinePlanner.Validate(routine);
        routine.Activities[0].GuideUrl = "javascript:alert(1)";
        Assert.Throws<ArgumentException>(() => WeeklyRoutinePlanner.Validate(routine));
        routine.Activities[0].GuideUrl = null; routine.Activities[0].ModuleSlug = "arbitrary";
        Assert.Throws<ArgumentException>(() => WeeklyRoutinePlanner.Validate(routine));
    }
}
