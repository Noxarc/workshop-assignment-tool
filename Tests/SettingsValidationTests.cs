using WorkshopAssignment.Services;
using Xunit;

namespace WorkshopAssignment.Tests;

public class SettingsValidationTests
{
    // ---------------------------------------------------------------
    //  SLOT NAME VALIDATION
    // ---------------------------------------------------------------

    [Fact]
    public void SlotName_Empty_Invalid()
    {
        Assert.False(SettingsValidator.IsValidSlotName(""));
    }

    [Fact]
    public void SlotName_Whitespace_Invalid()
    {
        Assert.False(SettingsValidator.IsValidSlotName("   "));
        Assert.False(SettingsValidator.IsValidSlotName("\t"));
        Assert.False(SettingsValidator.IsValidSlotName(" \n "));
    }

    [Fact]
    public void SlotName_ExceedsMaxLength_Invalid()
    {
        var tooLong = new string('a', 51);
        Assert.False(SettingsValidator.IsValidSlotName(tooLong));
    }

    [Fact]
    public void SlotName_MaxLength_Valid()
    {
        var exactlyMax = new string('a', 50);
        Assert.True(SettingsValidator.IsValidSlotName(exactlyMax));
    }

    [Fact]
    public void SlotName_Null_Invalid()
    {
        Assert.False(SettingsValidator.IsValidSlotName(null));
    }

    [Fact]
    public void SlotName_Normal_Valid()
    {
        Assert.True(SettingsValidator.IsValidSlotName("ganztags"));
        Assert.True(SettingsValidator.IsValidSlotName("vormittags"));
        Assert.True(SettingsValidator.IsValidSlotName("10:00-11:30"));
    }

    // ---------------------------------------------------------------
    //  SLOT NAME UNIQUENESS
    // ---------------------------------------------------------------

    [Fact]
    public void SlotNames_Duplicate_Invalid()
    {
        Assert.False(SettingsValidator.AreSlotNamesValid("Morning", "Morning", "Evening"));
        Assert.False(SettingsValidator.AreSlotNamesValid("Slot A", "Slot B", "Slot A"));
        Assert.False(SettingsValidator.AreSlotNamesValid("Same", "Same", "Same"));
    }

    [Fact]
    public void SlotNames_AllUnique_Valid()
    {
        Assert.True(SettingsValidator.AreSlotNamesValid("ganztags", "vormittags", "nachmittags"));
        Assert.True(SettingsValidator.AreSlotNamesValid("Slot 1", "Slot 2", "Slot 3"));
        Assert.True(SettingsValidator.AreSlotNamesValid("10:00-12:00", "13:00-15:00", "15:00-17:00"));
    }

    [Fact]
    public void SlotNames_OneInvalid_Invalid()
    {
        // Even if two are valid and unique, one invalid makes the whole thing invalid
        Assert.False(SettingsValidator.AreSlotNamesValid("", "Slot 2", "Slot 3"));
        Assert.False(SettingsValidator.AreSlotNamesValid("Slot 1", "   ", "Slot 3"));
        Assert.False(SettingsValidator.AreSlotNamesValid("Slot 1", "Slot 2", null));
    }

    // ---------------------------------------------------------------
    //  SOLVER TIME VALIDATION
    // ---------------------------------------------------------------

    [Fact]
    public void SolverTime_Zero_Invalid()
    {
        Assert.False(SettingsValidator.IsValidSolverTime(0, 0));
    }

    [Fact]
    public void SolverTime_OneSecond_Valid()
    {
        Assert.True(SettingsValidator.IsValidSolverTime(0, 1));
    }

    [Fact]
    public void SolverTime_MaxTime_Valid()
    {
        // 59 minutes 59 seconds is the maximum
        Assert.True(SettingsValidator.IsValidSolverTime(59, 59));
    }

    [Fact]
    public void SolverTime_ExceedsMax_Invalid()
    {
        // 60:00 exceeds the maximum of 59:59
        Assert.False(SettingsValidator.IsValidSolverTime(60, 0));
        // 59:60 also exceeds (invalid seconds component)
        Assert.False(SettingsValidator.IsValidSolverTime(59, 60));
    }

    [Fact]
    public void SolverTime_NegativeMinutes_Invalid()
    {
        Assert.False(SettingsValidator.IsValidSolverTime(-1, 30));
    }

    [Fact]
    public void SolverTime_NegativeSeconds_Invalid()
    {
        Assert.False(SettingsValidator.IsValidSolverTime(5, -1));
    }

    [Fact]
    public void SolverTime_DefaultValue_Valid()
    {
        // Default is 30 seconds per SETTINGS_SPEC.md
        Assert.True(SettingsValidator.IsValidSolverTime(0, 30));
    }

    [Fact]
    public void SolverTime_TypicalValues_Valid()
    {
        Assert.True(SettingsValidator.IsValidSolverTime(1, 0));   // 1 minute
        Assert.True(SettingsValidator.IsValidSolverTime(5, 0));   // 5 minutes
        Assert.True(SettingsValidator.IsValidSolverTime(30, 0));  // 30 minutes
        Assert.True(SettingsValidator.IsValidSolverTime(10, 30)); // 10:30
    }
}
